using System.Drawing;
using ClipRay.App.Settings;

namespace ClipRay.App.Recording;

internal sealed record RecordingRunOptions(string OutputDirectory, bool CopyToClipboard, string InterfaceLanguage);
internal interface IRecordingFeedback : IDisposable { bool IsReady { get; } }
internal interface IRecordingPendingPreview
{
    bool IsClosed { get; }
    void Pending(string message);
    void Attach(bool copied);
    void Fail(string message);
}
internal interface IRecordingEnvironment
{
    IReadOnlyList<Rectangle> Monitors();
    RecordingOutputPaths Paths(string root);
    IRecordingFeedback PrepareFeedback(Rectangle effectiveRegion, Action stop);
    IRecordingRuntime CreateRuntime(RecordingMonitorMapping mapping, RecordingOutputPaths paths, bool silent, RecordingFaultSignal faults);
    Task<bool> ConfirmSilentAsync(string message);
    IRecordingPendingPreview OpenPendingPreview(string path, string language);
    bool CopyFile(string path);
    void Notify(string message);
    Task DispatchAsync(Action action);
}

/// <summary>One owner from selection preparation until physical retirement, including late results.</summary>
internal sealed class RecordingApplicationController(IRecordingEnvironment environment)
{
    private Run? _current;
    private bool _closing;
    internal TimeSpan StopBudget { get; set; } = TimeSpan.FromSeconds(2);
    internal bool IsBusy => _current is not null;
    internal RecordingStopReport? LastReport { get; private set; }
    internal IReadOnlyList<string> DeliveryErrors { get; private set; } = [];
    internal Task? PhysicalCompletion => _current?.Completion;
    private sealed class Run(RecordingRunOptions options)
    {
        internal readonly RecordingRunOptions Options = options;
        internal readonly TaskCompletionSource Initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal RecordingOutputPaths? Paths;
        internal RecordingSession? Session;
        internal IRecordingFeedback? Feedback;
        internal IRecordingPendingPreview? Preview;
        internal Task<RecordingStopReport>? Completion;
        internal bool StopRequested;
        internal bool Started;
        internal Exception? StartError;
        internal readonly List<string> DeliveryErrors = [];
    }
    internal async Task<bool> StartAsync(RegionRecordingRequest request, RecordingRunOptions options,
        Action<string> restoreFailure, Action started, Func<bool>? canDeliverStartupUi = null)
    {
        if (_closing || !(canDeliverStartupUi?.Invoke() ?? true)) return false;
        if (IsBusy) { restoreFailure(Text(options,"录屏仍在进行或收尾，请稍后重试。","Recording is still running or retiring. Retry later.")); return false; }
        var run = new Run(options); _current = run;
        bool CanDeliver() => !_closing && ReferenceEquals(_current,run) && (canDeliverStartupUi?.Invoke() ?? true);
        void CheckStartupDelivery() { if (run.StopRequested || !CanDeliver()) throw new OperationCanceledException(); }
        try
        {
            var monitors = environment.Monitors();
            var mapping = RecordingStartupGuard.Execute(RecordingStartupStage.Selection,
                () => RecordingGeometry.Normalize(RecordingGeometry.MapToMonitor(request.PhysicalPixelRegion,monitors)));
            run.Paths = environment.Paths(options.OutputDirectory);
            run.Feedback = environment.PrepareFeedback(RecordingGeometry.EffectiveRegion(mapping), () => _ = StopAsync());
            if (run.StopRequested || !CanDeliver())
            {
                run.Feedback.Dispose(); run.Feedback = null;
                throw new OperationCanceledException();
            }
            if (!run.Feedback.IsReady) throw new RecordingStartupException(RecordingStartupStage.RecordingUi,new InvalidOperationException("capture-controls-not-safe"));
            for (var silent = false; ; silent = true)
            {
                CheckStartupDelivery();
                var faults = new RecordingFaultSignal();
                run.Session = new RecordingSession(environment.CreateRuntime(mapping,run.Paths,silent,faults));
                faults.Raised += fault => { _ = environment.DispatchAsync(() => { if (ReferenceEquals(_current,run)) _ = StopAsync(); }); };
                if (run.StopRequested || !CanDeliver()) { run.Session.RequestStop(); throw new OperationCanceledException(); }
                try { await run.Session.StartAsync(CancellationToken.None); break; }
                catch (Exception error) when (!silent && IsAudioUnavailable(error) && !run.StopRequested && CanDeliver())
                {
                    // Do not create another native owner until this failed attempt actually retired.
                    await run.Session.StopAsync().WaitAsync(StopBudget);
                    if (!run.Session.Report.ResourcesReleased) throw;
                    CheckStartupDelivery();
                    var recovery = run.Session.Report.Saved && System.IO.File.Exists(run.Paths.FinalPath) ? run.Paths.FinalPath
                        : System.IO.File.Exists(run.Paths.PartialPath) ? run.Paths.PartialPath : null;
                    var message = Text(options,"系统声音或 AAC 不可用。是否无声音继续？","System audio or AAC is unavailable. Continue without sound?");
                    message += recovery is null
                        ? Text(options,"本次尚未生成文件。"," No file was generated for this attempt.")
                        : Text(options,$"已有文件保留在：{recovery}",$" Existing recording kept at: {recovery}");
                    if (!await environment.ConfirmSilentAsync(message))
                        throw new OperationCanceledException("Silent recording was not selected.");
                    CheckStartupDelivery();
                    run.Paths = environment.Paths(options.OutputDirectory);
                }
            }
            CheckStartupDelivery();
            run.Started = true;
            started();
            environment.Notify(Text(options,"正在录屏","Recording"));
            return true;
        }
        catch (Exception error)
        {
            run.StartError = error;
            BeginStop(run,false);
            if (CanDeliver()) Safe(run,"restore", () => restoreFailure(RecordingFailureText.Format(error,
                run.Paths is { } paths && System.IO.File.Exists(paths.PartialPath) ? paths.PartialPath : null,options.InterfaceLanguage)));
            return false;
        }
        finally
        {
            run.Initialized.TrySetResult();
            if (run.StartError is not null) { BeginStop(run,false); await ObserveAsync(run); }
        }
    }
    private static bool IsAudioUnavailable(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is RecordingStartupException { Stage: RecordingStartupStage.AudioDevice or RecordingStartupStage.AudioEncoder or RecordingStartupStage.SystemAudio }) return true;
        return false;
    }
    internal Task<RecordingStopReport?> StopAsync()
    {
        var run = _current;
        if (run is null) return Task.FromResult(LastReport);
        BeginStop(run,true);
        return ObserveAsync(run);
    }
    private void BeginStop(Run run,bool preview)
    {
        if (run.Completion is not null) return;
        run.StopRequested = true;
        // Publish the task before callbacks/preview creation can re-enter Stop.
        var completion = new TaskCompletionSource<RecordingStopReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        run.Completion = completion.Task;
        Safe(run,"stop-request", () => run.Session?.RequestStop());
        Safe(run,"feedback-close", () => run.Feedback?.Dispose()); run.Feedback = null;
        if (preview && run.Paths is not null)
            Safe(run,"preview-create", () => run.Preview = environment.OpenPendingPreview(run.Paths.FinalPath,run.Options.InterfaceLanguage));
        Safe(run,"status", () => environment.Notify(Text(run.Options,"正在保存录屏…","Saving recording…")));
        _ = CompleteAsync(run,completion);
    }
    private async Task CompleteAsync(Run run,TaskCompletionSource<RecordingStopReport> completion)
    {
        RecordingStopReport report;
        try
        {
            await run.Initialized.Task.ConfigureAwait(false);
            if (run.Session is not null) await run.Session.StopAsync().ConfigureAwait(false);
            report = run.Session?.Report ?? new(run.Paths,false,false,null,[]);
            if (run.StartError is not null && report.FirstFault is null) report = report with { FirstFault = new("startup",run.StartError) };
        }
        catch(Exception error) { report = new(run.Paths,false,false,new("retirement",error),["retirement-failed"]); }
        try
        {
            await environment.DispatchAsync(() =>
            {
                LastReport = report;
                if (report.Saved && report.Paths is not null)
                {
                    var copied = false;
                    if (run.Started && run.Options.CopyToClipboard) Safe(run,"clipboard", () =>
                    { copied = environment.CopyFile(report.Paths.FinalPath); if (!copied) run.DeliveryErrors.Add("clipboard-unavailable"); });
                    if (run.Preview is { IsClosed:false }) Safe(run,"preview-attach", () => run.Preview.Attach(copied));
                }
                else if (run.Preview is { IsClosed:false }) Safe(run,"preview-failure", () => run.Preview.Fail(Outcome(run,report)));
                Safe(run,"status", () => environment.Notify(Outcome(run,report)));
                DeliveryErrors = run.DeliveryErrors.ToArray();
                if (report.ResourcesReleased && ReferenceEquals(_current,run)) _current = null;
            }).ConfigureAwait(false);
        }
        catch { run.DeliveryErrors.Add("dispatcher-unavailable"); }
        finally { completion.TrySetResult(report); }
    }
    private async Task<RecordingStopReport?> ObserveAsync(Run run)
    {
        try { return await run.Completion!.WaitAsync(StopBudget); }
        catch (TimeoutException)
        {
            Safe(run,"pending", () =>
            {
                var message = Text(run.Options,$"仍在收尾，尚未确认保存；临时路径：{run.Paths?.PartialPath}",
                    $"Still retiring; save is not yet confirmed. Partial path: {run.Paths?.PartialPath}");
                if (run.Preview is { IsClosed:false }) run.Preview.Pending(message);
                environment.Notify(message);
            });
            return new(run.Paths,false,false,run.Session?.Faults.First,[],PhysicalCompleted:false);
        }
    }
    internal Task RetireAsync()
    {
        _closing = true;
        var run = _current;
        if (run is null) return Task.CompletedTask;
        BeginStop(run,false);
        return CheckRetiredAsync(run);
    }
    private static async Task CheckRetiredAsync(Run run)
    {
        var report = await run.Completion!.ConfigureAwait(false);
        if (!report.ResourcesReleased) throw new InvalidOperationException("recording-resource-release-failed");
    }
    private static string Outcome(Run run,RecordingStopReport report)
    {
        var result = report.Saved
            ? Text(run.Options,$"已保存：{report.Paths?.FinalPath}",$"Saved: {report.Paths?.FinalPath}")
            : Text(run.Options,$"录屏未完成；临时路径：{report.Paths?.PartialPath ?? run.Paths?.PartialPath}",$"Recording incomplete; partial path: {report.Paths?.PartialPath ?? run.Paths?.PartialPath}");
        if (report.FirstFault is { } fault) result += $" [{fault.Stage}: 0x{unchecked((uint)fault.Error.HResult):X8}]";
        if (!report.ResourcesReleased) result += Text(run.Options,"；资源释放失败，暂不能开始新录屏。","; resource release failed; new recording is blocked.");
        if (run.DeliveryErrors.Count > 0) result += Text(run.Options,"；复制或预览反馈失败，文件状态不变。","; copy or preview feedback failed; file status is unchanged.");
        return result;
    }
    private static string Text(RecordingRunOptions options,string chinese,string english) => AppInterfaceLanguage.Text(options.InterfaceLanguage,chinese,english);
    private static void Safe(Run run,string name,Action action) { try { action(); } catch { run.DeliveryErrors.Add(name); } }
}

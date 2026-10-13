using System.Drawing;
using System.Windows;
using ClipRay.App.Recording;
using Forms = System.Windows.Forms;

namespace ClipRay.App;

public partial class App
{
    private RecordingApplicationController? _recordingController;
    private Task? _recordingRetirement;
    private bool _recordingAdmissionClosed;
    internal IRecordingEnvironment? RecordingEnvironmentOverride { get; set; }
    internal RecordingApplicationController RecordingController => _recordingController ??= new(RecordingEnvironmentOverride ?? new AppRecordingEnvironment(this));
    internal async Task<bool> BeginRecordingAsync(RegionRecordingRequest request,Action<string> restoreFailure,Action started,
        Func<bool>? canDeliverStartupUi = null)
    {
        bool CanDeliver() => !_recordingAdmissionClosed && (canDeliverStartupUi?.Invoke() ?? true);
        if (!CanDeliver()) return false;
        try
        {
            var settings = _settings!;
            return await RecordingController.StartAsync(request,new(settings.RecordingDirectory,settings.CopyToClipboard,settings.InterfaceLanguage),
                message => { if (CanDeliver()) restoreFailure(message); },
                () => { if (CanDeliver()) started(); },CanDeliver);
        }
        catch (Exception error)
        {
            // The outer fallback owns the same delivery guard as controller callbacks.
            if (CanDeliver())
                try { restoreFailure(RecordingFailureText.Format(error,null,_settings?.InterfaceLanguage ?? "zh-CN")); }
                catch { /* A failed UI callback must not escape the async event handler. */ }
            return false;
        }
    }
    internal void RequestRecordingRetirement()
    {
        _recordingAdmissionClosed = true;
        if (_recordingRetirement is not null || _recordingController is null) return;
        _recordingRetirement = _recordingController.RetireAsync();
        _translationRetirements.Track("recording",_recordingRetirement);
        if (!_recordingRetirement.IsCompleted)
            LastExitWarning = AppUi("录屏仍在收尾，尚未确认保存或资源释放。","Recording is still retiring; save and resource release are not yet confirmed.");
    }

    private sealed class AppRecordingEnvironment(App app) : IRecordingEnvironment
    {
        public IReadOnlyList<Rectangle> Monitors() => Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray();
        public RecordingOutputPaths Paths(string root) => RecordingOutputPaths.Create(root,DateTime.Now);
        public IRecordingFeedback PrepareFeedback(Rectangle region,Action stop)
        {
            RecordingBorderWindow? border = null; RecordingControlWindow? control = null;
            try
            {
                border = new(region); border.Show();
                control = new(region,app._settings?.InterfaceLanguage ?? "zh-CN");
                control.StopRequested += (_,_) => stop(); control.Show();
                var outside = control.PlaceOutside(region);
                var ready = RecordingControlReadiness.CanStart(border.IsCaptureSafe,control.AffinityApplied,outside);
                if (app._stopRecordingMenuItem is not null) app._stopRecordingMenuItem.Enabled = ready;
                return new Feedback(app,border,control,ready);
            }
            catch
            {
                try { border?.Close(); } finally { control?.Close(); }
                throw;
            }
        }
        public IRecordingRuntime CreateRuntime(RecordingMonitorMapping mapping,RecordingOutputPaths paths,bool silent,RecordingFaultSignal faults)
        {
            var timeline = RecordingTimeline.StartNow();
            return new RecordingRuntimeFactory(
                () => silent ? null : new WasapiSystemAudioCapture(timeline,faults),
                format => new MediaFoundationRecorder(paths,new NativeRecordingEncoderBackend(paths,mapping.CropInMonitor.Width,mapping.CropInMonitor.Height,format),faults),
                () => new GraphicsCaptureSource(mapping,timeline,onFault:error => faults.TrySet("graphics-capture",error),reuseBuffers:true),faults);
        }
        public Task<bool> ConfirmSilentAsync(string message) => Task.FromResult(System.Windows.MessageBox.Show(message,
            app.AppUi("录屏声音不可用","Recording audio unavailable"),MessageBoxButton.YesNo,MessageBoxImage.Warning) == MessageBoxResult.Yes);
        public IRecordingPendingPreview OpenPendingPreview(string path,string language)
        {
            app._recordingPreview?.Close();
            var preview = new RecordingPreviewWindow(path,pending:true,interfaceLanguage:language);
            app._recordingPreview=preview;
            preview.Closed += (_,_) => { if (ReferenceEquals(app._recordingPreview,preview)) app._recordingPreview=null; };
            preview.Show(); preview.Activate(); return new Preview(preview);
        }
        public bool CopyFile(string path) => RecordingResultDelivery.TryCopyFile(path,new WindowsRecordingFileClipboard());
        public void Notify(string message) => app.SetTrayTooltip("ClipRay | " + message);
        public Task DispatchAsync(Action action) => app.Dispatcher.CheckAccess() ? Invoke(action) : app.Dispatcher.InvokeAsync(action).Task;
        private static Task Invoke(Action action) { action(); return Task.CompletedTask; }
    }
    private sealed class Feedback(App app,RecordingBorderWindow border,RecordingControlWindow control,bool ready) : IRecordingFeedback
    {
        private int _disposed;
        public bool IsReady => ready;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed,1)!=0) return;
            try { border.Close(); }
            finally { try { control.Close(); } finally { if (app._stopRecordingMenuItem is not null) app._stopRecordingMenuItem.Enabled=false; } }
        }
    }
    private sealed class Preview(RecordingPreviewWindow window) : IRecordingPendingPreview
    {
        public bool IsClosed => window.ClosedForDelivery;
        public void Pending(string message) => window.MarkRetiring(message);
        public void Attach(bool copied) => window.AttachMedia(copied);
        public void Fail(string message) => window.Fail(message);
    }
}

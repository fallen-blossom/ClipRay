using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClipRay.App.Infrastructure;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using static BackgroundTask3Tests;

internal static class BackgroundTask3LifetimeTests
{
    internal static void RequestKeepsSelectedCredentialAndFallback() => RequestSnapshotAsync().GetAwaiter().GetResult();
    private static async Task RequestSnapshotAsync()
    {
        var app = FakeApp(); var entered = NewSignal(); var release = NewSignal();
        var backend = new SyntheticCredentialBackend();
        Set(app, "_credentialVault", new CredentialVault(backend, () => DateTimeOffset.UnixEpoch));
        var selected = Field<AppSettings>(app, "_settings") with { CredentialName = "selected-B", MachineTranslationFallbackEnabled = true };
        Set(app, "_settings", selected);
        var oldMachine = new SnapshotMachine(); var replacement = new SnapshotMachine();
        Set(app, "_machineTranslationProvider", oldMachine);
        var cloud = new SnapshotCloud(async () => { entered.TrySetResult(); await release.Task; });
        Set(app, "_translationServices", new TranslationRuntimeServices(() => cloud, () => throw new Exception("OCR not expected")));
        IReadOnlyList<TranslationRequest> requests = [new("English", "French", "a normal sentence", DeepSeekModels.Flash)];
        var actual = (Task<TranslationBatchOutcome>)Invoke(app, "TranslateVisualRequestsCoreAsync",
            TranslationRouteKind.DeepSeekFlash, requests, CancellationToken.None, null!, null!)!;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Invoke(app, "ApplySettings", selected with { CredentialName = CredentialVault.FixedDeepSeekSlot,
                MachineTranslationFallbackEnabled = false, TranslationTargetLanguage = "English" }, false);
            Set(app, "_machineTranslationProvider", replacement);
        }
        finally { release.TrySetResult(); await actual.WaitAsync(TimeSpan.FromSeconds(2)); }
        var result = await actual.WaitAsync(TimeSpan.FromSeconds(2));
        Check(cloud.Name == "selected-B" && backend.Reads.Count == 1
            && backend.Reads[0] == "ClipRay.Credential." + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("selected-B")),
            "Actual App request changed credential slot after admission.");
        Check(oldMachine.Calls == 1 && replacement.Calls == 0 && result.Results.Single().IsSuccess
            && cloud.Request!.TargetLanguage == "French" && cloud.Request.Model == DeepSeekModels.Flash,
            "Actual App request mixed new settings/provider with admitted options.");
        await app.RequestManagedShutdownAsync(finalShutdown: () => { });
    }

    internal static void CommonContextRetirement() => CommonContextRetirementAsync().GetAwaiter().GetResult();
    private static async Task CommonContextRetirementAsync()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var disposed = 0; var native = 0; var maximum = 0;
        var context = new NonPumpingContext();
        var runtime = new CommonScriptOcrRuntime(initializeBackend: () =>
        {
            maximum = Math.Max(maximum, Interlocked.Increment(ref native));
            entered.Set(); release.Wait(); Interlocked.Decrement(ref native);
        }, releaseBackend: () => { Check(native == 0, "Disposed a held native owner."); disposed++; });
        Task warming; Task retiring;
        var prior = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            warming = runtime.WarmupAsync(default);
            Check(entered.Wait(2000), "Fake backend did not enter.");
            retiring = runtime.DisposeAsync().AsTask(); runtime.Dispose();
            Check(!retiring.IsCompleted && disposed == 0, "Retirement released active resources.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); release.Set(); }
        try { await warming.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
        await retiring.WaitAsync(TimeSpan.FromSeconds(2));
        Check(disposed == 1 && maximum == 1 && context.Posts == 0, "Common OCR still depends on UI pumping or releases twice.");
        await ThrowsDisposed(() => runtime.WarmupAsync(default));
    }

    internal static void OcrServiceClosesWithoutRebuild() => OcrServiceClosesWithoutRebuildAsync().GetAwaiter().GetResult();

    internal static void FailedInitializationAndHeldRecognition() => FailedInitializationAndHeldRecognitionAsync().GetAwaiter().GetResult();
    private static async Task FailedInitializationAndHeldRecognitionAsync()
    {
        var attempts = 0; var releases = 0;
        var failing = new CommonScriptOcrRuntime(initializeBackend: () => { if (++attempts == 1) throw new InvalidOperationException("fixture init failure"); },
            releaseBackend: () => releases++);
        try
        {
            try { await failing.WarmupAsync(default); throw new Exception("Initialization failure hidden."); } catch (InvalidOperationException) { }
            await failing.WarmupAsync(default);
        }
        finally { await failing.DisposeAsync(); }
        Check(attempts == 2 && releases == 1, "Failed initialization poisoned admission or release count.");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var read = false; var freed = 0;
        var runtime = new CommonScriptOcrRuntime(initializeBackend: () => { }, recognizeBackend: (image, _) =>
        { entered.Set(); release.Wait(); image.GetPixel(0, 0); read = true; return []; }, releaseBackend: () => freed++);
        using var bitmap = Picture(0); var task = runtime.RecognizeAsync(bitmap, default); Task retirement = Task.CompletedTask;
        try
        {
            Check(entered.Wait(2000), "Recognition backend did not enter.");
            retirement = runtime.DisposeAsync().AsTask();
            Check(!retirement.IsCompleted && freed == 0, "Recognition lost its SKBitmap/native owner.");
        }
        finally { release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(3)); await retirement.WaitAsync(TimeSpan.FromSeconds(3)); }
        Check(read && freed == 1, "Held image was invalidated or released twice.");
    }

    internal static void LocalModelSnapshotDefersRetirement() => LocalModelSnapshotDefersRetirementAsync().GetAwaiter().GetResult();
    private static async Task LocalModelSnapshotDefersRetirementAsync()
    {
        var app = FakeApp(); var fakePath = Path.Combine(Path.GetTempPath(), "clipray-t3-model-never-opened");
        var local = new LocalTranslationProvider(new LocalLmtOptions(fakePath, fakePath));
        Set(app, "_localTranslationProvider", local);
        var settings = Field<AppSettings>(app, "_settings");
        var snapshot = (IDisposable)Invoke(app, "CaptureTranslationServices", TranslationRouteKind.LocalLmt, settings)!;
        Task? retirement = null;
        try
        {
            Invoke(app, "ApplySettings", settings with { LocalLmtModelPath = fakePath + "-new" }, false);
            retirement = Field<Task>(app, "_localProviderRetirement");
            Check(!retirement.IsCompleted, "Settings released provider while admitted task still owned it.");
            // Equivalent-language bypass exercises actual provider admission without model, process or file access.
            var result = await local.TranslateAsync(new("English", "English", "keep this"), default);
            Check(result.IsSuccess && result.Text == "keep this", "Old snapshot stopped using its captured provider.");
            try { Invoke(app, "GetLocalTranslationProvider"); throw new Exception("Overlapping model admitted."); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
        }
        finally { snapshot.Dispose(); await (retirement ?? local.RetireAsync()).WaitAsync(TimeSpan.FromSeconds(3)); }
        await ThrowsDisposed(() => local.TranslateAsync(new("English", "English", "keep this"), default));
    }
    private static async Task OcrServiceClosesWithoutRebuildAsync()
    {
        var entered = NewSignal(); var release = NewSignal(); var disposed = 0; var made = 0;
        var runtime = new FakeOcr(async (bitmap, _) => {
            entered.TrySetResult(); await release.Task; bitmap.GetPixel(0, 0); return [];
        }, () => { disposed++; });
        var service = new OcrService(() => { made++; return runtime; });
        using var picture = Picture(0);
        var recognizing = service.RecognizeAsync(picture, default);
        Task retirement = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            retirement = service.DisposeAsync().AsTask();
            Check(!retirement.IsCompleted && disposed == 0, "OcrService abandoned recognition.");
            await ThrowsDisposed(() => service.WarmupAsync(default));
            await ThrowsDisposed(() => service.RecognizeAsync(picture, default));
        }
        finally { release.TrySetResult(); await recognizing.WaitAsync(TimeSpan.FromSeconds(3)); await retirement.WaitAsync(TimeSpan.FromSeconds(3)); }
        Check(made == 1 && disposed == 1, "Closed OCR service recreated or double-freed runtime.");
        var services = new TranslationRuntimeServices(() => throw new Exception("No cloud creation"), () => throw new Exception("No OCR creation"));
        await services.RetireAsync();
        try { _ = services.Ocr; throw new Exception("Closed services recreated OCR."); } catch (ObjectDisposedException) { }
    }

    internal static void EntryStopAtEveryAwait() => EntryStopAtEveryAwaitAsync().GetAwaiter().GetResult();
    private static async Task EntryStopAtEveryAwaitAsync()
    {
        foreach (var stage in new[] { "selection", "warmup", "capture" })
        {
            var app = FakeApp(); var entered = NewSignal(); var release = NewSignal(); var sessions = 0; var closed = 0;
            app.LiveSelectionFactory = async operation => {
                operation.OwnSelection(() => closed++);
                if (stage == "selection") { entered.TrySetResult(); await release.Task; }
                return new PixelRect(0, 0, 120, 80);
            };
            app.LiveWarmupFactory = async _ => { if (stage == "warmup") { entered.TrySetResult(); await release.Task; } };
            app.LiveCaptureFactory = async (region, token, capture) => {
                if (stage == "capture") { entered.TrySetResult(); await release.Task; }
                return await LiveTranslationScreenCapture.StartForRegionAsync(region, default, capture, () => [region]);
            };
            app.LiveSessionFactory = async (operation, _) => {
                await using var capture = await app.PrepareLiveCaptureAsync(operation, new(0, 0, 120, 80), _ => Picture(0));
                sessions++;
            };
            var running = app.RunLiveTranslationEntryAsync(null);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Invoke(app, "StopLiveTranslation"); Invoke(app, "StopLiveTranslation");
                await running.WaitAsync(TimeSpan.FromSeconds(1));
                Check(Field<LiveTranslationStartupCoordinator>(app, "_liveStartup").Current is null && sessions == 0,
                    "Stop did not revoke " + stage + " owner.");
            }
            finally { release.TrySetResult(); await running; await DrainRegistry(); }
            Check(sessions == 0 && closed == 1, "Late " + stage + " resumed or cleanup repeated.");
        }
    }

    internal static void CloudSkipsMachineWarmup() => CloudSkipsMachineWarmupAsync().GetAwaiter().GetResult();

    internal static void RetainedFrameReachesAppGuard() => RetainedFrameReachesAppGuardAsync().GetAwaiter().GetResult();
    private static async Task RetainedFrameReachesAppGuardAsync()
    {
        var app = FakeApp(); var phase = 0; var applied = NewSignal(); var done = NewSignal();
        LiveTranslationWorkIdentity? original = null; var clears = 0; var retainedApplied = 0;
        using var before = Picture(0); using var after = Picture(0);
        using (var g = Graphics.FromImage(after))
        {
            g.FillRectangle(Brushes.Blue, 60, 4, 50, 8);
            g.FillRectangle(Brushes.Blue, 60, 35, 50, 8);
            g.FillRectangle(Brushes.Blue, 60, 65, 50, 8);
        }
        Check(LiveTranslationChangePlanner.Analyze(new(before), new(after), Frame().SourceRegions).IsViewportReplacement,
            "Fixture did not trigger viewport replacement.");
        app.LiveWarmupFactory = _ => Task.CompletedTask;
        app.LiveSessionFactory = async (owner, _) =>
        {
            using var session = new LiveTranslationSession(new(0,0,120,80), _ => new Bitmap(phase == 0 ? before : after),
                async (_, token) => { if (phase != 0) await Task.Delay(100, token); return Frame(); },
                TranslationRouteKind.Machine, validateLayoutRegions: true);
            Set(app, "_liveTranslationSession", session);
            session.FrameInvalidated += (_, _) => { if (phase != 0) clears++; };
            session.FrameReady += (_, e) => {
                var accepted = app.TryApplyLiveFrame(owner, session, e, () => true);
                Check(accepted, "Current retained display failed the production App guard.");
                if (phase == 0) { original = e.WorkIdentity; phase = 1; applied.TrySetResult(); }
                else if (e.WorkIdentity == original && e.PresentationViewportGeneration != original!.ViewportGeneration)
                {
                    Check(e.Frame.LayoutItems.Count == 2, "Static neighbors were lost.");
                    retainedApplied++; session.Stop(); done.TrySetResult();
                    Check(!app.TryApplyLiveFrame(owner, session, e, () => throw new Exception("Stopped apply ran")), "Stopped frame accepted.");
                }
            };
            try { await session.StartAsync(owner.Token); }
            finally { Set(app, "_liveTranslationSession", null!); }
        };
        var running = app.RunLiveTranslationEntryAsync(new(0,0,120,80));
        try { await applied.Task.WaitAsync(TimeSpan.FromSeconds(2)); await done.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { Invoke(app, "StopLiveTranslation"); await running; await DrainRegistry(); }
        Check(retainedApplied == 1 && clears == 0, "Viewport rejection blanked proven static neighbors.");
    }
    private static async Task CloudSkipsMachineWarmupAsync()
    {
        var app = FakeApp(); var ready = NewSignal(); var stop = NewSignal(); var machineCalls = 0;
        Set(app, "_machineTranslationProvider", new FakeMachine(() => { machineCalls++; return stop.Task; }));
        app.LiveSessionFactory = (_, _) => { ready.TrySetResult(); return Task.CompletedTask; };
        try { await app.RunLiveTranslationEntryAsync(new(0,0,120,80), TranslationRouteKind.DeepSeekFlash).WaitAsync(TimeSpan.FromSeconds(1)); }
        finally { stop.TrySetResult(); await DrainRegistry(); }
        Check(ready.Task.IsCompleted && machineCalls == 0, "Cloud route awaited unrelated machine translation.");
    }

    internal static void SettingsPreserveUnrelatedLiveOwner() => SettingsPreserveUnrelatedLiveOwnerAsync().GetAwaiter().GetResult();
    private static async Task SettingsPreserveUnrelatedLiveOwnerAsync()
    {
        var app = FakeApp(); var entered = NewSignal(); var release = NewSignal(); var hotkeys = 0;
        app.RegisterHotkeysOverride = () => hotkeys++;
        app.LiveWarmupFactory = _ => Task.CompletedTask;
        app.LiveSessionFactory = async (_, _) => { entered.TrySetResult(); await release.Task; };
        var running = app.RunLiveTranslationEntryAsync(new(0, 0, 120, 80));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var coordinator = Field<LiveTranslationStartupCoordinator>(app, "_liveStartup");
            var owner = coordinator.Current; var ocr = Field<OcrService>(app, "_machineOcr");
            var settings = Field<AppSettings>(app, "_settings");
            foreach (var updated in new[] { settings with { CopyToClipboard = false }, settings with { InterfaceLanguage = "en" },
                settings with { Provider = "DeepSeek", ScreenshotDirectory = Path.Combine(Path.GetTempPath(), "clipray-t3-output") } })
            {
                Invoke(app, "ApplySettings", updated, true);
                Check(ReferenceEquals(owner, coordinator.Current) && !owner!.Token.IsCancellationRequested
                    && ReferenceEquals(ocr, Field<OcrService>(app, "_machineOcr")), "Unrelated settings replaced live/native ownership.");
            }
        }
        finally { Invoke(app, "StopLiveTranslation"); release.TrySetResult(); await running; await DrainRegistry(); }
        Check(hotkeys >= 2, "Settings hotkey seam not called.");
    }

    internal static void ManagedExitDecidesBeforeRetirement() => ManagedExitAsync().GetAwaiter().GetResult();
    private static async Task ManagedExitAsync()
    {
        var app = FakeApp(); var entered = NewSignal(); var release = NewSignal(); var freed = 0; var shutdown = 0;
        var ocr = new OcrService(() => new FakeOcr(async (_, _) => { entered.TrySetResult(); await release.Task; return []; }, () => freed++));
        Set(app, "_machineOcr", ocr); using var image = Picture(0);
        var actual = ocr.RecognizeAsync(image, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!await app.RequestManagedShutdownAsync(() => Task.FromResult(false), () => shutdown++), "Cancelled exit finalized.");
            Check(!Field<bool>(app, "_translationClosing") && freed == 0, "Cancelled draft decision retired services.");
            Check(!await app.RequestManagedShutdownAsync(() => Task.FromResult(true), () => shutdown++, TimeSpan.FromMilliseconds(100)), "Held owner was reported drained.");
            Check(shutdown == 0 && freed == 0 && Field<bool>(app, "_translationClosing"), "Pending native was released or shutdown ran early.");
        }
        finally { release.TrySetResult(); await actual.WaitAsync(TimeSpan.FromSeconds(3)); }
        Check(await app.RequestManagedShutdownAsync(finalShutdown: () => shutdown++), "Actual drain did not allow exit retry.");
        Check(shutdown == 1 && freed == 1, "Managed shutdown/release count differs.");

        var failureApp = FakeApp(); var peerReleased = 0;
        var bad = new OcrService(() => new FakeOcr((_, _) => Task.FromResult<IReadOnlyList<OcrWord>>([]),
            () => throw new IOException("fixture disposal failure")));
        var peer = new OcrService(() => new FakeOcr((_, _) => Task.FromResult<IReadOnlyList<OcrWord>>([]), () => peerReleased++));
        await bad.WarmupAsync(default); await peer.WarmupAsync(default);
        Set(failureApp, "_machineOcr", bad);
        var peerServices = new TranslationRuntimeServices(() => throw new Exception("cloud not expected"), () => peer);
        _ = peerServices.Ocr;
        Set(failureApp, "_translationServices", peerServices);
        Check(!await failureApp.RequestManagedShutdownAsync(finalShutdown: () => shutdown++), "Failed disposal allowed final shutdown.");
        var failedDrain = await failureApp.DrainTranslationAsync(TimeSpan.FromSeconds(1));
        Check(failedDrain.Pending == 0 && failedDrain.Failures.Contains("common-ocr:IOException")
            && peerReleased == 1 && shutdown == 1, "Failure was confused with pending or skipped other resources.");
    }

    internal static ClipRay.App.App FakeApp()
    {
        var app = (ClipRay.App.App)RuntimeHelpers.GetUninitializedObject(typeof(ClipRay.App.App));
        Set(app, "_liveStartup", new LiveTranslationStartupCoordinator());
        Set(app, "_overlayTranslationOperations", new UiOperationOwner());
        Set(app, "_translationRetirements", new ResourceRetirementRegistry());
        Set(app, "_translationRequests", new AsyncResourceLifetime());
        Set(app, "_ocrGate", new SemaphoreSlim(1, 1));
        Set(app, "_localProviderOwners", new AsyncResourceLifetime());
        Set(app, "_machineOcr", new OcrService(() => new FakeOcr((_, _) => Task.FromResult<IReadOnlyList<OcrWord>>([]))));
        Set(app, "_translationServices", new TranslationRuntimeServices(() => throw new Exception("No real cloud"),
            () => new OcrService(() => new FakeOcr((_, _) => Task.FromResult<IReadOnlyList<OcrWord>>([])))));
        Set(app, "_settings", new AppSettings { Provider = "DeepSeek", LiveTranslationProvider = "Machine", MachineTranslationFallbackEnabled = false,
            ScreenshotDirectory = Path.GetTempPath(), LongCaptureDirectory = Path.GetTempPath(), RecordingDirectory = Path.GetTempPath() });
        return app;
    }
    internal static T Field<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    internal static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    internal static object? Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static async Task ThrowsDisposed(Func<Task> action)
    { try { await action(); throw new Exception("Closed object accepted new work."); } catch (ObjectDisposedException) { } }
    private sealed class NonPumpingContext : SynchronizationContext { internal int Posts; public override void Post(SendOrPostCallback d, object? state) => Posts++; }
    private sealed class FakeOcr(Func<Bitmap, CancellationToken, Task<IReadOnlyList<OcrWord>>> recognize, Action? dispose = null) : IOcrRuntime, IOcrWarmup, IDisposable
    { public Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap b, CancellationToken t) => recognize(b, t); public Task WarmupAsync(CancellationToken t) => Task.CompletedTask; public void Dispose() => dispose?.Invoke(); }
    private sealed class FakeMachine(Func<Task> work) : IMachineTranslationProvider
    { public async Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken token) { await work(); return []; } }
    private sealed class SyntheticCredentialBackend : ICredentialBackend
    {
        internal readonly List<string> Reads = [];
        public string? Read(string target) { Reads.Add(target); return "synthetic-value-only"; }
        public void Write(string target, string value) => throw new NotSupportedException();
        public void Delete(string target) => throw new NotSupportedException();
    }
    private sealed class SnapshotMachine : IMachineTranslationProvider
    {
        internal int Calls;
        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken token)
        { Calls++; return Task.FromResult<IReadOnlyList<TranslationResult>>(requests.Select(_ => new TranslationResult(true, "phrase traduite", "ok")).ToArray()); }
    }
    private sealed class SnapshotCloud(Func<Task> wait) : IDeepSeekTranslationProvider
    {
        internal string? Name; internal TranslationRequest? Request;
        public async Task<IReadOnlyList<TranslationResult>> TranslateBlocksAsync(IReadOnlyList<TranslationRequest> requests, string name, CancellationToken token)
        { Name = name; Request = requests.Single(); await wait(); return requests.Select(_ => new TranslationResult(false, "", "fixture-failed")).ToArray(); }
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, string name, CancellationToken token) => throw new NotSupportedException();
        public Task<TranslationResult> TestConnectionAsync(string name, string model, CancellationToken token) => throw new NotSupportedException();
        public Task<TranslationResult> TestCandidateConnectionAsync(string key, string model, CancellationToken token) => throw new NotSupportedException();
    }
}

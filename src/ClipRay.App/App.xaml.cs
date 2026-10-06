using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using ClipRay.App.Acceptance;
using ClipRay.App.Capture;
using ClipRay.App.Cleanup;
using ClipRay.App.Diagnostics;
using ClipRay.App.Feedback;
using ClipRay.App.Infrastructure;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.App.Scrolling;
using ClipRay.App.Recording;
using ClipRay.App.Translation;
using Forms = System.Windows.Forms;

namespace ClipRay.App;

public partial class App : System.Windows.Application
{
    private readonly ScreenCaptureService _captureService = new();
    private readonly CaptureFeedbackService _feedbackService = new();
    private readonly CredentialVault _credentialVault = new();
    private ISettingsStore? _settingsStore;
    private SettingsApplyCoordinator? _settingsApplyCoordinator;
    private AppSettings? _settings;
    private string? _settingsLoadWarning;
    private ImageOutputService? _outputService;
    private ImageOutputService? _longCaptureOutputService;
    private ApplicationPathSet? _paths;
    private ITranslationRunLogWriter? _translationRunLogWriter;
    private HotkeyHost? _hotkeyHost;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _applicationIcon;
    private SelectionOverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private TranslationWindow? _translationWindow;
    private LiveTranslationSession? _liveTranslationSession;
    private CancellationTokenSource? _liveTranslationStartup;
    private LiveTranslationEscapeListener? _liveTranslationEscape;
    private LiveTranslationOverlayWindow? _liveTranslationOverlay;
    private LiveTranslationStopControlWindow? _liveTranslationStopControl;
    private bool _liveTranslationSelectionActive;
    private readonly UiOperationOwner _overlayTranslationOperations = new();
    private readonly TranslationRuntimeServices _translationServices;
    private readonly TranslationApplicationOrchestrator _translationOrchestrator;
    private readonly OcrService _machineOcr = OcrService.CreateForCommonMachine();
    private readonly SemaphoreSlim _ocrGate = new(1, 1);
    private readonly TranslationApplicationOrchestrator _machineTranslationOrchestrator;
    private DeepSeekCostLedger? _deepSeekCostLedger;
    private LocalTranslationProvider? _localTranslationProvider;
    private IMachineTranslationProvider? _machineTranslationProvider;
    private Task? _machineTranslationWarmup;
    private CancellationTokenSource? _longCaptureCancellation;
    private ScrollCaptureControlWindow? _longCaptureControls;
    private ScrollCaptureBorderWindow? _longCaptureBorder;
    private ScrollCapturePreviewWindow? _longCapturePreview;
    private HotkeyHost? _longCaptureHotkeyHost;
    private RecordingSession? _recordingSession;
    private bool _recordingStopRequested;
    private RecordingBorderWindow? _recordingBorder;
    private RecordingControlWindow? _recordingControl;
    private RecordingPreviewWindow? _recordingPreview;
    private Forms.ToolStripMenuItem? _stopRecordingMenuItem;
    private Forms.ToolStripMenuItem? _stopLiveTranslationMenuItem;
    private RecordingOutputPaths? _recordingPaths;
    private SingleInstanceCoordinator? _singleInstance;

    public App()
    {
        _translationServices = new TranslationRuntimeServices(
            () => new DeepSeekTranslationProvider(_credentialVault),
            () => new OcrService());
        _translationOrchestrator = new TranslationApplicationOrchestrator(
            RecognizeVisualAsync,
            TranslateVisualRequestsAsync);
        _machineTranslationOrchestrator = new TranslationApplicationOrchestrator(
            RecognizeMachineAsync,
            TranslateVisualRequestsAsync);
    }

    private async Task<OcrRecognitionResult> RecognizeMachineAsync(Bitmap bitmap, CancellationToken token)
    {
        await _ocrGate.WaitAsync(token);
        try { return await _machineOcr.RecognizeAsync(bitmap, token); }
        finally { _ocrGate.Release(); }
    }

    private async Task<OcrRecognitionResult> RecognizeVisualAsync(Bitmap bitmap, CancellationToken token)
    {
        await _ocrGate.WaitAsync(token);
        try { return await _translationServices.Ocr.RecognizeAsync(bitmap, token); }
        finally { _ocrGate.Release(); }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegionAcceptanceCommand? acceptanceCommand = null;
        if (RegionAcceptanceCommand.IsRequested(e.Args)
            && !RegionAcceptanceCommand.TryParse(e.Args, out acceptanceCommand, out _))
        {
            Shutdown(2);
            return;
        }
        if (e.Args.Contains("--uninstall-cleanup", StringComparer.OrdinalIgnoreCase))
        {
            var paths = ApplicationPaths.Create(
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var recycleOutputs = e.Args.Contains("--recycle-outputs", StringComparer.OrdinalIgnoreCase);
            var result = new UninstallCleanupService(
                paths,
                Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClipRay.App.exe"))
                .Execute(recycleOutputs);
            Shutdown(result.FailedOutputCount == 0 ? 0 : 2);
            return;
        }

        if (e.Args.Contains("--configure-install-cleanup", StringComparer.OrdinalIgnoreCase))
        {
            var paths = ApplicationPaths.Create(
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            var installExecutablePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "ClipRay.App.exe");
            Shutdown(InstallerCleanupConfiguration.Execute(paths, installExecutablePath));
            return;
        }

        if (e.Args.Contains("--cleanup-scheduled", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(RunScheduledCleanup());
            return;
        }

        if (e.Args.Contains("--recording-border-probe", StringComparer.OrdinalIgnoreCase))
        {
            var paths = ApplicationPaths.Create(
                AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Dispatcher.BeginInvoke(() => RunRecordingBorderProbe(paths.DataRoot));
            return;
        }

#if DEBUG
        if (e.Args.Contains("--modern-overlay-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => StartOverlayPreview(showModernTools: true));
            return;
        }

        if (e.Args.Contains("--recording-overlay-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => StartOverlayPreview(
                showModernTools: false,
                launchMode: OverlayLaunchMode.Recording));
            return;
        }

        if (e.Args.Contains("--color-tools-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => StartOverlayPreview(showModernTools: false, showColorTools: true));
            return;
        }

        if (e.Args.Contains("--picker-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => StartOverlayPreview(showModernTools: false, showPicker: true));
            return;
        }

        if (e.Args.Contains("--overlay-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => StartOverlayPreview(showModernTools: false));
            return;
        }

        if (e.Args.Contains("--long-capture-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartLongCapturePreview);
            return;
        }

        if (e.Args.Contains("--long-capture-live-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartLongCaptureLivePreview);
            return;
        }

        if (e.Args.Contains("--long-capture-runtime-probe", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartLongCaptureRuntimeProbe);
            return;
        }

        if (e.Args.Contains("--recording-preview-probe", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartRecordingPreviewProbe);
            return;
        }

        if (e.Args.Contains("--translation-focus-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartTranslationFocusPreview);
            return;
        }
#endif

        var startupPaths = ApplicationPaths.Create(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (acceptanceCommand is null && !TryOwnSingleInstance(e.Args, startupPaths.ProfileId)) return;

        _paths = startupPaths;
        _translationRunLogWriter = new JsonlTranslationRunLogWriter(_paths.DiagnosticsRoot);
        _ = ScrollCaptureTempStore.CleanupOrphans(_paths.LongCaptureTempRoot);
        var settingsPath = Path.Combine(_paths.DataRoot, "settings.json");
        _deepSeekCostLedger = new DeepSeekCostLedger(Path.Combine(
            Path.GetDirectoryName(settingsPath)!,
            "deepseek-cost-summary.json"));
        _settingsStore = new SettingsStore(settingsPath, _paths.InstallRoot);
        var loadResult = _settingsStore.LoadRecoverable();
        if (_credentialVault.MigrateSelectedDeepSeekCredential(loadResult.Settings.CredentialName))
        {
            loadResult = loadResult with
            {
                Settings = loadResult.Settings with { CredentialName = CredentialVault.FixedDeepSeekSlot },
                RequiresSave = true,
            };
        }
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定 ClipRay 可执行文件路径。");
        var startupEnabled = StartupRegistration.IsEnabled(executablePath);
        if (loadResult.Settings.StartWithWindows != startupEnabled)
        {
            loadResult = loadResult with
            {
                Settings = loadResult.Settings with { StartWithWindows = startupEnabled },
                RequiresSave = true,
            };
        }
        if (loadResult.RequiresSave) _settingsStore.Save(loadResult.Settings);
        var cleanupScheduleRegistration = new WindowsCleanupScheduleRegistration();
        var cleanupRepairWarning = ExternalSettingsReconciler.RepairCleanupSchedule(
            loadResult.Settings,
            cleanupScheduleRegistration,
            executablePath);
        _settingsLoadWarning = string.Join(
            " ",
            new[] { loadResult.Warning, cleanupRepairWarning }.Where(static warning => !string.IsNullOrWhiteSpace(warning)));
        if (string.IsNullOrWhiteSpace(_settingsLoadWarning)) _settingsLoadWarning = null;
        OutputDirectoryLayout.Ensure(loadResult.Settings);
        _settingsApplyCoordinator = new SettingsApplyCoordinator(
            _settingsStore,
            new WindowsStartupRegistration(),
            cleanupScheduleRegistration,
            executablePath);
        ApplySettings(loadResult.Settings, registerHotkeys: false);
        if (acceptanceCommand is not null)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                var exitCode = await RunRegionAcceptanceAsync(acceptanceCommand);
                Shutdown(exitCode);
            });
            return;
        }
        CreateTrayIcon();
        RegisterHotkeys();
        _ = _translationServices.Ocr.WarmupAsync(CancellationToken.None);

#if DEBUG
        if (e.Args.Contains("--settings-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartSettingsPreview);
            return;
        }

        if (e.Args.Contains("--settings-signature-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartSettingsSignaturePreview);
            return;
        }

        if (e.Args.Contains("--translation-window-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartTranslationWindowPreview);
            return;
        }

        if (e.Args.Contains("--tray-menu-preview", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(StartTrayMenuPreview);
            return;
        }
#endif

        if (e.Args.Contains("--interactive", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => StartInteractiveCapture());
        }
        else if (e.Args.Contains("--open-settings", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(OpenSettings);
        }
        else if (e.Args.Contains("--quick-current", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(async () =>
            {
                await RunCurrentMonitorCaptureAsync();
                // 本地一次性验收入口需等待最长 330 ms 的失败双闪完成后再退出。
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                Shutdown();
            });
        }
    }

    private bool TryOwnSingleInstance(string[] arguments, string profileId)
    {
#if DEBUG
        const bool isDebugBuild = true;
#else
        const bool isDebugBuild = false;
#endif
        if (AppActivationPolicy.ShouldBypassSingleInstance(arguments, isDebugBuild)) return true;

        _singleInstance = new SingleInstanceCoordinator(AppActivationPolicy.CurrentUserScope(profileId));
        _singleInstance.ActivationRequested += command =>
            Dispatcher.BeginInvoke(() => HandleActivation(command));
        var result = _singleInstance.Start(AppActivationPolicy.ForwardedCommand(arguments));
        if (result == SingleInstanceStartResult.Primary) return true;

        _singleInstance.Dispose();
        _singleInstance = null;
        Shutdown();
        return false;
    }

    private async Task<int> RunRegionAcceptanceAsync(RegionAcceptanceCommand command)
    {
        Directory.CreateDirectory(command.OutputRoot);
        var output = new ImageOutputService(
            command.OutputRoot,
            copyToClipboard: false,
            command.Mode == RegionAcceptanceMode.Long
                ? OutputArtifactKind.LongCapture
                : OutputArtifactKind.Screenshot);
        string? outputPath = null;
        var acceptedFrameCount = 0;
        var outputSize = System.Drawing.Size.Empty;
        var status = "completed";
        var errorCategory = "none";
        try
        {
            if (command.ForegroundProcessId is int foregroundProcessId)
                AcceptanceForegroundWindow.EnsureForeground(foregroundProcessId);
            switch (command.Mode)
            {
                case RegionAcceptanceMode.Original:
                {
                    using var frame = _captureService.CaptureRegion(command.Region);
                    outputPath = output.SaveAndCopy(frame.Bitmap);
                    outputSize = frame.Bitmap.Size;
                    acceptedFrameCount = 1;
                    break;
                }
                case RegionAcceptanceMode.Translated:
                {
                    using var frame = _captureService.CaptureRegion(command.Region);
                    var outcome = await TranslateAcceptanceFrameAsync(frame.Bitmap, command.Route, CancellationToken.None, command.OutputRoot);
                    using var rendered = TranslatedSelectionRenderer.Render(
                        frame.Bitmap,
                        new ClipRay.Core.Geometry.PixelRect(0, 0, frame.Bitmap.Width, frame.Bitmap.Height),
                        outcome.LayoutItems);
                    outputPath = output.SaveAndCopy(rendered);
                    outputSize = rendered.Size;
                    acceptedFrameCount = 1;
                    status = outcome.Status;
                    errorCategory = TranslationAcceptancePolicy.ErrorCategory(outcome);
                    break;
                }
                case RegionAcceptanceMode.Long:
                {
                    var target = new System.Drawing.Point(
                        command.Region.Left + command.Region.Width / 2,
                        command.Region.Top + command.Region.Height / 2);
                    var diagnostics = new LongCaptureRunDiagnostics(
                        Guid.NewGuid().ToString("N"),
                        typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
                    using var session = new ScrollCaptureSession(
                        command.Region,
                        ScrollCaptureMode.Automatic,
                        new ScreenRegionCapture(_captureService),
                        new ScrollInputService(ScrollInputService.ResolveTarget(target)),
                        new DelayScrollSettler(),
                        Path.Combine(command.OutputRoot, "temp"),
                        new ScrollCaptureOptions(
                            MaximumFrames: command.Iterations,
                            SettleDelay: TimeSpan.FromMilliseconds(350),
                            MaximumPolls: Math.Max(12, command.Iterations * 6),
                            ConsecutiveRepeatedFramesToStop: 3),
                        diagnostics: diagnostics);
                    using var result = await session.RunAsync(CancellationToken.None);
                    using var composite = result.CreateComposite();
                    outputPath = output.SaveAndCopy(composite, diagnostics);
                    outputSize = composite.Size;
                    acceptedFrameCount = result.AcceptedFrameCount;
                    status = result.StopReason.ToString();
                    LongCaptureRunDiagnosticStore.TryWrite(command.OutputRoot, diagnostics);
                    result.CleanupAfterSuccessfulOutput();
                    break;
                }
                case RegionAcceptanceMode.Live:
                {
                    LiveTranslationFrame? latest = null;
                    var delayCount = 0;
                    var readyCount = 0;
                    var readySignaled = 0;
                    var firstPresented = new TaskCompletionSource<LiveTranslationFrame>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var presentationTasks = new List<Task>();
                    var readyPath = Path.Combine(command.OutputRoot, "live-overlay-ready.json");
                    File.Delete(readyPath);
                    if (command.ForegroundProcessId is not int sourceProcessId)
                        throw new InvalidOperationException("live-acceptance-source-window-required");
                    await using var sourceCapture = await LiveTranslationWindowCapture.StartAsync(
                        sourceProcessId,
                        command.Region,
                        CancellationToken.None,
                        region =>
                        {
                            using var frame = _captureService!.CaptureRegion(region);
                            return new Bitmap(frame.Bitmap);
                        },
                        preferNativeWindowCapture: _settings!.ShowLiveTranslationInRecordings);
                    var acceptanceOverlay = new LiveTranslationOverlayWindow(
                        command.Region,
                        LiveTranslationCaptureVisibilityPolicy.SelectedRegionBorderVisible,
                        _settings!.ShowLiveTranslationInRecordings);
                    using var stop = new CancellationTokenSource();
                    var sourceReleased = false;
                    var foregroundMonitor = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100),
                    };
                    try
                    {
                        acceptanceOverlay.Show();
                        if (!LiveTranslationCaptureVisibilityPolicy.CanStart(
                            acceptanceOverlay.ExcludeFromCaptureRequested,
                            acceptanceOverlay.DisplayAffinityApplied))
                            throw new InvalidOperationException("live-overlay-recording-exclusion-unavailable");
                        foregroundMonitor.Tick += (_, _) =>
                        {
                            if (sourceReleased || AcceptanceForegroundWindow.IsForeground(sourceProcessId)) return;
                            sourceReleased = true;
                            acceptanceOverlay.Clear();
                            stop.Cancel();
                            foregroundMonitor.Stop();
                        };
                        foregroundMonitor.Start();
                        using var session = new LiveTranslationSession(
                            command.Region,
                            region =>
                            {
                                AcceptanceForegroundWindow.EnsureForeground(sourceProcessId);
                                return sourceCapture.Capture(region);
                            },
                            async (bitmap, token) =>
                            {
                                var translated = await TranslateAcceptanceFrameAsync(
                                    bitmap, command.Route, token, command.OutputRoot);
                                return LiveTranslationFrame.FromOutcome(command.Route, translated);
                            },
                            command.Route,
                            async (_, token) =>
                            {
                                delayCount++;
                                if (delayCount >= command.Iterations)
                                {
                                    stop.Cancel();
                                    token.ThrowIfCancellationRequested();
                                }
                                await Task.Delay(TimeSpan.FromMilliseconds(350), token);
                            },
                            validateLayoutRegions: true,
                            verifySource: command.Route == TranslationRouteKind.Machine
                                ? RecognizeMachineAsync : RecognizeVisualAsync);
                        session.FrameReady += (_, args) =>
                        {
                            var task = PresentAcceptanceFrameAsync(args);
                            lock (presentationTasks) presentationTasks.Add(task);
                        };
                        async Task PresentAcceptanceFrameAsync(LiveTranslationFrameReadyEventArgs args)
                        {
                            var applied = await Dispatcher.InvokeAsync(() =>
                                !sourceReleased && acceptanceOverlay.Apply(args.Frame, args.Generation));
                            if (!applied) return;
                            if (!await acceptanceOverlay.AcknowledgePresentationAsync(args.Generation))
                                throw new InvalidOperationException("live-overlay-render-cycle-timeout");
                            if (sourceReleased) return;
                            latest = args.Frame;
                            firstPresented.TrySetResult(args.Frame);
                            Interlocked.Increment(ref readyCount);
                            if (Interlocked.Exchange(ref readySignaled, 1) == 0)
                            {
                                var temporaryReadyPath = readyPath + ".tmp";
                                File.WriteAllText(temporaryReadyPath, JsonSerializer.Serialize(new
                                {
                                    Status = args.Frame.Status,
                                    ResultState = args.Frame.ResultState.ToString(),
                                    LayoutCount = args.Frame.LayoutItems.Count,
                                    CaptureId = args.WorkIdentity?.CaptureId,
                                    PipelineId = args.WorkIdentity?.PipelineId,
                                    ViewportGeneration = args.WorkIdentity?.ViewportGeneration,
                                    ShowInRecordings = _settings.ShowLiveTranslationInRecordings,
                                }, new JsonSerializerOptions { WriteIndented = true }));
                                File.Move(temporaryReadyPath, readyPath, overwrite: true);
                            }
                        }
                        session.FrameInvalidated += (_, _) => acceptanceOverlay.Clear();
                        await session.StartAsync(stop.Token);
                        Task[] pendingPresentations;
                        lock (presentationTasks) pendingPresentations = presentationTasks.ToArray();
                        try { await Task.WhenAll(pendingPresentations); }
                        catch when (sourceReleased) { /* The owner ended the visible session. */ }
                        if (sourceReleased)
                            throw new RegionAcceptanceFailureException("aborted", "source-window-released");
                        if (latest is null)
                        {
                            try { latest = await firstPresented.Task.WaitAsync(TimeSpan.FromSeconds(1)); }
                            catch (TimeoutException) { }
                        }
                        if (latest is null) throw new InvalidOperationException("live-translation-produced-no-frame");
                        if (!AcceptanceForegroundWindow.IsForeground(sourceProcessId))
                            throw new RegionAcceptanceFailureException("aborted", "source-window-released");
                        using var finalBitmap = sourceCapture.Capture(command.Region);
                        using (var rendered = TranslatedSelectionRenderer.Render(
                            finalBitmap,
                            new ClipRay.Core.Geometry.PixelRect(0, 0, finalBitmap.Width, finalBitmap.Height),
                            latest.LayoutItems))
                        {
                            outputPath = output.SaveAndCopy(rendered);
                            outputSize = rendered.Size;
                        }
                        acceptedFrameCount = readyCount;
                        status = latest.Status;
                        errorCategory = TranslationAcceptancePolicy.LiveErrorCategory(session, latest);
                        if (session.State == LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
                        {
                            status = "live-stopped-after-failures";
                            errorCategory = "Live:consecutive-failures";
                        }
                        // Keep the proven-ready overlay visible long enough for an
                        // external recorder to start after observing the marker.
                        for (var step = 0; step < 300 && !sourceReleased; step++)
                            await Task.Delay(TimeSpan.FromMilliseconds(100));
                        if (sourceReleased)
                            throw new RegionAcceptanceFailureException("aborted", "source-window-released");
                    }
                    finally
                    {
                        foregroundMonitor.Stop();
                        acceptanceOverlay.Close();
                    }
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            status = "cancelled";
            errorCategory = "cancelled";
        }
        catch (RegionAcceptanceFailureException failure)
        {
            status = failure.Status;
            errorCategory = failure.Category;
        }
        catch (Exception exception)
        {
            status = "failed";
            errorCategory = $"{exception.GetType().Name}:{exception.TargetSite?.Name ?? "unknown"}";
        }

        var resultManifest = new RegionAcceptanceResult(
            RegionAcceptanceResult.CurrentSchemaVersion,
            command.Mode.ToString(),
            command.Route.ToString(),
            command.Region.Left,
            command.Region.Top,
            command.Region.Width,
            command.Region.Height,
            command.Iterations,
            acceptedFrameCount,
            outputSize.Width,
            outputSize.Height,
            outputPath is null ? null : Path.GetFileName(outputPath),
            status,
            errorCategory);
        WriteRegionAcceptanceResult(command.OutputRoot, resultManifest);
        return errorCategory == "none" ? 0 : 2;
    }

    private async Task<ApplicationTranslationOutcome> TranslateAcceptanceFrameAsync(
        Bitmap bitmap,
        TranslationRouteKind route,
        CancellationToken cancellationToken,
        string? diagnosticsRoot = null,
        bool requireDisplayable = true)
    {
        if (diagnosticsRoot is not null)
            bitmap.Save(Path.Combine(diagnosticsRoot, "acceptance-source.png"), System.Drawing.Imaging.ImageFormat.Png);
        TranslationPipelineTelemetry? timing = null;
        var orchestrator = route == TranslationRouteKind.Machine ? _machineTranslationOrchestrator : _translationOrchestrator;
        var outcome = await orchestrator.ExecuteAsync(
            bitmap,
            route,
            _settings!.TranslationTargetLanguage,
            static blocks => blocks,
            progress: null,
            telemetry: value => timing = value,
            cancellationToken);
        if (diagnosticsRoot is not null)
            File.WriteAllText(Path.Combine(diagnosticsRoot, "acceptance-diagnostics.json"),
                JsonSerializer.Serialize(new
                {
                    outcome.Status,
                    outcome.FailureStage,
                    OcrBlockCount = outcome.OcrBlocks.Count,
                    OcrGeometry = outcome.OcrBlocks.Select(static block => new
                    {
                        block.Bounds,
                        WordCount = block.Words.Count,
                        TextLength = block.Text.Length,
                    }),
                    RegionCount = outcome.Regions.Count,
                    RegionGeometry = outcome.Regions.Select(static region => new
                    {
                        region.Bounds,
                        region.Role,
                        LineCount = region.Lines.Count,
                    }),
                    TranslationCount = outcome.Results.Count,
                    TranslationResults = outcome.Results.Select(static result => new
                    {
                        result.IsSuccess,
                        result.Status,
                        DiagnosticKind = result.Diagnostic?.Kind.ToString(),
                    }),
                    LayoutCount = outcome.LayoutItems.Count,
                    outcome.LayoutRejections,
                    Timing = timing,
                }, new JsonSerializerOptions { WriteIndented = true }));
        if (requireDisplayable && !TranslationAcceptancePolicy.HasDisplayableResult(outcome))
        {
            throw new RegionAcceptanceFailureException(
                outcome.Status,
                TranslationAcceptancePolicy.ErrorCategory(outcome));
        }
        return outcome;
    }

    private sealed class RegionAcceptanceFailureException(string status, string category) : Exception
    {
        public string Status { get; } = status;
        public string Category { get; } = category;
    }

    private static void WriteRegionAcceptanceResult(string outputRoot, RegionAcceptanceResult result)
    {
        var path = Path.Combine(outputRoot, "acceptance-result.json");
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private void HandleActivation(AppActivationCommand command)
    {
        switch (command)
        {
            case AppActivationCommand.OpenSettings:
                OpenSettings();
                break;
            case AppActivationCommand.StartInteractiveCapture:
                StartInteractiveCapture();
                break;
            case AppActivationCommand.CaptureCurrentScreen:
                _ = RunCurrentMonitorCaptureIncludingTaskbarAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private static int RunScheduledCleanup()
    {
        var paths = ApplicationPaths.Create(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var statusStore = new CleanupRunStatusStore(Path.Combine(paths.DataRoot, "cleanup-status.json"));
        try
        {
            var settingsPath = Path.Combine(paths.DataRoot, "settings.json");
            var settings = new SettingsStore(settingsPath, paths.InstallRoot).Load();
            var plan = CleanupPlan.FromSettings(settings, DateTimeOffset.Now);
            if (plan is null)
            {
                statusStore.Write(new CleanupRunStatus(DateTimeOffset.UtcNow, true, 0, 0, 0));
                return 0;
            }
            var result = new CleanupService().Execute(plan);
            statusStore.Write(new CleanupRunStatus(
                DateTimeOffset.UtcNow,
                result.FailedPathCount == 0,
                result.DeletedFileCount,
                result.SkippedPathCount,
                result.FailedPathCount));
            return result.FailedPathCount == 0 ? 0 : 2;
        }
        catch
        {
            try
            {
                statusStore.Write(new CleanupRunStatus(DateTimeOffset.UtcNow, false, 0, 0, 1));
            }
            catch
            {
                // The scheduled process still returns failure if even status persistence is unavailable.
            }
            return 1;
        }
    }

    private void RunRecordingBorderProbe(string dataRoot)
    {
        RecordingBorderWindow? border = null;
        try
        {
            var screen = Forms.Screen.PrimaryScreen
                ?? throw new InvalidOperationException("No primary screen is available.");
            var bounds = screen.Bounds;
            var width = Math.Max(64, Math.Min(640, bounds.Width / 2));
            var height = Math.Max(64, Math.Min(360, bounds.Height / 2));
            var region = new Rectangle(
                bounds.Left + (bounds.Width - width) / 2,
                bounds.Top + (bounds.Height - height) / 2,
                width,
                height);
            border = new RecordingBorderWindow(region);
            border.Show();
            RecordingBorderProbeStore.Write(dataRoot, border.Diagnostics);
        }
        catch (Exception exception)
        {
            RecordingBorderProbeStore.Write(
                dataRoot,
                RecordingBorderDiagnostics.NotStarted with { ProbeError = exception.GetType().Name });
        }
        finally
        {
            border?.Close();
            Shutdown();
        }
    }

    private async void StartOverlayPreview(
        bool showModernTools,
        bool showColorTools = false,
        bool showPicker = false,
        OverlayLaunchMode launchMode = OverlayLaunchMode.Capture)
    {
        CapturedFrame? frame = null;
        try
        {
            frame = _captureService.CaptureCurrentMonitor();
            var initialSelection = CreateOverlayPreviewSelection(frame);
            _overlay = new SelectionOverlayWindow(
                frame,
                initialSelection,
                showPreviewHint: true,
                interactionSettings: _settings,
                launchMode: launchMode);
            frame = null;
            _overlay.Closed += (_, _) => _overlay = null;
            _overlay.Show();
            _overlay.Activate();
            if (showModernTools) _overlay.ShowModernVisualPreview();
            if (showColorTools) _overlay.ShowColorVisualPreview();
            if (showPicker) _overlay.ShowPickerVisualPreview();
            if (showModernTools)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(450));
                using var evidence = _captureService.CaptureCurrentMonitor();
                var outputDirectory = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "screenshots");
                Directory.CreateDirectory(outputDirectory);
                evidence.Bitmap.Save(
                    Path.Combine(outputDirectory, $"modern-overlay-{DateTime.Now:yyyyMMdd-HHmmssfff}.png"),
                    System.Drawing.Imaging.ImageFormat.Png);
            }
            await Task.Delay(TimeSpan.FromSeconds(15));
            _overlay?.Close();
        }
        catch
        {
            _overlay?.Close();
            _overlay = null;
        }
        finally
        {
            frame?.Dispose();
            Shutdown();
        }
    }

    private async void StartSettingsPreview()
    {
        OpenSettings();
        _settingsWindow?.ShowLocalProviderPreview();
        await Task.Delay(TimeSpan.FromSeconds(15));
        if (_settingsWindow is { } settingsWindow)
        {
            settingsWindow.DiscardDraftOnClose = true;
            settingsWindow.Close();
        }
        Shutdown();
    }

    private async void StartSettingsSignaturePreview()
    {
        OpenSettings();
        _settingsWindow?.ShowSaveSuccessPreview();
        await Task.Delay(TimeSpan.FromMilliseconds(450));
        if (_settingsWindow is not null) SaveWindowVisual(_settingsWindow, "settings-signature");
        _settingsWindow?.Close();
        Shutdown();
    }

    private async void StartTranslationWindowPreview()
    {
        OpenTranslationWindow();
        await Task.Delay(TimeSpan.FromMilliseconds(450));
        if (_translationWindow is not null) SaveWindowVisual(_translationWindow, "translation-window");
        _translationWindow?.Close();
        Shutdown();
    }

    private static void SaveWindowVisual(System.Windows.Window window, string prefix)
    {
        window.UpdateLayout();
        var source = PresentationSource.FromVisual(window)?.CompositionTarget;
        var scale = source?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * scale.M11));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * scale.M22));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96 * scale.M11, 96 * scale.M22, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var outputDirectory = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "screenshots");
        Directory.CreateDirectory(outputDirectory);
        using var stream = File.Create(Path.Combine(outputDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmssfff}.png"));
        encoder.Save(stream);
    }

    private async void StartTrayMenuPreview()
    {
        var screen = Forms.Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary screen is available.");
        var point = new System.Drawing.Point(
            screen.WorkingArea.Left + (screen.WorkingArea.Width / 2),
            screen.WorkingArea.Top + (screen.WorkingArea.Height / 2));
        _trayIcon?.ContextMenuStrip?.Show(point);
        await Task.Delay(TimeSpan.FromMilliseconds(450));
        using var evidence = _captureService.CaptureCurrentMonitor();
        var outputDirectory = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "screenshots");
        Directory.CreateDirectory(outputDirectory);
        evidence.Bitmap.Save(
            Path.Combine(outputDirectory, $"tray-menu-{DateTime.Now:yyyyMMdd-HHmmssfff}.png"),
            System.Drawing.Imaging.ImageFormat.Png);
        _trayIcon?.ContextMenuStrip?.Close();
        Shutdown();
    }

    private async void StartTranslationFocusPreview()
    {
        CapturedFrame? frame = null;
        try
        {
            frame = _captureService.CaptureCurrentMonitor();
            var selection = new ClipRay.Core.Geometry.PixelRect(
                (frame.Bitmap.Width - 280) / 2d,
                (frame.Bitmap.Height - 84) / 2d,
                280,
                84);
            _overlay = new SelectionOverlayWindow(frame, selection, showPreviewHint: true, interactionSettings: _settings);
            frame = null;
            _overlay.Closed += (_, _) => _overlay = null;
            _overlay.Show();
            _overlay.Activate();
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            _overlay.ShowTranslationFocusVisualPreview();
            _overlay.ShowTranslationStatus("翻译完成 · 本地 LMT · 已替换 29 处，保留原文 33 处");
            _overlay.ShowTranslationRouteMenuVisualPreview();
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            using var evidence = _captureService.CaptureCurrentMonitor();
            var outputDirectory = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "screenshots");
            Directory.CreateDirectory(outputDirectory);
            evidence.Bitmap.Save(
                Path.Combine(outputDirectory, $"translation-focus-{DateTime.Now:yyyyMMdd-HHmmssfff}.png"),
                System.Drawing.Imaging.ImageFormat.Png);
            _overlay.CloseTranslationRouteMenuVisualPreview();
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
        finally
        {
            frame?.Dispose();
            _overlay?.Close();
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _overlay?.Close();
        LongCaptureLifetime.Teardown(_longCaptureCancellation, _longCaptureControls);
        _longCaptureCancellation = null;
        _longCaptureControls = null;
        _longCaptureBorder?.Close();
        _longCaptureBorder = null;
        _longCapturePreview?.Close();
        _longCapturePreview = null;
        _longCaptureHotkeyHost?.Dispose();
        _longCaptureHotkeyHost = null;
        _settingsWindow?.Close();
        _translationWindow?.Close();
        _overlayTranslationOperations.Close();
        StopLiveTranslation();
        (_machineTranslationProvider as IDisposable)?.Dispose();
        _localTranslationProvider?.Dispose();
        _recordingBorder?.Close();
        _recordingControl?.Close();
        _recordingPreview?.Close();
        ShutdownSafety.Try(() => _recordingSession?.StopAsync().GetAwaiter().GetResult());
        _recordingSession = null;
        _feedbackService.Dispose();
        _translationServices.Dispose();
        _machineOcr.Dispose();
        _hotkeyHost?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _applicationIcon?.Dispose();

        base.OnExit(e);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        TrayMenuTheme.Apply(menu);
        menu.Items.Add("区域截图", null, (_, _) => StartInteractiveCapture());
        menu.Items.Add("全屏截图（含任务栏）", null, (_, _) =>
            _ = RunCurrentMonitorCaptureIncludingTaskbarAsync());
        menu.Items.Add("全屏截图（不含任务栏）", null, (_, _) =>
            _ = RunCurrentMonitorCaptureAsync());
        menu.Items.Add("截取全部屏幕", null, (_, _) =>
            RunCapture(_captureService.CaptureVirtualDesktop, AllMonitorBounds));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("翻译", null, (_, _) => OpenTranslationWindow());
        menu.Items.Add("实时区域翻译", null, (_, _) => StartLiveTranslationSelection());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        _stopLiveTranslationMenuItem = new Forms.ToolStripMenuItem("停止实时翻译", null, (_, _) => StopLiveTranslation()) { Enabled = false };
        menu.Items.Add(_stopLiveTranslationMenuItem);
        _stopRecordingMenuItem = new Forms.ToolStripMenuItem("停止录屏", null, (_, _) => StopRecording()) { Enabled = false };
        menu.Items.Add(_stopRecordingMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 ClipRay", null, (_, _) => Shutdown());

        _applicationIcon = ApplicationIconLoader.LoadOrDefault(
            Path.Combine(AppContext.BaseDirectory, "Assets", "ClipRay-v2.ico"));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "ClipRay 截光",
            ContextMenuStrip = menu,
            Visible = true,
        };
        AppInterfaceLanguage.UpdateTray(menu.Items, _settings!.InterfaceLanguage);
        _trayIcon.DoubleClick += (_, _) => StartInteractiveCapture();
    }

    private void RegisterHotkeys()
    {
        _hotkeyHost?.Dispose();
        _hotkeyHost = new HotkeyHost();
        var failures = new List<string>();
        RegisterAll(_settings!.RegionCaptureHotkey, _settings.RegionCaptureSecondaryHotkey, () => StartInteractiveCapture(), failures);
        RegisterAll(_settings.CurrentScreenHotkey, _settings.CurrentScreenSecondaryHotkey, () =>
            _ = RunCurrentMonitorCaptureAsync(), failures);
        RegisterAll(_settings.TaskbarFreeCurrentScreenHotkey, _settings.TaskbarFreeCurrentScreenSecondaryHotkey, () =>
            _ = RunCurrentMonitorCaptureIncludingTaskbarAsync(), failures);
        RegisterAll(_settings.VirtualDesktopHotkey, _settings.VirtualDesktopSecondaryHotkey, () =>
            RunCapture(_captureService.CaptureVirtualDesktop, AllMonitorBounds), failures);
        RegisterAll(_settings.StartRecordingHotkey, _settings.StartRecordingSecondaryHotkey,
            StartRecordingSelection, failures);
        RegisterAll(_settings.StopRecordingHotkey, _settings.StopRecordingSecondaryHotkey, StopRecording, failures);
        RegisterAll(_settings.StartLiveTranslationHotkey, _settings.StartLiveTranslationSecondaryHotkey,
            () => StartLiveTranslationSelection(), failures);
        RegisterAll(_settings.StopLiveTranslationHotkey, _settings.StopLiveTranslationSecondaryHotkey,
            StopLiveTranslation, failures);

        if (failures.Count > 0)
        {
            SetTrayTooltip(AppUi($"ClipRay | 快捷键冲突: {string.Join(", ", failures)}",
                $"ClipRay | Shortcut conflict: {string.Join(", ", failures)}"));
        }
        else
        {
            SetTrayTooltip(AppUi("ClipRay | 快捷键就绪", "ClipRay | Shortcuts ready"));
        }
    }

    private void Register(GlobalHotkey hotkey, string label, Action action, ICollection<string> failures)
    {
        if (!_hotkeyHost!.TryRegister(hotkey, action))
        {
            failures.Add(label);
        }
    }

    private void RegisterAll(
        GlobalHotkey primary,
        GlobalHotkey? secondary,
        Action action,
        ICollection<string> failures)
    {
        foreach (var hotkey in HotkeyBindingPolicy.Active(primary, secondary))
        {
            Register(hotkey, HotkeyDisplayFormatter.Format(hotkey), action, failures);
        }
    }

    private void StartInteractiveCapture(OverlayLaunchMode launchMode = OverlayLaunchMode.Capture)
    {
        if (InteractiveCapturePolicy.Resolve(_overlay is not null) is InteractiveCaptureAction.CancelExisting)
        {
            _overlay!.Close();
            return;
        }

        CapturedFrame? frame = null;
        try
        {
            frame = _captureService.CaptureVirtualDesktop();
            _overlay = new SelectionOverlayWindow(frame, interactionSettings: _settings, launchMode: launchMode);
            frame = null;
            _overlay.CaptureDelivery = HandleConfirmedCapture;
            _overlay.TranslationRequested += HandleOverlayTranslationRequested;
            _overlay.LiveTranslationRequested += (_, request) => Dispatcher.BeginInvoke(() => StartLiveTranslationRequest(request));
            _overlay.TextExtractionRequested += HandleTextExtractionRequested;
            _overlay.LongCaptureRequested += HandleLongCaptureRequested;
            _overlay.LongCaptureStopRequested += (_, _) => _longCaptureCancellation?.Cancel();
            _overlay.RecordingRequested += HandleRecordingRequested;
            _overlay.Closed += (_, _) =>
            {
                _longCaptureCancellation?.Cancel();
                _overlay = null;
            };
            _overlay.Show();
            _overlay.Activate();
        }
        catch
        {
            frame?.Dispose();
            _overlay?.Close();
            _overlay = null;
        }
    }

    private void StartRecordingSelection()
    {
        if (!RecordingStartPolicy.CanOpenSelection(_recordingSession is not null))
        {
            SetTrayTooltip(AppUi("ClipRay | 录屏已在进行中", "ClipRay | Recording is already running"));
            return;
        }

        StartInteractiveCapture(OverlayLaunchMode.Recording);
    }

    private async void HandleLongCaptureRequested(object? sender, LongCaptureRequest request)
    {
        if (sender is not SelectionOverlayWindow overlay) return;
        _longCaptureCancellation?.Cancel();
        _longCaptureCancellation?.Dispose();
        _longCaptureCancellation = CancellationTokenSource.CreateLinkedTokenSource(overlay.LifetimeToken);
        var cancellation = _longCaptureCancellation;
        var discard = false;
        overlay.ShowLongCaptureProgress(0, request.Mode == ScrollCaptureMode.Manual ? "手动滚动捕获" : "自动滚动捕获");
        await Dispatcher.InvokeAsync(overlay.Hide);
        var scrollTarget = ScrollInputService.ResolveTarget(new System.Drawing.Point(
            request.PhysicalPixelRegion.Left + request.PhysicalPixelRegion.Width / 2,
            request.PhysicalPixelRegion.Top + request.PhysicalPixelRegion.Height / 2));
        var tempRoot = _paths!.LongCaptureTempRoot;
        var longCaptureDiagnostics = new LongCaptureRunDiagnostics(
            Guid.NewGuid().ToString("N"),
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
        using var session = new ScrollCaptureSession(
            request.PhysicalPixelRegion,
            request.Mode,
            new ScreenRegionCapture(_captureService),
            new ScrollInputService(scrollTarget),
            new DelayScrollSettler(),
            tempRoot,
            diagnostics: longCaptureDiagnostics);
        _longCaptureBorder = new ScrollCaptureBorderWindow(request.PhysicalPixelRegion);
        _longCaptureBorder.Show();
        _longCapturePreview = new ScrollCapturePreviewWindow(request.PhysicalPixelRegion);
        _longCaptureControls = new ScrollCaptureControlWindow(
            _settings!.StopLongCaptureHotkey,
            request.Mode,
            secondaryStopHotkey: _settings.StopLongCaptureSecondaryHotkey,
            interfaceLanguage: _settings.InterfaceLanguage);
        void CompleteCapture()
        {
            session.RequestCompletion();
            TryCancel(cancellation);
        }
        _longCaptureControls.StopRequested += (_, _) => CompleteCapture();
        _longCaptureControls.CancelRequested += (_, _) =>
        {
            discard = true;
            TryCancel(cancellation);
        };
        _longCaptureControls.AutomaticScrollingChanged += (_, enabled) => session.SetAutomaticScrolling(enabled);
        _longCaptureHotkeyHost?.Dispose();
        _longCaptureHotkeyHost = new HotkeyHost();
        foreach (var hotkey in HotkeyBindingPolicy.Active(
                     _settings.StopLongCaptureHotkey,
                     _settings.StopLongCaptureSecondaryHotkey))
        {
            _longCaptureHotkeyHost.TryRegister(hotkey, CompleteCapture);
        }
        _longCaptureHotkeyHost.TryRegister(GlobalHotkey.Parse("Enter"), CompleteCapture);
        _longCaptureControls.Show();
        var placedOutside = _longCaptureControls.PlaceOutside(request.PhysicalPixelRegion);
        if (!ScrollCaptureControlReadiness.CanStart(_longCaptureControls.AffinityApplied, placedOutside))
        {
            _longCaptureControls.IsNormalTeardown = true;
            _longCaptureControls.Close();
            _longCaptureControls = null;
            _longCaptureBorder.Close();
            _longCaptureBorder = null;
            _longCapturePreview.Close();
            _longCapturePreview = null;
            _longCaptureHotkeyHost.Dispose();
            _longCaptureHotkeyHost = null;
            if (ReferenceEquals(_longCaptureCancellation, cancellation)) _longCaptureCancellation = null;
            cancellation.Dispose();
            LongCaptureRunDiagnosticStore.TryWrite(_paths.DiagnosticsRoot, longCaptureDiagnostics);
            overlay.RestoreAfterLongCapture("无法安全放置长截图停止按钮，请缩小选区后重试。");
            return;
        }
        session.PreviewChanged += (_, preview) =>
        {
            if (_longCapturePreview is not { } window) return;
            if (!window.IsVisible) window.Show();
            window.ShowPreview(preview.Bitmap);
        };
        session.ProgressChanged += (_, progress) => Dispatcher.BeginInvoke(() =>
        {
            if (!overlay.IsLoaded || _longCaptureControls is not { } controls) return;
            controls.SetProgress(progress);
        });
        using var result = await session.RunAsync(cancellation.Token);
        var status = $"{result.AcceptedFrameCount} 帧";
        try
        {
            if (discard)
            {
                result.CleanupAfterSuccessfulOutput();
                status = AppUi("已取消长截图", "Scrolling capture cancelled");
            }
            else if (result.AcceptedFrameCount > 0)
            {
                using var bitmap = result.CreateComposite();
                CloseLongCaptureCaptureUi();
                if (overlay.IsLoaded) overlay.Close();
                var delivery = _longCaptureOutputService!.SaveWithDelivery(bitmap, longCaptureDiagnostics);
                result.CleanupAfterSuccessfulOutput();
                status = ScrollCaptureResultWindow.SavedStatus(_settings.InterfaceLanguage, delivery.ClipboardRequested, delivery.ClipboardCopied);
                var preview = new ScrollCaptureResultWindow(
                    bitmap,
                    result.StopReason,
                    delivery.Path,
                    delivery.ClipboardCopied,
                    () => _longCaptureOutputService.TryCopySavedFile(bitmap, delivery.Path),
                    interfaceLanguage: _settings.InterfaceLanguage,
                    clipboardRequested: delivery.ClipboardRequested);
                preview.ShowDialog();
            }
            else
            {
                result.PreserveForRecovery();
                status = AppUi($"未取得有效帧（{result.StopReason}），临时目录已保留",
                    $"No valid frames ({result.StopReason}); temporary files were kept");
            }
        }
        catch
        {
            status = AppUi($"已保留 partial recovery：{result.TempSessionDirectory}",
                $"Partial capture kept for recovery: {result.TempSessionDirectory}");
            try { result.PreserveForRecovery(); }
            catch { /* Preserve flag is already set even if the marker cannot be written. */ }
            System.Windows.MessageBox.Show(AppUi($"长截图保存未完成，已有片段保留在：\n{result.TempSessionDirectory}",
                $"Scrolling capture could not be saved. Captured pieces remain at:\n{result.TempSessionDirectory}"),
                AppUi("长截图可恢复", "Scrolling capture can be recovered"));
        }
        finally
        {
            if (ReferenceEquals(_longCaptureCancellation, cancellation)) _longCaptureCancellation = null;
            cancellation.Dispose();
            LongCaptureRunDiagnosticStore.TryWrite(_paths.DiagnosticsRoot, longCaptureDiagnostics);
            CloseLongCaptureCaptureUi();
            if (overlay.IsLoaded) overlay.RestoreAfterLongCapture(status);
        }
    }

    private void CloseLongCaptureCaptureUi()
    {
        if (_longCaptureControls is not null)
        {
            _longCaptureControls.IsNormalTeardown = true;
            _longCaptureControls.Close();
            _longCaptureControls = null;
        }
        _longCaptureBorder?.Close();
        _longCaptureBorder = null;
        _longCapturePreview?.Close();
        _longCapturePreview = null;
        _longCaptureHotkeyHost?.Dispose();
        _longCaptureHotkeyHost = null;
    }

    private async void StartLongCapturePreview()
    {
        CapturedFrame? frame = null;
        try
        {
            frame = _captureService.CaptureCurrentMonitor();
            _overlay = new SelectionOverlayWindow(frame, CreateOverlayPreviewSelection(frame), showPreviewHint: true, interactionSettings: _settings);
            frame = null;
            _overlay.Closed += (_, _) => _overlay = null;
            _overlay.Show();
            _overlay.ShowLongCaptureChoicesForPreview();
            await Task.Delay(TimeSpan.FromSeconds(8));
            _overlay?.Close();
        }
        finally
        {
            frame?.Dispose();
            Shutdown();
        }
    }

    private async void StartLongCaptureLivePreview()
    {
        CapturedFrame? frame = null;
        ScrollCaptureBorderWindow? border = null;
        ScrollCapturePreviewWindow? preview = null;
        ScrollCaptureControlWindow? controls = null;
        try
        {
            frame = _captureService.CaptureCurrentMonitor();
            var width = Math.Min(446, frame.Bitmap.Width - 48);
            var height = Math.Min(644, frame.Bitmap.Height - 160);
            var local = new Rectangle(24, Math.Max(40, (frame.Bitmap.Height - height) / 2), width, height);
            var region = new Rectangle(frame.ScreenBounds.Left + local.Left, frame.ScreenBounds.Top + local.Top, width, height);
            using var crop = frame.Bitmap.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var composite = new Bitmap(width, height * 3, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(composite))
            {
                graphics.DrawImageUnscaled(crop, 0, 0);
                graphics.DrawImageUnscaled(crop, 0, height);
                graphics.DrawImageUnscaled(crop, 0, height * 2);
            }

            border = new ScrollCaptureBorderWindow(region, excludeFromCapture: false);
            preview = new ScrollCapturePreviewWindow(region, excludeFromCapture: false);
            controls = new ScrollCaptureControlWindow(mode: ScrollCaptureMode.Automatic, excludeFromCapture: false, interfaceLanguage: _settings?.InterfaceLanguage ?? "zh-CN");
            border.Show();
            preview.Show();
            controls.Show();
            controls.PlaceOutside(region);
            controls.SetProgress(new ScrollCaptureProgress(12));
            preview.ShowPreview(composite);
            await Task.Delay(TimeSpan.FromMilliseconds(450));

            using var evidence = _captureService.CaptureRegion(frame.ScreenBounds);
            var outputDirectory = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "screenshots");
            Directory.CreateDirectory(outputDirectory);
            evidence.Bitmap.Save(
                Path.Combine(outputDirectory, $"long-capture-live-{DateTime.Now:yyyyMMdd-HHmmssfff}.png"),
                System.Drawing.Imaging.ImageFormat.Png);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            controls?.Close();
            preview?.Close();
            border?.Close();
            frame?.Dispose();
            Shutdown();
        }
    }

    private async void StartLongCaptureRuntimeProbe()
    {
        System.Windows.Window? target = null;
        try
        {
            var rows = new System.Windows.Controls.StackPanel();
            for (var index = 0; index < 72; index++)
            {
                rows.Children.Add(new System.Windows.Controls.Border
                {
                    Height = 58,
                    Background = new System.Windows.Media.SolidColorBrush(index % 2 == 0
                        ? System.Windows.Media.Color.FromRgb(0x15, 0x1C, 0x29)
                        : System.Windows.Media.Color.FromRgb(0x21, 0x2B, 0x3C)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0x46, 0x60)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = $"ClipRay long capture row {index:D2}  ·  {index * 7919:D6}",
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 18,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(18, 0, 0, 0),
                    },
                });
            }
            var scroll = new System.Windows.Controls.ScrollViewer
            {
                Content = rows,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0C, 0x10, 0x17)),
            };
            target = new System.Windows.Window
            {
                Content = scroll,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Width = 452,
                Height = 600,
            };
            target.Show();
            target.Activate();
            var source = PresentationSource.FromVisual(target)?.CompositionTarget
                ?? throw new InvalidOperationException("Runtime probe window has no composition target.");
            var scale = source.TransformToDevice;
            var screen = Forms.Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary screen is available.");
            target.Width = 452 / scale.M11;
            target.Height = 600 / scale.M22;
            target.Left = (screen.WorkingArea.Left + 10) / scale.M11;
            target.Top = (screen.WorkingArea.Top + (screen.WorkingArea.Height - 600) / 2d) / scale.M22;
            target.UpdateLayout();
            await Task.Delay(TimeSpan.FromMilliseconds(350));

            var topLeft = target.PointToScreen(new System.Windows.Point(0, 0));
            var region = new Rectangle((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y), 452, 600);
            var input = new ScrollInputService(ScrollInputService.ResolveTarget(new System.Drawing.Point(region.Left + 226, region.Top + 300)));
            var tempRoot = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "tmp", "long-capture-probe");
            var diagnosticRoot = Path.Combine(tempRoot, $"frames-{DateTime.Now:yyyyMMdd-HHmmssfff}");
            using var session = new ScrollCaptureSession(
                region,
                ScrollCaptureMode.Automatic,
                new ProbeRegionCapture(_captureService, diagnosticRoot),
                input,
                new DelayScrollSettler(),
                tempRoot,
                new ScrollCaptureOptions(MaximumFrames: 6, SettleDelay: TimeSpan.FromMilliseconds(220), MaximumPolls: 10));
            var result = await session.RunAsync(CancellationToken.None);
            using var composite = result.CreateComposite();
            var outputDirectory = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "screenshots");
            Directory.CreateDirectory(outputDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            var outputPath = Path.Combine(outputDirectory, $"long-capture-runtime-{stamp}.png");
            composite.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText(
                Path.Combine(outputDirectory, $"long-capture-runtime-{stamp}.txt"),
                $"frames={result.AcceptedFrameCount}{Environment.NewLine}height={composite.Height}{Environment.NewLine}reason={result.StopReason}{Environment.NewLine}");
            result.CleanupAfterSuccessfulOutput();
        }
        catch (Exception exception)
        {
            var outputDirectory = Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "artifacts", "screenshots");
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, $"long-capture-runtime-failed-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt"), exception.GetType().Name);
        }
        finally
        {
            target?.Close();
            Shutdown();
        }
    }

    private async void StartRecordingPreviewProbe()
    {
        RecordingPreviewWindow? preview = null;
        try
        {
            var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
            var video = Directory.GetFiles(Path.Combine(workspace, "artifacts", "recording-fixture"), "*.mp4")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? throw new FileNotFoundException("No recording fixture exists.");
            var copied = RecordingResultDelivery.TryCopyFile(video, new WindowsRecordingFileClipboard());
            var clipboardMatches = Forms.Clipboard.ContainsFileDropList()
                && Forms.Clipboard.GetFileDropList().Cast<string>().Any(path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(video), StringComparison.OrdinalIgnoreCase));
            preview = new RecordingPreviewWindow(video, initiallyCopied: copied, interfaceLanguage: _settings?.InterfaceLanguage ?? "zh-CN");
            preview.Show();
            preview.Activate();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
            while (!preview.LoadCompleted && DateTime.UtcNow < deadline) await Task.Delay(100);
            await Task.Delay(300);
            using var evidence = _captureService.CaptureCurrentMonitor();
            var outputDirectory = Path.Combine(workspace, "artifacts", "screenshots");
            Directory.CreateDirectory(outputDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            evidence.Bitmap.Save(Path.Combine(outputDirectory, $"recording-preview-{stamp}.png"), System.Drawing.Imaging.ImageFormat.Png);
            File.WriteAllText(
                Path.Combine(outputDirectory, $"recording-preview-{stamp}.txt"),
                $"opened={preview.MediaOpenedSuccessfully}{Environment.NewLine}copied={copied}{Environment.NewLine}clipboardMatches={clipboardMatches}{Environment.NewLine}status={preview.Status}{Environment.NewLine}video={video}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            var workspace = WorkspaceLocator.Find(AppContext.BaseDirectory);
            var outputDirectory = Path.Combine(workspace, "artifacts", "screenshots");
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, $"recording-preview-failed-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt"), exception.GetType().Name);
        }
        finally
        {
            preview?.Close();
            Shutdown();
        }
    }

    private bool HandleConfirmedCapture(Bitmap bitmap)
    {
        try
        {
            var delivery = _outputService!.SaveWithDelivery(bitmap);
            if (delivery.ClipboardRequested && !delivery.ClipboardCopied)
            {
                SetTrayTooltip(AppUi("ClipRay | 已保存截图，剪贴板暂忙", "ClipRay | Saved; clipboard is busy"));
                _trayIcon?.ShowBalloonTip(4000, AppUi("截图已保存", "Screenshot saved"),
                    AppUi("PNG 已保存；剪贴板暂忙，未复制。", "PNG saved, but the clipboard is busy; it was not copied."), Forms.ToolTipIcon.Warning);
            }
            return true;
        }
        catch
        {
            _overlay?.ShowTranslationStatus(AppUi("保存失败，编辑内容已保留；请重试保存或调整输出目录。",
                "Save failed. Your edits were kept; retry or choose another output folder."));
            return false;
        }
    }

    private async void HandleOverlayTranslationRequested(object? sender, TranslationCapture capture)
    {
        using (capture)
        {
            if (sender is not SelectionOverlayWindow overlay) return;
            var operation = _overlayTranslationOperations.Begin(overlay.LifetimeToken);
            var runId = Guid.NewGuid().ToString("N");
            var startedUtc = DateTimeOffset.UtcNow;
            TranslationPipelineTelemetry? telemetry = null;
            TranslationPipelineStage? activeStage = null;
            using var screenshotEvidence = ScreenshotTranslationEvidence.StartFromEnvironment(runId, startedUtc);
            using var screenshotEvidenceScope = screenshotEvidence?.Push();
            using var screenshotWireScope = screenshotEvidence is null ? null
                : TranslationWireTrace.Push(screenshotEvidence.RecordWire);
            try
            {
                var translationRoute = ResolveTranslationRoute(capture.Route);
                var isDeepSeek = translationRoute is TranslationRouteKind.DeepSeekFlash or TranslationRouteKind.DeepSeekPro;
                screenshotEvidence?.RecordContext(new
                {
                    run_id = runId,
                    started_utc = startedUtc,
                    app_version = GetType().Assembly.GetName().Version?.ToString(3),
                    executable_sha256 = CurrentExecutableSha256(),
                    capture_version = capture.Version,
                    capture.Selection,
                    capture.CaptureBounds,
                    route = translationRoute.ToString(),
                    target_language = _settings!.TranslationTargetLanguage,
                    source_width = capture.Bitmap.Width,
                    source_height = capture.Bitmap.Height
                });
                screenshotEvidence?.SaveSource(capture.Bitmap);
                var outcome = await _translationOrchestrator.ExecuteAsync(
                    capture.Bitmap,
                    translationRoute,
                    _settings!.TranslationTargetLanguage,
                    blocks => SelectionCapturePolicy.ConstrainToSelection(
                        blocks, capture.Selection, capture.CaptureBounds),
                    stage =>
                    {
                        activeStage = stage;
                        screenshotEvidence?.Record("pipeline_stage_started", new { stage = stage.ToString() });
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!_overlayTranslationOperations.IsCurrent(operation.Version)) return;
                            var status = stage == TranslationPipelineStage.Ocr
                                ? "正在识别图片文字（本地 PP-OCR）…"
                                : translationRoute == TranslationRouteKind.Machine
                                    ? "正在使用本地机翻；图片和 OCR 文本均不离开本机…"
                                : isDeepSeek
                                    ? $"正在请求 {TranslationRouteCatalog.Get(translationRoute).ShortName} 翻译 → {_settings!.TranslationTargetLanguage}…"
                                    : GetLocalTranslationProvider().HasActiveSession
                                        ? "正在使用已加载的本地模型翻译…"
                                        : "正在加载本地模型；首次加载或卸载后重新加载可能需要较长时间…";
                            overlay.TryShowTranslationStatus(capture.Version, status);
                        });
                    },
                    stageTelemetry =>
                    {
                        telemetry = stageTelemetry;
                        screenshotEvidence?.Record("pipeline_timing", stageTelemetry);
                    },
                    operation.Token);
                screenshotEvidence?.RecordOutcome(outcome);
                if (outcome.FailureStage is TranslationPipelineStage.Ocr or TranslationPipelineStage.Scope)
                {
                    screenshotEvidence?.RecordOverlayDecision(false,
                        "pipeline-" + outcome.FailureStage.ToString().ToLowerInvariant(), 0);
                    WriteTranslationRunLog(runId, startedUtc, capture, translationRoute,
                        outcome, telemetry, "not-attempted-pipeline-" + outcome.FailureStage.ToString().ToLowerInvariant());
                    overlay.TryShowTranslationStatus(capture.Version,
                        outcome.FailureStage == TranslationPipelineStage.Scope
                            ? "框选范围内未识别到可翻译文字。"
                            : outcome.Status);
                    return;
                }

                var retainedCount = outcome.Regions.Count - outcome.LayoutItems.Count;
                var costEstimate = outcome.Results
                    .Select(static result => result.CostEstimate)
                    .FirstOrDefault(static estimate => estimate is not null);
                var success = new TranslationResult(
                    true,
                    string.Join(Environment.NewLine, outcome.LayoutItems.Select(static block => block.Text)),
                    TranslationStatusText.FormatOverlayStatus(
                        translationRoute,
                        outcome.ExecutionTrace,
                        outcome.LayoutItems.Count,
                        retainedCount,
                        outcome.Results,
                        costEstimate),
                    costEstimate);
                if (_overlayTranslationOperations.IsCurrent(operation.Version))
                {
                    var applied = overlay.ShowTranslation(capture.Version, capture.CaptureBounds,
                        outcome.LayoutItems);
                    var overlayDecision = !applied ? "overlay-refused"
                        : outcome.LayoutItems.Count == 0 ? "accepted-empty-overlay"
                        : "accepted-by-overlay";
                    screenshotEvidence?.RecordOverlayDecision(applied,
                        overlayDecision, outcome.LayoutItems.Count);
                    if (applied && outcome.LayoutItems.Count > 0)
                        screenshotEvidence?.SaveRenderedLayer(() => SelectionTranslationLayerRenderer.RenderOverlay(
                            capture.Bitmap.Width, capture.Bitmap.Height, outcome.LayoutItems));
                    WriteTranslationRunLog(runId, startedUtc, capture, translationRoute,
                        outcome, telemetry, overlayDecision);
                    overlay.TryShowTranslationStatus(
                        capture.Version,
                        success.Status,
                        isDeepSeek ? TranslationStatusText.FormatDeepSeekTooltip(costEstimate) : null);
                }
                else
                {
                    screenshotEvidence?.RecordOverlayDecision(false, "superseded", outcome.LayoutItems.Count);
                    WriteTranslationRunLog(runId, startedUtc, capture, translationRoute,
                        outcome, telemetry, "superseded");
                }
            }
            catch (OperationCanceledException)
            {
                // Overlay lifetime owns cancellation; no detached continuation remains.
                WriteTranslationRuntimeFailure(runId, startedUtc, capture, "cancelled");
                screenshotEvidence?.RecordTerminalFailure("cancelled", activeStage?.ToString());
                screenshotEvidence?.RecordOverlayDecision(false, "cancelled", 0);
            }
            catch (Exception exception)
            {
                WriteTranslationRuntimeFailure(runId, startedUtc, capture, "unhandled");
                screenshotEvidence?.RecordTerminalFailure(exception.GetType().Name,
                    activeStage?.ToString());
                screenshotEvidence?.RecordOverlayDecision(false, "unhandled", 0);
                overlay.TryShowTranslationStatus(capture.Version, "翻译失败，未显示任何私密诊断。");
            }
        }
    }
    private void WriteTranslationRunLog(
        string runId,
        DateTimeOffset startedUtc,
        TranslationCapture capture,
        TranslationRouteKind route,
        ApplicationTranslationOutcome outcome,
        TranslationPipelineTelemetry? telemetry,
        string overlayDecision)
    {
        var timing = telemetry ?? new TranslationPipelineTelemetry(0, 0, 0, 0);
        TranslationRunClassifier.TryClassify(outcome, out var classification);
        _translationRunLogWriter?.Write(new TranslationRunLog(
            TranslationRunLog.CurrentSchemaVersion,
            runId,
            startedUtc,
            _paths?.ProfileId ?? string.Empty,
            GetType().Assembly.GetName().Version?.ToString(3) ?? string.Empty,
            CurrentExecutableSha256(),
            route.ToString(),
            outcome.Status,
            outcome.FailureStage.ToString(),
            (int)Math.Round(capture.Selection.Width),
            (int)Math.Round(capture.Selection.Height),
            TouchesCaptureEdge(capture),
            outcome.OcrBlocks.Count,
            outcome.Regions.Count,
            classification.TranslatedCount,
            classification.PreservedCount,
            classification.ProviderFailedCount,
            classification.LayoutRejectedCount,
            outcome.ExecutionTrace?.RecoveredCount ?? 0,
            new TranslationStageTiming(0, timing.OcrMilliseconds, timing.TranslationMilliseconds,
                timing.LayoutMilliseconds, timing.TotalMilliseconds),
            outcome.IsSuccess ? "none" : outcome.Status,
            overlayDecision));
    }

    private void WriteTranslationRuntimeFailure(
        string runId,
        DateTimeOffset startedUtc,
        TranslationCapture capture,
        string errorCategory) =>
        _translationRunLogWriter?.Write(new TranslationRunLog(
            TranslationRunLog.CurrentSchemaVersion,
            runId,
            startedUtc,
            _paths?.ProfileId ?? string.Empty,
            GetType().Assembly.GetName().Version?.ToString(3) ?? string.Empty,
            CurrentExecutableSha256(),
            capture.Route.ToString(),
            "runtime-failed",
            "runtime",
            (int)Math.Round(capture.Selection.Width),
            (int)Math.Round(capture.Selection.Height),
            TouchesCaptureEdge(capture),
            0, 0, 0, 0, 0, 0, 0,
            new TranslationStageTiming(0, 0, 0, 0, 0),
            errorCategory));

    private static bool TouchesCaptureEdge(TranslationCapture capture) =>
        capture.Selection.X <= capture.CaptureBounds.X
        || capture.Selection.Y <= capture.CaptureBounds.Y
        || capture.Selection.X + capture.Selection.Width >= capture.CaptureBounds.X + capture.CaptureBounds.Width
        || capture.Selection.Y + capture.Selection.Height >= capture.CaptureBounds.Y + capture.CaptureBounds.Height;

    private static string CurrentExecutableSha256()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;
            using var stream = File.OpenRead(executablePath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<TranslationBatchOutcome> TranslateVisualRequestsAsync(
        TranslationRouteKind route,
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        var screenshotEvidence = ScreenshotTranslationEvidence.Current;
        screenshotEvidence?.RecordTranslationInputs(route, requests);
        TranslationBatchOutcome Complete(TranslationBatchOutcome outcome)
        {
            screenshotEvidence?.RecordTranslationOutputs(outcome);
            return outcome;
        }

        if (route == TranslationRouteKind.Machine)
        {
            var machine = GetMachineTranslationProvider();
            if (screenshotEvidence is not null)
                machine = screenshotEvidence.ObserveMachineProvider(machine, "direct-machine");
            var results = await machine.TranslateManyAsync(requests, cancellationToken).ConfigureAwait(false);
            return Complete(new(results, new TranslationExecutionTrace(route, 0, 0, 0)));
        }

        async Task<IReadOnlyList<TranslationResult>> Primary(
            TranslationRouteKind primaryRoute,
            IReadOnlyList<TranslationRequest> primaryRequests,
            CancellationToken token)
        {
            Task<IReadOnlyList<TranslationResult>> Invoke() => primaryRoute == TranslationRouteKind.LocalLmt
                ? GetLocalTranslationProvider().TranslateManyAsync(primaryRequests, token)
                : VisualTranslationExecutionPolicy.ExecuteCloudAsync(
                    primaryRequests,
                    _credentialVault,
                    GetTranslationProvider(),
                    RecordDeepSeekCost,
                    token);
            return screenshotEvidence is null
                ? await Invoke().ConfigureAwait(false)
                : await screenshotEvidence.ObserveProviderAsync(
                    "primary", primaryRoute, primaryRequests, Invoke).ConfigureAwait(false);
        }

        if (_settings!.MachineTranslationFallbackEnabled)
        {
            var machine = GetMachineTranslationProvider();
            if (screenshotEvidence is not null)
                machine = screenshotEvidence.ObserveMachineProvider(machine, "machine-fallback");
            var outcome = await new TranslationFallbackCoordinator(Primary, machine)
                .TranslateAsync(route, requests, cancellationToken).ConfigureAwait(false);
            var attempted = outcome.Diagnostics
                .Where(static diagnostic => diagnostic.Code.StartsWith("machine-fallback", StringComparison.Ordinal))
                .Sum(static diagnostic => diagnostic.Count);
            return Complete(new(outcome.Results, new TranslationExecutionTrace(
                route,
                attempted,
                outcome.RecoveredCount,
                outcome.FailedCount)));
        }
        var primaryResults = await Primary(route, requests, cancellationToken).ConfigureAwait(false);
        return Complete(new(primaryResults, new TranslationExecutionTrace(
            route,
            0,
            0,
            0)));
    }

    private async void HandleTextExtractionRequested(object? sender, TranslationCapture capture)
    {
        using (capture)
        {
            if (sender is not SelectionOverlayWindow overlay) return;
            try
            {
                var result = await RecognizeVisualAsync(capture.Bitmap, overlay.LifetimeToken);
                if (!result.IsSuccess)
                {
                    overlay.ShowExtractedText(capture.Version, string.Empty, result.Status);
                    return;
                }
                var scopedBlocks = SelectionCapturePolicy.ConstrainToSelection(
                    result.Blocks,
                    capture.Selection,
                    capture.CaptureBounds);
                var text = OcrTextLayout.FormatLines(scopedBlocks);
                overlay.ShowExtractedText(
                    capture.Version,
                    text,
                    string.IsNullOrWhiteSpace(text) ? "框选范围内未识别到文字" : $"已识别 {scopedBlocks.Count} 行");
            }
            catch (OperationCanceledException)
            {
                // Overlay lifetime owns cancellation.
            }
            catch
            {
                overlay.ShowExtractedText(capture.Version, string.Empty, "文字提取失败，未显示私密诊断。");
            }
        }
    }

    private void RunCapture(
        Func<CapturedFrame> capture,
        Func<CapturedFrame, IEnumerable<Rectangle>> successBounds)
    {
        try
        {
            using var frame = capture();
            _outputService!.SaveAndCopy(frame.Bitmap);
            if (_settings!.ShowBorderFeedback)
            {
                _feedbackService.ShowSuccess(successBounds(frame));
            }
        }
        catch
        {
            var pointerMonitor = Forms.Screen.FromPoint(Forms.Cursor.Position);
            if (_settings!.ShowBorderFeedback)
            {
                _feedbackService.ShowFailure(pointerMonitor.Bounds);
            }
        }
    }

    private async Task RunCurrentMonitorCaptureAsync()
    {
        try
        {
            using var frame = await _captureService.CaptureCurrentMonitorGameCompatibleAsync();
            _outputService!.SaveAndCopy(frame.Bitmap);
            if (_settings!.ShowBorderFeedback
                && CaptureFeedbackPolicy.ShouldShowAppSuccess(frame.HasSystemCaptureFeedback))
            {
                _feedbackService.ShowSuccess(CurrentMonitorBounds(frame));
            }
        }
        catch
        {
            var pointerMonitor = Forms.Screen.FromPoint(Forms.Cursor.Position);
            if (_settings!.ShowBorderFeedback)
            {
                _feedbackService.ShowFailure(pointerMonitor.Bounds);
            }
        }
    }

    private async Task RunCurrentMonitorCaptureIncludingTaskbarAsync()
    {
        try
        {
            using var frame = await _captureService.CaptureCurrentMonitorIncludingTaskbarGameCompatibleAsync();
            _outputService!.SaveAndCopy(frame.Bitmap);
            if (_settings!.ShowBorderFeedback
                && CaptureFeedbackPolicy.ShouldShowAppSuccess(frame.HasSystemCaptureFeedback))
            {
                _feedbackService.ShowSuccess(CurrentMonitorBounds(frame));
            }
        }
        catch
        {
            var pointerMonitor = Forms.Screen.FromPoint(Forms.Cursor.Position);
            if (_settings!.ShowBorderFeedback)
            {
                _feedbackService.ShowFailure(pointerMonitor.Bounds);
            }
        }
    }

    private static IEnumerable<Rectangle> CurrentMonitorBounds(CapturedFrame frame) => [frame.ScreenBounds];

    private static ClipRay.Core.Geometry.PixelRect CreateOverlayPreviewSelection(CapturedFrame frame)
    {
        var width = Math.Min(1600, Math.Max(8, frame.Bitmap.Width - 160));
        var height = Math.Min(900, Math.Max(8, frame.Bitmap.Height - 160));
        return new ClipRay.Core.Geometry.PixelRect(
            (frame.Bitmap.Width - width) / 2d,
            (frame.Bitmap.Height - height) / 2d,
            width,
            height);
    }

    private static IEnumerable<Rectangle> AllMonitorBounds(CapturedFrame _) =>
        Forms.Screen.AllScreens.Select(static screen => screen.Bounds);

    private void SetTrayTooltip(string text)
    {
        if (_trayIcon is null)
        {
            return;
        }

        const int maximumLength = 63;
        _trayIcon.Text = text.Length <= maximumLength
            ? text
            : string.Concat(text.AsSpan(0, maximumLength - 3), "...");
    }

    private string AppUi(string chinese, string english) =>
        AppInterfaceLanguage.Text(_settings?.InterfaceLanguage ?? "zh-CN", chinese, english);

    private string LiveUi(string chinese, string english) => AppUi(chinese, english);

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settings!,
            _settingsLoadWarning,
            _settingsApplyCoordinator!,
            _credentialVault,
            () => GetTranslationProvider(),
            (text, token) => TranslateConfiguredAsync(
                new TranslationRequest("auto", _settings!.TranslationTargetLanguage, text, _settings.DeepSeekModel),
                token),
            _deepSeekCostLedger?.Load() ?? DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow),
            () => _deepSeekCostLedger!.Load(),
            () => _deepSeekCostLedger!.Reset(DateTimeOffset.UtcNow));
        _settingsWindow.SettingsApplied += (_, settings) =>
        {
            _settingsLoadWarning = null;
            ApplySettings(settings, registerHotkeys: true);
        };
        _settingsWindow.HotkeyCaptureFocusChanged += (_, isCapturing) =>
        {
            if (isCapturing)
            {
                _hotkeyHost?.Dispose();
                _hotkeyHost = null;
            }
            else if (_settingsWindow is not null)
            {
                RegisterHotkeys();
            }
        };
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            RegisterHotkeys();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private TranslationWindow OpenTranslationWindow()
    {
        if (_translationWindow is not null)
        {
            _translationWindow.Activate();
            return _translationWindow;
        }

        _translationWindow = new TranslationWindow(
            TranslateUsingRouteAsync,
            ResolveConfiguredTranslationRoute(), _settings!.InterfaceLanguage,
            route => TranslationFileCheck.Inspect(route, AppContext.BaseDirectory, _settings.LocalLmtModelPath));
        _translationWindow.LiveTranslationRequested += (_, request) => StartLiveTranslationRequest(request);
        _translationWindow.Closed += (_, _) => _translationWindow = null;
        _translationWindow.Show();
        _translationWindow.Activate();
        return _translationWindow;
    }

    private void StartLiveTranslationRequest(LiveTranslationStartRequest request)
    {
        if (request.Region is { } region) StartLiveTranslation(region, request.Route, request.TargetLanguage);
        else StartLiveTranslationSelection(request.Route, request.TargetLanguage);
    }

    private async void StartLiveTranslationSelection(TranslationRouteKind? requestedRoute = null, string? targetLanguage = null)
    {
        if (_liveTranslationSelectionActive) return;
        if (_liveTranslationSession is not null || _liveTranslationStartup is not null)
        {
            SetTrayTooltip(LiveUi("ClipRay | 实时翻译已在进行中", "ClipRay | Live translation is already running"));
            return;
        }

        _liveTranslationSelectionActive = true;
        var warmup = EnsureMachineTranslationWarmup();
        try
        {
            var selector = new LiveTranslationSelectionWindow(_settings!.InterfaceLanguage);
            var selected = await selector.SelectAsync();
            if (selected is not { } pixelRegion) return;
            await warmup.ConfigureAwait(true);
            StartLiveTranslation(pixelRegion, requestedRoute, targetLanguage);
        }
        catch
        {
            SetTrayTooltip(LiveUi("ClipRay | 无法启动实时翻译", "ClipRay | Could not start live translation"));
        }
        finally
        {
            _liveTranslationSelectionActive = false;
        }
    }

    private async Task WarmMachineTranslationAsync()
    {
        try
        {
            await Task.WhenAll(_machineOcr.WarmupAsync(CancellationToken.None),
                GetMachineTranslationProvider().TranslateManyAsync(
                    [new TranslationRequest("en", "zh", "Ready.")], CancellationToken.None));
        }
        catch { /* Actual translation reports missing or failed components. */ }
    }

    private Task EnsureMachineTranslationWarmup() =>
        _machineTranslationWarmup ??= WarmMachineTranslationAsync();

    private async void StartLiveTranslation(ClipRay.Core.Geometry.PixelRect selected,
        TranslationRouteKind? requestedRoute = null, string? targetLanguage = null)
    {
        if (_liveTranslationSession is not null || _liveTranslationStartup is not null) return;
        if (LiveTranslationWorkerRegistry.HasUnresolvedWorker)
        {
            System.Windows.MessageBox.Show(
                LiveUi("上一次实时翻译任务未能退出。为避免新旧任务并发串帧，请重启 ClipRay 后再试。",
                    "The previous live translation task is still running. Restart ClipRay before trying again to avoid mixing frames."),
                LiveUi("实时翻译未启动", "Live translation did not start"));
            return;
        }
        CloseLiveTranslationUi();
        var region = new Rectangle(
            (int)Math.Round(selected.X),
            (int)Math.Round(selected.Y),
            (int)Math.Round(selected.Width),
            (int)Math.Round(selected.Height));
        var route = LiveTranslationRoutePolicy.Resolve(requestedRoute, _settings!.LiveTranslationProvider);
        var target = targetLanguage ?? _settings!.TranslationTargetLanguage;
        if (!Forms.Screen.AllScreens.Any(screen => screen.Bounds.Contains(region)))
        {
            System.Windows.MessageBox.Show(LiveUi("实时翻译当前支持单显示器选区，请在同一显示器内重新选择。",
                "Live translation currently supports a region on one display. Select a region within one display."),
                LiveUi("实时翻译未启动", "Live translation did not start"));
            return;
        }
        if (route == TranslationRouteKind.Machine && target is not ("简体中文" or "zh" or "Chinese"))
        {
            System.Windows.MessageBox.Show(LiveUi("实时本地机翻当前以简体中文为目标语言，请将目标语言设为简体中文。",
                "Local machine translation currently supports Simplified Chinese as the target. Choose Simplified Chinese."),
                LiveUi("实时翻译未启动", "Live translation did not start"));
            return;
        }
        var overlay = new LiveTranslationOverlayWindow(
            region,
            showBorder: LiveTranslationCaptureVisibilityPolicy.SelectedRegionBorderVisible,
            showInRecordings: _settings.ShowLiveTranslationInRecordings);
        var stopControl = _settings.ShowLiveTranslationStopControl
            ? new LiveTranslationStopControlWindow(region, _settings!.InterfaceLanguage) : null;
        using var acceptanceTrace = new LiveTranslationAcceptanceTrace();
        if (acceptanceTrace.Enabled)
            acceptanceTrace.Record("session_context", data: new {
                AppVersion = GetType().Assembly.GetName().Version?.ToString(3),
                ExecutableSha256 = CurrentExecutableSha256(),
                Region = region,
                Monitor = Forms.Screen.FromRectangle(region).Bounds,
                Route = route.ToString(),
                TargetLanguage = target,
                ShowInRecordings = _settings.ShowLiveTranslationInRecordings
            });
        var semanticCache = new LiveTranslationRequestCache(
            TranslateVisualRequestsAsync,
            diagnostic => acceptanceTrace.Record(diagnostic.Kind,
                identity: LiveTranslationWorkContext.Current, data: diagnostic));
        async Task<TranslationBatchOutcome> TracedTranslate(
            TranslationRouteKind selectedRoute,
            IReadOnlyList<TranslationRequest> requests,
            CancellationToken cancellationToken)
        {
            var identity = LiveTranslationWorkContext.Current;
            if (acceptanceTrace.Enabled)
                acceptanceTrace.Record("translation_requests", identity: identity, data: new {
                    Route = selectedRoute.ToString(),
                    Requests = requests.Select((request, index) => new {
                        Index = index,
                        request.SourceLanguage,
                        request.TargetLanguage,
                        request.OcrText,
                        request.Model,
                        TextKind = request.TextKind.ToString(),
                        request.Scene,
                        request.TranslationHint
                    }).ToArray()
                });
            try
            {
                var outcome = await semanticCache.TranslateAsync(
                    selectedRoute, requests, cancellationToken).ConfigureAwait(false);
                if (acceptanceTrace.Enabled)
                    acceptanceTrace.Record("translation_results", identity: identity, data: new {
                        Results = outcome.Results.Select((result, index) => new {
                            Index = index,
                            result.IsSuccess,
                            result.Text,
                            result.Status,
                            result.Diagnostic,
                            result.ChunkDiagnostics,
                            result.ExecutionEvidence
                        }).ToArray(),
                        outcome.Trace
                    });
                return outcome;
            }
            catch (Exception exception)
            {
                acceptanceTrace.Record("translation_exception", identity: identity,
                    data: new { Category = exception.GetType().Name });
                throw;
            }
        }
        var liveOrchestrator = new TranslationApplicationOrchestrator(RecognizeMachineAsync,
            TracedTranslate);
        if (stopControl is { CanPlaceOutside: false })
        {
            SetTrayTooltip(LiveUi("ClipRay | 请缩小实时翻译选区", "ClipRay | Select a smaller live translation region"));
            System.Windows.MessageBox.Show(LiveUi("选区外没有足够空间放置状态与停止按钮。请缩小选区后重试。",
                "There is not enough room outside the region for status and Stop controls. Select a smaller region and try again."),
                LiveUi("实时翻译未启动", "Live translation did not start"));
            overlay.Close();
            stopControl.Close();
            return;
        }
        using var startup = new CancellationTokenSource();
        _liveTranslationStartup = startup;
        LiveTranslationScreenCapture screenCapture;
        try
        {
            screenCapture = await LiveTranslationScreenCapture.StartForRegionAsync(
                region,
                startup.Token,
                CaptureSelectedPixels);
        }
        catch (OperationCanceledException)
        {
            acceptanceTrace.Record("capture_start_cancelled");
            overlay.Close();
            stopControl?.Close();
            return;
        }
        catch (Exception exception)
        {
            acceptanceTrace.Record("capture_start_failed",
                data: new { Category = exception.GetType().Name });
            overlay.Close();
            stopControl?.Close();
            SetTrayTooltip(LiveUi("ClipRay | 无法开始固定屏幕选区", "ClipRay | Cannot start the fixed screen region"));
            System.Windows.MessageBox.Show(
                LiveUi("无法开始固定屏幕选区。请确认选区仍在同一显示器内，然后重试。",
                    "Cannot start the fixed screen region. Keep the region within one display and try again."),
                LiveUi("实时翻译未启动", "Live translation did not start"));
            return;
        }
        finally
        {
            if (ReferenceEquals(_liveTranslationStartup, startup)) _liveTranslationStartup = null;
        }
        Bitmap CaptureSelectedPixels(Rectangle bounds)
        {
            var prepared = false;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (!ReferenceEquals(_liveTranslationOverlay, overlay))
                        throw new InvalidOperationException("screen-capture-session-ended");
                    prepared = true;
                    overlay.BeforeCapture();
                });
                using var frame = _captureService!.CaptureRegion(bounds);
                return new Bitmap(frame.Bitmap);
            }
            finally
            {
                if (prepared)
                    Dispatcher.Invoke(() =>
                    {
                        if (ReferenceEquals(_liveTranslationOverlay, overlay)) overlay.AfterCapture();
                    });
            }
        }
        LiveTranslationSession? session = null;
        try
        {
        var captureAttempt = 0L;
        Bitmap CaptureWithEvidence(Rectangle bounds)
        {
            var attempt = Interlocked.Increment(ref captureAttempt);
            acceptanceTrace.Record("capture_attempt", data: new { Attempt = attempt, Bounds = bounds });
            try
            {
                var frame = screenCapture.Capture(bounds);
                acceptanceTrace.Record("capture_succeeded", data: new {
                    Attempt = attempt, Bounds = bounds, frame.Width, frame.Height
                });
                return frame;
            }
            catch (Exception exception)
            {
                var category = exception is InvalidOperationException
                    && exception.Message.StartsWith("window-capture-", StringComparison.Ordinal)
                    ? exception.Message : exception.GetType().Name;
                acceptanceTrace.Record("capture_failed", data: new {
                    Attempt = attempt, Bounds = bounds, Category = category
                });
                throw;
            }
        }
        session = new LiveTranslationSession(
            region,
            CaptureWithEvidence,
            async (bitmap, cancellationToken) =>
            {
                var identity = LiveTranslationWorkContext.Current;
                acceptanceTrace.SaveSource(bitmap, identity);
                using var wireTrace = acceptanceTrace.Enabled
                    ? TranslationWireTrace.Push((kind, data) =>
                        acceptanceTrace.Record(kind, identity: identity, data: data))
                    : null;
                TranslationPipelineStage? activeStage = null;
                ApplicationTranslationOutcome outcome;
                try
                {
                    outcome = await liveOrchestrator.ExecuteAsync(
                        bitmap,
                        route,
                        target,
                        static blocks => blocks,
                        progress: stage => {
                            activeStage = stage;
                            acceptanceTrace.Record("pipeline_stage_started", identity: identity,
                                data: new { Stage = stage.ToString() });
                        },
                        telemetry: timing => acceptanceTrace.Record("pipeline", identity: identity, data: timing),
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    acceptanceTrace.Record("pipeline_exception", identity: identity,
                        data: new { Stage = activeStage?.ToString(), Category = exception.GetType().Name });
                    throw;
                }
                acceptanceTrace.Record("translated", identity: identity, data: new { outcome.Status,
                    Regions = outcome.Regions.Count, Layouts = outcome.LayoutItems.Count,
                    Results = outcome.Results.GroupBy(static result => result.Status)
                        .Select(static group => new { Status = group.Key, Count = group.Count() }).ToArray() });
                RecordDecisions(outcome, identity);
                return LiveTranslationFrame.FromOutcome(route, outcome);
            },
            route,
            validateLayoutRegions: true,
            verifySource: RecognizeMachineAsync);

        void RecordDecisions(ApplicationTranslationOutcome outcome, LiveTranslationWorkIdentity? identity)
        {
            if(!acceptanceTrace.Enabled) return;
            acceptanceTrace.Record("region_decisions",identity: identity,data:new {
                outcome.Status,
                FailureStage = outcome.FailureStage.ToString(),
                OcrBlocks = outcome.OcrBlocks.Select((block, index) => new {
                    Index = index, block.Text, block.Bounds, block.Words
                }).ToArray(),
                Regions = outcome.Regions.Select((region, index) => new {
                    Index = index, region.Id, region.SourceText, region.Bounds,
                    Role = region.Role.ToString(),
                    Confidence = region.Confidence.ToString(),
                    SourceLines = region.Lines.Select(line => new { line.Text, line.Bounds }).ToArray(),
                    OcrWords = region.OriginalOcrWords,
                    Result = index < outcome.Results.Count ? new {
                        outcome.Results[index].IsSuccess,
                        outcome.Results[index].Text,
                        outcome.Results[index].Status,
                        outcome.Results[index].Diagnostic,
                        outcome.Results[index].ExecutionEvidence
                    } : null
                }).ToArray(),
                outcome.LayoutRejections,
                Layouts = outcome.LayoutItems.Select(item => new {
                    Region = item.SourceRegion?.Id,
                    item.Text,
                    item.Bounds,
                    BackgroundMode = item.BackgroundMode.ToString(),
                    BackgroundArgb = item.BackgroundColor.ToArgb().ToString("X8"),
                    ForegroundArgb = item.ForegroundColor.ToArgb().ToString("X8"),
                    SourceStyle = item.SourceStyle is null ? null : new {
                        ForegroundArgb = item.SourceStyle.ForegroundColor.ToArgb().ToString("X8"),
                        item.SourceStyle.GlyphHeightPixels,
                        item.SourceStyle.LineHeightPixels,
                        item.SourceStyle.EstimatedFontSizePixels,
                        Weight = item.SourceStyle.FontWeight.ToString(),
                        Category = item.SourceStyle.FontCategory.ToString(),
                        Alignment = item.SourceStyle.Alignment.ToString(),
                        Confidence = item.SourceStyle.Confidence.ToString()
                    },
                    item.TextLayout
                }).ToArray()
            });
        }

        _liveTranslationOverlay = overlay;
        _liveTranslationStopControl = stopControl;
        _liveTranslationSession = session;
        session.CaptureDecision += (_, decision) =>
            acceptanceTrace.Record(decision.Kind, data: decision);
        session.Diagnostic += (_, diagnostic) => acceptanceTrace.Record(
            diagnostic.Kind,
            identity: diagnostic.Identity,
            data: new {
                State = diagnostic.ResultState.ToString(),
                diagnostic.Metrics,
                diagnostic.ErrorCategory,
                diagnostic.QuarantinedLayouts
            });
        session.FrameReady += (_, args) => Dispatcher.BeginInvoke(() => _ = PresentAsync(args));

        async Task PresentAsync(LiveTranslationFrameReadyEventArgs args)
        {
            if (!ReferenceEquals(_liveTranslationSession, session)
                || args.Generation != session.Generation
                || args.WorkIdentity?.ViewportGeneration != session.ViewportGeneration)
                return;
            if (!overlay.Apply(args.Frame, args.Generation)) return;
            acceptanceTrace.Record("applied", args.Generation, args.WorkIdentity, new {
                State = args.Frame.ResultState.ToString(),
                Layouts = args.Frame.LayoutItems.Count,
                Expected = args.Frame.ExpectedLayoutCount,
                Rejected = args.Frame.RejectedLayoutCount,
                Typography = args.Frame.LayoutItems.Select(item => new {
                    Region=item.SourceRegion?.Id,
                    Font=item.TextLayout?.FontSizePixels,
                    LineHeight=item.TextLayout?.LineHeightPixels
                }).ToArray()
            });
            acceptanceTrace.SaveRendered(overlay.RenderedImage, args.Generation, args.WorkIdentity);
            bool acknowledged;
            string? acknowledgeError = null;
            try
            {
                acknowledged = await overlay.AcknowledgePresentationAsync(args.Generation);
            }
            catch (Exception exception)
            {
                acknowledged = false;
                acknowledgeError = exception.GetType().Name;
            }
            if (acknowledged && ReferenceEquals(_liveTranslationSession, session)
                && args.Generation == session.Generation)
                acceptanceTrace.Record("render_cycle_observed", args.Generation, args.WorkIdentity,
                    new { Visible = overlay.IsVisible, Layouts = args.Frame.LayoutItems.Count });
            else
                acceptanceTrace.Record("render_cycle_not_observed", args.Generation, args.WorkIdentity,
                    new {
                        Reason = acknowledgeError is not null ? "exception"
                            : acknowledged ? "superseded-before-ack" : "render-or-visibility-check-failed",
                        Category = acknowledgeError,
                        Visible = overlay.IsVisible,
                        HasRenderedImage = overlay.RenderedImage is not null,
                        Layouts = args.Frame.LayoutItems.Count
                    });
        }
        session.FrameInvalidated += (_, _) => Dispatcher.Invoke(() =>
        {
            if (ReferenceEquals(_liveTranslationSession, session))
            {
                overlay.Clear();
                acceptanceTrace.Record("cleared", session.Generation);
            }
        });
        if (stopControl is not null) stopControl.RequestStop += (_, _) =>
        {
            acceptanceTrace.Record("stop_button", session.Generation);
            StopLiveTranslation();
        };
        if (stopControl is not null) stopControl.RequestRetry += (_, _) => StartLiveTranslation(selected, route, target);
        session.StatusChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_liveTranslationSession, session))
                stopControl?.ShowStatus($"{TranslationRouteCatalog.Get(route).ShortName} · {target}\n{session.Status}");
        });
        if (_stopLiveTranslationMenuItem is not null) _stopLiveTranslationMenuItem.Enabled = true;
        overlay.Show();
        if (!LiveTranslationCaptureVisibilityPolicy.CanStart(
            overlay.ExcludeFromCaptureRequested, overlay.DisplayAffinityApplied))
        {
            SetTrayTooltip(LiveUi("ClipRay | 无法保证实时译文不进入录屏", "ClipRay | Cannot exclude translation from recording"));
            overlay.Close();
            System.Windows.MessageBox.Show(
                LiveUi("Windows 未能启用录屏排除。为避免实时译文被录入，实时翻译没有启动。可在设置中明确开启“在录屏中显示实时译文”后重试。",
                    "Windows could not exclude the translation overlay from recordings, so live translation did not start. To allow it in recordings, enable that option in Settings and try again."),
                LiveUi("实时翻译未启动", "Live translation did not start"));
            return;
        }
        stopControl?.Show();
        acceptanceTrace.Record("presentation", data: new {
            Region = region,
            BorderVisible = overlay.IsSelectionBorderVisible,
            StopControlCreated = stopControl is not null,
            StopControlVisible = stopControl?.IsVisible ?? false });
        try
        {
            _liveTranslationEscape = new LiveTranslationEscapeListener(
                () => Dispatcher.BeginInvoke(() =>
                {
                    acceptanceTrace.Record("escape_stop", session.Generation);
                    StopLiveTranslation();
                }),
                () => Dispatcher.BeginInvoke(() => stopControl?.ShowStatus("再按一次 Esc 停止实时翻译")));
        }
        catch
        {
            stopControl?.ShowStatus("Esc 未注册 · 请点击停止按钮或使用停止快捷键");
        }
        SetTrayTooltip(LiveUi("ClipRay | 实时翻译中", "ClipRay | Live translation running"));
        await session.StartAsync(CancellationToken.None);
        if (session.State == LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
            SetTrayTooltip(LiveUi("ClipRay | 实时翻译连续失败，已停止", "ClipRay | Live translation stopped after repeated failures"));
        }
        finally
        {
            try
            {
                if (session is not null)
                {
                    acceptanceTrace.Record("ended", session.Generation, data: new { session.State });
                    if (ReferenceEquals(_liveTranslationSession, session))
                    {
                        _liveTranslationSession = null;
                        session.Dispose();
                        if (session.State == LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
                        {
                            overlay.Close();
                            _liveTranslationOverlay = null;
                            if (stopControl is null)
                            {
                                CloseLiveTranslationUi();
                                if (_stopLiveTranslationMenuItem is not null)
                                    _stopLiveTranslationMenuItem.Enabled = false;
                            }
                            else
                            {
                                stopControl.ShowStatus(session.Status,
                                    canRetry: session.LastErrorCategory != "stale-worker-cancel-timeout");
                            }
                        }
                        else
                        {
                            CloseLiveTranslationUi();
                            if (_stopLiveTranslationMenuItem is not null) _stopLiveTranslationMenuItem.Enabled = false;
                        }
                    }
                    else
                    {
                        session.Dispose();
                        overlay.Close();
                        stopControl?.Close();
                    }
                }
                else
                {
                    overlay.Close();
                    stopControl?.Close();
                }
            }
            finally
            {
                await screenCapture.DisposeAsync();
            }
        }
    }

    private void StopLiveTranslation()
    {
        _liveTranslationStartup?.Cancel();
        _liveTranslationSession?.Stop();
        CloseLiveTranslationUi();
        if (_stopLiveTranslationMenuItem is not null) _stopLiveTranslationMenuItem.Enabled = false;
    }

    private void CloseLiveTranslationUi()
    {
        _liveTranslationEscape?.Dispose();
        _liveTranslationEscape = null;
        _liveTranslationOverlay?.Close();
        _liveTranslationOverlay = null;
        _liveTranslationStopControl?.Close();
        _liveTranslationStopControl = null;
    }

    private IDeepSeekTranslationProvider GetTranslationProvider() => _translationServices.Provider;

    private Task<TranslationResult> TranslateConfiguredAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (_settings!.Provider == "Local LMT") return GetLocalTranslationProvider().TranslateAsync(request, cancellationToken);
        if (_settings.Provider == "Machine") return TranslateMachineAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(_settings.CredentialName))
        {
            return Task.FromResult(new TranslationResult(false, string.Empty, "请先在设置中选择本地保存的 DeepSeek 凭据。"));
        }
        return GetTranslationProvider().TranslateAsync(request, _settings.CredentialName, cancellationToken);
    }

    private async Task<TranslationResult> TranslateUsingRouteAsync(
        TranslationRequest request,
        TranslationRouteKind route,
        CancellationToken cancellationToken)
    {
        var settings = _settings ?? throw new InvalidOperationException("Translation settings are unavailable.");
        var resolved = ResolveTranslationRoute(route);
        TranslationResult result;
        if (resolved == TranslationRouteKind.LocalLmt)
        {
            result = await GetLocalTranslationProvider().TranslateAsync(request, cancellationToken);
        }
        else if (resolved == TranslationRouteKind.Machine)
        {
            return await TranslateMachineAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else if (string.IsNullOrWhiteSpace(settings.CredentialName))
        {
            result = new TranslationResult(false, string.Empty, "请先在设置中选择本地保存的 DeepSeek 凭据。");
        }
        else
        {
            var model = DeepSeekModels.Flash;
            var routed = new TranslationRequest(
                request.SourceLanguage,
                request.TargetLanguage,
                request.OcrText,
                model,
                request.TextKind,
                request.Scene,
                request.TranslationHint);
            result = await GetTranslationProvider().TranslateAsync(routed, settings.CredentialName, cancellationToken);
            RecordDeepSeekCost(result.CostEstimate);
        }
        if (!result.IsSuccess && settings.MachineTranslationFallbackEnabled)
        {
            var fallback = await TranslateMachineAsync(request, cancellationToken).ConfigureAwait(false);
            if (fallback.IsSuccess) return fallback;
        }
        return result;
    }

    private TranslationRouteKind ResolveConfiguredTranslationRoute() =>
        _settings!.Provider == "Local LMT"
            ? TranslationRouteKind.LocalLmt
            : _settings.Provider == "Machine"
                ? TranslationRouteKind.Machine
                : TranslationRouteKind.DeepSeekFlash;

    private TranslationRouteKind ResolveTranslationRoute(TranslationRouteKind route) =>
        route != TranslationRouteKind.FollowSettings
            ? route
            : ResolveConfiguredTranslationRoute();

    private void RecordDeepSeekCost(DeepSeekCostEstimate? estimate)
    {
        if (estimate is not null) _deepSeekCostLedger?.Record(estimate);
    }

    private LocalTranslationProvider GetLocalTranslationProvider() => _localTranslationProvider ??=
        new LocalTranslationProvider(LocalLmtOptions.Discover(_settings!.LocalLmtModelPath));

    private IMachineTranslationProvider GetMachineTranslationProvider() => _machineTranslationProvider ??=
        new LocalMachineTranslationProvider(new BundledMachineTranslationProcess(
            Path.Combine(AppContext.BaseDirectory, "machine-translation", "runtime", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "machine-translation", "machine_translation_worker.py"),
            MachineTranslationModelPaths.Resolve(AppContext.BaseDirectory), workerTimeout: TimeSpan.FromSeconds(10), keepAlive: true),
            LiveTranslationAcceptanceTrace.RecordIntegrityFailure,
            supportsCommonLanguages: MachineTranslationModelPaths.IsCommonLanguageModel(MachineTranslationModelPaths.Resolve(AppContext.BaseDirectory)),
            onProtectedFailure: LiveTranslationAcceptanceTrace.RecordProtectedFailure);

    private async Task<TranslationResult> TranslateMachineAsync(
        TranslationRequest request,
        CancellationToken cancellationToken) =>
        (await GetMachineTranslationProvider().TranslateManyAsync([request], cancellationToken).ConfigureAwait(false))[0];

    private void ApplySettings(AppSettings settings, bool registerHotkeys)
    {
        settings.Validate();
        if (AppInterfaceLanguage.IsOnlyInterfaceChange(_settings, settings))
        {
            _settings = settings;
            if (_trayIcon?.ContextMenuStrip is { } existingMenu)
                AppInterfaceLanguage.UpdateTray(existingMenu.Items, settings.InterfaceLanguage);
            return;
        }
        if (_liveTranslationSession is not null || _liveTranslationStartup is not null) StopLiveTranslation();
        _localTranslationProvider?.Dispose();
        _localTranslationProvider = null;
        (_machineTranslationProvider as IDisposable)?.Dispose();
        _machineTranslationProvider = null;
        _machineTranslationWarmup = null;
        _settings = settings;
        if (_trayIcon?.ContextMenuStrip is { } menu) AppInterfaceLanguage.UpdateTray(menu.Items, settings.InterfaceLanguage);
        _outputService = new ImageOutputService(
            settings.ScreenshotDirectory,
            settings.CopyToClipboard,
            OutputArtifactKind.Screenshot);
        _longCaptureOutputService = new ImageOutputService(
            settings.LongCaptureDirectory,
            settings.CopyToClipboard,
            OutputArtifactKind.LongCapture);
        if (settings.Provider == "Machine" || settings.MachineTranslationFallbackEnabled)
            _ = EnsureMachineTranslationWarmup();
        if (registerHotkeys) RegisterHotkeys();
    }

    private async void HandleRecordingRequested(object? sender, RegionRecordingRequest request)
    {
        if (sender is not SelectionOverlayWindow overlay) return;
        if (_recordingSession is not null)
        {
            overlay.RestoreAfterRecordingFailure("录屏已在进行中");
            return;
        }
        var mapping = RecordingGeometry.MapToMonitor(
            request.PhysicalPixelRegion,
            Forms.Screen.AllScreens.Select(static screen => screen.Bounds).ToArray());
        var crop = RecordingGeometry.NormalizeForEncoder(mapping.CropInMonitor);
        var timeline = RecordingTimeline.StartNow();
        _recordingPaths = RecordingOutputPaths.Create(_settings!.RecordingDirectory, DateTime.Now);
        try
        {
            var audio = RecordingStartupGuard.Execute(
                RecordingStartupStage.AudioDevice,
                () => new WasapiSystemAudioCaptureFactory(timeline).CreateDefaultRenderLoopback());
            var recorder = new MediaFoundationRecorder(_recordingPaths, crop.Width, crop.Height, audio.Format);
            var video = new GraphicsCaptureSource(mapping, timeline);
            var session = new RecordingSession(new RegionRecordingRuntime(video, audio, recorder));
            _recordingSession = session;
            await session.StartAsync(CancellationToken.None);
            RecordingStartupGuard.Execute(RecordingStartupStage.RecordingUi, () =>
            {
                _recordingBorder = new RecordingBorderWindow(request.PhysicalPixelRegion);
                _recordingBorder.Show();
                if (!_recordingBorder.IsCaptureSafe)
                {
                    _recordingBorder.Close();
                    _recordingBorder = null;
                }

                _recordingControl = new RecordingControlWindow(request.PhysicalPixelRegion, _settings.InterfaceLanguage);
                _recordingControl.StopRequested += (_, _) => StopRecording();
                _recordingControl.Show();
                _recordingControl.PlaceOutside(request.PhysicalPixelRegion);
                _stopRecordingMenuItem!.Enabled = true;
                overlay.Close();
                SetTrayTooltip(AppUi($"ClipRay | 正在录屏，按 {HotkeyBindingPolicy.FormatForDisplay(
                    _settings.StopRecordingHotkey,
                    _settings.StopRecordingSecondaryHotkey)} 停止",
                    $"ClipRay | Recording; press {HotkeyBindingPolicy.FormatForDisplay(
                    _settings.StopRecordingHotkey,
                    _settings.StopRecordingSecondaryHotkey)} to stop"));
            });
        }
        catch (Exception exception)
        {
            if (_recordingSession is { } failed)
            {
                try { await failed.StopAsync(); }
                catch { /* 停止失败时仍以原始 partial 路径作为可恢复边界。 */ }
            }
            _recordingSession = null;
            _recordingBorder?.Close();
            _recordingBorder = null;
            _recordingControl?.Close();
            _recordingControl = null;
            var partial = _recordingPaths.PartialPath;
            overlay.RestoreAfterRecordingFailure(
                RecordingFailureText.Format(exception, File.Exists(partial) ? partial : null,
                    _settings?.InterfaceLanguage ?? "zh-CN"));
        }
    }

    private async void StopRecording()
    {
        var session = _recordingSession;
        if (session is null || _recordingStopRequested) return;
        _recordingStopRequested = true;
        // 用户按下停止后录制反馈必须立刻消失；边框、计时条和托盘状态不能等待 finalize。
        DismissRecordingFeedback();
        SetTrayTooltip(AppUi("ClipRay | 正在保存录屏…", "ClipRay | Saving recording…"));
        // 编码器要把积压的帧编完再写 moov，2560x1600 下实测能到数秒。先把预览窗亮出来
        // 并置为等待态，停止到窗口出现之间就没有空档；收尾完成后再把成片接上去。
        RecordingPreviewWindow? preview = null;
        if (_recordingPaths?.FinalPath is { } pendingPath)
        {
            try
            {
                _recordingPreview?.Close();
                preview = new RecordingPreviewWindow(pendingPath, pending: true,
                    interfaceLanguage: _settings?.InterfaceLanguage ?? "zh-CN");
                _recordingPreview = preview;
                preview.Closed += (_, _) => _recordingPreview = null;
                preview.Show();
                preview.Activate();
            }
            catch
            {
                preview = null;
                _recordingPreview = null;
            }
        }
        try
        {
            // 编码收尾与文件 finalize 可能耗时；放到线程池执行，避免阻塞 UI 线程。
            await Task.Run(session.StopAsync);
            if (ReferenceEquals(_recordingSession, session) && _recordingPaths?.FinalPath is { } finalPath)
            {
                var copied = RecordingResultDelivery.TryCopyFile(finalPath, new WindowsRecordingFileClipboard());
                SetTrayTooltip(copied
                    ? AppUi($"ClipRay | 已保存并复制 {finalPath}", $"ClipRay | Saved and copied {finalPath}")
                    : AppUi($"ClipRay | 已保存 {finalPath}", $"ClipRay | Saved {finalPath}"));
                if (preview is not null)
                {
                    preview.AttachMedia(copied);
                }
                else
                {
                    ShowRecordingPreview(finalPath, copied);
                }
            }
        }
        catch
        {
            SetTrayTooltip($"ClipRay | partial: {_recordingPaths?.PartialPath}");
            preview?.Fail(AppUi("收尾失败：成片没有写成，partial 文件已保留。",
                "Finalizing failed. The video was not completed; the partial file was kept."));
        }
        finally
        {
            _recordingStopRequested = false;
            if (ReferenceEquals(_recordingSession, session)) _recordingSession = null;
            DismissRecordingFeedback();
        }
    }

    private void ShowRecordingPreview(string finalPath, bool copied)
    {
        try
        {
            _recordingPreview?.Close();
            _recordingPreview = new RecordingPreviewWindow(finalPath, initiallyCopied: copied,
                interfaceLanguage: _settings?.InterfaceLanguage ?? "zh-CN");
            _recordingPreview.Closed += (_, _) => _recordingPreview = null;
            _recordingPreview.Show();
            _recordingPreview.Activate();
        }
        catch
        {
            // 文件已经保存；预览失败时仍保留剪贴板文件投递结果。
        }
    }

    private void DismissRecordingFeedback()
    {
        _recordingBorder?.Close();
        _recordingBorder = null;
        _recordingControl?.Close();
        _recordingControl = null;
        if (_stopRecordingMenuItem is not null) _stopRecordingMenuItem.Enabled = false;
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private sealed class ScreenRegionCapture(ScreenCaptureService captureService) : IScrollRegionCapture
    {
        public Bitmap Capture(Rectangle physicalPixelRegion)
        {
            using var frame = captureService.CaptureRegion(physicalPixelRegion);
            return new Bitmap(frame.Bitmap);
        }
    }

    private sealed class ProbeRegionCapture(ScreenCaptureService captureService, string outputDirectory) : IScrollRegionCapture
    {
        private int _index;

        public Bitmap Capture(Rectangle physicalPixelRegion)
        {
            using var frame = captureService.CaptureRegion(physicalPixelRegion);
            var bitmap = new Bitmap(frame.Bitmap);
            Directory.CreateDirectory(outputDirectory);
            bitmap.Save(Path.Combine(outputDirectory, $"{_index++:D2}.png"), System.Drawing.Imaging.ImageFormat.Png);
            return bitmap;
        }
    }
}

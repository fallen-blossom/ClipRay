using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ClipRay.App.Recording;
using ClipRay.App.Capture;
using ClipRay.App.Editing;
using ClipRay.App.Infrastructure;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Overlay;
using ClipRay.App.Settings;
using ClipRay.Core.Geometry;
using ClipRay.App.Translation;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

internal static class BackgroundTask1Tests
{
    // This explicit list never shows windows, loads media, or uses real providers or the clipboard.
    internal static (string Name, Action Run)[] CreateCases() => new (string Name, Action Run)[]
        {
            ("draft", LateResultPreservesNewDraft),
            ("route", RouteChangeInvalidatesPendingResult),
            ("languages", LanguageChangeInvalidatesPendingResult),
            ("next-request", PreviousCompletionCannotResetNextRequest),
            ("late-failures", PreviousFailureCannotResetNextRequest),
            ("closed", ClosedWindowRejectsPendingResult),
            ("unsupported", ExplicitLocalLiveRequestIsRejected),
            ("route-policy", LiveTranslationRouteTests.ExplicitSupportedRouteWinsAndOnlyMissingRouteUsesLiveSettings),
            ("app-rejection", ActualAppEntriesRejectUnsupportedRouteBeforeStartup),
            ("copy-policy", TextCopyRetryHasFiniteOutcomes),
            ("window-copy", TranslationCopyFailurePreservesResult),
            ("extraction-copy", ExtractionCopyUsesTheSameFiniteRetry),
            ("result-failure", FailedTranslationKeepsPreviousResultAndAttribution),
            ("route-options", LocalLiveGuidanceAndLanguageChoicesArePreserved),
            ("seek", PreviewCommandsAndValueChangesSeek),
            ("scrub", ThumbDragPreservesPlaybackState),
            ("sync", ProgrammaticProgressDoesNotSeek),
        };

    public static int Run(string[] args)
    {
        var tests = CreateCases();
        var filterIndex = Array.IndexOf(args, "--case");
        if (filterIndex >= 0)
        {
            if (filterIndex + 1 >= args.Length) return 2;
            var names = args[filterIndex + 1].Split('|');
            tests = tests.Where(test => names.Contains(test.Name, StringComparer.Ordinal)).ToArray();
            if (tests.Length == 0) return 2;
        }
        var failed = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine($"PASS task-1/{test.Name}"); }
            catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL task-1/{test.Name}: {exception}"); }
        }
        Console.WriteLine($"TASK_1_BACKGROUND {tests.Length - failed}/{tests.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    private static void LateResultPreservesNewDraft()
    {
        var provider = new HeldProvider();
        var window = CreateWindow(provider);
        try
        {
            Find<TextBox>(window, "InputText").Text = "Original request A";
            Click(window, "TranslateButton");
            Require(provider.Calls.Count == 1 && provider.Calls[0].Request.OcrText == "Original request A", "Request did not capture the input.");
            Find<TextBox>(window, "InputText").Text = "New unsent draft B";
            provider.Complete(0, "Result A");
            DrainDispatcher();
            Require(Find<TextBox>(window, "InputText").Text == "New unsent draft B", "Late request A overwrote draft B.");
            Require(Find<TextBox>(window, "ResultText").Text != "Result A", "Invalidated result A was accepted for draft B.");
            Require(provider.Calls[0].Token.IsCancellationRequested, "Draft change did not cancel the old operation.");
            Require(Find<Button>(window, "TranslateButton").IsEnabled, "New draft cannot start a translation.");
        }
        finally { window.Close(); }
    }

    private static void RouteChangeInvalidatesPendingResult()
    {
        var provider = new HeldProvider();
        var window = CreateWindow(provider);
        try
        {
            Find<TextBox>(window, "InputText").Text = "Source A";
            Click(window, "TranslateButton");
            SelectRoute(window, TranslationRouteKind.LocalLmt);
            provider.Complete(0, "Old cloud result");
            DrainDispatcher();
            Require(Find<TextBox>(window, "ResultText").Text != "Old cloud result", "Old provider result was attributed to the newly selected local route.");
            Require(provider.Calls[0].Token.IsCancellationRequested, "Route change did not cancel the old operation.");
            Require(provider.Calls[0].Route == TranslationRouteKind.DeepSeekFlash, "The request route mutated after submission.");
            Require(Find<Button>(window, "TranslateButton").IsEnabled, "Cancelled request left the next request disabled.");
            Require(Equals(Find<Button>(window, "TranslateButton").Content, "开始翻译"), "Cancelled request still claims to be translating.");
        }
        finally { window.Close(); }
    }

    private static void LanguageChangeInvalidatesPendingResult()
    {
        foreach (var languageBox in new[] { "SourceLanguageBox", "TargetLanguageBox" })
        {
            var provider = new HeldProvider();
            var window = CreateWindow(provider);
            try
            {
                Find<TextBox>(window, "InputText").Text = "Source A";
                Click(window, "TranslateButton");
                Find<ComboBox>(window, languageBox).SelectedIndex = 1;
                provider.Complete(0, "Old language result");
                DrainDispatcher();
                Require(Find<TextBox>(window, "ResultText").Text != "Old language result", $"{languageBox} accepted an old-language result.");
                Require(provider.Calls[0].Token.IsCancellationRequested, $"{languageBox} did not cancel the old operation.");
                Require(provider.Calls[0].Request.SourceLanguage == "auto" && provider.Calls[0].Request.TargetLanguage == "简体中文", "The submitted languages were not fixed.");
            }
            finally { window.Close(); }
        }
    }

    private static void PreviousCompletionCannotResetNextRequest()
    {
        var provider = new HeldProvider();
        var window = CreateWindow(provider);
        try
        {
            Find<TextBox>(window, "InputText").Text = "Source A";
            Click(window, "TranslateButton");
            Find<TextBox>(window, "InputText").Text = "Source B";
            Click(window, "TranslateButton");
            Require(provider.Calls.Count == 2, "The new draft did not start a new request.");
            provider.Complete(0, "Late A");
            DrainDispatcher();
            Require(!Find<Button>(window, "TranslateButton").IsEnabled, "Old completion re-enabled the button while B is pending.");
            Require(Equals(Find<Button>(window, "TranslateButton").Content, "翻译中…"), "Old completion reset the current request caption.");
            provider.Complete(1, "Result B");
            DrainDispatcher();
            Require(Find<TextBox>(window, "ResultText").Text == "Result B", "The current result was not committed.");
            Require(Find<TextBox>(window, "InputText").Text == "Source B", "The current source was lost.");
            Require(Find<Button>(window, "TranslateButton").IsEnabled, "Current completion left translation disabled.");
        }
        finally { window.Close(); }
    }

    private static void ClosedWindowRejectsPendingResult()
    {
        var provider = new HeldProvider();
        var window = CreateWindow(provider);
        Find<TextBox>(window, "InputText").Text = "Source A";
        Click(window, "TranslateButton");
        window.Close();
        provider.Complete(0, "Closed result");
        DrainDispatcher();
        Require(Find<TextBox>(window, "ResultText").Text.Length == 0, "Closed window accepted a late result.");
        Require(provider.Calls[0].Token.IsCancellationRequested, "Closing did not cancel the current request.");
    }

    private static void PreviousFailureCannotResetNextRequest()
    {
        foreach (var cancelled in new[] { false, true })
        {
            var provider = new HeldProvider();
            var window = CreateWindow(provider);
            try
            {
                Find<TextBox>(window, "InputText").Text = "A";
                Click(window, "TranslateButton");
                Find<TextBox>(window, "InputText").Text = "B";
                Click(window, "TranslateButton");
                var progress = Find<TextBlock>(window, "ProgressStatus").Text;
                provider.Calls[0].Completion.SetException(cancelled ? new OperationCanceledException() : new InvalidOperationException("Private synthetic diagnostic"));
                DrainDispatcher();
                Require(!Find<Button>(window, "TranslateButton").IsEnabled
                    && Find<TextBlock>(window, "ProgressStatus").Text == progress, "Old failure/cancellation took ownership of B's progress.");
                provider.Complete(1, "Result B");
                DrainDispatcher();
                Require(Find<TextBox>(window, "ResultText").Text == "Result B", "B did not complete after an old failure.");
            }
            finally { window.Close(); }
        }
    }

    private static void ExplicitLocalLiveRequestIsRejected()
    {
        var window = new TranslationWindow((_, _, _) => throw new Exception("No provider may run."), TranslationRouteKind.LocalLmt);
        try
        {
            var live = Find<Button>(window, "LiveTranslateButton");
            Require(!live.IsEnabled, "Local LMT still offers an enabled live button.");
            var emitted = 0;
            window.LiveTranslationRequested += (_, _) => emitted++;
            Invoke(window, "OnLiveTranslate", live, new RoutedEventArgs());
            Require(emitted == 0, "The unsupported local live handler emitted a request.");
            Require(live.ToolTip?.ToString()?.Contains("不支持实时", StringComparison.Ordinal) == true, "Local live rejection lacks an understandable explanation.");
        }
        finally { window.Close(); }
    }

    private static void PreviewCommandsAndValueChangesSeek()
    {
        var preview = CreateSyntheticPreview(120);
        try
        {
            var slider = Field<Slider>(preview, "_seek");
            var media = Field<FakePlayback>(preview, "_playback");
            slider.SmallChange = 10;
            Require(Slider.IncreaseSmall.CanExecute(null, slider), "The keyboard slider command cannot execute.");
            Slider.IncreaseSmall.Execute(null, slider);
            Require(media.Position == TimeSpan.FromSeconds(10), "The arrow command moved the slider but did not seek.");
            Slider.DecreaseSmall.Execute(null, slider);
            Require(media.Position == TimeSpan.Zero, "The reverse arrow command did not seek.");
            slider.LargeChange = 20;
            Slider.IncreaseLarge.Execute(null, slider);
            Require(media.Position == TimeSpan.FromSeconds(20), "The page command did not seek.");
            Slider.MaximizeValue.Execute(null, slider);
            Require(media.Position == TimeSpan.FromSeconds(120), "The end command did not seek to the end.");
            Slider.MinimizeValue.Execute(null, slider);
            Require(media.Position == TimeSpan.Zero, "The home command did not seek to the start.");
            slider.Value = 60;
            Require(media.Position == TimeSpan.FromSeconds(60), "The track/value-change path did not seek.");
            Invoke(preview, "SyncTransport");
            Require(slider.Value == 60, "The progress tick erased the seek value.");
            Require(preview.TimeText == "1:00 / 2:00", "Seek time text does not describe the new media position.");
            preview.Close();
            Require(media.CloseCalls == 1, "Actual Closed handler did not close the injected playback owner once.");
        }
        finally { preview.Close(); }
    }

    private static void ActualAppEntriesRejectUnsupportedRouteBeforeStartup()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(WorkspaceLocator.Find(AppContext.BaseDirectory), "src", "ClipRay.App", "App.xaml.cs"));
        foreach (var signature in new[] { "private async void StartLiveTranslationSelection(", "private async void StartLiveTranslation(ClipRay.Core.Geometry.PixelRect" })
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            var body = source.IndexOf('{', start);
            Require(body > start && source[(body + 1)..].TrimStart().StartsWith("if (!TryResolveLiveRoute(requestedRoute, out", StringComparison.Ordinal),
                "Safety precondition: real App entry must reject unsupported routes before any side effect.");
        }
        // Do not run an App constructor, startup, resource initialization, OCR warmup or CredentialVault.
        // Only the first rejection branch needs an App receiver; all service and WPF App fields stay null.
        var app = RuntimeHelpers.GetUninitializedObject(typeof(ClipRay.App.App));
        foreach (var route in new[] { TranslationRouteKind.LocalLmt, TranslationRouteKind.FollowSettings, (TranslationRouteKind)99 })
        {
            Invoke(app, "StartLiveTranslationSelection", route, null!);
            Invoke(app, "StartLiveTranslation", new PixelRect(0, 0, 20, 20), route, null!);
            Invoke(app, "StartLiveTranslationRequest", new LiveTranslationStartRequest(null, route, "简体中文"));
            Invoke(app, "StartLiveTranslationRequest", new LiveTranslationStartRequest(new PixelRect(0, 0, 20, 20), route, "简体中文"));
            Require(Field<object?>(app, "_liveStartup") is null, "Unsupported request initialized a live owner.");
            foreach (var field in new[] { "_liveTranslationSession", "_liveTranslationStartup", "_liveCloudProvider", "_machineTranslationWarmup", "_translationServices", "_credentialVault" })
                Require(Field<object?>(app, field) is null, $"Unsupported request initialized {field}.");
        }
    }

    private static void TextCopyRetryHasFiniteOutcomes()
    {
        var attempts = 0;
        var waits = 0;
        var text = "Preserved translation\n第二行";
        Require(TextClipboardPublisher.TryCopy(text, value =>
        {
            Require(value == text, "Clipboard copy mutated the text.");
            if (++attempts < 3) throw new ExternalException("synthetic busy");
        }, pause => { Require(pause == TimeSpan.FromMilliseconds(25), "Unexpected retry delay."); waits++; }) == TextCopyResult.Copied,
            "Transient busy clipboard did not recover.");
        Require(attempts == 3 && waits == 2, "Busy retry budget is not bounded at three writes/two waits.");
        attempts = 0; waits = 0;
        Require(TextClipboardPublisher.TryCopy(text, _ => { attempts++; throw new ExternalException("synthetic busy"); }, _ => waits++) == TextCopyResult.Busy,
            "Persistent busy clipboard did not return an explicit outcome.");
        Require(attempts == 3 && waits == 2, "Persistent busy clipboard exceeded the retry budget.");
        attempts = 0;
        Require(TextClipboardPublisher.TryCopy(text, _ => { attempts++; throw new InvalidOperationException("synthetic unavailable"); }, _ => throw new Exception("No retry expected")) == TextCopyResult.Failed
            && attempts == 1, "Non-busy failure escaped or was retried as a busy clipboard.");
        Require(TextClipboardPublisher.TryCopy("", _ => throw new Exception("Empty copy must not publish.")) == TextCopyResult.Empty, "Empty text was published.");
    }

    private static void TranslationCopyFailurePreservesResult()
    {
        foreach (var language in new[] { "zh-CN", "en" })
        {
            var attempts = 0;
            var busy = true;
            var window = new TranslationWindow((_, _, _) => Task.FromResult(new TranslationResult(true, "Kept result", "Completed")),
                TranslationRouteKind.DeepSeekFlash, language, copy: text =>
                {
                    Require(text == "Kept result", "Copy used a different translation.");
                    attempts++;
                    if (busy) throw new ExternalException("synthetic busy");
                });
            try
            {
                Find<TextBox>(window, "InputText").Text = "Request A";
                Click(window, "TranslateButton");
                Click(window, "CopyButton");
                Require(attempts == 3 && Find<TextBox>(window, "ResultText").Text == "Kept result", "Busy copy escaped or erased the translation.");
                Require(Find<TextBlock>(window, "ProgressStatus").Text.Contains(language == "en" ? "busy" : "暂忙", StringComparison.Ordinal), "Copy failure was not shown in the interface language.");
                busy = false;
                Click(window, "CopyButton");
                Require(attempts == 4 && Find<TextBlock>(window, "ProgressStatus").Text.Contains(language == "en" ? "copied" : "已复制", StringComparison.Ordinal), "Retrying copy did not report success.");
                window.Close();
                Click(window, "CopyButton");
                Require(attempts == 4, "Closed translation window copied text.");
            }
            finally { window.Close(); }
        }
        var failure = new TranslationWindow((_, _, _) => throw new Exception("No translation expected"), copy: _ => throw new InvalidOperationException("synthetic failure"));
        try
        {
            Find<TextBox>(failure, "ResultText").Text = "Kept result";
            Click(failure, "CopyButton");
            Require(Find<TextBox>(failure, "ResultText").Text == "Kept result"
                && Find<TextBlock>(failure, "ProgressStatus").Text.Contains("暂不可用", StringComparison.Ordinal), "Non-busy copy failure escaped the UI or lost the text.");
        }
        finally { failure.Close(); }
    }

    private static void ExtractionCopyUsesTheSameFiniteRetry()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources["AppFont"] = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        foreach (var language in new[] { "zh-CN", "en" })
        {
            var clipboard = new BusyTextSink();
            using var frame = new CapturedFrame(new System.Drawing.Bitmap(32, 24), new System.Drawing.Rectangle(0, 0, 32, 24));
            var window = new SelectionOverlayWindow(frame, clipboardTextSink: clipboard,
                interactionSettings: new AppSettings { InterfaceLanguage = language },
                windowCandidateSource: new EmptyWindows(), uiElementCandidateSource: new EmptyElements());
            try
            {
                Find<TextBox>(window, "ExtractedTextBox").Text = "Original extracted text";
                Invoke(window, "OnCopyExtractedText", window, new RoutedEventArgs());
                Require(clipboard.Attempts == 3 && clipboard.Text == "Original extracted text", "Extraction did not use the finite text-copy policy.");
                Require(Find<TextBlock>(window, "ExtractionStatusText").Text.Contains(language == "en" ? "busy" : "暂忙", StringComparison.Ordinal), "Extraction busy outcome lacks localized feedback.");
                Require(Find<TextBox>(window, "ExtractedTextBox").Text == "Original extracted text", "Extraction copy failure erased the text.");
            }
            finally { window.Close(); }
        }
    }

    private static void FailedTranslationKeepsPreviousResultAndAttribution()
    {
        var provider = new HeldProvider();
        var window = CreateWindow(provider);
        try
        {
            Find<TextBox>(window, "InputText").Text = "A";
            Click(window, "TranslateButton");
            provider.Complete(0, "Valid A");
            DrainDispatcher();
            var attribution = Find<TextBox>(window, "ResultText").ToolTip;
            SelectRoute(window, TranslationRouteKind.LocalLmt);
            Find<TextBox>(window, "InputText").Text = "B";
            Click(window, "TranslateButton");
            provider.Calls[1].Completion.SetResult(new TranslationResult(false, "", "local-lmt-failed"));
            DrainDispatcher();
            Require(Find<TextBox>(window, "ResultText").Text == "Valid A", "Failed B erased successful A.");
            Require(Equals(Find<TextBox>(window, "ResultText").ToolTip, attribution), "Failed B changed A's provider/language attribution.");
            Require(Find<TextBox>(window, "InputText").Text == "B", "Failed B replaced the current draft.");
        }
        finally { window.Close(); }
    }

    private static void LocalLiveGuidanceAndLanguageChoicesArePreserved()
    {
        var window = new TranslationWindow((_, _, _) => throw new Exception("No provider expected"), TranslationRouteKind.LocalLmt, "en");
        try
        {
            var live = Find<Button>(window, "LiveTranslateButton");
            Require(!live.IsEnabled && live.ToolTip?.ToString()?.Contains("does not support live", StringComparison.Ordinal) == true, "English local live rejection is missing.");
            Require(System.Windows.Controls.ToolTipService.GetShowOnDisabled(live), "Disabled live button hides the reason.");
            Find<ComboBox>(window, "SourceLanguageBox").SelectedIndex = 3;
            Find<ComboBox>(window, "TargetLanguageBox").SelectedIndex = 2;
            SelectRoute(window, TranslationRouteKind.Machine);
            Require(live.IsEnabled && Find<ComboBox>(window, "SourceLanguageBox").SelectedIndex == 0
                && Find<ComboBox>(window, "TargetLanguageBox").SelectedIndex == 0, "Machine route lost its supported pair/live entry.");
            SelectRoute(window, TranslationRouteKind.LocalLmt);
            Require(Find<ComboBox>(window, "SourceLanguageBox").SelectedIndex == 3 && Find<ComboBox>(window, "TargetLanguageBox").SelectedIndex == 2, "Switching back lost non-machine language choices.");
            SelectRoute(window, TranslationRouteKind.DeepSeekFlash);
            Require(live.IsEnabled && live.ToolTip is null, "Supported cloud live entry inherited local rejection.");
        }
        finally { window.Close(); }
        var missing = new TranslationWindow((_, _, _) => throw new Exception("Missing route must not start"), TranslationRouteKind.LocalLmt,
            fileCheck: route => route == TranslationRouteKind.LocalLmt ? new TranslationFileCheck("fixture", ["GGUF"]) : null);
        try
        {
            Find<TextBox>(missing, "InputText").Text = "Kept draft";
            Click(missing, "TranslateButton");
            Require(!Find<Button>(missing, "TranslateButton").IsEnabled && !Find<Button>(missing, "LiveTranslateButton").IsEnabled, "Missing route enabled a start entry.");
            Require(Find<TextBox>(missing, "InputText").Text == "Kept draft", "Missing route erased the draft.");
        }
        finally { missing.Close(); }
    }

    private static void ThumbDragPreservesPlaybackState()
    {
        foreach (var wasPlaying in new[] { false, true })
        {
            var preview = CreateSyntheticPreview(120);
            try
            {
                var slider = Field<Slider>(preview, "_seek");
                Set(preview, "_playing", wasPlaying);
                slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                Require(!Field<bool>(preview, "_playing"), "Dragging did not pause playback.");
                Require(Field<FakePlayback>(preview, "_playback").PauseCalls == 1, "Dragging did not reach the playback pause operation.");
                slider.Value = 75;
                slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                Require(Field<FakePlayback>(preview, "_playback").Position == TimeSpan.FromSeconds(75), "Dragging did not seek to its target.");
                Require(Field<bool>(preview, "_playing") == wasPlaying, "Dragging changed the user's playback/pause choice.");
                Require(Field<FakePlayback>(preview, "_playback").PlayCalls == (wasPlaying ? 1 : 0), "Drag completion did not preserve the actual play/pause operation.");
                Require(preview.LastScrubTarget == TimeSpan.FromSeconds(75), "The drag target was not recorded.");
            }
            finally { preview.Close(); }
        }
    }

    private static void ProgrammaticProgressDoesNotSeek()
    {
        var preview = CreateSyntheticPreview(120);
        try
        {
            var media = Field<FakePlayback>(preview, "_playback");
            media.Position = TimeSpan.FromSeconds(25);
            Invoke(preview, "SyncTransport");
            Require(Field<Slider>(preview, "_seek").Value == 25, "Programmatic progress did not update the slider.");
            Require(preview.LastScrubTarget is null && media.Position == TimeSpan.FromSeconds(25), "Programmatic progress fed back into a user seek.");
            Set(preview, "_duration", TimeSpan.FromSeconds(10));
            Invoke(preview, "SyncTransport");
            Require(Field<Slider>(preview, "_seek").Value == 10 && media.Position == TimeSpan.FromSeconds(25)
                && preview.LastScrubTarget is null, "Duration/maximum synchronization triggered a user seek through value coercion.");
        }
        finally { preview.Close(); }
        var unknown = CreateSyntheticPreview(0);
        try
        {
            Field<Slider>(unknown, "_seek").Maximum = 100;
            Field<Slider>(unknown, "_seek").Value = 40;
            Require(Field<FakePlayback>(unknown, "_playback").Position == TimeSpan.Zero && unknown.LastScrubTarget is null, "Unknown duration created a seek target.");
        }
        finally { unknown.Close(); }
    }

    private static TranslationWindow CreateWindow(HeldProvider provider) => new(provider.Translate, TranslationRouteKind.DeepSeekFlash);
    private static RecordingPreviewWindow CreateSyntheticPreview(double duration)
    {
        // Pending keeps Source null and bypasses file inspection. There is no actual media or temp-file deletion.
        var preview = new RecordingPreviewWindow(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clipray-not-opened.mp4"),
            clipboard: null, initiallyCopied: false, pending: true, interfaceLanguage: "zh-CN", playback: new FakePlayback());
        Set(preview, "_duration", TimeSpan.FromSeconds(duration));
        var seek = Field<Slider>(preview, "_seek");
        seek.Maximum = duration;
        seek.IsEnabled = duration > 0;
        return preview;
    }
    private static void SelectRoute(TranslationWindow window, TranslationRouteKind route)
    {
        var box = Find<ComboBox>(window, "RouteBox");
        box.SelectedItem = box.Items.OfType<TranslationRouteOption>().Single(item => item.Kind == route);
    }
    private static T Find<T>(FrameworkElement window, string name) => (T)window.FindName(name);
    private static void Click(TranslationWindow window, string name) => Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    private sealed class EmptyWindows : IWindowCandidateSource { public IEnumerable<WindowSnapCandidate> Enumerate() => []; }
    private sealed class EmptyElements : IUiElementCandidateSource { public IReadOnlyList<PixelRect> GetHierarchy(System.Drawing.Point point, IntPtr window) => []; }
    private sealed class FakePlayback : IRecordingPreviewPlayback
    {
        public TimeSpan Position { get; set; }
        public TimeSpan NaturalDuration => TimeSpan.Zero;
        public int PlayCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public void Play() => PlayCalls++;
        public void Pause() => PauseCalls++;
        public void Close() => CloseCalls++;
    }
    private sealed class HeldProvider
    {
        public List<(TranslationRequest Request, TranslationRouteKind Route, CancellationToken Token, TaskCompletionSource<TranslationResult> Completion)> Calls { get; } = [];
        public Task<TranslationResult> Translate(TranslationRequest request, TranslationRouteKind route, CancellationToken token)
        {
            var completion = new TaskCompletionSource<TranslationResult>();
            Calls.Add((request, route, token, completion));
            return completion.Task;
        }
        // Ignore cancellation deliberately: late providers must still be unable to publish.
        public void Complete(int index, string text) => Calls[index].Completion.SetResult(new TranslationResult(true, text, "Completed"));
    }

    private sealed class BusyTextSink : IClipboardTextSink
    {
        public int Attempts { get; private set; }
        public string? Text { get; private set; }
        public void SetText(string text) { Attempts++; Text = text; throw new ExternalException("synthetic busy"); }
    }
}

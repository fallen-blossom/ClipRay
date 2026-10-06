using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Infrastructure;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Recording;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.App.Translation;
using Forms = System.Windows.Forms;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;

internal static class InterfaceLanguageTests
{
    public static void Run()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        var root = Path.Combine(Path.GetTempPath(), "clipray-interface-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        VerifyFirstRunLanguage(root);
        VerifyCredentialDeletionLanguage();
        VerifySettingsLoadWarningLanguage();
        VerifyRecordingCompanionLanguage(root);
        var initial = AppSettings.CreateDefault(root, "zh-CN") with { InterfaceLanguageFollowsWindows = false };
        VerifyMissingLocalSelection(initial, root);
        var english = initial with { InterfaceLanguage = "en" };
        var store = new SettingsStore(Path.Combine(root, "settings.json"), root);
        store.Save(english);
        if (store.Load().InterfaceLanguage != "en" || store.Load().InterfaceLanguageFollowsWindows
            || store.Load().TranslationTargetLanguage != initial.TranslationTargetLanguage)
            throw new Exception("Interface language persistence changed translation settings.");
        File.WriteAllText(Path.Combine(root, "old.json"), "{}");
        if (new SettingsStore(Path.Combine(root, "old.json"), root).Load() is not { InterfaceLanguage: "zh-CN", InterfaceLanguageFollowsWindows: false }
            || (initial with { InterfaceLanguage = "invalid" }).WithMissingDefaults(root).InterfaceLanguage != "zh-CN")
            throw new Exception("Legacy/invalid interface language fallback failed.");
        if (!AppInterfaceLanguage.IsOnlyInterfaceChange(initial, english)
            || AppInterfaceLanguage.IsOnlyInterfaceChange(initial, english with { CopyToClipboard = !initial.CopyToClipboard })
            || AppInterfaceLanguage.IsOnlyInterfaceChange(null, english))
            throw new Exception("Non-UI setting was classified as interface-only.");
        using var menu = new Forms.ContextMenuStrip();
        var item = new Forms.ToolStripMenuItem("停止录屏") { Enabled = false }; menu.Items.Add(item);
        AppInterfaceLanguage.UpdateTray(menu.Items, "en");
        if (item.Text != "Stop recording" || item.Enabled) throw new Exception("Tray translation changed enabled state.");
        AppInterfaceLanguage.UpdateTray(menu.Items, "zh-CN");
        if (item.Text != "停止录屏" || item.Enabled) throw new Exception("Tray reverse translation lost original text.");
        var memory = new MemoryStore(); var events = 0; var scheduleReads = 0; var startupReads = 0;
        var window = new SettingsWindow(initial, null, new SettingsApplyCoordinator(memory, new NoStartup(), "test.exe"),
            new EmptyVault(), () => throw new Exception("Unexpected provider access"),
            (_, _) => throw new Exception("Unexpected translation"), DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow),
            () => throw new Exception("UI-only save refreshed runtime data"), () => throw new Exception("Unexpected cost reset"),
            () => { scheduleReads++; return new(CleanupScheduleRegistrationState.Missing); }, () => { startupReads++; return false; });
        var initialScheduleReads = scheduleReads; var initialStartupReads = startupReads;
        var previous = initial;
        window.SettingsApplied += (_, value) => { if (!AppInterfaceLanguage.IsOnlyInterfaceChange(previous, value)) throw new Exception("Form normalized unrelated settings"); previous = value; events++; };
        try
        {
            ((System.Windows.Controls.ComboBox)window.FindName("InterfaceLanguageBox")).SelectedIndex = 2;
            memory.Fail = true;
            ((System.Windows.Controls.Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (events != 0 || window.Title != "ClipRay 设置") throw new Exception("Failed save propagated interface language.");
            memory.Fail = false;
            ((System.Windows.Controls.Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (events != 1 || window.Title != "ClipRay Settings" || memory.Saved?.InterfaceLanguage != "en"
                || !((TextBlock)window.FindName("VersionText")).Text.StartsWith("Version ", StringComparison.Ordinal))
                throw new Exception("UI language save did not propagate.");
            var frequency = (ComboBox)window.FindName("CleanupFrequencyBox");
            if (frequency.SelectedItem is not ComboBoxItem { Content: "Daily", Tag: "每天" }
                || ((CheckBox)window.FindName("StartupBox")).Content?.ToString() != "Start ClipRay when signing in to Windows (current user)"
                || ((Button)window.FindName("SaveSettingsButton")).Content?.ToString() != "Refresh now")
                throw new Exception("Translated display changed stable choices or made the form dirty.");
            if (((System.Windows.Documents.Run)window.FindName("StartupStateText")).Text != "Disabled"
                || ((System.Windows.Documents.Run)window.FindName("CleanupScheduleStateText")).Text != "Disabled (no task)"
                || ((Button)window.FindName("TestProviderButton")).Content?.ToString() != "Test translation")
                throw new Exception("Runtime status or primary provider action was not localized.");
            VerifyAccessibleName(window, "ScreenshotDirectoryBox", "Screenshot save folder");
            VerifyAccessibleName(window, "ChooseScreenshotFolderButton", "Browse for screenshot save folder");
            VerifyAccessibleName(window, "OpenRecordingFolderButton", "Open recording save folder");
            VerifyAccessibleName(window, "CleanupDailyTimeBox", "Scheduled cleanup time");
            VerifyAccessibleName(window, "ProviderBox", "Region translation provider");
            VerifyAccessibleName(window, "LocalTrialResultBox", "Test translation result");
            VerifyAccessibleName(window, "CredentialValueBox", "DeepSeek API key");
            VerifyAccessibleName(window, "RegionHotkeyBox", "Capture region primary shortcut");
            VerifyAccessibleName(window, "RegionSecondaryHotkeyBox", "Capture region alternate shortcut");
            VerifyAccessibleName(window, "StopLiveTranslationSecondaryHotkeyBox", "Stop live translation alternate shortcut");
            if (System.Windows.Automation.AutomationProperties.GetLiveSetting((TextBlock)window.FindName("StatusText"))
                != System.Windows.Automation.AutomationLiveSetting.Polite)
                throw new Exception("Settings status is not exposed as a polite live region.");
            var content = (FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(900, 700)); content.Arrange(new Rect(0, 0, 900, 700)); content.UpdateLayout();
            var image = new RenderTargetBitmap(900, 700, 96, 96, PixelFormats.Pbgra32); image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(root, "settings-en-partial.png")); encoder.Save(stream);
            var scroll = (ScrollViewer)window.FindName("SettingsScroll");
            foreach (var offset in new[] { 0d, 480d, 960d, 1440d, 1920d, 2400d })
            {
                content.Measure(new System.Windows.Size(820, 700)); content.Arrange(new Rect(0, 0, 820, 700)); content.UpdateLayout();
                scroll.ScrollToVerticalOffset(offset); content.UpdateLayout();
                var narrow = new RenderTargetBitmap(820, 700, 96, 96, PixelFormats.Pbgra32); narrow.Render(content);
                var page = new PngBitmapEncoder(); page.Frames.Add(BitmapFrame.Create(narrow));
                using var output = File.Create(Path.Combine(root, $"settings-en-{offset}.png")); page.Save(output);
            }
            var providerBox = (ComboBox)window.FindName("ProviderBox");
            var previousProvider = providerBox.SelectedItem;
            providerBox.SelectedItem = providerBox.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Content, "DeepSeek"));
            if (((FrameworkElement)window.FindName("CredentialCard")).Visibility != Visibility.Visible
                || ((TextBlock)window.FindName("CredentialSlotStatusText")).Text != "Local credential slot: empty"
                || ((TextBlock)window.FindName("DeepSeekCostSummaryText")).Text != "No DeepSeek usage costs recorded")
                throw new Exception("DeepSeek panel did not show localized cached state.");
            var captureTarget = (ComboBox)window.FindName("TranslationTargetLanguageBox");
            var englishTarget = captureTarget.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "English"));
            captureTarget.SelectedItem = englishTarget;
            providerBox.SelectedItem = previousProvider;
            if (captureTarget.SelectedItem is not ComboBoxItem { Tag: "简体中文", Content: "Simplified Chinese" }
                || captureTarget.Items.OfType<ComboBoxItem>().Count(item => item.IsEnabled) != 1)
                throw new Exception("Settings offered a Machine capture target that the package cannot translate.");
            providerBox.SelectedItem = providerBox.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Content, "DeepSeek"));
            if (captureTarget.SelectedItem != englishTarget
                || captureTarget.Items.OfType<ComboBoxItem>().Count(item => item.IsEnabled) != captureTarget.Items.Count)
                throw new Exception("Switching settings provider lost the prior target language.");
            captureTarget.SelectedItem = captureTarget.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "简体中文"));
            content.UpdateLayout(); scroll.ScrollToBottom(); content.UpdateLayout();
            var cloudImage = new RenderTargetBitmap(820, 700, 96, 96, PixelFormats.Pbgra32); cloudImage.Render(content);
            var cloudEncoder = new PngBitmapEncoder(); cloudEncoder.Frames.Add(BitmapFrame.Create(cloudImage));
            using (var output = File.Create(Path.Combine(root, "settings-en-deepseek.png"))) cloudEncoder.Save(output);
            var pathBox = (System.Windows.Controls.TextBox)window.FindName("LocalLmtModelPathBox");
            var previousPath = pathBox.Text;
            var fixtureOption = new ComboBoxItem { Content = "UI fixture (not a runtime model)",
                Tag = new LocalLmtDevelopmentModelOption("UI fixture", Path.Combine(root, "fixture.gguf"), true) };
            providerBox.Items.Add(fixtureOption); providerBox.SelectedItem = fixtureOption;
            if (((FrameworkElement)window.FindName("LocalModelPanel")).Visibility != Visibility.Visible
                || ((TextBlock)window.FindName("LocalModelStatusText")).Text != "Model selection changed; save to apply, or run a test.")
                throw new Exception("Local model panel did not show the selection state.");
            content.UpdateLayout(); scroll.ScrollToBottom(); content.UpdateLayout();
            var localImage = new RenderTargetBitmap(820, 700, 96, 96, PixelFormats.Pbgra32); localImage.Render(content);
            var localEncoder = new PngBitmapEncoder(); localEncoder.Frames.Add(BitmapFrame.Create(localImage));
            using (var output = File.Create(Path.Combine(root, "settings-en-local-fixture.png"))) localEncoder.Save(output);
            providerBox.SelectedItem = previousProvider;
            pathBox.Text = previousPath; providerBox.Items.Remove(fixtureOption);
            ((ComboBox)window.FindName("InterfaceLanguageBox")).SelectedIndex = 1;
            ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (events != 2 || frequency.SelectedItem is not ComboBoxItem { Content: "每天", Tag: "每天" }
                || !((TextBlock)window.FindName("VersionText")).Text.StartsWith("版本 ", StringComparison.Ordinal)
                || ((Button)window.FindName("SaveSettingsButton")).Content?.ToString() != "立即刷新")
                throw new Exception("Chinese restoration changed choices or draft state.");
            VerifyAccessibleName(window, "ScreenshotDirectoryBox", "截图保存目录");
            VerifyAccessibleName(window, "ChooseScreenshotFolderButton", "选择截图保存目录");
            VerifyAccessibleName(window, "RegionSecondaryHotkeyBox", "区域截图备用快捷键");
            if (scheduleReads != initialScheduleReads || startupReads != initialStartupReads)
                throw new Exception("Language change reread runtime registrations instead of rendering cached facts.");
            ((ComboBox)window.FindName("InterfaceLanguageBox")).SelectedIndex = 0;
            ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (events != 3 || memory.Saved?.InterfaceLanguageFollowsWindows != true
                || memory.Saved.InterfaceLanguage != AppInterfaceLanguage.ForWindowsUiCulture(culture))
                throw new Exception("Follow Windows mode was not restored or saved.");
            content.Measure(new System.Windows.Size(900, 700)); content.Arrange(new Rect(0, 0, 900, 700)); content.UpdateLayout();
            scroll.ScrollToTop(); content.UpdateLayout();
            var autoImage = new RenderTargetBitmap(900, 700, 96, 96, PixelFormats.Pbgra32); autoImage.Render(content);
            var autoEncoder = new PngBitmapEncoder(); autoEncoder.Frames.Add(BitmapFrame.Create(autoImage));
            using (var output = File.Create(Path.Combine(root, "settings-auto-partial.png"))) autoEncoder.Save(output);
            Console.WriteLine("Interface language checks passed; offscreen evidence: " + root);
        }
        finally { window.DiscardDraftOnClose = true; window.Close(); }
        if (CultureInfo.CurrentUICulture.Name != culture) throw new Exception("Interface language changed OCR culture.");
        VerifyTestSaveFlow(initial with { InterfaceLanguage = "en" });
        VerifyLateCloudResult(initial with { InterfaceLanguage = "en", Provider = "DeepSeek" });
        VerifyStandaloneTranslationWindows();
    }

    private static void VerifyFirstRunLanguage(string root)
    {
        var examples = new (string Culture, string Expected)[]
        {
            ("zh-CN", "zh-CN"), ("zh-Hans", "zh-CN"), ("zh-Hans-SG", "zh-CN"),
            ("zh-SG", "zh-CN"), ("zh-MY", "zh-CN"), ("en-US", "en"),
            ("en-GB", "en"), ("zh-TW", "en"), ("zh-Hant", "en"),
            ("fr-FR", "en"), ("ja-JP", "en")
        };
        foreach (var (culture, expected) in examples)
        {
            if (AppInterfaceLanguage.ForWindowsUiCulture(culture) != expected
                || AppSettings.CreateDefault(root, culture) is not { InterfaceLanguageFollowsWindows: true } created
                || created.InterfaceLanguage != expected)
                throw new Exception($"First-run interface language mismatch for {culture}.");
        }
        if (AppInterfaceLanguage.ForWindowsUiCulture(null) != "en")
            throw new Exception("Unknown Windows UI culture must use English fallback.");
        var missingSettings = new SettingsStore(Path.Combine(root, "missing-settings.json"), root);
        if (missingSettings.Load() is not { InterfaceLanguageFollowsWindows: true } missing
            || missing.InterfaceLanguage != AppInterfaceLanguage.ForWindowsUiCulture(CultureInfo.CurrentUICulture.Name))
            throw new Exception("Missing settings did not follow the current Windows UI culture.");
        var simulatedWindowsLanguage = "zh-CN";
        var automaticStore = new SettingsStore(Path.Combine(root, "automatic-settings.json"), root,
            () => simulatedWindowsLanguage);
        automaticStore.Save(AppSettings.CreateDefault(root, "en-US"));
        if (automaticStore.Load() is not { InterfaceLanguageFollowsWindows: true } automatic
            || automatic.InterfaceLanguage != "zh-CN")
            throw new Exception("Saved automatic mode did not follow the current Windows UI culture.");
        simulatedWindowsLanguage = "en-GB";
        if (automaticStore.Load().InterfaceLanguage != "en")
            throw new Exception("Automatic mode did not follow a later Windows display language change.");
    }

    private static void VerifyCredentialDeletionLanguage()
    {
        var prompts = new List<(string Message, string Title)>();
        var confirmed = CredentialDeletionPolicy.Confirm("ClipRay.DeepSeek", (message, title) =>
        {
            prompts.Add((message, title));
            return true;
        }, "en");
        if (!confirmed || prompts.Count != 2
            || prompts[0].Title != "Delete local API credential"
            || !prompts[0].Message.Contains("does not revoke the key", StringComparison.Ordinal)
            || prompts[1].Title != "Final confirmation"
            || !prompts[1].Message.Contains("ClipRay.DeepSeek", StringComparison.Ordinal)
            || !CredentialDeletionPolicy.Success("ClipRay.DeepSeek", "en").StartsWith("Deleted API credential", StringComparison.Ordinal))
            throw new Exception("English credential deletion confirmation or result is incomplete.");
    }

    private static void VerifySettingsLoadWarningLanguage()
    {
        const string warning = "设置文件无效，已使用默认设置。 自动清理计划尚未修复（错误 0x80070005）；请在设置中重试。";
        var english = SettingsLoadWarningText.ForInterface(warning, "en");
        if (!english.Contains("settings file is invalid", StringComparison.Ordinal)
            || !english.Contains("error 0x80070005", StringComparison.Ordinal)
            || english.Any(character => character is >= '\u4e00' and <= '\u9fff')
            || SettingsLoadWarningText.ForInterface(warning, "zh-CN") != warning)
            throw new Exception("Settings recovery warning did not follow the interface language.");
    }

    private static void VerifyRecordingCompanionLanguage(string root)
    {
        var partialPath = Path.Combine(root, "recording.partial.mp4");
        var failure = new RecordingStartupException(RecordingStartupStage.RecordingUi,
            new InvalidOperationException("private device name"));
        var english = RecordingFailureText.Format(failure, partialPath, "en");
        if (!english.Contains("recording controls", StringComparison.Ordinal)
            || !english.Contains(partialPath, StringComparison.Ordinal)
            || english.Contains("private device name", StringComparison.Ordinal))
            throw new Exception("English recording recovery text lost the stage or leaked private detail.");
        var preview = new RecordingPreviewWindow(Path.Combine(root, "pending.mp4"), pending: true,
            interfaceLanguage: "en");
        try
        {
            if (preview.Title != "ClipRay Recording Preview" || preview.Status != "Saving recording…")
                throw new Exception("Pending recording preview did not render in English.");
            var content = (FrameworkElement)preview.Content;
            content.Measure(new System.Windows.Size(820, 560));
            content.Arrange(new Rect(0, 0, 820, 560));
            content.UpdateLayout();
            var image = new RenderTargetBitmap(820, 560, 96, 96, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.Combine(root, "recording-preview-en-pending.png"))) encoder.Save(output);
            preview.Fail("Finalizing failed; the partial file was kept.");
            if (preview.IsPending || !preview.Status.Contains("partial file was kept", StringComparison.Ordinal))
                throw new Exception("Recording preview recovery status was lost.");
        }
        finally { preview.Close(); }
    }

    private static void VerifyStandaloneTranslationWindows()
    {
        var selector = new LiveTranslationSelectionWindow("en");
        try
        {
            if (((TextBlock)selector.FindName("StatusText")).Text
                != "Drag to select a live translation area. Press Esc to cancel.")
                throw new Exception("Live area selector did not render English guidance.");
        }
        finally { selector.Close(); }
        var translation = new TranslationWindow(
            (_, _, _) => Task.FromResult(new TranslationResult(true, "translated", "Translation completed.")),
            TranslationRouteKind.Machine, "en");
        try
        {
            if (translation.Title != "ClipRay Translation"
                || ((Button)translation.FindName("TranslateButton")).Content?.ToString() != "Translate"
                || ((ComboBox)translation.FindName("SourceLanguageBox")).Text != "Auto detect"
                || ((ComboBox)translation.FindName("RouteBox")).Text != "Local machine translation (offline)")
                throw new Exception("Standalone translation window did not render English controls.");
            VerifyAccessibleName(translation, "SourceLanguageBox", "Source language");
            VerifyAccessibleName(translation, "RouteBox", "Model for this session");
            VerifyAccessibleName(translation, "InputText", "Source text");
            VerifyAccessibleName(translation, "ResultText", "Translation result");
            var operation = translation.BeginOperation();
            if (!translation.TrySetResult(operation, "source", new TranslationResult(true, "translated", "Translation completed."))
                || ((TextBlock)translation.FindName("ProgressStatus")).Text != "Translation completed.")
                throw new Exception("Standalone translation status was not rendered in English.");
            var failedOperation = translation.BeginOperation();
            if (!translation.TrySetResult(failedOperation, "source", new TranslationResult(false, "", "machine-output-unchanged"))
                || !((TextBlock)translation.FindName("ProgressStatus")).Text.Contains("retry with another model", StringComparison.Ordinal))
                throw new Exception("Machine translation failure lacked English recovery guidance.");
            var sourceLanguages = (ComboBox)translation.FindName("SourceLanguageBox");
            var targetLanguages = (ComboBox)translation.FindName("TargetLanguageBox");
            var routes = (ComboBox)translation.FindName("RouteBox");
            if (targetLanguages.Items.OfType<ComboBoxItem>().Count(item => item.IsEnabled) != 1
                || targetLanguages.SelectedItem is not ComboBoxItem { Tag: "简体中文", Content: "Simplified Chinese" }
                || sourceLanguages.Items.OfType<ComboBoxItem>().Any(item => item.IsEnabled && item.Tag is ("Japanese" or "Korean")))
                throw new Exception("Machine route offered an unsupported language pair.");
            var translationContent = (FrameworkElement)translation.Content;
            translationContent.Measure(new System.Windows.Size(940, 720));
            translationContent.Arrange(new Rect(0, 0, 940, 720));
            translationContent.UpdateLayout();
            var translationImage = new RenderTargetBitmap(940, 720, 96, 96, PixelFormats.Pbgra32);
            translationImage.Render(translationContent);
            var translationEncoder = new PngBitmapEncoder(); translationEncoder.Frames.Add(BitmapFrame.Create(translationImage));
            using (var output = File.Create(Path.Combine(Path.GetTempPath(), "clipray-translation-machine-en-20260927.png")))
                translationEncoder.Save(output);
            routes.SelectedItem = routes.Items.Cast<TranslationRouteOption>().Single(option => option.Kind == TranslationRouteKind.LocalLmt);
            sourceLanguages.SelectedItem = sourceLanguages.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "Japanese"));
            targetLanguages.SelectedItem = targetLanguages.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "Japanese"));
            routes.SelectedItem = routes.Items.Cast<TranslationRouteOption>().Single(option => option.Kind == TranslationRouteKind.Machine);
            if (sourceLanguages.SelectedItem is not ComboBoxItem { Tag: "auto" }
                || targetLanguages.SelectedItem is not ComboBoxItem { Tag: "简体中文" })
                throw new Exception("Machine route did not select a supported language pair.");
            routes.SelectedItem = routes.Items.Cast<TranslationRouteOption>().Single(option => option.Kind == TranslationRouteKind.LocalLmt);
            if (sourceLanguages.SelectedItem is not ComboBoxItem { Tag: "Japanese" }
                || targetLanguages.SelectedItem is not ComboBoxItem { Tag: "Japanese" })
                throw new Exception("Switching routes lost the user's prior language choices.");
            routes.SelectedItem = routes.Items.Cast<TranslationRouteOption>().Single(option => option.Kind == TranslationRouteKind.Machine);
            LiveTranslationStartRequest? request = null;
            translation.LiveTranslationRequested += (_, value) => request = value;
            ((Button)translation.FindName("LiveTranslateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (request?.TargetLanguage != "简体中文")
                throw new Exception("English interface changed the stable translation target value.");
        }
        finally { translation.Close(); }

        var calls = 0;
        var missingTranslation = new TranslationWindow(
            (_, _, _) => { calls++; return Task.FromResult(new TranslationResult(true, "unexpected", "ok")); },
            TranslationRouteKind.Machine, "en",
            route => route is TranslationRouteKind.Machine or TranslationRouteKind.LocalLmt
                ? new TranslationFileCheck("fixture", ["model.bin"])
                : null);
        try
        {
            var routes = (ComboBox)missingTranslation.FindName("RouteBox");
            var translate = (Button)missingTranslation.FindName("TranslateButton");
            var live = (Button)missingTranslation.FindName("LiveTranslateButton");
            if (routes.Items.Cast<TranslationRouteOption>().Select(option => option.Kind)
                    .SequenceEqual([TranslationRouteKind.Machine, TranslationRouteKind.DeepSeekFlash]) == false
                || translate.IsEnabled || live.IsEnabled
                || !((TextBlock)missingTranslation.FindName("ProgressStatus")).Text.Contains("model.bin", StringComparison.Ordinal))
                throw new Exception("Missing local translation files were still offered as executable routes.");
            ((System.Windows.Controls.TextBox)missingTranslation.FindName("InputText")).Text = "source";
            translate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (calls != 0) throw new Exception("Missing local route invoked the provider.");
            routes.SelectedItem = routes.Items.Cast<TranslationRouteOption>().Single(option => option.Kind == TranslationRouteKind.DeepSeekFlash);
            if (!translate.IsEnabled || !live.IsEnabled
                || ((TextBlock)missingTranslation.FindName("ProgressStatus")).Text != "Ready")
                throw new Exception("An explicit switch away from the missing local route did not recover.");
        }
        finally { missingTranslation.Close(); }

        var stop = new ClipRay.App.LiveTranslation.LiveTranslationStopControlWindow(
            new System.Drawing.Rectangle(100, 100, 320, 240), "en");
        try
        {
            if (((Button)stop.FindName("StopButton")).Content?.ToString() != "Stop translation"
                || ((TextBlock)stop.FindName("EscapeHint")).Text != "Press Esc twice to exit"
                || stop.FindName("WorkbenchButton") is not null)
                throw new Exception("Live stop control has an unexpected action.");
            stop.ShowStatus("本地机翻 · 简体中文\n已更新 · 等待画面变化", canRetry: true);
            if (!((TextBlock)stop.FindName("StatusText")).Text.Contains("Updated · waiting for screen changes", StringComparison.Ordinal)
                || ((Button)stop.FindName("RetryButton")).Visibility != Visibility.Visible)
                throw new Exception("Live stop control did not localize session status or expose retry.");
        }
        finally { stop.Close(); }
    }

    private static void VerifyAccessibleName(FrameworkElement window, string elementName, string expected)
    {
        if (window.FindName(elementName) is not DependencyObject element
            || System.Windows.Automation.AutomationProperties.GetName(element) != expected)
            throw new Exception($"Accessible name for {elementName} was not localized: expected {expected}.");
    }

    private static void VerifyMissingLocalSelection(AppSettings initial, string root)
    {
        foreach (var fileName in new[] { "LMT-60-0.6B.Q4_K_M.gguf", "custom.gguf", "" })
        {
            var path = fileName.Length == 0 ? "" : Path.Combine(root, "missing", fileName);
            var settings = initial with { Provider = "Local LMT", LocalLmtModelPath = path };
            var memory = new MemoryStore();
            var window = new SettingsWindow(settings, null,
                new SettingsApplyCoordinator(memory, new NoStartup(), "test.exe"), new EmptyVault(),
                () => throw new Exception("Unexpected provider access"),
                (_, _) => throw new Exception("Unexpected translation"), DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow),
                () => DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow), () => throw new Exception("Unexpected cost reset"),
                () => new(CleanupScheduleRegistrationState.Missing), () => false);
            try
            {
                var selected = ((ComboBox)window.FindName("ProviderBox")).SelectedItem as ComboBoxItem;
                if (selected?.Tag is not LocalLmtDevelopmentModelOption option
                    || (path.Length > 0 && option.Path != path))
                    throw new Exception("Missing explicit model changed provider or model identity.");
                if (((FrameworkElement)window.FindName("LocalModelPanel")).Visibility != Visibility.Visible)
                    throw new Exception("Missing model hid its recovery panel.");
                ((ComboBox)window.FindName("InterfaceLanguageBox")).SelectedIndex = 2;
                ((Button)window.FindName("SaveSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (memory.Saved?.Provider != "Local LMT" || (path.Length > 0 && memory.Saved.LocalLmtModelPath != path))
                    throw new Exception("Saving missing model substituted its provider or path.");
            }
            finally { window.Close(); }
        }
    }

    private static void VerifyTestSaveFlow(AppSettings initial)
    {
        var memory = new MemoryStore { Fail = true }; var calls = 0; var cloudCalls = 0;
        var window = new SettingsWindow(initial, null, new SettingsApplyCoordinator(memory, new NoStartup(), "test.exe"),
            new EmptyVault(), () => { cloudCalls++; throw new Exception("Unexpected cloud access"); },
            (_, _) => { calls++; if (calls == 2) throw new IOException("private diagnostic fixture"); if (calls == 3) throw new OperationCanceledException(); return Task.FromResult(new TranslationResult(true, "fixture translation", "fixture-ok")); },
            DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow), () => DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow),
            () => throw new Exception("Unexpected reset"), () => new(CleanupScheduleRegistrationState.Missing), () => false);
        try
        {
            ((CheckBox)window.FindName("CopyToClipboardBox")).IsChecked = !initial.CopyToClipboard;
            var test = (Button)window.FindName("TestProviderButton");
            test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (calls != 0 || string.IsNullOrWhiteSpace(((TextBlock)window.FindName("StatusText")).Text))
                throw new Exception("Test did not report save failure before provider access.");
            memory.Fail = false;
            test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (calls != 1 || memory.Saved?.CopyToClipboard == initial.CopyToClipboard
                || ((Button)window.FindName("SaveSettingsButton")).Content?.ToString() != "Refresh now")
                throw new Exception("Successful test did not synchronize the saved draft.");
            test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var resultBox = (System.Windows.Controls.TextBox)window.FindName("LocalTrialResultBox");
            var localStatus = (TextBlock)window.FindName("LocalModelStatusText");
            if (calls != 2 || resultBox.Text.Contains("fixture translation")
                || localStatus.Text.Contains("succeeded") || !test.IsEnabled
                || resultBox.Text.Contains("private diagnostic fixture"))
                throw new Exception("Failed retry retained success, leaked diagnostics, or disabled recovery.");
            test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (calls != 3 || resultBox.Text.Length != 0 || !localStatus.Text.Contains("cancelled") || !test.IsEnabled)
                throw new Exception("Cancelled provider test did not expose a recoverable state.");
            test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (calls != 4 || resultBox.Text != "fixture translation" || !test.IsEnabled)
                throw new Exception("Retry did not recover after a provider exception.");
            var live = (ComboBox)window.FindName("LiveProviderBox");
            live.SelectedItem = live.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "DeepSeek"));
            ((PasswordBox)window.FindName("CredentialValueBox")).Password = "not-a-key";
            ((Button)window.FindName("SaveCredentialButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (cloudCalls != 0
                || !((TextBlock)window.FindName("StatusText")).Text.Contains("beginning with sk-", StringComparison.Ordinal)
                || ((TextBlock)window.FindName("StatusText")).Text.Contains("not-a-key", StringComparison.Ordinal))
                throw new Exception("DeepSeek credential validation lacked recoverable English guidance or leaked the candidate.");
        }
        finally { window.DiscardDraftOnClose = true; window.Close(); }
    }
    private static void VerifyLateCloudResult(AppSettings initial)
    {
        var provider = new DelayedProvider();
        var window = new SettingsWindow(initial, null, new SettingsApplyCoordinator(new MemoryStore(), new NoStartup(), "test.exe"),
            new EmptyVault(), () => provider, (_, _) => throw new Exception("Unexpected local inference"),
            DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow), () => DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow),
            () => throw new Exception("Unexpected reset"), () => new(CleanupScheduleRegistrationState.Missing), () => false);
        var priorContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            ((Button)window.FindName("TestProviderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!provider.Started) throw new Exception("Cloud test fixture did not start.");
            var choices = (ComboBox)window.FindName("ProviderBox");
            var fixture = new ComboBoxItem { Content = "Delayed-result fixture", Tag = new LocalLmtDevelopmentModelOption("Fixture", "fixture.gguf", true) };
            choices.Items.Add(fixture); choices.SelectedItem = fixture;
            var expected = ((TextBlock)window.FindName("LocalModelStatusText")).Text;
            provider.Completion.SetResult(new TranslationResult(true, "cloud result", "fixture-ok"));
            var frame = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            if (!provider.Token.IsCancellationRequested
                || ((TextBlock)window.FindName("LocalModelStatusText")).Text != expected
                || !string.IsNullOrEmpty(((System.Windows.Controls.TextBox)window.FindName("LocalTrialResultBox")).Text))
                throw new Exception("Late cloud result contaminated the new local model selection.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(priorContext); window.DiscardDraftOnClose = true; window.Close(); }
    }

    private sealed class DelayedProvider : IDeepSeekTranslationProvider
    {
        public TaskCompletionSource<TranslationResult> Completion { get; } = new();
        public bool Started; public CancellationToken Token;
        public Task<TranslationResult> TestConnectionAsync(string name, string model, CancellationToken token)
        { Started = true; Token = token; return Completion.Task; }
        public Task<TranslationResult> TestCandidateConnectionAsync(string key, string model, CancellationToken token) => throw new NotSupportedException();
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, string name, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<TranslationResult>> TranslateBlocksAsync(IReadOnlyList<TranslationRequest> requests, string name, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class MemoryStore : ISettingsStore
    {
        public bool Fail; public AppSettings? Saved;
        public AppSettings Load() => throw new NotSupportedException();
        public SettingsLoadResult LoadRecoverable() => throw new NotSupportedException();
        public void Save(AppSettings settings) { if (Fail) throw new IOException("fixture save failure"); Saved = settings; }
    }
    private sealed class NoStartup : IStartupRegistration
    { public void SetEnabled(bool enabled, string executablePath) => throw new Exception("UI save touched startup"); }
    private sealed class EmptyVault : ICredentialVault
    {
        public IReadOnlyList<CredentialMetadata> List() => [];
        public void Save(string name, string provider, string value) => throw new NotSupportedException();
        public void Delete(string name) => throw new NotSupportedException();
        public string? GetForUse(string name) => throw new NotSupportedException();
    }
}

using System.Drawing;
using System.IO;
using ClipRay.App.Infrastructure;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Settings;
using ClipRay.App.Translation;
using System.Text.Json.Nodes;

internal static class LiveRepairTests
{
    public static void FailuresRecoverWithoutStoppingOrRetryCounter()
    {
        var calls = 0;
        var block = new ClipRay.App.Ocr.OcrTextBlock("body", new ClipRay.Core.Geometry.PixelRect(0,0,20,20), []);
        var region = new VisualTextRegion("body", "body", [block], block.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        using var session = new LiveTranslationSession(new Rectangle(0, 0, 20, 20), _ => new Bitmap(20,20), (_, _) =>
        {
            calls++;
            if (calls < 3) throw new IOException("private text");
            var item = new InPlaceLayoutItem(block, "正文", block.Bounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [item]) { SourceRegions=[region] });
        }, TranslationRouteKind.Machine, (_, _) => Task.CompletedTask, validateLayoutRegions: true);
        session.FrameReady += (_, _) => session.Stop();
        session.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (calls != 3)
            throw new Exception("An unchanged failed frame never recovered in the active live session.");
        if (session.State == LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
            throw new Exception("Routine translation failures stopped the live selection.");
        if (session.Status.Contains("重试", StringComparison.Ordinal)
            || session.Status.Contains("/3", StringComparison.Ordinal))
            throw new Exception("Live failure status still exposes a three-attempt retry counter.");
    }

    public static void OverlayKeepsVisibleRegionBorder()
    {
        var overlay = new LiveTranslationOverlayWindow(new Rectangle(0, 0, 200, 100));
        if (overlay.FindName("SelectionBorder") is not System.Windows.Controls.Border)
            throw new Exception("Running live translation has no persistent region frame.");
    }

    public static void LiveHotkeysExistInSettings()
    {
        if (typeof(AppSettings).GetProperty("StartLiveTranslationHotkey") is null
            || typeof(AppSettings).GetProperty("StopLiveTranslationHotkey") is null)
            throw new Exception("Live start and stop cannot be configured.");
        var root = WorkspaceLocator.Find(AppContext.BaseDirectory);
        var xaml = File.ReadAllText(Path.Combine(root, "src", "ClipRay.App", "Settings", "SettingsWindow.xaml"));
        if (!xaml.Contains("StartLiveTranslationHotkeyBox") || !xaml.Contains("StopLiveTranslationHotkeyBox"))
            throw new Exception("Live hotkeys have no settings controls.");
    }

    public static void FailureStaysActiveWithObservableReason()
    {
        var delays = 0;
        using var budget = new CancellationTokenSource();
        using var session = new LiveTranslationSession(new Rectangle(0, 0, 20, 20), _ => new Bitmap(20, 20),
            (_, _) => throw new IOException("secret text"), TranslationRouteKind.Machine, (_, _) =>
            {
                if (++delays == 4) budget.Cancel();
                return Task.CompletedTask;
            }, validateLayoutRegions: true);
        session.StartAsync(budget.Token).GetAwaiter().GetResult();
        if (session.State == LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
            throw new Exception("A routine failure incorrectly became terminal.");
        var value = typeof(LiveTranslationSession).GetProperty("LastErrorCategory")?.GetValue(session) as string;
        if (value != "runtime-failed") throw new Exception("Active session has no safe observable error reason.");
    }

    public static void LiveUsesSharedTypographyRenderer()
    {
        var overlay = new LiveTranslationOverlayWindow(new Rectangle(0, 0, 200, 100));
        var block = new ClipRay.App.Ocr.OcrTextBlock("source", new ClipRay.Core.Geometry.PixelRect(0, 0, 100, 40), []);
        var item = new InPlaceLayoutItem(block, "译文", block.Bounds, InPlaceBackgroundMode.FlatSampled, Color.White, Color.Black);
        overlay.Apply(new LiveTranslationFrame("Machine", "completed", [item]));
        var canvas = (System.Windows.Controls.Canvas)overlay.FindName("TranslationCanvas");
        if (canvas.Children.Count != 1 || canvas.Children[0] is not System.Windows.Controls.Image)
            throw new Exception("Live uses an independent fixed-font renderer instead of the shared typography plan.");
    }

    public static void OverlayRejectsFailedViewportResults()
    {
        var overlay = new LiveTranslationOverlayWindow(new Rectangle(0, 0, 200, 100));
        var failed = new LiveTranslationFrame("Machine", "translation-failed", [], "viewport-result-failed")
            { ResultState = LiveTranslationViewportResultState.Failed, ExpectedLayoutCount = 4 };
        if (overlay.Apply(failed, 42))
            throw new Exception("Overlay accepted a failed viewport result.");
        var canvas = (System.Windows.Controls.Canvas)overlay.FindName("TranslationCanvas");
        if (canvas.Children.Count != 0)
            throw new Exception("Failed viewport result changed the visible overlay.");
    }

    public static void NewHotkeysDoNotResetLegacySettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = AppSettings.CreateDefault(root) with { RegionCaptureHotkey = ClipRay.App.Infrastructure.GlobalHotkey.Parse("Ctrl+Win+T") };
            var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(settings))!.AsObject();
            json.Remove(nameof(AppSettings.StartLiveTranslationHotkey));
            json.Remove(nameof(AppSettings.StopLiveTranslationHotkey));
            var file = Path.Combine(root, "settings.json");
            File.WriteAllText(file, json.ToJsonString());
            var loaded = new SettingsStore(file, root).LoadRecoverable();
            if (loaded.Settings.RegionCaptureHotkey != settings.RegionCaptureHotkey)
                throw new Exception("Adding live hotkeys reset the user's existing capture shortcut.");
            loaded.Settings.ValidateHotkeys();
        }
        finally { Directory.Delete(root, true); }
    }

}

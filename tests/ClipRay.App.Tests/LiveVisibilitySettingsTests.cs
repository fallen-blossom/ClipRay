using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClipRay.App.Settings;

internal static class LiveVisibilitySettingsTests
{
    public static void SelectedRegionBorderIsVisible()
    {
        if (!ClipRay.App.LiveTranslation.LiveTranslationCaptureVisibilityPolicy.SelectedRegionBorderVisible)
            throw new Exception("Live translation must keep the selected-region blue border visible.");
    }

    public static void BorderVisibilitySurvivesRefresh()
    {
        foreach (var visible in new[] { false, true })
        {
            var overlay = new ClipRay.App.LiveTranslation.LiveTranslationOverlayWindow(
                new System.Drawing.Rectangle(10, 10, 600, 400), visible);
            try
            {
                overlay.Clear();
                var border = (System.Windows.Controls.Border)overlay.FindName("SelectionBorder");
                var expected = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                if (border.Visibility != expected)
                    throw new Exception("Refreshing the overlay changed the selected border visibility.");
            }
            finally { overlay.Close(); }
        }
    }

    public static void OverlayKeepsTheSelectedPhysicalPixelBounds()
    {
        var expected = new System.Drawing.Rectangle(25, 47, 818, 86);
        var overlay = new ClipRay.App.LiveTranslation.LiveTranslationOverlayWindow(expected, showBorder: true);
        try
        {
            overlay.Show();
            overlay.ReassertPhysicalBounds();
            var handle = new WindowInteropHelper(overlay).Handle;
            if (!GetWindowRect(handle, out var actual))
                throw new Exception("Could not read the live overlay window bounds.");
            var observed = System.Drawing.Rectangle.FromLTRB(actual.Left, actual.Top, actual.Right, actual.Bottom);
            if (observed != expected)
                throw new Exception($"Live overlay was DPI-scaled away from the selected physical pixels: {observed}.");
        }
        finally { overlay.Close(); }
    }

    public static void VisibilityChoicesPersistIndependently()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{}")!;
        if (!legacy.ShowLiveTranslationStopControl || legacy.ShowLiveTranslationBorder || legacy.ShowLiveTranslationInRecordings)
            throw new Exception("Release settings must keep the stop control visible without a live-region border or recording capture by default.");
        foreach (var stop in new[] { false, true })
        foreach (var border in new[] { false, true })
        foreach (var inRecordings in new[] { false, true })
        {
            var settings = AppSettings.CreateDefault(Environment.CurrentDirectory) with
            {
                ShowLiveTranslationStopControl = stop,
                ShowLiveTranslationBorder = border,
                ShowLiveTranslationInRecordings = inRecordings,
            };
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            if (restored.ShowLiveTranslationStopControl != stop ||
                restored.ShowLiveTranslationBorder != border ||
                restored.ShowLiveTranslationInRecordings != inRecordings)
                throw new Exception("Live visibility choices did not persist independently.");
        }
    }

    public static void RecordingVisibilityControlsCaptureExclusion()
    {
        var privateOverlay = new ClipRay.App.LiveTranslation.LiveTranslationOverlayWindow(
            new System.Drawing.Rectangle(10, 10, 600, 400), showBorder: true, showInRecordings: false);
        var recordedOverlay = new ClipRay.App.LiveTranslation.LiveTranslationOverlayWindow(
            new System.Drawing.Rectangle(10, 10, 600, 400), showBorder: true, showInRecordings: true);
        try
        {
            if (!privateOverlay.ExcludeFromCaptureRequested)
                throw new Exception("Private live translation must request capture exclusion.");
            if (recordedOverlay.ExcludeFromCaptureRequested)
                throw new Exception("Recording-visible live translation must not request capture exclusion.");
        }
        finally
        {
            privateOverlay.Close();
            recordedOverlay.Close();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

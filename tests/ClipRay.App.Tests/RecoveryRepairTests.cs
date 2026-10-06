using System.Drawing;
using System.IO;
using System.Reflection;
using ClipRay.App.Capture;
using ClipRay.App.Overlay;
using ClipRay.App.Scrolling;
using ClipRay.Core.Geometry;

internal static class RecoveryRepairTests
{
    public static void PreserveSurvivesDisposeAndOrphanCleanup()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = new ScrollCaptureSession(new Rectangle(0, 0, 20, 20), ScrollCaptureMode.Automatic,
                new FixtureCapture(), new Input(), new Settler(), root, new ScrollCaptureOptions(MaximumFrames: 1));
            var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            result.PreserveForRecovery();
            result.Dispose();
            if (!Directory.Exists(result.TempSessionDirectory)) throw new Exception("Recovery disappeared during Dispose.");
            ScrollCaptureTempStore.CleanupOrphans(root);
            if (!Directory.Exists(result.TempSessionDirectory)) throw new Exception("Recovery disappeared on next startup cleanup.");
            result.CleanupAfterSuccessfulOutput();
            if (Directory.Exists(result.TempSessionDirectory)) throw new Exception("Explicit successful-output cleanup failed.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static void CaptureDeliveryFailureKeepsEditingState()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application();
        application.Resources["AppFont"] = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        var bitmap = new Bitmap(40, 40);
        var overlay = new SelectionOverlayWindow(new CapturedFrame(bitmap, new Rectangle(0, 0, 40, 40)), new PixelRect(0, 0, 30, 30));
        var delivery = typeof(SelectionOverlayWindow).GetProperty("CaptureDelivery");
        if (delivery is null) throw new Exception("Capture has no acknowledged delivery boundary.");
        delivery.SetValue(overlay, (Func<Bitmap, bool>)(_ => false));
        var closed = false;
        overlay.Closed += (_, _) => closed = true;
        typeof(SelectionOverlayWindow).GetMethod("ConfirmSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, null);
        if (closed) throw new Exception("Failed save closed the editor.");
        _ = bitmap.GetPixel(0, 0);
        overlay.Close();
    }

    public static void ClipboardFailureDoesNotLoseSavedImage()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-delivery-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var bitmap = new Bitmap(20, 20);
            var output = new ImageOutputService(root, true, clipboard: _ => throw new IOException("busy"));
            var delivered = output.SaveWithDelivery(bitmap);
            if (!File.Exists(delivered.Path) || !delivered.ClipboardRequested || delivered.ClipboardCopied)
                throw new Exception("Saved image and clipboard states were conflated.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    public static void LongPreviewCanRetryClipboardWithoutOwningSave()
    {
        using var bitmap = new Bitmap(20, 20);
        _ = System.Windows.Application.Current ?? new System.Windows.Application();
        var retries = 0;
        var preview = new ScrollCaptureResultWindow(bitmap, ScrollCaptureStopReason.FrameLimit,
            Path.Combine(Path.GetTempPath(), "already-saved.png"), clipboardCopied: false,
            copyAgain: () => ++retries > 1);
        var copy = typeof(ScrollCaptureResultWindow).GetMethod("CopyAgain", BindingFlags.Instance | BindingFlags.NonPublic)!;
        copy.Invoke(preview, null);
        copy.Invoke(preview, null);
        if (retries != 2) throw new Exception("The saved long capture could not retry clipboard delivery in preview.");
        preview.Close();
    }

    public static void DirtySettingsRequireAnExplicitCloseDecision()
    {
        var type = typeof(ClipRay.App.Settings.SettingsDraftState);
        var close = type.GetMethod("CanClose");
        if (close is null) throw new Exception("Settings dirty state is ignored when closing.");
        var choice = type.Assembly.GetType("ClipRay.App.Settings.SettingsDraftCloseChoice")!;
        var state = new ClipRay.App.Settings.SettingsDraftState("saved");
        state.Observe("edited");
        bool Decide(string name, bool saveWorks) => (bool)close.Invoke(state,
            [Enum.Parse(choice, name), (Func<bool>)(() => saveWorks)])!;
        if (Decide("Cancel", true) || Decide("Save", false) || !Decide("Discard", false) || !Decide("Save", true))
            throw new Exception("Settings close decision lost a dirty or failed-save draft.");
    }

    private sealed class FixtureCapture : IScrollRegionCapture { public Bitmap Capture(Rectangle region) => new(region.Width, region.Height); }
    private sealed class Input : IScrollInputService { public Task SendWheelAsync(Point target, int delta, CancellationToken token) => Task.CompletedTask; }
    private sealed class Settler : IScrollSettler { public Task WaitForSettleAsync(TimeSpan delay, CancellationToken token) => Task.CompletedTask; }
}

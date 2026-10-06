using System.Drawing;

namespace ClipRay.App.Recording;

public static class WindowCaptureGeometry
{
    public static void EnsureStable(
        Rectangle originalWindowBounds,
        Rectangle currentWindowBounds,
        Size capturedFrameSize)
    {
        if (currentWindowBounds != originalWindowBounds || capturedFrameSize != originalWindowBounds.Size)
            throw new InvalidOperationException("window-capture-geometry-changed");
    }

    public static Rectangle MapScreenRegion(
        Rectangle windowBounds,
        Size capturedFrameSize,
        Rectangle screenRegion)
    {
        if (capturedFrameSize != windowBounds.Size)
            throw new InvalidOperationException("window-capture-frame-size-mismatch");
        if (!windowBounds.Contains(screenRegion))
            throw new ArgumentOutOfRangeException(nameof(screenRegion), "window-capture-region-outside-source-window");
        return new Rectangle(
            screenRegion.X - windowBounds.X,
            screenRegion.Y - windowBounds.Y,
            screenRegion.Width,
            screenRegion.Height);
    }
}

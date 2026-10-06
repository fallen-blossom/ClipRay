namespace ClipRay.App.Scrolling;

/// <summary>长截图预览的缩放边界与光标锚点滚动计算，保持窗口代码只负责呈现。</summary>
public static class ScrollCaptureZoomPolicy
{
    public const double MinimumZoom = 0.10;
    public const double MaximumZoom = 4.00;

    public static double Clamp(double zoom) => Math.Clamp(zoom, MinimumZoom, MaximumZoom);

    public static double FitWidth(double viewportWidth, double imageWidth) =>
        imageWidth <= 0 ? 1d : Clamp(viewportWidth / imageWidth);

    public static double OffsetForPointer(double offset, double pointer, double oldZoom, double newZoom) =>
        Math.Max(0, (offset + pointer) * newZoom / oldZoom - pointer);
}

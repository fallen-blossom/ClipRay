using System.Drawing;
using ClipRay.App.Capture;
using Forms = System.Windows.Forms;

namespace ClipRay.App.LiveTranslation;

/// <summary>Captures the current pixels of one fixed physical screen region.</summary>
internal sealed class LiveTranslationScreenCapture : IAsyncDisposable
{
    private readonly Rectangle _screenRegion;
    private readonly Func<Rectangle, Bitmap> _captureRegion;
    private readonly Func<IReadOnlyList<Rectangle>> _monitorBounds;

    private LiveTranslationScreenCapture(
        Rectangle screenRegion,
        Func<Rectangle, Bitmap> captureRegion,
        Func<IReadOnlyList<Rectangle>> monitorBounds)
    {
        _screenRegion = screenRegion;
        _captureRegion = captureRegion;
        _monitorBounds = monitorBounds;
    }

    public static Task<LiveTranslationScreenCapture> StartForRegionAsync(
        Rectangle screenRegion,
        CancellationToken cancellationToken,
        Func<Rectangle, Bitmap>? captureRegion = null,
        Func<IReadOnlyList<Rectangle>>? monitorBounds = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        monitorBounds ??= static () => Forms.Screen.AllScreens
            .Select(static screen => screen.Bounds)
            .ToArray();
        EnsureValidRegion(screenRegion, monitorBounds());
        captureRegion ??= static region =>
        {
            using var frame = new ScreenCaptureService().CaptureRegion(region);
            return new Bitmap(frame.Bitmap);
        };
        return Task.FromResult(new LiveTranslationScreenCapture(
            screenRegion, captureRegion, monitorBounds));
    }

    public Bitmap Capture(Rectangle screenRegion)
    {
        if (screenRegion != _screenRegion)
            throw new ArgumentOutOfRangeException(nameof(screenRegion), "screen-capture-region-changed");
        EnsureValidRegion(_screenRegion, _monitorBounds());

        // Read the visible pixels each time. The caller owns the returned bitmap.
        var bitmap = _captureRegion(_screenRegion);
        if (bitmap is null)
            throw new InvalidOperationException("screen-capture-empty-frame");
        if (bitmap.Width == _screenRegion.Width && bitmap.Height == _screenRegion.Height)
            return bitmap;

        bitmap.Dispose();
        throw new InvalidOperationException("screen-capture-frame-size-mismatch");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static void EnsureValidRegion(
        Rectangle region,
        IReadOnlyList<Rectangle>? monitorBounds)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), "screen-capture-invalid-region");
        if (monitorBounds is null || !monitorBounds.Any(monitor => Contains(monitor, region)))
            throw new InvalidOperationException("screen-capture-region-outside-single-monitor");
    }

    private static bool Contains(Rectangle monitor, Rectangle region) =>
        monitor.Width > 0 && monitor.Height > 0
        && (long)region.Left >= monitor.Left
        && (long)region.Top >= monitor.Top
        && (long)region.Left + region.Width <= (long)monitor.Left + monitor.Width
        && (long)region.Top + region.Height <= (long)monitor.Top + monitor.Height;
}

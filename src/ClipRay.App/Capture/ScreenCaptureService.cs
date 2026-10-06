using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClipRay.App.Recording;
using Forms = System.Windows.Forms;

namespace ClipRay.App.Capture;

public sealed class ScreenCaptureService
{
    public CapturedFrame CaptureCurrentMonitor()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        return Capture(CurrentScreenCaptureBounds.Resolve(screen.Bounds, screen.WorkingArea));
    }

    public CapturedFrame CaptureCurrentMonitorIncludingTaskbar()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        return Capture(screen.Bounds);
    }

    public Task<CapturedFrame> CaptureCurrentMonitorGameCompatibleAsync(CancellationToken cancellationToken = default)
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var bounds = CurrentScreenCaptureBounds.Resolve(screen.Bounds, screen.WorkingArea);
        return GameCompatibleScreenCapturePolicy.CaptureAsync(
            token => WindowsGraphicsScreenCapture.CaptureMonitorAsync(bounds, token),
            () => Capture(bounds),
            cancellationToken);
    }

    public Task<CapturedFrame> CaptureCurrentMonitorIncludingTaskbarGameCompatibleAsync(CancellationToken cancellationToken = default)
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var bounds = screen.Bounds;
        return GameCompatibleScreenCapturePolicy.CaptureAsync(
            token => WindowsGraphicsScreenCapture.CaptureMonitorAsync(bounds, token),
            () => Capture(bounds),
            cancellationToken);
    }

    public Task<CapturedFrame> CaptureCurrentMonitorWithWindowsGraphicsAsync(CancellationToken cancellationToken = default)
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var bounds = CurrentScreenCaptureBounds.Resolve(screen.Bounds, screen.WorkingArea);
        return WindowsGraphicsScreenCapture.CaptureMonitorAsync(bounds, cancellationToken);
    }

    public CapturedFrame CaptureVirtualDesktop() => Capture(Forms.SystemInformation.VirtualScreen);

    public CapturedFrame CaptureRegion(Rectangle physicalPixelBounds) => Capture(physicalPixelBounds);

    private static CapturedFrame Capture(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return new CapturedFrame(bitmap, bounds);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}

public static class CurrentScreenCaptureBounds
{
    public static Rectangle Resolve(Rectangle monitorBounds, Rectangle workingArea) =>
        workingArea.Width > 0 && workingArea.Height > 0 ? workingArea : monitorBounds;
}

public static class GameCompatibleScreenCapturePolicy
{
    public static async Task<CapturedFrame> CaptureAsync(
        Func<CancellationToken, Task<CapturedFrame>> preferredCapture,
        Func<CapturedFrame> fallbackCapture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferredCapture);
        ArgumentNullException.ThrowIfNull(fallbackCapture);
        try
        {
            return await preferredCapture(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return fallbackCapture();
        }
    }
}

internal static class WindowsGraphicsScreenCapture
{
    public static async Task<CapturedFrame> CaptureMonitorAsync(
        Rectangle monitorBounds,
        CancellationToken cancellationToken)
    {
        // The requested rectangle may exclude a taskbar; WGC still captures the physical monitor.
        var mapping = RecordingGeometry.MapToMonitor(
            monitorBounds,
            Forms.Screen.AllScreens.Select(static screen => screen.Bounds).ToArray());
        await using var source = new GraphicsCaptureSource(
            mapping,
            RecordingTimeline.StartNow(),
            normalizeForEncoder: false);
        var frameReady = new TaskCompletionSource<VideoRecordingSample>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await source.StartAsync(
            sample =>
            {
                frameReady.TrySetResult(sample);
                return ValueTask.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        var sample = await frameReady.Task
            .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
            .ConfigureAwait(false);
        return ToCapturedFrame(sample, monitorBounds.Location);
    }

    private static CapturedFrame ToCapturedFrame(VideoRecordingSample sample, Point origin)
    {
        var bitmap = new Bitmap(sample.Width, sample.Height, PixelFormat.Format32bppPArgb);
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            try
            {
                var sourceStride = sample.Width * 4;
                for (var row = 0; row < sample.Height; row++)
                {
                    Marshal.Copy(
                        sample.BgraPixels,
                        row * sourceStride,
                        IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride),
                        sourceStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        return new CapturedFrame(
            bitmap,
            new Rectangle(origin, bitmap.Size),
            hasSystemCaptureFeedback: true);
    }
}

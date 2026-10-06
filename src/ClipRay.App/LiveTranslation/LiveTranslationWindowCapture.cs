using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using ClipRay.App.Capture;
using ClipRay.App.Recording;

namespace ClipRay.App.LiveTranslation;

internal sealed class LiveTranslationWindowCapture : IAsyncDisposable
{
    private readonly Rectangle _screenRegion;
    private readonly IntPtr _windowHandle;
    private readonly Rectangle _windowBounds;
    private readonly Func<Rectangle, Bitmap> _captureRegion;
    private readonly bool _preferNativeWindowCapture;

    private LiveTranslationWindowCapture(
        Rectangle screenRegion,
        IntPtr windowHandle,
        Rectangle windowBounds,
        Func<Rectangle, Bitmap> captureRegion,
        bool preferNativeWindowCapture)
    {
        _screenRegion = screenRegion;
        _windowHandle = windowHandle;
        _windowBounds = windowBounds;
        _captureRegion = captureRegion;
        _preferNativeWindowCapture = preferNativeWindowCapture;
    }

    public static async Task<LiveTranslationWindowCapture> StartAsync(
        int processId,
        Rectangle screenRegion,
        CancellationToken cancellationToken,
        Func<Rectangle, Bitmap>? captureRegion = null,
        bool preferNativeWindowCapture = false)
    {
        using var process = Process.GetProcessById(processId);
        var handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("window-capture-source-unavailable");
        return await StartAsync(handle, screenRegion, cancellationToken,
            captureRegion, preferNativeWindowCapture).ConfigureAwait(false);
    }

    public static Task<LiveTranslationWindowCapture> StartForRegionAsync(
        Rectangle screenRegion,
        CancellationToken cancellationToken,
        Func<Rectangle, Bitmap>? captureRegion = null,
        bool preferNativeWindowCapture = false)
    {
        var center = new NativePoint(
            screenRegion.Left + screenRegion.Width / 2,
            screenRegion.Top + screenRegion.Height / 2);
        var handle = ResolveRootWindow(center);
        var corners = new[]
        {
            new NativePoint(screenRegion.Left, screenRegion.Top),
            new NativePoint(screenRegion.Right - 1, screenRegion.Top),
            new NativePoint(screenRegion.Left, screenRegion.Bottom - 1),
            new NativePoint(screenRegion.Right - 1, screenRegion.Bottom - 1),
        };
        if (handle == IntPtr.Zero || corners.Any(point => ResolveRootWindow(point) != handle)
            || handle == GetShellWindow() || handle == GetDesktopWindow() || IsIconic(handle)
            || GetWindowThreadProcessId(handle, out var processId) == 0
            || processId == Environment.ProcessId)
            throw new InvalidOperationException("window-capture-source-unavailable");
        return StartAsync(handle, screenRegion, cancellationToken,
            captureRegion, preferNativeWindowCapture);
    }

    private static IntPtr ResolveRootWindow(NativePoint point)
    {
        var child = WindowFromPoint(point);
        if (child == IntPtr.Zero) return IntPtr.Zero;
        var root = GetAncestor(child, GaRoot);
        return root != IntPtr.Zero && IsWindowVisible(root) && !IsDesktopHost(root) ? root : IntPtr.Zero;
    }

    private static bool IsDesktopHost(IntPtr window)
    {
        var className = new StringBuilder(64);
        if (GetClassName(window, className, className.Capacity) == 0) return false;
        return className.ToString() is "WorkerW" or "Progman";
    }

    private static Task<LiveTranslationWindowCapture> StartAsync(
        IntPtr handle,
        Rectangle screenRegion,
        CancellationToken cancellationToken,
        Func<Rectangle, Bitmap>? captureRegion,
        bool preferNativeWindowCapture)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var windowBounds = ReadExtendedFrameBounds(handle);
        if (!windowBounds.Contains(screenRegion))
            throw new InvalidOperationException("window-capture-region-outside-source-window");
        captureRegion ??= static region =>
        {
            using var frame = new ScreenCaptureService().CaptureRegion(region);
            return new Bitmap(frame.Bitmap);
        };
        return Task.FromResult(new LiveTranslationWindowCapture(
            screenRegion, handle, windowBounds, captureRegion, preferNativeWindowCapture));
    }

    public Bitmap Capture(Rectangle screenRegion)
    {
        if (screenRegion != _screenRegion)
            throw new ArgumentOutOfRangeException(nameof(screenRegion), "window-capture-region-changed");
        if (!IsWindow(_windowHandle) || !IsWindowVisible(_windowHandle))
            throw new InvalidOperationException("window-capture-source-closed");
        if (IsIconic(_windowHandle))
            throw new InvalidOperationException("window-capture-source-minimized");
        WindowCaptureGeometry.EnsureStable(_windowBounds, ReadExtendedFrameBounds(_windowHandle), _windowBounds.Size);
        if (_preferNativeWindowCapture
            && TryCaptureWindowRegion(screenRegion, out var nativeCapture))
            return nativeCapture;

        // PrintWindow is unavailable for a small set of accelerated windows.
        // A visible screen fallback is safe only while the selected root window
        // remains foreground; otherwise another app could enter OCR.
        var foreground = GetAncestor(GetForegroundWindow(), GaRoot);
        if (foreground != _windowHandle)
            throw new InvalidOperationException("window-capture-source-inactive");
        return _captureRegion(screenRegion);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private bool TryCaptureWindowRegion(Rectangle screenRegion, out Bitmap bitmap)
    {
        // PrintWindow paints in GetWindowRect coordinates, including the invisible
        // resize border. DWM extended bounds omit that border, so using them here
        // shifts both the text mask and translated text on the real screen.
        if (!GetWindowRect(_windowHandle, out var windowRectangle))
            throw new InvalidOperationException("window-capture-bounds-unavailable");
        var printBounds = windowRectangle.ToRectangle();
        if (printBounds.Width <= 0 || printBounds.Height <= 0)
            throw new InvalidOperationException("window-capture-bounds-unavailable");
        using var windowBitmap = new Bitmap(
            printBounds.Width,
            printBounds.Height,
            PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(windowBitmap);
        var deviceContext = graphics.GetHdc();
        bool rendered;
        try
        {
            rendered = PrintWindow(_windowHandle, deviceContext, PwRenderFullContent)
                || PrintWindow(_windowHandle, deviceContext, 0);
        }
        finally
        {
            graphics.ReleaseHdc(deviceContext);
        }
        if (!rendered)
        {
            bitmap = null!;
            return false;
        }

        var relative = new Rectangle(
            screenRegion.Left - printBounds.Left,
            screenRegion.Top - printBounds.Top,
            screenRegion.Width,
            screenRegion.Height);
        if (relative.Left < 0 || relative.Top < 0
            || relative.Right > windowBitmap.Width || relative.Bottom > windowBitmap.Height)
            throw new InvalidOperationException("window-capture-region-outside-source-window");
        bitmap = windowBitmap.Clone(relative, PixelFormat.Format32bppPArgb);
        return true;
    }

    private static Rectangle ReadExtendedFrameBounds(IntPtr window)
    {
        if (DwmGetWindowAttribute(window, DwmExtendedFrameBounds, out var bounds, Marshal.SizeOf<NativeRectangle>()) == 0)
        {
            var rectangle = bounds.ToRectangle();
            if (rectangle.Width > 0 && rectangle.Height > 0) return rectangle;
        }
        if (!GetWindowRect(window, out bounds))
            throw new InvalidOperationException("window-capture-bounds-unavailable");
        return bounds.ToRectangle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom)
    {
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    private const int DwmExtendedFrameBounds = 9;
    private const uint GaRoot = 2;
    private const uint PwRenderFullContent = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out NativeRectangle value,
        int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
}

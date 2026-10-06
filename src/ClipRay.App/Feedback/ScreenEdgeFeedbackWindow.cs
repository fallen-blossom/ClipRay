using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace ClipRay.App.Feedback;

public sealed class ScreenEdgeFeedbackWindow : Window
{
    private const int BorderPhysicalPixels = 4;
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Rectangle _physicalBounds;
    private readonly Border _border;
    private IntPtr _handle;

    public ScreenEdgeFeedbackWindow(Rectangle bounds, MediaColor color)
    {
        _physicalBounds = bounds;
        _border = new Border
        {
            BorderBrush = new SolidColorBrush(color),
            Background = MediaBrushes.Transparent,
            SnapsToDevicePixels = true,
        };

        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Content = _border;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        SnapsToDevicePixels = true;
        SizeToContent = SizeToContent.Manual;
        Topmost = true;
        WindowStyle = WindowStyle.None;

        // WPF 公共几何使用 DIP；真实物理像素矩形由下面的 native 定位负责。
        Width = 1;
        Height = 1;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        AddExtendedStyles(_handle);
        PositionInPhysicalPixels(_handle, _physicalBounds);
        ApplyPhysicalBorderThickness();

        // native 移动会在首次绘制前确定目标显示器的 DPI。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyPhysicalBorderThickness);
    }

    private void ApplyPhysicalBorderThickness()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var dpi = GetDpiForWindow(_handle);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        var dipThickness = BorderPhysicalPixels / scale;
        _border.BorderThickness = new Thickness(dipThickness);
    }

    private static void AddExtendedStyles(IntPtr handle)
    {
        Marshal.SetLastPInvokeError(0);
        var current = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var error = Marshal.GetLastWin32Error();
        if (current == 0 && error != 0)
        {
            throw new Win32Exception(error);
        }

        var updated = new IntPtr(current | WsExNoActivate | WsExTransparent | WsExToolWindow);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(handle, GwlExStyle, updated);
        error = Marshal.GetLastWin32Error();
        if (previous == IntPtr.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }
    }

    private static void PositionInPhysicalPixels(IntPtr handle, Rectangle bounds)
    {
        if (!SetWindowPos(
                handle,
                HwndTopmost,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SwpNoActivate | SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) => IntPtr.Size == 8
        ? GetWindowLongPtr64(handle, index)
        : new IntPtr(GetWindowLong32(handle, index));

    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) => IntPtr.Size == 8
        ? SetWindowLongPtr64(handle, index, value)
        : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

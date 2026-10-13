using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipRay.App.Capture;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;

namespace ClipRay.App.Scrolling;

public sealed class ScrollCaptureBorderWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const uint WdaExcludeFromCapture = 0x11;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly Rectangle _region;
    private readonly bool _excludeFromCapture;

    public ScrollCaptureBorderWindow(Rectangle region, bool excludeFromCapture = true)
    {
        _region = region;
        _excludeFromCapture = excludeFromCapture;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x12, 0xC7, 0xFF)),
            BorderThickness = new Thickness(2),
            Background = MediaBrushes.Transparent,
        };
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Width = 1;
        Height = 1;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var current = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(current | WsExTransparent | WsExToolWindow | WsExNoActivate));
        if (_excludeFromCapture && !SetWindowDisplayAffinity(handle, WdaExcludeFromCapture)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetWindowPos(handle, HwndTopmost, _region.X - 2, _region.Y - 2, _region.Width + 4, _region.Height + 4, 0x0010 | 0x0020))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));
    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr handle, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

public sealed class ScrollCapturePreviewWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const uint WdaExcludeFromCapture = 0x11;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly Rectangle _region;
    private readonly bool _excludeFromCapture;
    private readonly System.Windows.Controls.Image _image;
    private readonly Border _viewportBand;

    public ScrollCapturePreviewWindow(Rectangle region, bool excludeFromCapture = true)
    {
        _region = region;
        _excludeFromCapture = excludeFromCapture;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        Width = 1;
        Height = 1;

        _image = new System.Windows.Controls.Image { Stretch = Stretch.Uniform };
        _viewportBand = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x48, 0x70, 0xE5, 0xFF)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0xB0, 0xD7, 0xF8, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 1),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        };
        var grid = new Grid();
        grid.Children.Add(_image);
        grid.Children.Add(_viewportBand);
        Content = new Border
        {
            Child = grid,
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x12, 0xC7, 0xFF)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(MediaColor.FromRgb(0x0D, 0x12, 0x1C)),
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var current = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(current | WsExTransparent | WsExToolWindow | WsExNoActivate));
            if (_excludeFromCapture) SetWindowDisplayAffinity(handle, WdaExcludeFromCapture);
        };
    }

    public void ShowPreview(Bitmap bitmap)
    {
        ShowPreview(BitmapSourceFactory.Create(bitmap), bitmap.Size);
    }

    public void ShowPreview(System.Windows.Media.Imaging.BitmapSource sourceBitmap, System.Drawing.Size compositeSize)
    {
        _image.Source = sourceBitmap;
        var workArea = Forms.Screen.FromRectangle(_region).WorkingArea;
        var placement = ScrollCapturePreviewLayout.Calculate(_region, compositeSize, workArea).Bounds;
        var source = PresentationSource.FromVisual(this)?.CompositionTarget;
        var scaleX = source?.TransformToDevice.M11 ?? 1;
        var scaleY = source?.TransformToDevice.M22 ?? 1;
        Width = placement.Width / scaleX;
        Height = placement.Height / scaleY;
        _viewportBand.Height = Math.Max(2, placement.Height * Math.Min(1d, _region.Height / (double)compositeSize.Height) / scaleY);
        UpdateLayout();
        _ = SetWindowPos(new WindowInteropHelper(this).Handle, HwndTopmost, placement.X, placement.Y, placement.Width, placement.Height, 0x0010);
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));
    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr handle, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

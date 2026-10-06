using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClipRay.Core.Geometry;
using ClipRay.App.Settings;
using Forms = System.Windows.Forms;

namespace ClipRay.App.LiveTranslation;

public partial class LiveTranslationSelectionWindow : Window
{
    private readonly string _interfaceLanguage;
    private readonly TaskCompletionSource<PixelRect?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LiveTranslationSelectionCoordinator _coordinator;
    private readonly System.Drawing.Rectangle _monitorBounds;
    private PixelPoint? _pointerDown;
    private bool _selectionCompleted;

    public LiveTranslationSelectionWindow(string interfaceLanguage = "zh-CN")
    {
        _interfaceLanguage = AppInterfaceLanguage.Normalize(interfaceLanguage);
        InitializeComponent();
        StatusText.Text = AppInterfaceLanguage.Text(_interfaceLanguage,
            "拖动框选实时翻译区域，Esc 取消", "Drag to select a live translation area. Press Esc to cancel.");
        _monitorBounds = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        _coordinator = new LiveTranslationSelectionCoordinator(
            Forms.Screen.AllScreens.Select(static screen => new PixelRect(
                screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height)).ToArray());
        SourceInitialized += OnSourceInitialized;
        PreviewMouseLeftButtonDown += OnPointerDown;
        PreviewMouseMove += OnPointerMove;
        PreviewMouseLeftButtonUp += OnPointerUp;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) =>
        {
            if (!_selectionCompleted) _completion.TrySetResult(null);
        };
    }

    public Task<PixelRect?> SelectAsync()
    {
        Show();
        Activate();
        Focus();
        return _completion.Task;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowPos(
            handle, HWND_TOPMOST,
            _monitorBounds.X, _monitorBounds.Y, _monitorBounds.Width, _monitorBounds.Height,
            SWP_SHOWWINDOW);
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        _pointerDown = CursorPoint();
        CaptureMouse();
        UpdateSelectionVisual(_pointerDown.Value, _pointerDown.Value);
        e.Handled = true;
    }

    private void OnPointerMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_pointerDown is { } start && e.LeftButton == MouseButtonState.Pressed)
            UpdateSelectionVisual(start, CursorPoint());
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_pointerDown is not { } start) return;
        ReleaseMouseCapture();
        var end = CursorPoint();
        _pointerDown = null;
        if (!_coordinator.TryComplete(start, end, out var region, out var error))
        {
            StatusText.Text = error switch
            {
                "请在同一显示器内重新框选" => AppInterfaceLanguage.Text(_interfaceLanguage,
                    error, "Select an area on one monitor."),
                "框选区域太小，请重新框选" => AppInterfaceLanguage.Text(_interfaceLanguage,
                    error, "The selected area is too small. Try again."),
                _ => error,
            };
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        // The selector must be gone before the first capture is allowed to run.
        _selectionCompleted = true;
        Close();
        _completion.TrySetResult(region);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void UpdateSelectionVisual(PixelPoint start, PixelPoint end)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var a = fromDevice.Transform(new System.Windows.Point(start.X - _monitorBounds.X, start.Y - _monitorBounds.Y));
        var b = fromDevice.Transform(new System.Windows.Point(end.X - _monitorBounds.X, end.Y - _monitorBounds.Y));
        Canvas.SetLeft(SelectionBorder, Math.Min(a.X, b.X));
        Canvas.SetTop(SelectionBorder, Math.Min(a.Y, b.Y));
        SelectionBorder.Width = Math.Abs(a.X - b.X);
        SelectionBorder.Height = Math.Abs(a.Y - b.Y);
        SelectionBorder.Visibility = Visibility.Visible;
    }

    private static PixelPoint CursorPoint()
    {
        _ = GetPhysicalCursorPos(out var point);
        return new PixelPoint(point.X, point.Y);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetPhysicalCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}

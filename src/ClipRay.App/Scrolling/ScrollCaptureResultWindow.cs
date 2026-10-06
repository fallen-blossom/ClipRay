using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipRay.App.Capture;
using ClipRay.App.Ui;
using ClipRay.App.Settings;
using Forms = System.Windows.Forms;

namespace ClipRay.App.Scrolling;

public sealed class ScrollCaptureResultWindow : Window
{
    private readonly System.Windows.Controls.Image _image;
    private readonly ScrollViewer _viewer;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly double _sourceWidth;
    private readonly TextBlock _saveStatus = new() { Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(10, 6, 10, 4), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxHeight = 96 };
    private readonly string _savedPath;
    private readonly Func<bool>? _copyAgain;
    private bool _fitWidth = true;
    private double _zoom = 1;
    private bool _isMousePanning;
    private System.Windows.Point _panStartPointer;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private readonly string _language;

    public ScrollCaptureResultWindow(Bitmap bitmap, ScrollCaptureStopReason reason, string savedPath, bool clipboardCopied, Func<bool>? copyAgain = null, string interfaceLanguage = "zh-CN", bool clipboardRequested = true)
    {
        _language = AppInterfaceLanguage.Normalize(interfaceLanguage);
        ArgumentNullException.ThrowIfNull(bitmap);
        _savedPath = Path.GetFullPath(savedPath ?? throw new ArgumentNullException(nameof(savedPath)));
        _copyAgain = copyAgain;
        Title = reason == ScrollCaptureStopReason.EndOfScroll ? T("长截图结果", "Scrolling capture result") : T("长截图部分结果", "Partial scrolling capture");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var workArea = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        Width = Math.Min(1200, workArea.Width * 0.90);
        Height = Math.Min(900, workArea.Height * 0.90);
        MinWidth = 520;
        MinHeight = 400;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0x13, 0x1B));

        var source = BitmapSourceFactory.Create(bitmap);
        _sourceWidth = source.Width;
        _image = new System.Windows.Controls.Image
        {
            Source = source,
            Stretch = Stretch.None,
            LayoutTransform = _scale,
            SnapsToDevicePixels = true,
        };
        _viewer = new ScrollViewer
        {
            Content = _image,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.Black,
            PanningMode = PanningMode.Both,
        };
        _viewer.PreviewMouseWheel += OnPreviewMouseWheel;
        _viewer.SizeChanged += (_, _) => { if (_fitWidth) ApplyFitWidth(); };
        if (System.Windows.Application.Current.TryFindResource("DarkScrollBarStyle") is Style darkScrollBarStyle)
        {
            _viewer.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = darkScrollBarStyle;
        }
        _image.MouseLeftButtonDown += OnImageMouseLeftButtonDown;
        _image.MouseMove += OnImageMouseMove;
        _image.MouseLeftButtonUp += OnImageMouseLeftButtonUp;
        _image.LostMouseCapture += OnImageLostMouseCapture;

        var toolbar = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(10, 8, 10, 8),
        };
        toolbar.Children.Add(CreateToolbarButton(ToolbarIconKind.ZoomOut, "缩小", () => SetZoom(_zoom / 1.2, false)));
        toolbar.Children.Add(CreateToolbarButton(ToolbarIconKind.ZoomIn, "放大", () => SetZoom(_zoom * 1.2, false)));
        toolbar.Children.Add(CreateToolbarButton(ToolbarIconKind.FitWidth, "适合宽度", ApplyFitWidth));
        toolbar.Children.Add(CreateToolbarButton(ToolbarIconKind.Reset, "原始大小", () => SetZoom(1, false)));
        toolbar.Children.Add(CreateToolbarButton(ToolbarIconKind.Folder, "在文件夹中显示", ShowInFolder));
        toolbar.Children.Add(CreateToolbarButton(ToolbarIconKind.Copy, "再次复制", CopyAgain));
        toolbar.Children.Add(CreateToolbarButton(ToolbarIconKind.Complete, "关闭", Close, ToolbarIconTone.Confirm));

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Bottom);
        root.Children.Add(toolbar);
        DockPanel.SetDock(_saveStatus, Dock.Top);
        root.Children.Add(_saveStatus);
        root.Children.Add(_viewer);
        Content = root;
        _saveStatus.Text = SavedStatus(_language, clipboardRequested, clipboardCopied) + "\n" + _savedPath;
        _saveStatus.ToolTip = _saveStatus.Text;
        Loaded += (_, _) => ApplyFitWidth();
    }

    private string T(string chinese, string english) => AppInterfaceLanguage.Text(_language, chinese, english);

    public static string SavedStatus(string language, bool clipboardRequested, bool clipboardCopied) => clipboardCopied
        ? AppInterfaceLanguage.Text(language, "已保存并复制：", "Saved and copied:")
        : clipboardRequested
            ? AppInterfaceLanguage.Text(language, "已保存，但复制未完成。可点击“再次复制”重试。", "Saved, but not copied. Choose Copy again to retry.")
            : AppInterfaceLanguage.Text(language, "已保存：", "Saved:");

    private void ShowInFolder()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_savedPath}\"") { UseShellExecute = true });
    }

    private void CopyAgain()
    {
        var copied = _copyAgain?.Invoke() == true;
        _saveStatus.Text = copied ? T($"已重新复制：\n{_savedPath}", $"Copied again:\n{_savedPath}") : T($"复制未完成：\n{_savedPath}", $"Copy did not complete:\n{_savedPath}");
        _saveStatus.ToolTip = _saveStatus.Text;
        _saveStatus.Foreground = copied ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.OrangeRed;
    }

    private System.Windows.Controls.Button CreateToolbarButton(ToolbarIconKind icon, string label, Action action, ToolbarIconTone tone = ToolbarIconTone.Primary)
    {
        if (_language == "en") label = label switch
        {
            "缩小" => "Zoom out", "放大" => "Zoom in", "适合宽度" => "Fit width", "原始大小" => "Original size",
            "在文件夹中显示" => "Show in folder", "再次复制" => "Copy again", "关闭" => "Close", _ => label
        };
        var button = new System.Windows.Controls.Button
        {
            Content = ToolbarIconFactory.Create(icon, tone),
            ToolTip = label,
            Width = 42,
            Height = 34,
            Margin = new Thickness(4, 0, 0, 0),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
        e.Handled = true;
        var oldZoom = _zoom;
        var newZoom = ScrollCaptureZoomPolicy.Clamp(oldZoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        if (Math.Abs(oldZoom - newZoom) < 0.0001) return;
        var pointer = e.GetPosition(_viewer);
        SetZoom(newZoom, false);
        Dispatcher.BeginInvoke(() =>
        {
            _viewer.ScrollToHorizontalOffset(ScrollCaptureZoomPolicy.OffsetForPointer(_viewer.HorizontalOffset, pointer.X, oldZoom, _zoom));
            _viewer.ScrollToVerticalOffset(ScrollCaptureZoomPolicy.OffsetForPointer(_viewer.VerticalOffset, pointer.Y, oldZoom, _zoom));
        });
    }

    private void OnImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isMousePanning = true;
        _panStartPointer = e.GetPosition(_viewer);
        _panStartHorizontalOffset = _viewer.HorizontalOffset;
        _panStartVerticalOffset = _viewer.VerticalOffset;
        _image.Cursor = System.Windows.Input.Cursors.SizeAll;
        _image.CaptureMouse();
        e.Handled = true;
    }

    private void OnImageMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isMousePanning || e.LeftButton != MouseButtonState.Pressed) return;
        var pointer = e.GetPosition(_viewer);
        _viewer.ScrollToHorizontalOffset(ScrollCapturePanPolicy.Offset(
            _panStartHorizontalOffset,
            _panStartPointer.X,
            pointer.X,
            _viewer.ScrollableWidth));
        _viewer.ScrollToVerticalOffset(ScrollCapturePanPolicy.Offset(
            _panStartVerticalOffset,
            _panStartPointer.Y,
            pointer.Y,
            _viewer.ScrollableHeight));
        e.Handled = true;
    }

    private void OnImageMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndMousePan();
        e.Handled = true;
    }

    private void OnImageLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => EndMousePan(releaseCapture: false);

    private void EndMousePan(bool releaseCapture = true)
    {
        if (!_isMousePanning) return;
        _isMousePanning = false;
        _image.Cursor = System.Windows.Input.Cursors.Hand;
        if (releaseCapture && _image.IsMouseCaptured) _image.ReleaseMouseCapture();
    }

    private void ApplyFitWidth() => SetZoom(ScrollCaptureZoomPolicy.FitWidth(_viewer.ViewportWidth, _sourceWidth), true);

    private void SetZoom(double zoom, bool fitWidth)
    {
        _zoom = ScrollCaptureZoomPolicy.Clamp(zoom);
        _fitWidth = fitWidth;
        _scale.ScaleX = _zoom;
        _scale.ScaleY = _zoom;
    }
}

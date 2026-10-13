using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClipRay.App.Infrastructure;
using ClipRay.App.Settings;
using ClipRay.App.Ui;
using Forms = System.Windows.Forms;

namespace ClipRay.App.Scrolling;

public static class ScrollCaptureControlReadiness
{
    public static bool CanStart(bool affinityApplied, bool placedOutside) => affinityApplied || placedOutside;
}

/// <summary>会话期间独立显示，并排除在屏幕复制之外的控制面板。</summary>
public sealed class ScrollCaptureControlWindow : Window
{
    private const uint WdaExcludeFromCapture = 0x11;
    private readonly GlobalHotkey _stopHotkey;
    private readonly GlobalHotkey? _secondaryStopHotkey;
    private readonly bool _excludeFromCapture;
    private readonly System.Windows.Controls.Button _automaticButton;
    private bool _automaticScrolling;
    private bool _automaticWasStarted;
    private readonly string _language;

    public ScrollCaptureControlWindow(
        GlobalHotkey? stopHotkey = null,
        ScrollCaptureMode mode = ScrollCaptureMode.Manual,
        bool excludeFromCapture = true,
        GlobalHotkey? secondaryStopHotkey = null,
        string interfaceLanguage = "zh-CN")
    {
        _language = AppInterfaceLanguage.Normalize(interfaceLanguage);
        Title = T("ClipRay 长截图控制", "ClipRay scrolling capture controls");
        _stopHotkey = stopHotkey ?? GlobalHotkey.Parse("Escape");
        _secondaryStopHotkey = secondaryStopHotkey;
        _automaticScrolling = mode == ScrollCaptureMode.Automatic;
        _automaticWasStarted = _automaticScrolling;
        _excludeFromCapture = excludeFromCapture;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = 510;
        Background = System.Windows.Media.Brushes.Transparent;
        AllowsTransparency = true;

        var root = new Grid { Margin = new Thickness(8, 6, 8, 6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Status = new TextBlock
        {
            Text = T("0 帧", "0 frames"),
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        Grid.SetRow(Status, 0);
        root.Children.Add(Status);
        var controls = new WrapPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        _automaticButton = CreateLabeledButton(
            _automaticScrolling ? ToolbarIconKind.Pause : ToolbarIconKind.Play,
            ToolbarIconTone.Primary,
            AutomaticControlLabel());
        _automaticButton.Click += (_, _) => ToggleAutomaticScrolling();
        controls.Children.Add(_automaticButton);

        var cancel = CreateLabeledButton(ToolbarIconKind.Cancel, ToolbarIconTone.Danger, T("取消", "Cancel"));
        cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        controls.Children.Add(cancel);
        var complete = CreateLabeledButton(ToolbarIconKind.Complete, ToolbarIconTone.Confirm, T("完成", "Finish"));
        complete.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        controls.Children.Add(complete);
        Grid.SetRow(controls, 1);
        root.Children.Add(controls);
        Content = new Border
        {
            Child = root,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF2, 0x18, 0x1D, 0x27)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5A, 0x66, 0x7A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
        };
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => AffinityApplied = !_excludeFromCapture || SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture);
        Closed += (_, _) => { if (!IsNormalTeardown) StopRequested?.Invoke(this, EventArgs.Empty); };
    }

    public event EventHandler? StopRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler<bool>? AutomaticScrollingChanged;
    public TextBlock Status { get; }
    public bool AffinityApplied { get; private set; }
    public bool IsNormalTeardown { get; set; }

    private string T(string chinese, string english) => AppInterfaceLanguage.Text(_language, chinese, english);
    public void SetProgress(ScrollCaptureProgress progress)
    {
        if (progress.AutomaticScrolling is {} automatic && automatic != _automaticScrolling)
        {
            _automaticScrolling = automatic;
            var label = AutomaticControlLabel();
            _automaticButton.Content = CreateLabeledButton(automatic ? ToolbarIconKind.Pause : ToolbarIconKind.Play, ToolbarIconTone.Primary, label).Content;
            _automaticButton.ToolTip = label;
            AutomationProperties.SetName(_automaticButton, label);
        }
        var detail = progress.Status ?? "";
        if (_language == "en") detail = detail switch
        {
            "当前画面暂时无法可靠拼接；仍在等待手动滚动或完成。" => "Cannot stitch this view reliably yet. Scroll manually or finish.",
            "尚未检测到可拼接滚动；自动滚动仍在继续。" => "No stitchable scroll detected yet. Automatic scrolling continues.",
            "暂时无法确认新内容；自动滚动仍在继续。" => "New content is not confirmed yet. Automatic scrolling continues.",
            "未检测到滚动，自动滚动已暂停；可手动滚动或完成。" => "No scroll detected. Automatic scrolling paused; scroll manually or finish.",
            "来源已变化，自动滚动已暂停；可手动滚动或完成。" => "Source changed. Automatic scrolling paused; scroll manually or finish.",
            _ => detail
        };
        Status.Text = T($"{progress.AcceptedFrameCount} 帧", $"{progress.AcceptedFrameCount} frames")
            + (string.IsNullOrWhiteSpace(detail) ? "" : " · " + detail);
        Status.ToolTip = Status.Text;
    }

    public bool PlaceOutside(System.Drawing.Rectangle region)
    {
        var source = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (source is null) return false;
        var scale = source.TransformToDevice;
        var workingArea = Forms.Screen.FromRectangle(region).WorkingArea;
        Width = Math.Min(510, Math.Max(160, (workingArea.Width - 20) / scale.M11));
        UpdateLayout();
        var size = new System.Drawing.Size((int)Math.Ceiling(ActualWidth * scale.M11), (int)Math.Ceiling(ActualHeight * scale.M22));
        var areas = Forms.Screen.AllScreens.Select(static screen => screen.WorkingArea).ToArray();
        var placement = ScrollCapturePlacementPolicy.PlaceToolbar(region, size, areas);
        if (placement is null) return false;
        return SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, placement.Value.Bounds.X, placement.Value.Bounds.Y, 0, 0, 0x0001);
    }

    private static System.Windows.Controls.Button CreateLabeledButton(ToolbarIconKind icon, ToolbarIconTone tone, string label)
    {
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(CreateButtonIcon(icon, tone));
        content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new System.Windows.Controls.Button
        {
            Content = content,
            Foreground = System.Windows.Media.Brushes.White,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            MinWidth = 38,
            Cursor = System.Windows.Input.Cursors.Hand,
            FontWeight = FontWeights.SemiBold,
            ToolTip = label,
        };
        AutomationProperties.SetName(button, label);
        return button;
    }

    private static FrameworkElement CreateButtonIcon(ToolbarIconKind icon, ToolbarIconTone tone)
    {
        if (icon is not (ToolbarIconKind.Cancel or ToolbarIconKind.Complete))
            return ToolbarIconFactory.Create(icon, tone);

        return new Viewbox
        {
            Width = 28,
            Height = 28,
            Child = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(icon == ToolbarIconKind.Cancel
                    ? "M5,5 L23,23 M23,5 L5,23"
                    : "M4,15 L11,22 L24,6"),
                Stroke = new System.Windows.Media.SolidColorBrush(icon == ToolbarIconKind.Cancel
                    ? System.Windows.Media.Color.FromRgb(0xFF, 0x6D, 0x72)
                    : System.Windows.Media.Color.FromRgb(0x5E, 0xE0, 0xA0)),
                StrokeThickness = 2.8,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            },
        };
    }

    private void ToggleAutomaticScrolling()
    {
        _automaticScrolling = !_automaticScrolling;
        if (_automaticScrolling) _automaticWasStarted = true;
        var label = AutomaticControlLabel();
        _automaticButton.Content = CreateLabeledButton(
            _automaticScrolling ? ToolbarIconKind.Pause : ToolbarIconKind.Play,
            ToolbarIconTone.Primary,
            label).Content;
        _automaticButton.ToolTip = label;
        AutomationProperties.SetName(_automaticButton, label);
        AutomaticScrollingChanged?.Invoke(this, _automaticScrolling);
    }

    private string AutomaticControlLabel() => _automaticScrolling
        ? T("暂停滚动", "Pause scrolling")
        : _automaticWasStarted ? T("继续滚动", "Resume scrolling") : T("启动自动滚动", "Start automatic scrolling");

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = HotkeyCapturePolicy.ResolveEventKey(
            e.Key,
            e.SystemKey,
            e.ImeProcessedKey,
            e.DeadCharProcessedKey);
        if (key == Key.Enter || key == Key.Return
            || HotkeyCapturePolicy.MatchesAny(_stopHotkey, _secondaryStopHotkey, key, HotkeyCapturePolicy.ReadCurrentModifiers()))
        {
            StopRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipRay.App.Scrolling;
using ClipRay.App.Settings;
using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace ClipRay.App.Recording;

public sealed class RecordingControlWindow : Window
{
    private const uint WdaExcludeFromCapture = 0x11;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _time;

    public RecordingControlWindow(System.Drawing.Rectangle region, string interfaceLanguage = "zh-CN")
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = MediaBrushes.Transparent;
        AllowsTransparency = true;

        var stop = new WpfButton
        {
            Content = AppInterfaceLanguage.Text(interfaceLanguage, "■  停止录屏", "■  Stop recording"),
            Foreground = MediaBrushes.White,
            Background = new SolidColorBrush(MediaColor.FromRgb(0xC9, 0x3D, 0x49)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(10, 5, 5, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        System.Windows.Automation.AutomationProperties.SetName(stop, AppInterfaceLanguage.Text(interfaceLanguage, "停止录屏", "Stop recording"));
        _time = new TextBlock { Text = "00:00", Foreground = MediaBrushes.White, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0x4D, 0x5A)), Margin = new Thickness(0, 0, 8, 0) };
        var row = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(12, 7, 7, 7) };
        row.Children.Add(dot); row.Children.Add(_time); row.Children.Add(stop);
        Content = new Border
        {
            Child = row,
            Background = new SolidColorBrush(MediaColor.FromArgb(0xF2, 0x18, 0x1D, 0x27)),
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x5A, 0x66, 0x7A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => _time.Text = $"{(int)_elapsed.Elapsed.TotalMinutes:00}:{_elapsed.Elapsed.Seconds:00}";
        _timer.Start();
        SourceInitialized += (_, _) => AffinityApplied = SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture);
        Closed += (_, _) => _timer.Stop();
    }

    public event EventHandler? StopRequested;
    public bool AffinityApplied { get; private set; }

    public bool PlaceOutside(System.Drawing.Rectangle region)
    {
        UpdateLayout();
        var source = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (source is null) return false;
        var scale = source.TransformToDevice;
        var size = new System.Drawing.Size((int)Math.Ceiling(ActualWidth * scale.M11), (int)Math.Ceiling(ActualHeight * scale.M22));
        var placement = ScrollCapturePlacementPolicy.Place(region, size, Forms.Screen.AllScreens.Select(static screen => screen.WorkingArea).ToArray());
        if (placement is null) return false;
        var handle = new WindowInteropHelper(this).Handle;
        if (!SetWindowPos(handle, IntPtr.Zero, placement.Value.Bounds.X, placement.Value.Bounds.Y, 0, 0, 0x0001)
            || !GetWindowRect(handle,out var actual)) return false;
        return actual.Right > actual.Left && actual.Bottom > actual.Top
            && !region.IntersectsWith(System.Drawing.Rectangle.FromLTRB(actual.Left,actual.Top,actual.Right,actual.Bottom));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window,out WindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClipRay.App.Capture;
using ClipRay.App.Scrolling;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Bitmap = System.Drawing.Bitmap;
using Rectangle = System.Drawing.Rectangle;

namespace ClipRay.ScrollFixture;

// A real visible scroll target, native wheel delivery, screen capture and production
// stitch/persistence. Every physical source row has an independently readable ID.
internal static class ScrollCadenceProbe
{
    private const int Width = 520, Height = 600, DocumentHeight = 3600;

    public static int Run(string output, int animationMs)
    {
        if (Directory.Exists(output)) throw new IOException("A fresh output directory is required.");
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application();
        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
        };
        var window = new Window
        {
            Title = "ClipRay scroll cadence acceptance", WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            ShowActivated = false, Left = 40, Top = 80, Content = viewer,
            Width = Width, Height = Height, UseLayoutRounding = true,
        };
        var code = 1;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                window.Width = Width / dpi.DpiScaleX;
                window.Height = Height / dpi.DpiScaleY;
                viewer.Content = new Document(dpi.DpiScaleX, dpi.DpiScaleY);
                window.UpdateLayout();
                var wheelCount = 0;
                var offsets = new List<double>();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                var motion = new Stopwatch();
                double start = 0, target = 0;
                // Decode the native wheel at this owned window. WPF otherwise
                // routes a posted wheel using the user's actual mouse-over state.
                var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
                source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message != 0x020A) return IntPtr.Zero;
                    handled = true;
                    var delta = unchecked((short)(wParam.ToInt64() >> 16));
                    wheelCount++;
                    if (animationMs <= 0)
                    {
                        viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset - delta / 120d * 48, 0, viewer.ScrollableHeight));
                        return IntPtr.Zero;
                    }
                    start = viewer.VerticalOffset;
                    target = Math.Clamp(target - delta / 120d * 48, 0, viewer.ScrollableHeight);
                    motion.Restart();
                    timer.Start();
                    return IntPtr.Zero;
                });
                timer.Tick += (_, _) =>
                {
                    var t = Math.Min(1, motion.Elapsed.TotalMilliseconds / animationMs);
                    viewer.ScrollToVerticalOffset(start + (target - start) * (1 - Math.Pow(1 - t, 3)));
                    if (t >= 1) timer.Stop();
                };
                await Task.Delay(350);
                var origin = viewer.PointToScreen(new Point());
                var region = new Rectangle((int)Math.Round(origin.X), (int)Math.Round(origin.Y), Width, Height);
                var trace = new LongCaptureRunDiagnostics("cadence", "probe", output);
                using var session = new ScrollCaptureSession(region, ScrollCaptureMode.Automatic,
                    new ScreenCapture(), new ScrollInputService(new WindowInteropHelper(window).Handle),
                    new DelayScrollSettler(), Path.Combine(output, "segments"), diagnostics: trace);
                session.PreviewChanged += (_, _) => offsets.Add(viewer.VerticalOffset * dpi.DpiScaleY);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var elapsed = Stopwatch.StartNew();
                using var result = await session.RunAsync(cancellation.Token);
                elapsed.Stop();
                timer.Stop();
                using var composite = result.CreateComposite();
                composite.Save(Path.Combine(output, "composite.png"));
                var firstBadRow = -1;
                for (var y = 0; y < composite.Height; y++)
                {
                    var actual = composite.GetPixel(8, y);
                    var expected = RowColor(y);
                    if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B)
                    { firstBadRow = y; break; }
                }
                var pass = result.StopReason == ScrollCaptureStopReason.EndOfScroll
                    && composite.Height == DocumentHeight && firstBadRow == -1;
                var summary = new
                {
                    Pass = pass, AnimationMs = animationMs, ElapsedMs = elapsed.Elapsed.TotalMilliseconds,
                    WheelCount = wheelCount, result.AcceptedFrameCount, StopReason = result.StopReason.ToString(),
                    composite.Width, composite.Height, FirstBadRow = firstBadRow, SourceHeight = DocumentHeight,
                    SourceOffset = viewer.VerticalOffset * dpi.DpiScaleY, AcceptedOffsets = offsets,
                };
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
                LongCaptureRunDiagnosticStore.TryWrite(output, trace);
                code = pass ? 0 : 1;
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); }
            finally { window.Close(); }
        };
        app.Run(window);
        return code;
    }

    private static MediaColor RowColor(int y) => MediaColor.FromRgb((byte)(y >> 8), (byte)y, (byte)(y * 73));

    private sealed class Document(double scaleX, double scaleY) : FrameworkElement
    {
        protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(ScrollCadenceProbe.Width / scaleX, DocumentHeight / scaleY);
        protected override void OnRender(DrawingContext dc)
        {
            dc.PushTransform(new ScaleTransform(1 / scaleX, 1 / scaleY));
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ScrollCadenceProbe.Width, DocumentHeight));
            for (var y = 0; y < DocumentHeight; y++)
                dc.DrawRectangle(new SolidColorBrush(RowColor(y)), null, new Rect(0, y, 24, 1));
            for (var i = 0; i < 60; i++)
            {
                var text = new FormattedText($"{i + 1:D2}. Normal scrolling content / 正常长页面\nCapture every row; preserve the original layout.",
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 16, Brushes.Black, 1);
                dc.DrawText(text, new Point(38, i * 60 + 8));
                dc.DrawRectangle(new SolidColorBrush(MediaColor.FromRgb(225, 228, 234)), null, new Rect(32, i * 60 + 58, ScrollCadenceProbe.Width - 32, 1));
            }
            dc.Pop();
        }
    }

    private sealed class ScreenCapture : IScrollRegionCapture
    {
        private readonly ScreenCaptureService _capture = new();
        public Bitmap Capture(Rectangle region)
        {
            using var frame = _capture.CaptureRegion(region);
            return frame.Bitmap.Clone(new Rectangle(0, 0, frame.Bitmap.Width, frame.Bitmap.Height), frame.Bitmap.PixelFormat);
        }
    }
}

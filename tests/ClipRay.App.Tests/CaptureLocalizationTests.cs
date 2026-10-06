using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Overlay;
using ClipRay.App.Settings;
using ClipRay.Core.Geometry;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

internal static class CaptureLocalizationTests
{
    public static void Run()
    {
        var output = Path.Combine(Path.GetTempPath(), "clipray-capture-localization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        foreach (var language in new[] { "en", "zh-CN" })
        {
            using var bitmap = new System.Drawing.Bitmap(1080, 720);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.DarkSlateGray);
            var overlay = new SelectionOverlayWindow(new CapturedFrame(bitmap, new System.Drawing.Rectangle(0, 0, 1080, 720)),
                new PixelRect(80, 100, 840, 350), interactionSettings: new AppSettings { InterfaceLanguage = language });
            try
            {
                var move = (Button)overlay.FindName("MoveToolButton");
                if (!ReferenceEquals(move.Command, OverlayTool.Move) || move.Content is string
                    || move.ToolTip?.ToString() != (language == "en" ? "Move (keep size)" : "移动（锁定尺寸）"))
                    throw new Exception("Localization damaged capture command, icon or tooltip.");
                foreach (var button in Descendants(overlay).OfType<Button>().Where(button => button.Content is not string && button.ToolTip is string))
                    if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))) throw new Exception("Unnamed capture icon button.");
                var red = Descendants(overlay).OfType<Button>().Single(button => Equals(button.Tag, "#FF4D4F"));
                if (red.ToolTip?.ToString() != (language == "en" ? "Red" : "红色")) throw new Exception("Color value / label mismatch.");
                var toggle = (Button)overlay.FindName("TranslationToggle");
                var layer = (Canvas)overlay.FindName("TranslationLayer");
                layer.Visibility = Visibility.Visible;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (layer.Visibility != Visibility.Collapsed || toggle.Content?.ToString() != (language == "en" ? "Show translation" : "显示译文"))
                    throw new Exception("Translation toggle lost language or behavior.");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (layer.Visibility != Visibility.Visible || toggle.Content?.ToString() != (language == "en" ? "Show original" : "显示原文"))
                    throw new Exception("Translation toggle reverse failed.");
                overlay.ShowColorVisualPreview(); // Changes offscreen controls only; never calls Window.Show.
                if (((TextBlock)overlay.FindName("ToolStatusText")).Text != (language == "en" ? "Pen: drag inside the selection" : "画笔：在选区内拖动绘制"))
                    throw new Exception("Dynamic editing status is not localized.");
                if (AutomationProperties.GetName((DependencyObject)overlay.FindName("PenWidthInput")) != (language == "en" ? "Pen width" : "画笔粗细"))
                    throw new Exception("Pen slider has no localized accessible name.");
                // Arrange individual panels without a desktop HWND. These images verify labels and
                // icon layout, not screen placement, DPI or foreground interaction.
                ((TextBlock)overlay.FindName("SizeLabel")).Text = "840 × 350";
                foreach (var panelName in new[] { "Toolbar", "ToolProperties" })
                {
                    var panel = (FrameworkElement)overlay.FindName(panelName);
                    panel.Visibility = Visibility.Visible;
                    panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var size = panel.DesiredSize;
                    if (size.Width > 1080 || size.Height <= 0) throw new Exception("Capture panel exceeds test viewport.");
                    panel.Arrange(new Rect(0, 0, size.Width, size.Height)); panel.UpdateLayout();
                    var rendered = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32); rendered.Render(panel);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
                    using var file = File.Create(Path.Combine(output, $"{panelName}-{language}.png")); encoder.Save(file);
                }
                if (overlay.IsVisible) throw new Exception("Offscreen test displayed a window.");
            }
            finally { overlay.Close(); }
        }
        Console.WriteLine($"Capture localization and command checks passed offscreen: {output}");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

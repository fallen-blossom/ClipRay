using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Recording;
using ClipRay.App.Scrolling;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

internal static class CaptureCompanionLanguageTests
{
    public static void Run()
    {
        var output = Path.Combine(Path.GetTempPath(), "clipray-companion-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        foreach (var language in new[] { "en", "zh-CN" })
        {
            var english = language == "en";
            var control = new ScrollCaptureControlWindow(interfaceLanguage: language) { IsNormalTeardown = true };
            try
            {
                var states = new List<bool>(); var stops = 0; var cancels = 0;
                control.AutomaticScrollingChanged += (_, state) => states.Add(state);
                control.StopRequested += (_, _) => stops++;
                control.CancelRequested += (_, _) => cancels++;
                var start = FindButton(control, english ? "Start automatic scrolling" : "启动自动滚动");
                Click(start);
                if (AutomationProperties.GetName(start) != (english ? "Pause scrolling" : "暂停滚动")) throw new Exception("Scroll pause label mismatch.");
                Click(start);
                if (AutomationProperties.GetName(start) != (english ? "Resume scrolling" : "继续滚动")) throw new Exception("Scroll resume label mismatch.");
                Click(start);
                if (!states.SequenceEqual(new[] { true, false, true })) throw new Exception("Localization changed scroll transitions.");
                Click(FindButton(control, english ? "Cancel" : "取消"));
                Click(FindButton(control, english ? "Finish" : "完成"));
                if (stops != 1 || cancels != 1) throw new Exception("Stop/cancel callbacks changed.");
                control.SetProgress(new ScrollCaptureProgress(12, Status: "当前画面暂时无法可靠拼接；仍在等待手动滚动或完成。"));
                if (!control.Status.Text.StartsWith(english ? "12 frames" : "12 帧")
                    || english && !control.Status.Text.Contains("Scroll manually or finish")) throw new Exception("Progress recovery label mismatch.");
                Render(control, output, $"scroll-{language}", 760, 110);
            }
            finally { control.Close(); }
            var recording = new RecordingControlWindow(new System.Drawing.Rectangle(0, 0, 200, 100), language);
            try
            {
                var stops = 0; recording.StopRequested += (_, _) => stops++;
                Click(FindButton(recording, english ? "Stop recording" : "停止录屏"));
                if (stops != 1) throw new Exception("Recording stop callback changed.");
                Render(recording, output, $"recording-{language}", 300, 70);
            }
            finally { recording.Close(); }
            using var bitmap = new System.Drawing.Bitmap(300, 800);
            var copySucceeds = false; var copyCalls = 0;
            var path = Path.Combine(output, "saved-example.png"); // Display-only; no disk/clipboard action.
            var result = new ScrollCaptureResultWindow(bitmap, ScrollCaptureStopReason.CaptureFailure, path, false,
                () => { copyCalls++; return copySucceeds; }, language, clipboardRequested: false);
            try
            {
                if (copyCalls != 0 || Descendants(result).OfType<TextBlock>().Any(text => text.Text.Contains(english ? "not copied" : "复制未完成")))
                    throw new Exception("Disabled auto-copy was reported as a clipboard failure or copied implicitly.");
                var copy = FindButton(result, english ? "Copy again" : "再次复制"); Click(copy);
                if (!Descendants(result).OfType<TextBlock>().Any(text => text.Text.StartsWith(english ? "Copy did not complete:" : "复制未完成：")))
                    throw new Exception("Retry failure feedback missing.");
                copySucceeds = true; Click(copy);
                if (copyCalls != 2 || !Descendants(result).OfType<TextBlock>().Any(text => text.Text.StartsWith(english ? "Copied again:" : "已重新复制：")))
                    throw new Exception("Retry recovery feedback missing.");
                if (result.Title != (english ? "Partial scrolling capture" : "长截图部分结果")) throw new Exception("Partial result title mismatch.");
                Render(result, output, $"result-{language}", 760, 500);
            }
            finally { result.Close(); }
            var failedCopy = new ScrollCaptureResultWindow(bitmap, ScrollCaptureStopReason.EndOfScroll, path, false,
                interfaceLanguage: language, clipboardRequested: true);
            try
            {
                if (!Descendants(failedCopy).OfType<TextBlock>().Any(text => text.Text.StartsWith(english ? "Saved, but not copied." : "已保存，但复制未完成。")))
                    throw new Exception("Requested copy failure is indistinguishable from disabled copying.");
                Render(failedCopy, output, $"copy-recovery-{language}", 520, 400);
            }
            finally { failedCopy.Close(); }
        }
        Console.WriteLine($"Capture companion languages and recovery checks passed offscreen: {output}");
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button FindButton(DependencyObject root, string name) => Descendants(root).OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }
    private static void Render(Window window, string output, string name, int width, int height)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
        if (window.IsVisible) throw new Exception("Test occupied foreground.");
    }
}

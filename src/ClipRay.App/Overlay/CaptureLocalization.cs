using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ClipRay.App.Settings;
using Button = System.Windows.Controls.Button;

namespace ClipRay.App.Overlay;

// Display-only translations. Never translate command identities, tags or user OCR text.
internal static class CaptureLocalization
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["自定义选区尺寸"] = "Set selection size", ["移动（锁定尺寸）"] = "Move (keep size)",
        ["画笔"] = "Pen", ["马赛克"] = "Mosaic", ["取色"] = "Pick color", ["长截图"] = "Scrolling capture",
        ["标注工具"] = "Annotations", ["箭头"] = "Arrow", ["矩形"] = "Rectangle", ["圆形"] = "Circle",
        ["直线"] = "Line", ["直线：拖动绘制"] = "Line: drag to draw",
        ["椭圆"] = "Ellipse", ["文字框"] = "Text box", ["关闭标注"] = "Stop annotating", ["字号"] = "Font size",
        ["宽 (px)"] = "Width (px)", ["高 (px)"] = "Height (px)",
        ["箭头：拖动绘制由细到粗的箭头"] = "Arrow: drag to draw a tapered arrow",
        ["矩形：拖动绘制，Shift 绘制正方形"] = "Rectangle: drag; hold Shift for a square",
        ["椭圆：拖动绘制，Shift 绘制圆形"] = "Ellipse: drag; hold Shift for a circle",
        ["圆形：拖动绘制"] = "Circle: drag to draw",
        ["文字框：点击或拖动输入，Ctrl+Enter 完成"] = "Text box: click or drag, Ctrl+Enter to finish",
        ["翻译（右键临时切换模型）"] = "Translate (right-click to choose a model)",
        ["实时翻译当前选区（页面不会冻结）"] = "Translate this region live (page stays interactive)",
        ["实时翻译当前选区"] = "Translate this region live", ["实时"] = "Live",
        ["文字提取"] = "Extract text", ["录屏"] = "Record", ["撤回"] = "Undo", ["取消"] = "Cancel",
        ["确认（Enter）"] = "Confirm (Enter)", ["红色"] = "Red", ["橙色"] = "Orange",
        ["黄色"] = "Yellow", ["绿色"] = "Green", ["青色"] = "Cyan", ["蓝色"] = "Blue",
        ["紫色"] = "Purple", ["白色"] = "White", ["色盘"] = "Palette", ["粗细"] = "Width",
        ["圆形笔刷"] = "Brush size", ["像素块"] = "Pixel size", ["手动滚动"] = "Scroll manually",
        ["自动滚动"] = "Scroll automatically", ["停止并生成"] = "Stop and stitch",
        ["宽"] = "Width", ["高"] = "Height", ["应用尺寸"] = "Apply size",
        ["显示原文"] = "Show original", ["显示译文"] = "Show translation",
        ["PP-OCR 主路径 · Windows OCR 回退"] = "PP-OCR primary · Windows OCR fallback",
        ["收起"] = "Collapse", ["按视觉行保留换行"] = "Keep line breaks from the image",
        ["一键复制"] = "Copy text", ["再按一次 Esc 退出"] = "Press Esc again to exit",
        ["已撤回上一步"] = "Last edit undone", ["已重做上一步"] = "Last edit redone",
        ["选择滚动方式"] = "Choose how to scroll", ["正在运行本地 OCR 快速路径…"] = "Running local OCR…",
        ["已按行复制到剪贴板"] = "Text copied with line breaks", ["剪贴板暂忙，请稍后重试"] = "Clipboard is busy. Try again.",
        ["剪贴板暂不可用，请重试"] = "Clipboard is unavailable. Try again.",
        ["编辑模式已关闭：可拖动选区或调整手柄"] = "Editing off: move or resize the selection",
        ["画笔：在选区内拖动绘制"] = "Pen: drag inside the selection",
        ["马赛克：在选区内拖动涂抹"] = "Mosaic: drag inside the selection",
        ["取色：点击冻结截图中的像素"] = "Pick color: click a pixel in the captured image",
        ["移动：尺寸已锁定，拖动框内或边框"] = "Move: drag inside or on the border; size stays fixed",
        ["移动模式已关闭"] = "Move mode off", ["请输入不小于 8 的宽和高"] = "Enter a width and height of at least 8",
        ["请框选录屏区域，按 Enter 或点击确认开始录屏"] = "Select a region, then press Enter or Confirm to start recording",
        ["本地 LMT（隐私优先）"] = "Local LMT (privacy first)", ["本地机翻（离线神经翻译）"] = "Local machine translation (offline)",
        ["本地 LMT"] = "Local LMT", ["本地机翻"] = "Local machine translation"
    };

    public static string Text(string language, string source) =>
        AppInterfaceLanguage.Normalize(language) == "en" && English.TryGetValue(source, out var value) ? value : source;

    public static void ApplyStatic(DependencyObject node, string language)
    {
        if (node is TextBlock text) text.Text = Text(language, text.Text);
        if (node is Button button && button.Content is string caption) button.Content = Text(language, caption);
        if (node is FrameworkElement element)
        {
            if (element.ToolTip is string tooltip) element.ToolTip = Text(language, tooltip);
            var name = AutomationProperties.GetName(element);
            if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(element, Text(language, name));
            else if (element is Button && element.ToolTip is string label) AutomationProperties.SetName(element, label);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().ToArray()) ApplyStatic(child, language);
    }
}

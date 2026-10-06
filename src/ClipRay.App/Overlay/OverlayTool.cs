using System.Windows.Input;

namespace ClipRay.App.Overlay;

public static class OverlayTool
{
    public static RoutedUICommand Move { get; } = Create("移动", nameof(Move));

    public static RoutedUICommand Pen { get; } = Create("画笔", nameof(Pen));

    public static RoutedUICommand Mosaic { get; } = Create("马赛克", nameof(Mosaic));

    public static RoutedUICommand Picker { get; } = Create("取色", nameof(Picker));

    public static RoutedUICommand ExtractText { get; } = Create("文字提取", nameof(ExtractText));

    public static RoutedUICommand LongCapture { get; } = Create("长截图", nameof(LongCapture));

    public static RoutedUICommand Translate { get; } = Create("翻译", nameof(Translate));

    public static RoutedUICommand LiveTranslate { get; } = Create("实时翻译", nameof(LiveTranslate));

    public static RoutedUICommand Record { get; } = Create("录屏", nameof(Record));

    public static RoutedUICommand Undo { get; } = Create("撤回", nameof(Undo));

    public static RoutedUICommand Cancel { get; } = Create("取消", nameof(Cancel));

    public static RoutedUICommand Confirm { get; } = Create("确认", nameof(Confirm));

    private static RoutedUICommand Create(string text, string name) => new(text, name, typeof(OverlayTool));
}

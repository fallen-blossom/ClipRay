using System.Windows.Media;
using System.Windows.Shapes;

namespace ClipRay.App.Ui;

public enum ToolbarIconKind { Pause, Play, Save, ViewResult, Cancel, Complete, ZoomIn, ZoomOut, FitWidth, Reset, Copy, Folder }

public enum ToolbarIconTone { Neutral, Primary, Danger, Confirm }

public static class ToolbarIconFactory
{
    public static Path Create(ToolbarIconKind kind, ToolbarIconTone tone) => new()
    {
        Data = Geometry.Parse(PathData(kind)),
        Stroke = new SolidColorBrush(ToneColor(tone)),
        StrokeThickness = 2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 20,
        Height = 20,
        Stretch = Stretch.Uniform,
    };

    private static string PathData(ToolbarIconKind kind) => kind switch
    {
        ToolbarIconKind.Pause => "M 6,4 L 6,16 M 14,4 L 14,16",
        ToolbarIconKind.Play => "M 7,4 L 17,10 L 7,16 Z",
        ToolbarIconKind.Save => "M 4,3 L 16,3 L 18,5 L 18,17 L 4,17 Z M 7,3 L 7,8 L 15,8 L 15,3 M 7,17 L 7,12 L 15,12 L 15,17",
        ToolbarIconKind.Copy => "M 7,7 L 17,7 L 17,18 L 7,18 Z M 4,14 L 3,14 L 3,3 L 13,3 L 13,4",
        ToolbarIconKind.Folder => "M 2,6 L 2,16 L 18,16 L 18,6 L 9,6 L 7,3 L 2,3 Z",
        ToolbarIconKind.ViewResult => "M 2,10 C 5,5 15,5 18,10 C 15,15 5,15 2,10 Z M 7,10 A 3,3 0 1 0 13,10 A 3,3 0 1 0 7,10",
        ToolbarIconKind.Cancel => "M 5,5 L 15,15 M 15,5 L 5,15",
        ToolbarIconKind.Complete => "M 4,10 L 8,14 L 16,5",
        ToolbarIconKind.ZoomIn => "M 8,4 A 5,5 0 1 0 8,14 A 5,5 0 1 0 8,4 M 12,12 L 17,17 M 8,6 L 8,12 M 5,9 L 11,9",
        ToolbarIconKind.ZoomOut => "M 8,4 A 5,5 0 1 0 8,14 A 5,5 0 1 0 8,4 M 12,12 L 17,17 M 5,9 L 11,9",
        ToolbarIconKind.FitWidth => "M 3,5 L 17,5 M 3,15 L 17,15 M 6,8 L 3,10 L 6,12 M 14,8 L 17,10 L 14,12",
        ToolbarIconKind.Reset => "M 16,8 A 6,6 0 1 0 16,12 M 16,4 L 16,8 L 12,8",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static System.Windows.Media.Color ToneColor(ToolbarIconTone tone) => tone switch
    {
        ToolbarIconTone.Neutral => System.Windows.Media.Color.FromRgb(0xE8, 0xEC, 0xF5),
        ToolbarIconTone.Primary => System.Windows.Media.Color.FromRgb(0x61, 0xB8, 0xFF),
        ToolbarIconTone.Danger => System.Windows.Media.Color.FromRgb(0xFF, 0x78, 0x78),
        ToolbarIconTone.Confirm => System.Windows.Media.Color.FromRgb(0x72, 0xE8, 0x9D),
        _ => throw new ArgumentOutOfRangeException(nameof(tone)),
    };
}

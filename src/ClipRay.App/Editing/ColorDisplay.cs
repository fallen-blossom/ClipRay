using System.Drawing;

namespace ClipRay.App.Editing;

public sealed record ColorDisplay(string Hex, string Rgb)
{
    public static ColorDisplay Format(Color color) => new(
        $"#{color.R:X2}{color.G:X2}{color.B:X2}",
        $"RGB({color.R}, {color.G}, {color.B})");
}

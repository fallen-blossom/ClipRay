using System.Drawing;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public enum AnnotationKind { Arrow, Rectangle, Ellipse, Text, Line }

public sealed record Annotation(AnnotationKind Kind, PixelPoint Start, PixelPoint End,
    Color Color, float Width, string Text = "", float FontSize = 22)
{
    public PixelRect Bounds => SelectionMath.FromPoints(Start, End);
}

public static class AnnotationGeometry
{
    // A pointed tail widens continuously towards the shoulder; no constant-width shaft.
    public static PixelPoint[] Arrow(PixelPoint start, PixelPoint end, double width)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return [];
        var ux = dx / length; var uy = dy / length;
        var half = Math.Min(length * .10, Math.Min(12, width * .5 + length * .004));
        var headLength = Math.Min(length * .4, Math.Clamp(half * 3.4, 8, 36));
        var shoulder = new PixelPoint(end.X - ux * headLength, end.Y - uy * headLength);
        PixelPoint Side(double offset) => new(shoulder.X - uy * offset, shoulder.Y + ux * offset);
        return [start, Side(half), Side(half * 2.2), end, Side(-half * 2.2), Side(-half)];
    }

    public static PixelPoint ConstrainCircle(PixelPoint start, PixelPoint end, PixelRect selection)
    {
        var sx = end.X < start.X ? -1 : 1; var sy = end.Y < start.Y ? -1 : 1;
        var availableX = sx > 0 ? selection.Right - 1 - start.X : start.X - selection.Left;
        var availableY = sy > 0 ? selection.Bottom - 1 - start.Y : start.Y - selection.Top;
        var side = Math.Max(0, Math.Min(Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)),
            Math.Min(availableX, availableY)));
        return new(start.X + sx * side, start.Y + sy * side);
    }
}

using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public static class SelectionDrawableBounds
{
    public static bool Contains(PixelRect selection, PixelPoint point) =>
        point.X >= selection.Left
        && point.X < selection.Right
        && point.Y >= selection.Top
        && point.Y < selection.Bottom;

    public static PixelPoint Clamp(PixelRect selection, PixelPoint point)
    {
        if (selection.Width <= 0 || selection.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selection), "A drawable selection needs positive dimensions.");
        }

        return new PixelPoint(
            Math.Clamp(point.X, selection.Left, Math.BitDecrement(selection.Right)),
            Math.Clamp(point.Y, selection.Top, Math.BitDecrement(selection.Bottom)));
    }
}

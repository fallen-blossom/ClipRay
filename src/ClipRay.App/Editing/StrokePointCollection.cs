using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public static class StrokePointCollection
{
    public static bool AppendClampedDistinct(List<PixelPoint> points, PixelPoint point, PixelRect selection)
    {
        var clamped = SelectionDrawableBounds.Clamp(selection, point);
        if (points.Count > 0 && points[^1] == clamped)
        {
            return false;
        }

        points.Add(clamped);
        return true;
    }
}

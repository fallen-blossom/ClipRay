using System.Drawing;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public sealed class PenStroke
{
    public PenStroke(IReadOnlyList<PixelPoint> points, Color color, float width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        Points = points.Count == 0 ? throw new ArgumentException("A pen stroke needs at least one point.", nameof(points)) : points.ToArray();
        Color = color;
        Width = width;
    }

    public IReadOnlyList<PixelPoint> Points { get; }

    public Color Color { get; }

    public float Width { get; }
}

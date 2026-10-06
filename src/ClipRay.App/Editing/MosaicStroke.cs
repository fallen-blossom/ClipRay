using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public sealed class MosaicStroke
{
    public MosaicStroke(IReadOnlyList<PixelPoint> points, int brushSize, int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(brushSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        Points = points.Count == 0 ? throw new ArgumentException("A mosaic stroke needs at least one point.", nameof(points)) : points.ToArray();
        BrushSize = brushSize;
        BlockSize = blockSize;
    }

    public IReadOnlyList<PixelPoint> Points { get; }

    public int BrushSize { get; }

    public int BlockSize { get; }
}

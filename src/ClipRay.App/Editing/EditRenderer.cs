using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public static class EditRenderer
{
    public static Bitmap Composite(Bitmap source, EditLayer layer) => CompositeCore(source, layer, null);

    public static Bitmap CompositeSelection(Bitmap selectionBase, Bitmap mosaicSource, EditLayer layer)
    {
        ArgumentNullException.ThrowIfNull(selectionBase);
        ArgumentNullException.ThrowIfNull(mosaicSource);
        ArgumentNullException.ThrowIfNull(layer);
        var expected = ToRectangle(layer.Selection, mosaicSource);
        if (selectionBase.Width != expected.Width || selectionBase.Height != expected.Height)
            throw new ArgumentException("Selection bitmap dimensions do not match the edit layer.", nameof(selectionBase));
        var output = selectionBase.Clone(
            new Rectangle(0, 0, selectionBase.Width, selectionBase.Height),
            PixelFormat.Format32bppPArgb);
        ApplyOperations(mosaicSource, output, layer, activeMosaic: null);
        return output;
    }

    public static Bitmap CompositePreview(Bitmap source, EditLayer layer, MosaicStroke activeMosaic) =>
        CompositeCore(source, layer, activeMosaic);

    private static Bitmap CompositeCore(Bitmap source, EditLayer layer, MosaicStroke? activeMosaic)
    {
        var selection = ToRectangle(layer.Selection, source);
        var output = source.Clone(selection, PixelFormat.Format32bppPArgb);
        ApplyOperations(source, output, layer, activeMosaic);
        return output;
    }

    private static void ApplyOperations(Bitmap mosaicSource, Bitmap output, EditLayer layer, MosaicStroke? activeMosaic)
    {
        foreach (var operation in layer.Operations)
        {
            switch (operation)
            {
                case PenEditOperation pen:
                    DrawPenStroke(output, pen.Stroke, layer.Selection);
                    break;
                case MosaicEditOperation mosaic:
                    DrawMosaicStroke(mosaicSource, output, mosaic.Stroke, layer.Selection);
                    break;
            }
        }

        if (activeMosaic is not null)
        {
            DrawMosaicStroke(mosaicSource, output, activeMosaic, layer.Selection);
        }
    }

    private static void DrawPenStroke(Bitmap output, PenStroke stroke, PixelRect selection)
    {
        using var graphics = Graphics.FromImage(output);
        graphics.SetClip(new Rectangle(0, 0, output.Width, output.Height));
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.Default;
        using var pen = new Pen(stroke.Color, stroke.Width) { LineJoin = LineJoin.Round };
        if (stroke.Points.Count == 1)
        {
            var point = ToOutputPoint(stroke.Points[0], selection);
            DrawPenStamp(graphics, stroke.Color, stroke.Width, point, selection);
            return;
        }

        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var start = ToOutputPoint(stroke.Points[index - 1], selection);
            var end = ToOutputPoint(stroke.Points[index], selection);
            if (TryClipLine(ref start, ref end, selection))
            {
                graphics.DrawLine(pen, start, end);
            }
        }

        foreach (var point in stroke.Points)
        {
            DrawPenStamp(graphics, stroke.Color, stroke.Width, ToOutputPoint(point, selection), selection);
        }
    }

    private static void DrawPenStamp(Graphics graphics, Color color, float width, PointF point, PixelRect selection)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= selection.Width || point.Y >= selection.Height)
        {
            return;
        }

        var size = Math.Max(1, (int)Math.Ceiling(width));
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, (int)Math.Floor(point.X), (int)Math.Floor(point.Y), size, size);
    }

    private static bool TryClipLine(ref PointF start, ref PointF end, PixelRect selection)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var entering = 0f;
        var leaving = 1f;
        return Clip(-deltaX, start.X, ref entering, ref leaving)
            && Clip(deltaX, (float)(selection.Width - 1 - start.X), ref entering, ref leaving)
            && Clip(-deltaY, start.Y, ref entering, ref leaving)
            && Clip(deltaY, (float)(selection.Height - 1 - start.Y), ref entering, ref leaving)
            && ApplyClip(ref start, ref end, entering, leaving);
    }

    private static bool Clip(float delta, float edge, ref float entering, ref float leaving)
    {
        if (delta == 0)
        {
            return edge >= 0;
        }

        var ratio = edge / delta;
        if (delta < 0)
        {
            if (ratio > leaving) return false;
            entering = Math.Max(entering, ratio);
        }
        else
        {
            if (ratio < entering) return false;
            leaving = Math.Min(leaving, ratio);
        }

        return true;
    }

    private static bool ApplyClip(ref PointF start, ref PointF end, float entering, float leaving)
    {
        if (entering > leaving)
        {
            return false;
        }

        var originalStart = start;
        var deltaX = end.X - originalStart.X;
        var deltaY = end.Y - originalStart.Y;
        start = new PointF(originalStart.X + entering * deltaX, originalStart.Y + entering * deltaY);
        end = new PointF(originalStart.X + leaving * deltaX, originalStart.Y + leaving * deltaY);
        return true;
    }

    private static void DrawMosaicStroke(Bitmap source, Bitmap output, MosaicStroke stroke, PixelRect selection)
    {
        var visited = new HashSet<(int X, int Y)>();
        var brushes = new Dictionary<int, SolidBrush>();
        using var graphics = Graphics.FromImage(output);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        try
        {
            foreach (var point in stroke.Points)
            {
                var radius = Math.Max(0.5, stroke.BrushSize / 2.0);
                var startX = (int)Math.Floor(point.X - radius);
                var endX = (int)Math.Ceiling(point.X + radius);
                var startY = (int)Math.Floor(point.Y - radius);
                var endY = (int)Math.Ceiling(point.Y + radius);
                for (var y = startY; y <= endY; y++)
                {
                    for (var x = startX; x <= endX; x++)
                    {
                        var deltaX = x + 0.5 - point.X;
                        var deltaY = y + 0.5 - point.Y;
                        if (deltaX * deltaX + deltaY * deltaY > radius * radius || !visited.Add((x, y))) continue;
                        FillMosaicPixel(source, graphics, brushes, x, y, stroke.BlockSize, selection);
                    }
                }
            }
        }
        finally
        {
            foreach (var brush in brushes.Values) brush.Dispose();
        }
    }

    private static void FillMosaicPixel(
        Bitmap source,
        Graphics graphics,
        IDictionary<int, SolidBrush> brushes,
        int x,
        int y,
        int blockSize,
        PixelRect selection)
    {
        if (x < selection.Left || x >= selection.Right || y < selection.Top || y >= selection.Bottom) return;
        var blockX = FloorToBlock(x, blockSize);
        var blockY = FloorToBlock(y, blockSize);
        var sourceLeft = Math.Clamp(blockX, 0, source.Width - 1);
        var sourceTop = Math.Clamp(blockY, 0, source.Height - 1);
        var color = source.GetPixel(sourceLeft, sourceTop);
        if (!brushes.TryGetValue(color.ToArgb(), out var brush))
        {
            brush = new SolidBrush(color);
            brushes.Add(color.ToArgb(), brush);
        }
        graphics.FillRectangle(brush, x - (int)selection.X, y - (int)selection.Y, 1, 1);
    }

    private static Rectangle ToRectangle(PixelRect rect, Bitmap source)
    {
        var left = Math.Clamp((int)Math.Floor(rect.Left), 0, source.Width - 1);
        var top = Math.Clamp((int)Math.Floor(rect.Top), 0, source.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right), left + 1, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom), top + 1, source.Height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static PointF ToOutputPoint(PixelPoint point, PixelRect selection) => new(
        (float)(point.X - selection.X),
        (float)(point.Y - selection.Y));

    private static int FloorToBlock(int value, int blockSize) =>
        (int)Math.Floor(value / (double)blockSize) * blockSize;
}

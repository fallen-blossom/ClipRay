using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Editing;

public static class EditRenderer
{
    public static Bitmap Composite(Bitmap source, EditLayer layer) => CompositeCore(source, layer, null);

    // Retained operations keep screen coordinates. OCR padding must keep their masks, too.
    public static Bitmap CompositeCapture(Bitmap source, EditLayer layer, PixelRect captureBounds)
    {
        var capture = ToRectangle(captureBounds, source);
        // Round dots and wide-line centers may lie outside the crop while their ink reaches it.
        // Replay with enough room for the full pen width, then crop without changing export rules.
        var margin = layer.PenStrokes.Count == 0 ? 0 : (int)Math.Clamp(
            Math.Ceiling(layer.PenStrokes.Max(stroke => stroke.Width)), 1, Math.Max(source.Width, source.Height));
        var working = Rectangle.FromLTRB(
            Math.Max(0, capture.Left - margin), Math.Max(0, capture.Top - margin),
            (int)Math.Min(source.Width, (long)capture.Right + margin),
            (int)Math.Min(source.Height, (long)capture.Bottom + margin));
        var output = source.Clone(working, PixelFormat.Format32bppPArgb);
        ApplyOperations(source, output, layer, null, new PixelRect(working.X, working.Y, working.Width, working.Height));
        if (working == capture) return output;
        using (output)
        {
            return output.Clone(new Rectangle(capture.X - working.X, capture.Y - working.Y, capture.Width, capture.Height),
                PixelFormat.Format32bppPArgb);
        }
    }

    public static Bitmap RenderOverlay(Bitmap source, EditLayer layer, MosaicStroke? activeMosaic = null)
    {
        var bounds = ToRectangle(layer.Selection, source);
        var output = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        ApplyOperations(source, output, layer, activeMosaic);
        return output;
    }

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

    private static void ApplyOperations(Bitmap mosaicSource, Bitmap output, EditLayer layer, MosaicStroke? activeMosaic, PixelRect? outputBounds = null)
    {
        var selection = outputBounds ?? layer.Selection;
        foreach (var operation in layer.Operations)
        {
            switch (operation)
            {
                case PenEditOperation pen:
                    DrawPenStroke(output, pen.Stroke, selection);
                    break;
                case MosaicEditOperation mosaic:
                    DrawMosaicStroke(mosaicSource, output, mosaic.Stroke, selection);
                    break;
                case AnnotationEditOperation annotation:
                    DrawAnnotation(output, annotation.Annotation, selection);
                    break;
            }
        }

        if (activeMosaic is not null)
        {
            DrawMosaicStroke(mosaicSource, output, activeMosaic, selection);
        }
    }

    internal static void DrawOperation(Bitmap source, Bitmap output, EditOperation operation, PixelRect selection)
    {
        if(operation is PenEditOperation pen) DrawPenStroke(output,pen.Stroke,selection);
        else if(operation is MosaicEditOperation mosaic) DrawMosaicStroke(source,output,mosaic.Stroke,selection);
        else if(operation is AnnotationEditOperation annotation) DrawAnnotation(output,annotation.Annotation,selection);
    }

    private static void DrawAnnotation(Bitmap output, Annotation annotation, PixelRect selection)
    {
        using var graphics = Graphics.FromImage(output);
        graphics.SetClip(new Rectangle(0, 0, output.Width, output.Height));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(annotation.Color);
        using var pen = new Pen(annotation.Color, annotation.Width);
        var bounds = annotation.Bounds;
        var rectangle = new RectangleF((float)(bounds.X - selection.X), (float)(bounds.Y - selection.Y),
            (float)bounds.Width, (float)bounds.Height);
        switch (annotation.Kind)
        {
            case AnnotationKind.Line:
                pen.StartCap = pen.EndCap = LineCap.Round;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawLine(pen, ToOutputPoint(annotation.Start, selection), ToOutputPoint(annotation.End, selection));
                break;
            case AnnotationKind.Arrow:
                var points = AnnotationGeometry.Arrow(annotation.Start, annotation.End, annotation.Width);
                if (points.Length >= 3) graphics.FillPolygon(brush, points.Select(p => ToOutputPoint(p, selection)).ToArray());
                break;
            case AnnotationKind.Rectangle when bounds.Width > 0 && bounds.Height > 0:
                graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                break;
            case AnnotationKind.Ellipse when bounds.Width > 0 && bounds.Height > 0:
                graphics.DrawEllipse(pen, rectangle);
                break;
            case AnnotationKind.Text when bounds.Width > 0 && bounds.Height > 0:
                using (var font = new Font("Segoe UI", annotation.FontSize, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var format = new StringFormat(StringFormat.GenericTypographic))
                {
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    graphics.DrawString(annotation.Text, font, brush, rectangle, format);
                }
                break;
        }
    }

    private static void DrawPenStroke(Bitmap output, PenStroke stroke, PixelRect selection)
    {
        using var graphics = Graphics.FromImage(output);
        graphics.SetClip(new Rectangle(0, 0, output.Width, output.Height));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        using var pen = new Pen(stroke.Color, stroke.Width)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        if (stroke.Points.Count == 1)
        {
            var point = ToOutputPoint(stroke.Points[0], selection);
            var radius = stroke.Width / 2;
            using var brush = new SolidBrush(stroke.Color);
            graphics.FillEllipse(brush, point.X - radius, point.Y - radius, stroke.Width, stroke.Width);
            return;
        }

        if (stroke.Points.Count > 1)
        {
            // Keep original centers and let the bitmap clip the complete rounded path.
            graphics.DrawLines(pen, stroke.Points.Select(point => ToOutputPoint(point, selection)).ToArray());
        }
    }

    internal static void DrawMosaicStroke(Bitmap source, Bitmap output, MosaicStroke stroke, PixelRect selection, MosaicPaintState? retained = null)
    {
        var state=retained??new MosaicPaintState();
        var visited = state.Visited;
        var brushes = state.Brushes;
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
                        FillMosaicPixel(source, graphics, state, x, y, stroke.BlockSize, selection);
                    }
                }
            }
        }
        finally
        {
            if(retained is null) state.Dispose();
        }
    }

    private static void FillMosaicPixel(
        Bitmap source,
        Graphics graphics,
        MosaicPaintState state,
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
        if(!state.Colors.TryGetValue((sourceLeft,sourceTop),out var color))
        { color=source.GetPixel(sourceLeft, sourceTop);state.Colors.Add((sourceLeft,sourceTop),color);state.SourceSamples++; }
        var brushes=state.Brushes;
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

internal sealed class MosaicPaintState : IDisposable
{
    internal readonly HashSet<(int X,int Y)> Visited=[];
    internal readonly Dictionary<(int X,int Y),Color> Colors=[];
    internal readonly Dictionary<int,SolidBrush> Brushes=[];
    internal int SourceSamples;
    public void Dispose() {foreach(var brush in Brushes.Values)brush.Dispose();Brushes.Clear();}
}

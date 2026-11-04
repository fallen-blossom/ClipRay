using System.Drawing;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public enum VisualSafetyConfidence
{
    Low,
    Medium,
    High,
}
public sealed class TextMaskResult : IDisposable
{
    public TextMaskResult(Bitmap mask, PixelRect bounds, VisualSafetyConfidence confidence)
    {
        Mask = mask;
        Bounds = bounds;
        Confidence = confidence;
    }

    public Bitmap Mask { get; }
    public PixelRect Bounds { get; }
    public VisualSafetyConfidence Confidence { get; }

    public void Dispose() => Mask.Dispose();
}

public static class TextReplacementMask
{
    private const double MinimumInkDistance = 30;

    public static TextMaskResult Build(Bitmap source, VisualTextRegion region,
        IReadOnlyList<PixelRect>? protectedNeighborBounds = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(region);
        var mask = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        // CTC word intervals locate style spans; blank/time-step boundaries can
        // cut through real glyphs. Follow connected ink into the detected parent
        // line, while leaving isolated, unrecognized icons in word gaps alone.
        var words = region.Lines.SelectMany(ReplacementWords).ToArray();
        if (words.Length == 0)
        {
            var bounds = Clamp(region.Bounds, source);
            if (region.Role == VisualTextRole.Control && !bounds.IsEmpty)
            {
                for (var y = (int)bounds.Top; y < (int)bounds.Bottom; y++)
                for (var x = (int)bounds.Left; x < (int)bounds.Right; x++)
                {
                    if (IsProtected(x, y, protectedNeighborBounds)) continue;
                    mask.SetPixel(x, y, Color.FromArgb(255, 255, 255, 255));
                }
                return new TextMaskResult(mask, bounds, VisualSafetyConfidence.High);
            }
            return new TextMaskResult(mask, bounds, VisualSafetyConfidence.Low);
        }

        var marked = 0;
        foreach (var word in words)
        {
            var bounds = Clamp(word.Bounds, source);
            if (bounds.IsEmpty) continue;
            var backing = SampleDominantRing(source, bounds);
            var left = (int)bounds.Left;
            var top = (int)bounds.Top;
            var right = (int)bounds.Right;
            var bottom = (int)bounds.Bottom;
            var ink = new List<(int X, int Y)>();
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                if (ColorDistance(source.GetPixel(x, y), backing) >= MinimumInkDistance)
                {
                    ink.Add((x, y));
                }
            }

            if (word.IsDetectedLine && word.RecognitionWords.Count > 0)
                ink = ConnectedRecognizedInk(ink, bounds, word.RecognitionWords, protectedNeighborBounds);

            foreach (var pixel in ink)
            {
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var x = pixel.X + dx;
                    var y = pixel.Y + dy;
                    if (x < left || x >= right || y < top || y >= bottom) continue;
                    if (IsProtected(x, y, protectedNeighborBounds)) continue;
                    if (mask.GetPixel(x, y).A == 0) marked++;
                    mask.SetPixel(x, y, Color.FromArgb(255, 255, 255, 255));
                }
            }
        }

        if (marked == 0 && region.Role == VisualTextRole.Control)
        {
            // A failed parent-line seed cannot authorize clearing its gaps.
            foreach (var word in region.Lines.SelectMany(static line =>
                ReplacementWords(line with { PrimaryRecognitionWords = [] })))
            {
                var bounds = Clamp(word.Bounds, source);
                for (var y = (int)bounds.Top; y < (int)bounds.Bottom; y++)
                for (var x = (int)bounds.Left; x < (int)bounds.Right; x++)
                {
                    if (IsProtected(x, y, protectedNeighborBounds)) continue;
                    mask.SetPixel(x, y, Color.FromArgb(255, 255, 255, 255));
                    marked++;
                }
            }
        }

        var confidence = marked == 0
            ? VisualSafetyConfidence.Low
            : region.Confidence == VisualStructureConfidence.Low
                ? VisualSafetyConfidence.Medium
                : VisualSafetyConfidence.High;
        return new TextMaskResult(mask, Clamp(region.Bounds, source), confidence);
    }

    private static Color SampleDominantRing(Bitmap source, PixelRect bounds)
    {
        var left = Math.Max(0, (int)Math.Floor(bounds.Left) - 2);
        var top = Math.Max(0, (int)Math.Floor(bounds.Top) - 2);
        var right = Math.Min(source.Width, (int)Math.Ceiling(bounds.Right) + 2);
        var bottom = Math.Min(source.Height, (int)Math.Ceiling(bounds.Bottom) + 2);
        var innerLeft = (int)Math.Floor(bounds.Left);
        var innerTop = (int)Math.Floor(bounds.Top);
        var innerRight = (int)Math.Ceiling(bounds.Right);
        var innerBottom = (int)Math.Ceiling(bounds.Bottom);
        var samples = new List<Color>();
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            if (x < innerLeft || x >= innerRight || y < innerTop || y >= innerBottom)
            {
                samples.Add(source.GetPixel(x, y));
            }
        }
        if (samples.Count == 0) return Color.Black;
        var cluster = samples
            .GroupBy(static color => (color.R / 16, color.G / 16, color.B / 16))
            .OrderByDescending(static group => group.Count())
            .First()
            .ToArray();
        return Color.FromArgb(
            255,
            (int)Math.Round(cluster.Average(static color => color.R)),
            (int)Math.Round(cluster.Average(static color => color.G)),
            (int)Math.Round(cluster.Average(static color => color.B)));
    }

    private static string Compact(string text) => string.Concat(text.Where(static value => !char.IsWhiteSpace(value)));

    private static IEnumerable<ClipRay.App.Ocr.OcrWord> ReplacementWords(ClipRay.App.Ocr.OcrTextBlock line)
    {
        var parents = line.PrimaryRecognitionWords;
        var parentText = Compact(string.Concat(parents.Select(static word => word.Text)));
        var text = Compact(line.Text);
        var matches = string.Equals(parentText, text, StringComparison.Ordinal)
            || line.SelectionBounds.HasValue && text.Length > 0
                && string.Equals(Compact(string.Concat(line.Words.Select(static word => word.Text))), text, StringComparison.Ordinal)
                && parentText.Contains(text, StringComparison.Ordinal);
        var useParents = matches && parents.Count > 0
            && parents.All(static word => word.IsDetectedLine && word.RecognitionWords.Count > 0);
        foreach (var word in useParents ? parents : line.Words)
        {
            if (line.SelectionBounds is not { } clip) { yield return word; continue; }
            clip = new PixelRect(Math.Max(clip.Left, line.Bounds.Left), Math.Max(clip.Top, line.Bounds.Top),
                Math.Max(0, Math.Min(clip.Right, line.Bounds.Right) - Math.Max(clip.Left, line.Bounds.Left)),
                Math.Max(0, Math.Min(clip.Bottom, line.Bounds.Bottom) - Math.Max(clip.Top, line.Bounds.Top)));
            var left = Math.Max(word.Bounds.Left, clip.Left);
            var top = Math.Max(word.Bounds.Top, clip.Top);
            var right = Math.Min(word.Bounds.Right, clip.Right);
            var bottom = Math.Min(word.Bounds.Bottom, clip.Bottom);
            if (right <= left || bottom <= top) continue;
            yield return word with
            {
                Bounds = new(left, top, right - left, bottom - top),
                // Seed only the selected glyph boxes, never an excluded sibling.
                RecognitionWords = useParents ? line.Words : word.RecognitionWords,
            };
        }
    }

    private static List<(int X,int Y)> ConnectedRecognizedInk(List<(int X,int Y)> ink, PixelRect bounds,
        IReadOnlyList<ClipRay.App.Ocr.OcrWord> recognized, IReadOnlyList<PixelRect>? protectedBounds)
    {
        var left=(int)bounds.Left; var top=(int)bounds.Top;
        var width=(int)bounds.Width; var height=(int)bounds.Height;
        var states=new byte[checked(width*height)];
        foreach(var pixel in ink)
            if(!IsProtected(pixel.X,pixel.Y,protectedBounds)) states[(pixel.Y-top)*width+pixel.X-left]=1;
        var output=new List<(int X,int Y)>();
        foreach(var pixel in ink)
        {
            var initial=(pixel.Y-top)*width+pixel.X-left;
            if(states[initial]!=1) continue;
            var component=new List<(int X,int Y)> {pixel};
            states[initial]=2;
            var hasRecognizedInk=false;
            for(var index=0;index<component.Count;index++)
            {
                var current=component[index];
                hasRecognizedInk |= recognized.Any(word=>current.X>=word.Bounds.Left && current.X<word.Bounds.Right
                    && current.Y>=word.Bounds.Top && current.Y<word.Bounds.Bottom);
                for(var dy=-1;dy<=1;dy++)
                for(var dx=-1;dx<=1;dx++)
                {
                    var x=current.X+dx-left; var y=current.Y+dy-top;
                    if(x<0||x>=width||y<0||y>=height||states[y*width+x]!=1) continue;
                    states[y*width+x]=2;
                    component.Add((x+left,y+top));
                }
            }
            if(hasRecognizedInk) output.AddRange(component);
        }
        return output;
    }

    private static bool IsProtected(int x, int y, IReadOnlyList<PixelRect>? protectedBounds)
    {
        if (protectedBounds is null) return false;
        foreach (var bounds in protectedBounds)
        {
            if (x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom)
                return true;
        }
        return false;
    }

    private static PixelRect Clamp(PixelRect bounds, Bitmap source)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, source.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static double ColorDistance(Color first, Color second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }
}

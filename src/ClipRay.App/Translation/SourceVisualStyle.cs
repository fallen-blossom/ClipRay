using System.Drawing;
using ClipRay.App.Ocr;

namespace ClipRay.App.Translation;

public enum SourceFontWeight
{
    Normal,
    SemiBold,
    Bold,
}

public enum SourceTextAlignment
{
    Left,
    Center,
    Right,
}

public enum SourceFontCategory
{
    SansSerif,
    Serif,
    Monospace,
}

public enum VisualStyleConfidence
{
    Low,
    Medium,
    High,
}

public sealed record SourceVisualStyle(
    Color ForegroundColor,
    double GlyphHeightPixels,
    double LineHeightPixels,
    SourceFontWeight FontWeight,
    SourceFontCategory FontCategory,
    bool IsItalic,
    SourceTextAlignment Alignment,
    double RotationDegrees,
    VisualStyleConfidence Confidence,
    double? EstimatedFontSizePixels = null);

public static class SourceVisualStyleEstimator
{
    private const double MinimumInkDistance = 24;

    public static SourceVisualStyle EstimateRegion(
        Bitmap source,
        VisualTextRegion region,
        IReadOnlyList<OcrTextBlock> siblingBlocks,
        Color backing)
    {
        ArgumentNullException.ThrowIfNull(region);
        // A multiline region is already the semantic paragraph boundary. Using
        // unrelated buttons and metadata from the surrounding card here can turn
        // a left-aligned paragraph into a centered one.
        var alignmentPeers = region.Lines.Count > 1 ? region.Lines : siblingBlocks;
        var styles = region.Lines
            .Select(line => Estimate(source, line, alignmentPeers, backing))
            .ToArray();
        if (styles.Length == 0)
        {
            throw new ArgumentException("A visual text region must contain at least one OCR line.", nameof(region));
        }

        var foreground = Average(styles.Select(static style => style.ForegroundColor));
        var glyphHeight = Median(styles.Select(static style => style.GlyphHeightPixels));
        var lineHeight = region.Lines.Count > 1
            ? Median(region.Lines.Zip(region.Lines.Skip(1), static (first, second) =>
                second.Bounds.Top - first.Bounds.Top))
            : Median(styles.Select(static style => style.LineHeightPixels));
        var weight = styles.GroupBy(static style => style.FontWeight).OrderByDescending(static group => group.Count()).First().Key;
        var category = styles.GroupBy(static style => style.FontCategory).OrderByDescending(static group => group.Count()).First().Key;
        var alignment = styles.GroupBy(static style => style.Alignment).OrderByDescending(static group => group.Count()).First().Key;
        if(region.Role==VisualTextRole.Body) alignment=SourceTextAlignment.Left;
        var confidence = styles.All(static style => style.Confidence == VisualStyleConfidence.Low)
            ? VisualStyleConfidence.Low
            : styles.Any(static style => style.Confidence == VisualStyleConfidence.High)
                ? VisualStyleConfidence.High
                : VisualStyleConfidence.Medium;

        // OCR rectangles and anti-aliasing are not reliable evidence for UI italics or
        // rotation. Preserve these only when a future detector has explicit line-level
        // consensus; ordinary screenshot text remains horizontal and non-italic.
        return new SourceVisualStyle(
            foreground, glyphHeight, Math.Max(glyphHeight, lineHeight), weight, category,
            false, alignment, 0, confidence,
            Median(styles.Select((style,index) => style.GlyphHeightPixels
                / TranslationTextLayoutPolicy.MeasureSourceInkRatio(region.Lines[index].Text,style.FontCategory))));
    }

    public static SourceVisualStyle Estimate(
        Bitmap source,
        OcrTextBlock block,
        IReadOnlyList<OcrTextBlock> siblingBlocks,
        Color backing)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(siblingBlocks);

        var candidates = ReadCandidates(source, block.Bounds, backing);
        var alignment = EstimateAlignment(block, siblingBlocks);
        var fontCategory = EstimateFontCategory(block);
        if (candidates.Count == 0)
        {
            return new SourceVisualStyle(
                ChooseContrastForeground(backing),
                Math.Max(1, block.Bounds.Height * 0.72),
                Math.Max(1, block.Bounds.Height),
                SourceFontWeight.Normal,
                fontCategory,
                false,
                alignment,
                0,
                VisualStyleConfidence.Low);
        }

        var foregroundSeed = SelectDominantForeground(candidates, backing, out var ambiguousForeground);
        var foreground = Average(foregroundSeed.Select(static pixel => pixel.Color));
        var stable = candidates
            .Where(pixel => ColorDistance(pixel.Color, foreground) <= 56)
            .ToArray();
        if (stable.Length < 4) stable = foregroundSeed;

        var minX = stable.Min(static pixel => pixel.X);
        var maxX = stable.Max(static pixel => pixel.X);
        var minY = stable.Min(static pixel => pixel.Y);
        var maxY = stable.Max(static pixel => pixel.Y);
        var glyphWidth = Math.Max(1, maxX - minX + 1);
        var geometryHeight = Math.Max(1, maxY - minY + 1);
        var glyphHeight = EstimateTypicalGlyphHeight(
            stable, block.Bounds.Height, geometryHeight, block.Words.Count > 0);
        var blockArea = Math.Max(1, block.Bounds.Width * block.Bounds.Height);
        var inkCoverage = stable.Length / blockArea;
        var weight = inkCoverage >= 0.32
            ? SourceFontWeight.Bold
            : inkCoverage >= 0.18
                ? SourceFontWeight.SemiBold
                : SourceFontWeight.Normal;
        var spread = foregroundSeed.Average(pixel => ColorDistance(pixel.Color, foreground));
        var confidence = ambiguousForeground || spread > 48
            ? VisualStyleConfidence.Low
            : stable.Length >= 6 && geometryHeight >= 3
                ? VisualStyleConfidence.High
                : VisualStyleConfidence.Medium;
        var rotation = EstimateRotation(stable, glyphWidth, geometryHeight);
        var isItalic = Math.Abs(rotation) < 2 && EstimateItalicShear(stable, minY, maxY);

        return new SourceVisualStyle(
            foreground,
            glyphHeight,
            Math.Max(glyphHeight, block.Bounds.Height),
            weight,
            fontCategory,
            isItalic,
            alignment,
            rotation,
            confidence);
    }

    private static List<InkPixel> ReadCandidates(Bitmap source, ClipRay.Core.Geometry.PixelRect bounds, Color backing)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, source.Height);
        var pixels = new List<InkPixel>();
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var color = source.GetPixel(x, y);
            var distance = ColorDistance(color, backing);
            if (distance >= MinimumInkDistance) pixels.Add(new InkPixel(x, y, color, distance));
        }
        return pixels;
    }

    private static InkPixel[] SelectDominantForeground(
        IReadOnlyList<InkPixel> candidates,
        Color backing,
        out bool ambiguous)
    {
        var strongestDistance = candidates.Max(static pixel => pixel.Distance);
        var minimumStableDistance = Math.Max(40, strongestDistance * 0.35);
        var eligible = candidates
            .Where(pixel => pixel.Distance >= minimumStableDistance)
            .ToArray();
        if (eligible.Length < 4)
        {
            ambiguous = false;
            return candidates
                .OrderByDescending(static pixel => pixel.Distance)
                .Take(Math.Clamp(candidates.Count / 4, 4, 64))
                .ToArray();
        }

        var clusters = eligible
            .GroupBy(static pixel => (
                Red: pixel.Color.R / 24,
                Green: pixel.Color.G / 24,
                Blue: pixel.Color.B / 24))
            .Select(static group => new
            {
                Pixels = group.ToArray(),
                Score = group.Count() * Math.Sqrt(group.Average(static pixel => pixel.Distance)),
            })
            .OrderByDescending(static group => group.Score)
            .ThenByDescending(static group => group.Pixels.Average(static pixel => pixel.Distance))
            .ToArray();
        var strongest = clusters[0];
        var strongestColor = Average(strongest.Pixels.Select(static pixel => pixel.Color));
        ambiguous = clusters.Skip(1).Any(cluster =>
            cluster.Score >= strongest.Score * 0.55
            && IsMateriallyDifferentHue(
                strongestColor,
                Average(cluster.Pixels.Select(static pixel => pixel.Color)),
                backing));
        return strongest.Pixels;
    }

    private static bool IsMateriallyDifferentHue(Color first, Color second, Color backing)
    {
        if (ColorDistance(first, second) < 72) return false;
        var firstVector = new[]
        {
            (double)first.R - backing.R,
            first.G - backing.G,
            first.B - backing.B,
        };
        var secondVector = new[]
        {
            (double)second.R - backing.R,
            second.G - backing.G,
            second.B - backing.B,
        };
        var firstLength = Math.Sqrt(firstVector.Sum(static value => value * value));
        var secondLength = Math.Sqrt(secondVector.Sum(static value => value * value));
        if (firstLength < 1 || secondLength < 1) return false;
        var cosine = firstVector.Zip(secondVector, static (left, right) => left * right).Sum()
            / (firstLength * secondLength);
        return cosine < 0.96;
    }

    private static SourceTextAlignment EstimateAlignment(OcrTextBlock block, IReadOnlyList<OcrTextBlock> siblings)
    {
        var nearby = siblings.Where(candidate =>
        {
            var verticalDistance = Math.Abs(
                candidate.Bounds.Top + candidate.Bounds.Height / 2
                - (block.Bounds.Top + block.Bounds.Height / 2));
            return verticalDistance <= Math.Max(candidate.Bounds.Height, block.Bounds.Height) * 8;
        }).ToArray();
        // Partial horizontal overlap can mix a search field or the right half
        // of another line into this column. Compare local edge/center anchors
        // separately, then prefer the anchor supported by the most peers.
        var tolerance = Math.Max(8, block.Bounds.Height * 1.5);
        var leftPeers = nearby.Where(candidate =>
            Math.Abs(candidate.Bounds.Left - block.Bounds.Left) <= tolerance).ToArray();
        var rightPeers = nearby.Where(candidate =>
            Math.Abs(candidate.Bounds.Right - block.Bounds.Right) <= tolerance).ToArray();
        var center = block.Bounds.Left + block.Bounds.Width / 2;
        var centerTolerance = Math.Max(8, block.Bounds.Height * 0.75);
        var centerPeers = nearby.Where(candidate =>
            Math.Abs(candidate.Bounds.Left + candidate.Bounds.Width / 2 - center) <= centerTolerance).ToArray();
        var count = Math.Max(leftPeers.Length, Math.Max(rightPeers.Length, centerPeers.Length));
        if (count < 2) return SourceTextAlignment.Left;

        var leftSpread = leftPeers.Length >= 2
            ? Spread(leftPeers.Select(static item => item.Bounds.Left)) : double.PositiveInfinity;
        var rightSpread = rightPeers.Length >= 2
            ? Spread(rightPeers.Select(static item => item.Bounds.Right)) : double.PositiveInfinity;
        var centerSpread = centerPeers.Length >= 2
            ? Spread(centerPeers.Select(static item => item.Bounds.Left + item.Bounds.Width / 2))
            : double.PositiveInfinity;
        // One extra account row can accidentally match a heading's center while
        // several labels share its exact left edge. Prefer that tight local edge
        // over a larger but loosely scattered center/right peer set.
        if (leftPeers.Length >= 3 && leftPeers.Length >= count - 1
            && leftSpread + Math.Max(4, block.Bounds.Height * .2)
                < Math.Min(rightSpread, centerSpread))
            return SourceTextAlignment.Left;
        if (leftPeers.Length != count) leftSpread = double.PositiveInfinity;
        if (rightPeers.Length != count) rightSpread = double.PositiveInfinity;
        if (centerPeers.Length != count) centerSpread = double.PositiveInfinity;
        if (centerSpread + 1 < leftSpread && centerSpread + 1 < rightSpread)
            return SourceTextAlignment.Center;
        if (rightSpread + 1 < leftSpread && rightSpread + 1 <= centerSpread)
            return SourceTextAlignment.Right;
        return double.IsFinite(leftSpread) ? SourceTextAlignment.Left
            : double.IsFinite(centerSpread) ? SourceTextAlignment.Center
            : SourceTextAlignment.Right;
    }

    private static SourceFontCategory EstimateFontCategory(OcrTextBlock block)
    {
        var widths = block.Words
            .Where(static word => word.Text.Length > 0)
            .Select(static word => word.Bounds.Width / word.Text.Length)
            .Where(static width => width > 0)
            .ToArray();
        if (widths.Length < 3) return SourceFontCategory.SansSerif;
        var mean = widths.Average();
        var deviation = Math.Sqrt(widths.Average(width => Math.Pow(width - mean, 2)));
        return deviation / mean <= 0.12 ? SourceFontCategory.Monospace : SourceFontCategory.SansSerif;
    }

    private static double EstimateRotation(IReadOnlyList<InkPixel> pixels, int glyphWidth, int glyphHeight)
    {
        if (pixels.Count < 12 || glyphWidth < glyphHeight * 2) return 0;
        var meanX = pixels.Average(static pixel => (double)pixel.X);
        var meanY = pixels.Average(static pixel => (double)pixel.Y);
        var varianceX = pixels.Average(pixel => Math.Pow(pixel.X - meanX, 2));
        var varianceY = pixels.Average(pixel => Math.Pow(pixel.Y - meanY, 2));
        var covariance = pixels.Average(pixel => (pixel.X - meanX) * (pixel.Y - meanY));
        if (varianceX <= varianceY) return 0;
        var angle = 0.5 * Math.Atan2(2 * covariance, varianceX - varianceY) * 180 / Math.PI;
        return Math.Abs(angle) is >= 2 and <= 30 ? angle : 0;
    }

    private static int EstimateTypicalGlyphHeight(
        IReadOnlyList<InkPixel> pixels,
        double boundsHeight,
        int geometryHeight,
        bool hasOcrWords)
    {
        var columnSpans = pixels
            .GroupBy(static pixel => pixel.X)
            .Select(static column => column.Max(static pixel => pixel.Y) - column.Min(static pixel => pixel.Y) + 1)
            .Order()
            .ToArray();
        if (columnSpans.Length == 0) return 1;
        // OCR word geometry can be tight around ordinary ascenders/descenders.
        // A complete-height ink box alone is not evidence of an adjacent badge.
        if (hasOcrWords) return Math.Min(geometryHeight, Math.Max(1,(int)Math.Round(boundsHeight)));
        var middle = columnSpans.Length / 2;
        var columnMedian = columnSpans.Length % 2 == 0
            ? Math.Max(1, (int)Math.Round((columnSpans[middle - 1] + columnSpans[middle]) / 2d))
            : Math.Max(1, columnSpans[middle]);
        if (geometryHeight <= boundsHeight * 0.8)
        {
            var ocrFloor = hasOcrWords ? boundsHeight * 0.62 : 1;
            return Math.Max(geometryHeight, (int)Math.Round(ocrFloor));
        }

        // A glyph box that spans nearly the complete OCR height usually contains
        // an adjacent icon or badge. Recover the dominant text scale from the
        // column evidence, but keep it within conservative OCR-box proportions.
        var recovered = Math.Max(columnMedian * 2.2, boundsHeight * 0.4);
        return Math.Max(1, (int)Math.Round(Math.Min(boundsHeight * 0.64, recovered)));
    }

    private static bool EstimateItalicShear(IReadOnlyList<InkPixel> pixels, int minY, int maxY)
    {
        var height = maxY - minY + 1;
        if (height < 6) return false;
        var rows = pixels.GroupBy(static pixel => pixel.Y)
            .Select(static row => (Y: row.Key, Left: row.Min(static pixel => pixel.X)))
            .OrderBy(static row => row.Y)
            .ToArray();
        if (rows.Length < 6) return false;
        var third = Math.Max(2, rows.Length / 3);
        var topLeft = rows.Take(third).Average(static row => row.Left);
        var bottomLeft = rows.TakeLast(third).Average(static row => row.Left);
        var shear = (topLeft - bottomLeft) / height;
        return Math.Abs(shear) is >= 0.12 and <= 0.6;
    }

    private static double Spread(IEnumerable<double> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? double.PositiveInfinity : array.Max() - array.Min();
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

    private static Color Average(IEnumerable<Color> colors)
    {
        var array = colors.ToArray();
        return Color.FromArgb(
            255,
            (int)Math.Round(array.Average(static color => color.R)),
            (int)Math.Round(array.Average(static color => color.G)),
            (int)Math.Round(array.Average(static color => color.B)));
    }

    private static Color ChooseContrastForeground(Color backing) =>
        0.2126 * backing.R + 0.7152 * backing.G + 0.0722 * backing.B < 140 ? Color.White : Color.Black;

    private static double ColorDistance(Color first, Color second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private sealed record InkPixel(int X, int Y, Color Color, double Distance);
}

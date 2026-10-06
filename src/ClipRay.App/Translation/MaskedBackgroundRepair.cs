using System.Drawing;
using System.IO;

namespace ClipRay.App.Translation;

public sealed record BackgroundRepairResult(
    byte[]? PatchPng,
    InPlaceBackgroundMode Mode,
    VisualSafetyConfidence Confidence);

public static class MaskedBackgroundRepair
{
    public static BackgroundRepairResult Repair(Bitmap source, TextMaskResult mask)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Confidence == VisualSafetyConfidence.Low || mask.Bounds.IsEmpty)
        {
            return new BackgroundRepairResult(null, InPlaceBackgroundMode.ComplexBacking, VisualSafetyConfidence.Low);
        }

        var samples = ReadBackingSamples(source, mask).ToArray();
        if (samples.Length < 8)
        {
            return new BackgroundRepairResult(null, InPlaceBackgroundMode.ComplexBacking, VisualSafetyConfidence.Low);
        }

        var dominant = Dominant(samples);
        var dominantRatio = samples.Count(color => Distance(color, dominant) <= 30) / (double)samples.Length;
        var smoothness = NeighborDifference(source, mask);
        var flat = dominantRatio >= 0.65;
        if (!flat && smoothness > 36)
        {
            return new BackgroundRepairResult(null, InPlaceBackgroundMode.ComplexBacking, VisualSafetyConfidence.Low);
        }

        var bounds = mask.Bounds;
        var width = (int)bounds.Width;
        var height = (int)bounds.Height;
        using var patch = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var localY = 0; localY < height; localY++)
        for (var localX = 0; localX < width; localX++)
        {
            var x = (int)bounds.Left + localX;
            var y = (int)bounds.Top + localY;
            if (mask.Mask.GetPixel(x, y).A == 0)
            {
                patch.SetPixel(localX, localY, Color.Transparent);
                continue;
            }

            var repaired = flat ? dominant : InterpolateBacking(source, mask.Mask, x, y, dominant);
            patch.SetPixel(localX, localY, Color.FromArgb(255, repaired.R, repaired.G, repaired.B));
        }

        using var stream = new MemoryStream();
        patch.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return new BackgroundRepairResult(
            stream.ToArray(),
            flat ? InPlaceBackgroundMode.FlatSampled : InPlaceBackgroundMode.ComplexBacking,
            VisualSafetyConfidence.High);
    }

    private static IEnumerable<Color> ReadBackingSamples(Bitmap source, TextMaskResult mask)
    {
        var left = Math.Max(0, (int)mask.Bounds.Left - 3);
        var top = Math.Max(0, (int)mask.Bounds.Top - 3);
        var right = Math.Min(source.Width, (int)mask.Bounds.Right + 3);
        var bottom = Math.Min(source.Height, (int)mask.Bounds.Bottom + 3);
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            if (mask.Mask.GetPixel(x, y).A == 0) yield return source.GetPixel(x, y);
        }
    }

    private static double NeighborDifference(Bitmap source, TextMaskResult mask)
    {
        var differences = new List<double>();
        var left = Math.Max(0, (int)mask.Bounds.Left - 3);
        var top = Math.Max(0, (int)mask.Bounds.Top - 3);
        var right = Math.Min(source.Width - 1, (int)mask.Bounds.Right + 2);
        var bottom = Math.Min(source.Height - 1, (int)mask.Bounds.Bottom + 2);
        for (var y = top; y < bottom; y += 2)
        for (var x = left; x < right; x += 2)
        {
            if (mask.Mask.GetPixel(x, y).A != 0) continue;
            if (mask.Mask.GetPixel(x + 1, y).A == 0)
            {
                differences.Add(Distance(source.GetPixel(x, y), source.GetPixel(x + 1, y)));
            }
            if (mask.Mask.GetPixel(x, y + 1).A == 0)
            {
                differences.Add(Distance(source.GetPixel(x, y), source.GetPixel(x, y + 1)));
            }
        }
        return differences.Count == 0 ? double.PositiveInfinity : differences.Average();
    }

    private static Color InterpolateBacking(Bitmap source, Bitmap mask, int x, int y, Color fallback)
    {
        var left = Find(source, mask, x, y, -1, 0);
        var right = Find(source, mask, x, y, 1, 0);
        if (left.Color is { } leftColor && right.Color is { } rightColor)
        {
            var amount = left.Distance / (double)(left.Distance + right.Distance);
            return Interpolate(leftColor, rightColor, amount);
        }

        var top = Find(source, mask, x, y, 0, -1);
        var bottom = Find(source, mask, x, y, 0, 1);
        if (top.Color is { } topColor && bottom.Color is { } bottomColor)
        {
            var amount = top.Distance / (double)(top.Distance + bottom.Distance);
            return Interpolate(topColor, bottomColor, amount);
        }
        return left.Color ?? right.Color ?? top.Color ?? bottom.Color ?? fallback;
    }

    private static (Color? Color, int Distance) Find(
        Bitmap source,
        Bitmap mask,
        int x,
        int y,
        int dx,
        int dy)
    {
        for (var distance = 1; distance <= 12; distance++)
        {
            var sampleX = x + dx * distance;
            var sampleY = y + dy * distance;
            if (sampleX < 0 || sampleX >= source.Width || sampleY < 0 || sampleY >= source.Height) break;
            if (mask.GetPixel(sampleX, sampleY).A == 0)
            {
                return (source.GetPixel(sampleX, sampleY), distance);
            }
        }
        return (null, 0);
    }

    private static Color Interpolate(Color first, Color second, double amount) => Color.FromArgb(
        255,
        (int)Math.Round(first.R + (second.R - first.R) * amount),
        (int)Math.Round(first.G + (second.G - first.G) * amount),
        (int)Math.Round(first.B + (second.B - first.B) * amount));

    private static Color Dominant(IReadOnlyList<Color> colors)
    {
        var cluster = colors
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

    private static double Distance(Color first, Color second)
    {
        var red = first.R - second.R;
        var green = first.G - second.G;
        var blue = first.B - second.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }
}

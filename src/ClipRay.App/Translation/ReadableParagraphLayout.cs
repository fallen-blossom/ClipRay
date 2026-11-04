using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

/// <summary>Uses verified blank rows for normal paragraphs without enlarging source erasure.</summary>
internal static class ReadableParagraphLayout
{
    public static (PixelRect Bounds, byte[] Patch)? Expand(Bitmap source, VisualTextRegion region,
        IReadOnlyList<VisualTextRegion> regions, string text, SourceVisualStyle style,
        BackgroundRepairResult repair, IReadOnlyList<TranslatedVisualStyleRun> styleRuns)
    {
        var original = region.Bounds;
        // Other roles may be inside buttons or badges whose container is unknown.
        // Fractional source patches retain their current exact transform until
        // their erasure bounds can be represented independently of the text box.
        if (region.Role is not (VisualTextRole.Body or VisualTextRole.Heading)
            || Math.Abs(style.RotationDegrees) > .001
            || repair.Confidence != VisualSafetyConfidence.High || repair.PatchPng is null
            || original.IsEmpty || !Integral(original.Left) || !Integral(original.Top)
            || !Integral(original.Width) || !Integral(original.Height)) return null;

        var size = style.EstimatedFontSizePixels ?? style.GlyphHeightPixels / TranslationTextLayoutPolicy.VisibleGlyphRatio;
        var lineHeight = size * Math.Clamp(style.LineHeightPixels / size, .95, 1.5);
        if (!double.IsFinite(size + lineHeight) || size <= 0) return null;
        var trial = TranslationTextLayoutPolicy.FitAtSize(text,
            new(original.X, original.Y, original.Width, 100_000), style, size, lineHeight);
        trial = TranslationTextLayoutPolicy.WithStyleRuns(text,
            new(original.X, original.Y, original.Width, 100_000), style, trial, styleRuns);
        var formatted = TranslationTextLayoutPolicy.Format(text, original.Width, trial, System.Windows.Media.Brushes.Black);
        var required = Math.Ceiling(Math.Max(formatted.Height,
            formatted.BuildGeometry(new System.Windows.Point()).Bounds.Bottom));
        if (required <= original.Height + .5) return null;

        var bottom = Math.Min(source.Height, original.Bottom + Math.Min(256, Math.Max(original.Height * 2, lineHeight * 4)));
        foreach (var line in region.Lines)
        {
            if (line.SelectionBounds is not { } selected) continue;
            if (original.Left < selected.Left || original.Right > selected.Right || original.Top < selected.Top)
                return null;
            bottom = Math.Min(bottom, selected.Bottom);
        }
        foreach (var other in regions)
        {
            if (ReferenceEquals(other, region) || other.Bounds.Left >= original.Right
                || other.Bounds.Right <= original.Left || other.Bounds.Bottom <= original.Top) continue;
            bottom = Math.Min(bottom, other.Bounds.Top - 2);
        }
        var height = (int)required;
        if (original.Top + height > Math.Floor(bottom) || original.Left < 0 || original.Right > source.Width)
            return null;

        using var input = new MemoryStream(repair.PatchPng, writable: false);
        using var patch = new Bitmap(input);
        if (patch.Width != (int)original.Width || patch.Height != (int)original.Height) return null;
        Color? backing = null;
        for (var y = 0; y < patch.Height && backing is null; y++)
        for (var x = 0; x < patch.Width; x++)
            if (patch.GetPixel(x, y).A == 255) { backing = patch.GetPixel(x, y); break; }
        if (backing is null) return null;

        var strip = new Rectangle((int)original.Left, (int)original.Bottom,
            (int)original.Width, height - (int)original.Height);
        if (!IsSmoothBlankArea(source, strip)) return null;

        using var padded = new Bitmap(patch.Width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(padded))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(patch, 0, 0);
        }
        using var output = new MemoryStream();
        padded.Save(output, ImageFormat.Png);
        return (new(original.X, original.Y, original.Width, height), output.ToArray());
    }

    private static bool IsSmoothBlankArea(Bitmap source, Rectangle strip)
    {
        // Include rows below the strip so a faint divider at its bottom edge
        // cannot be mistaken for a smooth gradient. Rows inside the OCR box
        // may contain descenders and are intentionally not sampled here.
        var top = strip.Top;
        var bottom = Math.Min(source.Height, strip.Bottom + 2);
        var area = Rectangle.FromLTRB(strip.Left, top, strip.Right, bottom);
        using var pixels = source.Clone(area, PixelFormat.Format32bppArgb);
        var data = pixels.LockBits(new(0, 0, pixels.Width, pixels.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rows = new byte[pixels.Height][];
            for (var y = 0; y < pixels.Height; y++)
            {
                rows[y] = new byte[checked(pixels.Width * 4)];
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), rows[y], 0, rows[y].Length);
            }
            var lastX = (pixels.Width - 1) * 4;
            var lastY = pixels.Height - 1;
            for (var y = 0; y < pixels.Height; y++)
            for (var x = 0; x < pixels.Width; x++)
            {
                var index = x * 4;
                if (rows[y][index + 3] != 255) return false;
                var fx = pixels.Width == 1 ? 0 : x / (double)(pixels.Width - 1);
                var fy = lastY == 0 ? 0 : y / (double)lastY;
                for (var channel = 0; channel < 3; channel++)
                {
                    var upper = rows[0][channel] * (1 - fx) + rows[0][lastX + channel] * fx;
                    var lower = rows[lastY][channel] * (1 - fx) + rows[lastY][lastX + channel] * fx;
                    if (Math.Abs(rows[y][index + channel] - (upper * (1 - fy) + lower * fy)) > 1.25)
                        return false;
                }
            }
            return true;
        }
        finally { pixels.UnlockBits(data); }
    }

    private static bool Integral(double value) => double.IsFinite(value) && Math.Abs(value - Math.Round(value)) < .001;
}

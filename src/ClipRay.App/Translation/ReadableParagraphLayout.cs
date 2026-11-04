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
            || repair.Mode != InPlaceBackgroundMode.FlatSampled || repair.PatchPng is null
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
        if (!Uniform(source, strip, backing.Value)) return null;

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

    private static bool Uniform(Bitmap source, Rectangle strip, Color backing)
    {
        // Scan every pixel, including unknown icons/lines; subsampling could
        // miss exactly the neighbor that makes expansion unsafe.
        using var pixels = source.Clone(strip, PixelFormat.Format32bppArgb);
        var data = pixels.LockBits(new(0, 0, pixels.Width, pixels.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[checked(pixels.Width * 4)];
            for (var y = 0; y < pixels.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (var x = 0; x < row.Length; x += 4)
                    if (row[x + 3] != 255 || row[x] != backing.B
                        || row[x + 1] != backing.G || row[x + 2] != backing.R)
                        return false;
            }
            return true;
        }
        finally { pixels.UnlockBits(data); }
    }

    private static bool Integral(double value) => double.IsFinite(value) && Math.Abs(value - Math.Round(value)) < .001;
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClipRay.App.Ocr;
using ClipRay.App.Translation.Upstream.Mort;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

internal static class MortSourceColorAdapter
{
    internal static OverlayColorAnalysis Analyze(Bitmap source, OcrTextBlock block)
    {
        var image = new Rectangle(0, 0, source.Width, source.Height);
        var region = Rectangle.Intersect(ToRectangle(block.Bounds), image);
        if (region.IsEmpty) return default;
        var cropBounds = Rectangle.Intersect(Rectangle.Inflate(region, 4, 4), image);
        using var crop = new Bitmap(cropBounds.Width, cropBounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(crop))
            graphics.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), cropBounds, GraphicsUnit.Pixel);
        var bytes = new byte[checked(crop.Width * crop.Height * 4)];
        var data = crop.LockBits(new(0, 0, crop.Width, crop.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < crop.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * crop.Width * 4, crop.Width * 4);
        }
        finally { crop.UnlockBits(data); }
        Rectangle Relative(PixelRect bounds)
        {
            var rect = Rectangle.Intersect(ToRectangle(bounds), image);
            rect.Offset(-cropBounds.Left, -cropBounds.Top);
            return rect;
        }
        return OverlayColorAnalyzer.Analyze(bytes, 4, crop.Width, crop.Height,
            Relative(block.Bounds), block.Words.Select(word => Relative(word.Bounds)).ToArray());
    }

    private static Rectangle ToRectangle(PixelRect bounds) => Rectangle.FromLTRB(
        (int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
        (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom));
}

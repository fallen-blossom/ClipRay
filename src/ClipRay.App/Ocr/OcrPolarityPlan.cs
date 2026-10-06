using System.Drawing;
using System.Drawing.Imaging;

namespace ClipRay.App.Ocr;

public static class OcrPolarityPlan
{
    private const double DarkLuminanceThreshold = 110;
    private const int SampleGrid = 48;

    public static bool ShouldInvert(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var stepX = Math.Max(1, bitmap.Width / SampleGrid);
        var stepY = Math.Max(1, bitmap.Height / SampleGrid);
        double luminance = 0;
        var samples = 0;
        for (var y = stepY / 2; y < bitmap.Height; y += stepY)
        for (var x = stepX / 2; x < bitmap.Width; x += stepX)
        {
            var pixel = bitmap.GetPixel(x, y);
            luminance += 0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B;
            samples++;
        }
        return samples > 0 && luminance / samples < DarkLuminanceThreshold;
    }

    public static Bitmap CreatePreparedBitmap(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!ShouldInvert(source)) return new Bitmap(source);

        var prepared = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(prepared);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(
        [
            [-1, 0, 0, 0, 0],
            [0, -1, 0, 0, 0],
            [0, 0, -1, 0, 0],
            [0, 0, 0, 1, 0],
            [1, 1, 1, 0, 1],
        ]));
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, source.Width, source.Height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return prepared;
    }
}

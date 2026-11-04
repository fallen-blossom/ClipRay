using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ClipRay.App.Ocr;

internal static class OcrBitmapPixels
{
    // Opaque screen captures need no PNG compression/decompression between APIs.
    // Both sides own their storage; neither native inference nor disposal can
    // outlive a borrowed GDI lock. Use premultiplied BGRA on both sides.
    internal static SKBitmap Copy(Bitmap source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            var hasTransparency = false;
            var data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[checked(source.Width * 4)];
                for (var y = 0; y < source.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), row, 0, row.Length);
                    for (var alpha = 3; alpha < row.Length; alpha += 4)
                        if (row[alpha] != 255) { hasTransparency = true; break; }
                    if (hasTransparency) break;
                    Marshal.Copy(row, 0, IntPtr.Add(result.GetPixels(), checked(y * result.RowBytes)), row.Length);
                }
            }
            finally { source.UnlockBits(data); }
            if (hasTransparency)
            {
                // GDI's PArgb storage can differ from PNG/Skia's unpremultiply /
                // premultiply rounding. Preserve the original conversion exactly
                // for transparent inputs instead of changing recognizer pixels.
                result.Dispose();
                token.ThrowIfCancellationRequested();
                using var png = new MemoryStream();
                source.Save(png, ImageFormat.Png);
                token.ThrowIfCancellationRequested();
                return SKBitmap.Decode(png.ToArray())
                    ?? throw new OcrUnavailableException("The local OCR image could not be decoded.");
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}

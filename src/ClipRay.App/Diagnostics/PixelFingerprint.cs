using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipRay.App.Diagnostics;

internal sealed record PixelFingerprint(string sha256, int width, int height,
    string pixel_format, long byte_count)
{
    public static PixelFingerprint Create(Bitmap source)
    {
        var pixels = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var row = new byte[checked(source.Width * 4)];
            for (var y = 0; y < source.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(pixels.Scan0, y * pixels.Stride), row, 0, row.Length);
                hash.AppendData(row);
            }
            return new(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                source.Width, source.Height, "BGRA32", checked((long)source.Width * source.Height * 4));
        }
        finally { source.UnlockBits(pixels); }
    }

    public static PixelFingerprint Create(BitmapSource image)
    {
        BitmapSource normalized = image.Format == PixelFormats.Bgra32
            ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = checked(normalized.PixelWidth * 4);
        var pixels = new byte[checked(stride * normalized.PixelHeight)];
        normalized.CopyPixels(pixels, stride, 0);
        return new(Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant(),
            normalized.PixelWidth, normalized.PixelHeight, "BGRA32", pixels.LongLength);
    }
}

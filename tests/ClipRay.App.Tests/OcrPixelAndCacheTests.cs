using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ClipRay.App.Ocr;
using RapidOcrNet;
using SkiaSharp;

internal static class OcrPixelAndCacheTests
{
    internal static void Run()
    {
        foreach (var format in new[] { PixelFormat.Format24bppRgb, PixelFormat.Format32bppRgb,
                     PixelFormat.Format32bppArgb, PixelFormat.Format32bppPArgb })
        {
            using var bitmap = new Bitmap(19, 13, format);
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(format == PixelFormat.Format24bppRgb
                    || format == PixelFormat.Format32bppRgb ? 255 : (x * 17 + y * 13) % 256,
                    x * 11 % 256, y * 19 % 256, (x + y) * 7 % 256));
            ComparePng(bitmap);
            // The same alpha-capable format must still use equivalent pixels
            // when it holds an ordinary opaque screen capture.
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
            bitmap.SetPixel(18, 12, Color.OrangeRed);
            ComparePng(bitmap);
        }
        var buffer = Marshal.AllocHGlobal(16 * 7);
        try
        {
            using var reversed = new Bitmap(3, 7, -16, PixelFormat.Format32bppArgb, IntPtr.Add(buffer, 16 * 6));
            using (var graphics = Graphics.FromImage(reversed)) graphics.Clear(Color.Coral);
            reversed.SetPixel(0, 0, Color.Blue);
            reversed.SetPixel(2, 6, Color.Green);
            ComparePng(reversed);
        }
        finally { Marshal.FreeHGlobal(buffer); }

        using var crop = new SKBitmap(10, 5, SKColorType.Bgra8888, SKAlphaType.Premul);
        crop.Erase(SKColors.White);
        var first = OcrLineRecognitionCache.Snapshot(crop, default);
        var cache = new OcrLineRecognitionCache(maximumEntries: 1, maximumPixelBytes: 300);
        var recognized = new OcrLineRecognitionCache.Recognition(
            [new("English中文", .96, "cjk")], new Dictionary<string, TextLine>());
        cache.Store(first, recognized);
        Check(ReferenceEquals(cache.Find(OcrLineRecognitionCache.Snapshot(crop, default)), recognized), "Exact crop missed cache.");
        crop.SetPixel(9, 4, SKColors.Black);
        var changed = OcrLineRecognitionCache.Snapshot(crop, default);
        Check(cache.Find(changed) is null, "Changed last pixel reused stale text.");
        Check(cache.Find(first with { Width = 5, Height = 10 }) is null, "Different crop shape reused stale geometry.");
        Check(cache.Find(changed with { Digest = first.Digest }) is null, "Hash collision bypassed exact pixel comparison.");
        cache.Store(changed, recognized);
        Check(cache.Find(first) is null && cache.Find(changed) is not null, "Entry capacity failed to evict.");
        cache.Clear();
        Check(cache.Find(changed) is null, "Retirement retained OCR content.");
        var tooSmall = new OcrLineRecognitionCache(maximumPixelBytes: 100);
        tooSmall.Store(first, recognized);
        Check(tooSmall.Find(first) is null, "Pixel memory limit was exceeded.");
        using var cancelled = new CancellationTokenSource();
        using var cancelledBitmap = new Bitmap(1, 1);
        cancelled.Cancel();
        try { using var unexpected = OcrBitmapPixels.Copy(cancelledBitmap, cancelled.Token); throw new Exception("Cancellation was ignored."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("OCR pixel copy and exact line cache checks passed.");
    }

    private static void ComparePng(Bitmap bitmap)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        using var expected = SKBitmap.Decode(png.ToArray());
        using var actual = OcrBitmapPixels.Copy(bitmap, default);
        Check(actual.Width == expected.Width && actual.Height == expected.Height, "Pixel copy resized input.");
        // Compare canonical premultiplied bytes: these are what native OCR reads.
        for (var y = 0; y < actual.Height; y++)
        for (var x = 0; x < actual.Width * 4; x++)
            Check(Marshal.ReadByte(actual.GetPixels(), y * actual.RowBytes + x)
                == Marshal.ReadByte(expected.GetPixels(), y * expected.RowBytes + x),
                $"Pixel copy differs from PNG for {bitmap.PixelFormat} at row {y}, byte {x}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

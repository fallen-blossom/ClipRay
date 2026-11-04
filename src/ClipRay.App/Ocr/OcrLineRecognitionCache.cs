using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RapidOcrNet;
using SkiaSharp;

namespace ClipRay.App.Ocr;

// Owned by one gated runtime/model set. Only exact, complete detected crops
// reuse recognition; detection and output geometry are always recomputed.
internal sealed class OcrLineRecognitionCache(int maximumEntries = 64, int maximumPixelBytes = 8 * 1024 * 1024)
{
    internal sealed record Pixels(int Width, int Height, byte[] Bytes, string Digest);
    internal sealed record Recognition(IReadOnlyList<ScriptOcrCandidate> Candidates, IReadOnlyDictionary<string, TextLine> Lines);
    private sealed record Entry(Pixels Pixels, Recognition Recognition);
    private readonly LinkedList<Entry> _recent = new();
    private int _pixelBytes;

    internal static Pixels Snapshot(SKBitmap crop, CancellationToken token)
    {
        // Runtime crops inherit the canonical BGRA format. Exclude stride padding
        // while preserving every pixel, including alpha, and both dimensions.
        if (crop.ColorType != SKColorType.Bgra8888 || crop.AlphaType != SKAlphaType.Premul)
            throw new InvalidOperationException("OCR crop must use canonical premultiplied BGRA pixels.");
        var rowLength = checked(crop.Width * 4);
        var bytes = new byte[checked(rowLength * crop.Height)];
        for (var y = 0; y < crop.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            Marshal.Copy(IntPtr.Add(crop.GetPixels(), checked(y * crop.RowBytes)), bytes, y * rowLength, rowLength);
        }
        return new(crop.Width, crop.Height, bytes, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    internal Recognition? Find(Pixels pixels)
    {
        for (var node = _recent.First; node is not null; node = node.Next)
        {
            var stored = node.Value.Pixels;
            if (stored.Width != pixels.Width || stored.Height != pixels.Height || stored.Digest != pixels.Digest
                || !stored.Bytes.AsSpan().SequenceEqual(pixels.Bytes)) continue;
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Recognition;
        }
        return null;
    }

    internal void Store(Pixels pixels, Recognition recognition)
    {
        if (maximumEntries <= 0 || pixels.Bytes.Length > maximumPixelBytes || Find(pixels) is not null) return;
        while (_recent.Count >= maximumEntries || _pixelBytes + pixels.Bytes.Length > maximumPixelBytes)
        {
            _pixelBytes -= _recent.Last!.Value.Pixels.Bytes.Length;
            _recent.RemoveLast();
        }
        _recent.AddFirst(new Entry(pixels, recognition));
        _pixelBytes += pixels.Bytes.Length;
    }

    internal void Clear() { _recent.Clear(); _pixelBytes = 0; }
}

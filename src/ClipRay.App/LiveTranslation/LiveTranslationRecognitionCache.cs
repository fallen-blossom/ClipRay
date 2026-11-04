using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ClipRay.App.Ocr;

namespace ClipRay.App.LiveTranslation;

// One instance belongs to one live session and one immutable recognition profile.
// This owns only completed OCR data. Bitmap/native ownership stays with the caller
// and the recognizer; its original native gate is still entered on every miss.
internal sealed class LiveTranslationRecognitionCache
{
    internal const string CommonMachineProfile = "common-machine-v1";
    internal const string VisualProfile = "visual-multilingual-v1";
    internal const int Capacity = 2;
    private readonly string _profile;
    private readonly Size _viewportSize;
    private readonly Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>> _recognize;
    private readonly SemaphoreSlim _use = new(1, 1);
    private readonly LinkedList<Entry> _completed = new();
    private long _completedReuseCount;
    internal long CompletedReuseCount => Interlocked.Read(ref _completedReuseCount);

    public LiveTranslationRecognitionCache(string profile, Size viewportSize,
        Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>> recognize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportSize));
        _profile = profile;
        _viewportSize = viewportSize;
        _recognize = recognize ?? throw new ArgumentNullException(nameof(recognize));
    }

    public async Task<OcrRecognitionResult> RecognizeAsync(Bitmap bitmap, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        token.ThrowIfCancellationRequested();
        // The existing recognizer delegate does not carry crop origin. Only a
        // complete fixed viewport has a proven scope; never infer crop equality
        // from identical dimensions/pixels at two different screen positions.
        if (bitmap.Size != _viewportSize)
        {
            var cropped = await _recognize(bitmap, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return cropped;
        }
        // Serialize admission, not task subscriptions. A canceled waiter cannot
        // cancel the real OCR owner, and it never receives an older cached result.
        await _use.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var key = CreateKey(bitmap);
            token.ThrowIfCancellationRequested();
            for (var node = _completed.First; node is not null; node = node.Next)
                if (node.Value.Key == key)
                {
                    var result = node.Value.Result;
                    _completed.Remove(node);
                    _completed.AddFirst(node);
                    token.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref _completedReuseCount);
                    return result;
                }

            var recognized = await _recognize(bitmap, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!recognized.IsSuccess) return recognized;
            var frozen = Freeze(recognized);
            token.ThrowIfCancellationRequested();
            _completed.AddFirst(new Entry(key, frozen));
            if (_completed.Count > Capacity) _completed.RemoveLast();
            return frozen;
        }
        finally { _use.Release(); }
    }

    private Key CreateKey(Bitmap bitmap)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        // Read exact converted ARGB channels without drawing/compositing onto a
        // background. No similarity threshold, text comparison or crop inference.
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[checked(bitmap.Width * 4)];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                hash.AppendData(row);
            }
        }
        finally { bitmap.UnlockBits(data); }
        return new(bitmap.Width, bitmap.Height, _profile, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static OcrRecognitionResult Freeze(OcrRecognitionResult result)
    {
        static OcrWord FreezeWord(OcrWord word) => word with
        { RecognitionWords = Array.AsReadOnly(word.RecognitionWords.Select(FreezeWord).ToArray()) };
        return result with { Blocks = Array.AsReadOnly(result.Blocks.Select(block => block with {
            Words = Array.AsReadOnly(block.Words.Select(FreezeWord).ToArray()),
            PrimaryRecognitionWords = Array.AsReadOnly(block.PrimaryRecognitionWords.Select(FreezeWord).ToArray())
        }).ToArray()) };
    }

    private sealed record Key(int Width, int Height, string Profile, string PixelHash);
    private sealed record Entry(Key Key, OcrRecognitionResult Result);
}

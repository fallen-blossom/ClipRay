using System.Drawing;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

internal static class FastEnglishOcrTests
{
    public static void RealFallbackIsFastAndReadsEnglish()
    {
        using var bitmap = new Bitmap(1100, 230);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Arial", 24))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("chatgpt pro accounts for sale", font, Brushes.Black, 20, 20);
            graphics.DrawString("screenshots as proof and asking higher prices", font, Brushes.Black, 20, 85);
            graphics.DrawString("please be careful about fake accounts", font, Brushes.Black, 20, 150);
        }
        using var service = OcrService.CreateForEnglishMachine(new Runtime(2));
        var warmed = new List<long>();
        for (var pass = 0; pass < 4; pass++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = service.RecognizeAsync(bitmap, CancellationToken.None).GetAwaiter().GetResult();
            var elapsed = clock.ElapsedMilliseconds;
            var text = string.Join(" ", result.Blocks.Select(block => block.Text)).ToLowerInvariant();
            if (!result.IsSuccess || !text.Contains("chatgpt pro accounts") || !text.Contains("screenshots as proof") || !text.Contains("fake accounts"))
                throw new InvalidOperationException("Real machine fallback lost the English screen text");
            Console.WriteLine($"English fallback pass={pass + 1} elapsed_ms={elapsed} blocks={result.Blocks.Count}");
            if (pass > 0) warmed.Add(elapsed);
        }
        if (warmed.Order().ElementAt(1) >= 1500)
            throw new InvalidOperationException("Real English fallback warm median exceeds 1500 ms");
    }

    public static void UsesEnglishWithoutSupplement() => Check(0);
    public static void EmptyEnglishUsesFallback() => Check(1);
    public static void FailedEnglishUsesFallback() => Check(2);
    public static void CancellationDoesNotStartFallback() => Check(3);
    public static void MissingEnglishDoesNotUseChinese()
    {
        var method = typeof(OcrLanguageSelector).GetMethod("SelectEnglish") ?? throw new InvalidOperationException("Strict English selector missing");
        try { method.Invoke(null, [new string[] { "zh-Hans-CN" }]); }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException?.GetType().Name == "OcrUnavailableException") { return; }
        throw new InvalidOperationException("Machine OCR silently selected a non-English language");
    }
    public static void RegionalEnglishIsAccepted()
    {
        var method = typeof(OcrLanguageSelector).GetMethod("SelectEnglish") ?? throw new InvalidOperationException("Strict English selector missing");
        if (!Equals("en-GB", method.Invoke(null, [new string[] { "zh-Hans-CN", "en-GB" }]))) throw new InvalidOperationException("Regional English was lost");
    }

    private static void Check(int mode)
    {
        using var cancellation = new CancellationTokenSource();
        var english = new Runtime(mode, cancellation);
        var fallback = new Runtime(0);
        using var service = OcrService.CreateForEnglishMachine(english, fallback);
        using var bitmap = new Bitmap(100, 50);
        try
        {
            var result = service.RecognizeAsync(bitmap, cancellation.Token).GetAwaiter().GetResult();
            if (mode == 3 || !result.IsSuccess || result.Blocks.Count == 0)
                throw new InvalidOperationException("OCR result or cancellation was lost");
        }
        catch (OperationCanceledException) when (mode == 3) { }
        if (english.Calls != 1 || fallback.Calls != (mode is 1 or 2 ? 1 : 0))
            throw new InvalidOperationException("Machine OCR ran an unnecessary fallback or lost recovery");
    }

    private sealed class Runtime(int mode, CancellationTokenSource? cancellation = null) : IOcrRuntime
    {
        public int Calls;
        public Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken token)
        {
            Calls++;
            if (mode == 2) throw new InvalidOperationException("unavailable");
            if (mode == 3) { cancellation!.Cancel(); throw new OperationCanceledException(token); }
            return Task.FromResult<IReadOnlyList<OcrWord>>(mode == 1 ? [] : [new("Hello world", new PixelRect(2, 2, 80, 20))]);
        }
    }
}

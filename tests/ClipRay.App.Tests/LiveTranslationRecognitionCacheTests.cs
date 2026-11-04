using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveTranslationRecognitionCacheTests
{
    public static void SemanticAndPipelineShareCompletedRecognition()
    {
        using var source = Image(8, 8, Color.White);
        var calls = 0;
        var cache = Cache((_, _) => {
            calls++;
            return Task.FromResult(new OcrRecognitionResult(true, [], "completed"));
        });
        var semantic = cache.RecognizeAsync(source, default).GetAwaiter().GetResult();
        var evidence = LiveTranslationSemanticEvidence.FromRecognition(semantic, source);
        var orchestrator = new TranslationApplicationOrchestrator(cache.RecognizeAsync,
            (_, _, _) => Task.FromException<TranslationBatchOutcome>(
                new Exception("Empty OCR must not reach translation.")));
        var pipeline = orchestrator.ExecuteAsync(source, TranslationRouteKind.Machine, "zh",
            static blocks => blocks, null, null, default).GetAwaiter().GetResult();
        if (calls != 1 || cache.CompletedReuseCount != 1 || evidence is null
            || pipeline.FailureStage != TranslationPipelineStage.Scope
            || !ReferenceEquals(pipeline.OcrBlocks, semantic.Blocks))
            throw new Exception("Semantic verification and the real pipeline did not share completed successful OCR.");
    }

    public static void ExactPixelsDimensionsAndCropsStaySeparate()
    {
        var calls = 0;
        var cache = Cache((_, _) => { calls++; return Task.FromResult(Success()); });
        using var first = Image(8, 8, Color.White);
        using var same = new Bitmap(first);
        Read(cache, first);
        Read(cache, same);
        if (calls != 1) throw new Exception("An exact complete viewport was not reused.");
        same.SetPixel(7, 7, Color.FromArgb(255, 254, 255, 255));
        Read(cache, same);
        if (calls != 2) throw new Exception("A single changed channel reused old OCR.");
        using var reshaped = Image(4, 16, Color.White);
        Read(cache, reshaped);
        Read(cache, reshaped);
        using var cropA = first.Clone(new Rectangle(0, 0, 4, 4), first.PixelFormat);
        using var cropB = first.Clone(new Rectangle(4, 4, 4, 4), first.PixelFormat);
        Read(cache, cropA);
        Read(cache, cropB);
        if (calls != 6) throw new Exception("A different dimension or unproven crop origin was cached.");
    }

    public static void FailuresAndExceptionsAreRetried()
    {
        using var source = Image(8, 8, Color.White);
        var calls = 0;
        var cache = Cache((_, _) => {
            calls++;
            if (calls == 1) return Task.FromResult(new OcrRecognitionResult(false, [], "failed"));
            if (calls == 2) throw new InvalidOperationException("native-test-failure");
            return Task.FromResult(Success());
        });
        if (Read(cache, source).IsSuccess) throw new Exception("Failure became successful OCR.");
        try { Read(cache, source); throw new Exception("OCR exception was swallowed."); }
        catch (InvalidOperationException) { }
        if (!Read(cache, source).IsSuccess || !Read(cache, source).IsSuccess || calls != 3)
            throw new Exception("A failed/throwing recognition was retained, or recovery was not reused.");
    }

    public static void CanceledCallersCannotReadOrPopulateTheCache()
    {
        using var source = Image(8, 8, Color.White);
        var calls = 0;
        var cache = Cache((_, _) => { calls++; return Task.FromResult(Success()); });
        Read(cache, source);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        MustCancel(cache.RecognizeAsync(source, canceled.Token));
        Read(cache, source);
        if (calls != 1 || cache.CompletedReuseCount != 1)
            throw new Exception("A canceled hit disrupted the retained success or counted as completed reuse.");

        using var nativeCancellation = new CancellationTokenSource();
        var nativeCalls = 0;
        var late = Cache((_, _) => {
            if (++nativeCalls == 1) nativeCancellation.Cancel();
            return Task.FromResult(Success()); // Simulate a native owner ignoring cancellation.
        });
        MustCancel(late.RecognizeAsync(source, nativeCancellation.Token));
        Read(late, source);
        if (nativeCalls != 2) throw new Exception("A canceled native owner's late success was cached.");
    }

    public static void CancelingAWaiterPreservesTheRealOwner()
    {
        using var source = Image(8, 8, Color.White);
        using var same = new Bitmap(source);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<OcrRecognitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = Cache(async (_, token) => {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        });
        var owner = cache.RecognizeAsync(source, default);
        entered.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        using var waiterCancellation = new CancellationTokenSource();
        var waiter = cache.RecognizeAsync(same, waiterCancellation.Token);
        waiterCancellation.Cancel();
        MustCancel(waiter);
        if (owner.IsCompleted || calls != 1)
            throw new Exception("Canceling a waiter canceled or duplicated the native owner.");
        release.TrySetResult(Success());
        owner.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Read(cache, same);
        if (calls != 1) throw new Exception("A completed real owner did not release admission for reuse.");
    }

    public static void SessionsProfilesAndCapacityAreBounded()
    {
        using var source = Image(8, 8, Color.White);
        var calls = 0;
        Task<OcrRecognitionResult> Recognize(Bitmap _, CancellationToken __)
        { calls++; return Task.FromResult(Success()); }
        var first = Cache(Recognize);
        var second = Cache(Recognize);
        var visual = new LiveTranslationRecognitionCache(
            LiveTranslationRecognitionCache.VisualProfile, source.Size, Recognize);
        Read(first, source);
        Read(second, source);
        Read(visual, source);
        if (calls != 3) throw new Exception("Sessions or recognizer profiles shared OCR state.");
        using var red = Image(8, 8, Color.Red);
        using var blue = Image(8, 8, Color.Blue);
        Read(first, red);
        Read(first, blue);
        Read(first, source);
        if (LiveTranslationRecognitionCache.Capacity != 2 || calls != 6)
            throw new Exception("The fixed two-entry completed cache did not evict old pixels.");
    }

    public static void CachedDataCannotBeChangedThroughNativeResultLists()
    {
        using var source = Image(8, 8, Color.White);
        var bounds = new PixelRect(0, 0, 4, 4);
        var recognizedWords = new[] { new OcrWord("source", bounds) };
        var words = new[] { new OcrWord("source", bounds) { RecognitionWords = recognizedWords } };
        var primary = new[] { new OcrWord("source", bounds) };
        var blocks = new[] { new OcrTextBlock("source", bounds, words) { PrimaryRecognitionWords = primary } };
        var cache = Cache((_, _) => Task.FromResult(new OcrRecognitionResult(true, blocks, "completed")));
        Read(cache, source);
        recognizedWords[0] = new("changed-nested", bounds);
        words[0] = new("changed-word", bounds);
        primary[0] = new("changed-primary", bounds);
        blocks[0] = new("changed-block", bounds, []);
        var retained = Read(cache, source).Blocks[0];
        if (retained.Text != "source" || retained.Words[0].Text != "source"
            || retained.Words[0].RecognitionWords[0].Text != "source"
            || retained.PrimaryRecognitionWords[0].Text != "source")
            throw new Exception("Mutable native OCR lists changed a retained successful result.");
        if (retained.Words is IList<OcrWord> mutable && !mutable.IsReadOnly)
            throw new Exception("The cached OCR exposed writable lists to pipeline consumers.");
    }

    private static LiveTranslationRecognitionCache Cache(
        Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>> recognize) => new(
            LiveTranslationRecognitionCache.CommonMachineProfile, new Size(8, 8), recognize);
    private static OcrRecognitionResult Success() => new(true, [], "completed");
    private static OcrRecognitionResult Read(LiveTranslationRecognitionCache cache, Bitmap bitmap) =>
        cache.RecognizeAsync(bitmap, default).GetAwaiter().GetResult();
    private static void MustCancel(Task<OcrRecognitionResult> task)
    {
        try { task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return; }
        throw new Exception("A canceled recognition caller received a result.");
    }
    private static Bitmap Image(int width, int height, Color color)
    {
        var image = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(color);
        return image;
    }
}

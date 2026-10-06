using System.Drawing;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class WorkbenchSessionTests
{
    public static void Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task RunAsync()
    {
        Require(ClipRay.App.Ocr.OcrLanguageSelector.SelectExact(["en-US", "ja-JP"], "JA-jp") == "ja-JP",
            "explicit OCR language uses installed canonical tag");
        var missingLanguageRejected = false;
        try { ClipRay.App.Ocr.OcrLanguageSelector.SelectExact(["en-US"], "ja-JP"); }
        catch (ClipRay.App.Ocr.OcrUnavailableException) { missingLanguageRejected = true; }
        Require(missingLanguageRejected, "explicit OCR language never silently falls back");
        using var source = new Bitmap(100, 80);
        source.SetPixel(0, 0, Color.Red);
        using (var snapshots = new ClipRay.App.LiveTranslation.LiveWorkbenchSnapshot())
        {
            Require(snapshots.CopyFresh(TimeSpan.FromSeconds(2)) is null, "no fabricated live frame");
            snapshots.Record(source);
            source.SetPixel(0, 0, Color.Blue);
            using var frozen = snapshots.CopyFresh(TimeSpan.FromSeconds(2));
            Require(frozen?.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "live handoff owns original raw pixels");
            Require(snapshots.CopyFresh(TimeSpan.FromTicks(-1)) is null, "expired live frame rejected");
            snapshots.Clear();
            Require(snapshots.CopyFresh(TimeSpan.FromSeconds(2)) is null, "failed capture clears stale snapshot");
            snapshots.Dispose(); snapshots.Record(source);
            Require(snapshots.CopyFresh(TimeSpan.FromSeconds(2)) is null, "closed live buffer cannot be revived");
            Require(frozen?.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "workbench copy survives live disposal");
            source.SetPixel(0, 0, Color.Red);
        }
        await VerifyOrchestrator(source);
        var identity = new VisualTextRegion("identity", "Someone @someone · 2h", [], new PixelRect(5, 5, 70, 20),
            VisualTextRole.Metadata, VisualStructureConfidence.High);
        var identityPlan = InPlaceLayoutService.BuildRegionPlan(source, [identity], [new(true, "某人", "completed")]);
        Require(identityPlan.Items.Count == 0 && identityPlan.Rejections.Single().Reason == "identity-pixels-preserved",
            "shared layout protects inferred identity rows for every caller");
        var bodyMention = identity with { Role = VisualTextRole.Body, SourceText = "Ask @someone to review the changes." };
        Require(VisualTranslationRequestFactory.ClassifyRegions([bodyMention])[0] == TranslationTextKind.Body,
            "body mention does not become an identity row");
        await VerifyDocument(source);
        var region = new VisualTextRegion("old", "Original", [], new PixelRect(5, 5, 70, 20),
            VisualTextRole.Body, VisualStructureConfidence.High);
        using var session = new TranslationWorkbenchSession(source, new PixelRect(40, 50, 100, 80),
            new PixelRect(45, 55, 90, 70), [region, region with { Id = "second" }], TranslationRouteKind.Machine, "zh");
        source.SetPixel(0, 0, Color.Blue);
        using (var copy = session.CopySource()) Require(copy.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "independent original");
        var a = session.Regions[0].Id; var b = session.Regions[1].Id;
        var first = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = session.TranslateAsync(a, (_, _, _) => first.Task);
        session.Edit(a, "Corrected");
        first.SetResult(new(a, new(true, "旧译文", "completed")));
        await old;
        Require(session.Regions[0].State == WorkbenchRegionState.Modified && session.Regions[0].Translation == "", "edit rejects late result");
        Require(session.Regions[0].Original.SourceText == "Original" && session.Regions[0].Id == a, "stable original and identity");
        Require(session.Attempts.Count == 1 && session.Attempts[0].Completed
            && !session.Attempts[0].CurrentAtCompletion && session.Attempts[0].SourceText == "Original", "stale attempt retains its own input evidence");

        var late = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var earlier = session.TranslateAsync(a, (_, _, _) => late.Task);
        var executionEvidence = new TranslationExecutionEvidence("test-engine", "test-model", "test-digest", "CPU");
        await session.TranslateAsync(a, (_, input, _) => Task.FromResult(new WorkbenchTranslationOutput(a,
            new(true, "新译文", "completed", ExecutionEvidence: executionEvidence), LayoutFailure: "text-does-not-fit")));
        late.SetResult(new(a, new(true, "迟到译文", "completed"))); await earlier;
        Require(session.Regions[0].Translation == "新译文" && session.Regions[0].State == WorkbenchRegionState.LayoutRejected, "same revision attempts and layout fallback");
        Require(session.Attempts.Count == 3 && session.Attempts[2].CurrentAtCompletion
            && !session.Attempts[1].CurrentAtCompletion, "attempt evidence follows completion identity");
        Require(session.Attempts[2].ExecutionEvidence == executionEvidence
            && session.Attempts[1].ExecutionEvidence is null, "execution evidence stays with its producing attempt");
        Require(session.Attempts[2].Status == "text-does-not-fit",
            "attempt records layout rejection instead of provider success");

        var aWait = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bWait = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aTask = session.TranslateAsync(a, (_, _, _) => aWait.Task);
        var bTask = session.TranslateAsync(b, (_, _, _) => bWait.Task);
        session.Cancel(a);
        aWait.SetResult(new(a, new(true, "不应显示", "completed")));
        bWait.SetResult(new(b, new(true, "有效文字", "completed")));
        await Task.WhenAll(aTask, bTask);
        Require(session.Regions[0].State == WorkbenchRegionState.Cancelled && session.Regions[1].Translation == "有效文字", "independent cancellation");
        await session.TranslateAsync(a, (_, _, _) => Task.FromResult(new WorkbenchTranslationOutput(b, new(true, "wrong", "completed"))));
        Require(session.Regions[0].Detail == "region-result-mismatch", "wrong result rejected");
        session.RestoreOriginal(a);
        await session.TranslateAsync(a, (_, _, _) => Task.FromResult(new WorkbenchTranslationOutput(a, new(true, "Original", "machine-preserved"))));
        Require(session.Regions[0].State == WorkbenchRegionState.Preserved, "preserved is not translated");

        session.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        await session.TranslateAsync(a, (pixels, input, _) =>
        {
            pixels.SetPixel(0, 0, Color.Green);
            var block = new ClipRay.App.Ocr.OcrTextBlock(input.Region.SourceText, input.Region.Bounds, []);
            var layout = new InPlaceLayoutItem(block, "原文", input.Region.Bounds,
                InPlaceBackgroundMode.FlatSampled, Color.White, Color.Black, SourceRegion: input.Region);
            return Task.FromResult(new WorkbenchTranslationOutput(a, new(true, "原文", "completed"), layout));
        });
        Require(session.Regions[0].State == WorkbenchRegionState.Translated, "matched layout succeeds despite observer failure");
        var manualAttempt = session.BeginManualTranslation(a, "人工修正译文");
        var manualLayout = session.Regions[0].ManualBaseline!.Layout! with { Text = "人工修正译文" };
        Require(session.CompleteManualTranslation(a, manualAttempt, manualLayout, null)
            && session.Regions[0].Translation == "人工修正译文"
            && session.Regions[0].ManualBaseline!.Translation == "原文", "manual correction preserves model baseline");
        Require(session.RestoreTranslationBeforeManualEdit(a)
            && session.Regions[0].State == WorkbenchRegionState.Translated
            && session.Regions[0].Translation == "原文" && session.Regions[0].ManualBaseline is null,
            "undo manual correction restores model text and layout");
        var delayedManual = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manualRace = session.TranslateAsync(a, (_, _, _) => delayedManual.Task);
        manualAttempt = session.BeginManualTranslation(a, "迟到请求不能覆盖人工修正");
        Require(session.CompleteManualTranslation(a, manualAttempt, null, "text-does-not-fit"),
            "manual correction can retain readable text when image layout fails");
        delayedManual.SetResult(new(a, new(true, "迟到结果", "completed")));
        await manualRace;
        Require(session.Regions[0].Translation == "迟到请求不能覆盖人工修正"
            && session.Regions[0].State == WorkbenchRegionState.LayoutRejected,
            "manual correction rejects late provider completion");
        Require(session.RestoreTranslationBeforeManualEdit(a)
            && session.Regions[0].State == WorkbenchRegionState.Cancelled,
            "undo after editing an in-flight request never revives a translating state");
        using (var copy = session.CopySource()) Require(copy.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "provider cannot mutate original");
        session.Configure(a, "en", "zh", TranslationRouteKind.Machine);
        Require(session.Regions[0].Translation == "" && session.Regions[0].State == WorkbenchRegionState.Modified, "configuration invalidates translation");

        Bitmap? workerPixels = null;
        var closedWait = new TaskCompletionSource<WorkbenchTranslationOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = session.TranslateAsync(a, (pixels, _, _) => { workerPixels = pixels; return closedWait.Task; });
        session.Dispose();
        Require(workerPixels!.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb(), "in-flight pixels survive close");
        closedWait.SetResult(new(a, new(true, "late", "completed"))); await closing;
        Require(session.Regions.Count == 0, "close clears private results");
        Require(session.Attempts.Count == 0, "close clears private attempt history");
        Console.WriteLine("Workbench session lifecycle checks passed (no foreground).");
    }
    private static async Task VerifyDocument(Bitmap source)
    {
        var replies = new Queue<Task<ClipRay.App.Ocr.OcrRecognitionResult>>();
        var delayed = new TaskCompletionSource<ClipRay.App.Ocr.OcrRecognitionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        replies.Enqueue(delayed.Task);
        replies.Enqueue(Task.FromResult(new ClipRay.App.Ocr.OcrRecognitionResult(true, [], "completed")));
        replies.Enqueue(Task.FromException<ClipRay.App.Ocr.OcrRecognitionResult>(new InvalidOperationException("private diagnostic")));
        var block = new ClipRay.App.Ocr.OcrTextBlock("A complete sentence.", new PixelRect(5, 5, 80, 20), []);
        replies.Enqueue(Task.FromResult(new ClipRay.App.Ocr.OcrRecognitionResult(true, [block], "completed")));
        var orchestrator = new TranslationApplicationOrchestrator((_, _) => replies.Dequeue(),
            (_, _, _) => Task.FromResult<IReadOnlyList<TranslationResult>>([]));
        using var document = new TranslationWorkbenchDocument(source, new PixelRect(0, 0, 100, 80),
            new PixelRect(0, 0, 100, 80), TranslationRouteKind.Machine, "zh", orchestrator, blocks => blocks);
        var old = document.RecognizeAsync();
        document.CancelRecognition();
        await document.RecognizeAsync();
        delayed.SetResult(new(true, [block], "completed")); await old;
        Require(document.State == WorkbenchRecognitionState.Empty && document.Session is null, "cancelled OCR cannot replace retry");
        await document.RecognizeAsync();
        Require(document.State == WorkbenchRecognitionState.Failed, "OCR exception permits recovery");
        using (var copy = document.CopySource()) Require(copy.Width == 100, "failed OCR retains source");
        await document.RecognizeAsync();
        Require(document.State == WorkbenchRecognitionState.Recognized && document.Session!.Regions.Count > 0, "OCR retry creates editable session");
        document.Dispose();
        Require(document.State == WorkbenchRecognitionState.Closed && document.Session is null, "document releases private session");
    }

    private static async Task VerifyOrchestrator(Bitmap source)
    {
        var ocrCalls = 0;
        var translationCalls = 0;
        var region = new VisualTextRegion("target", "Corrected sentence.", [], new PixelRect(5, 5, 70, 20),
            VisualTextRole.Body, VisualStructureConfidence.High);
        var sibling = region with { Id = "neighbor", SourceText = "Neighbor" };
        var id = Guid.NewGuid();
        var input = new WorkbenchTranslationInput(Guid.NewGuid(), id, 1, 1, region, "es", "zh", TranslationRouteKind.Machine);
        var wrongCount = false;
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => { ocrCalls++; return Task.FromResult(new ClipRay.App.Ocr.OcrRecognitionResult(true, [], "completed")); },
            (route, requests, _) =>
            {
                translationCalls++;
                Require(route == TranslationRouteKind.Machine && requests.Count == 1, "one explicit route and request");
                Require(requests[0].SourceLanguage == "es" && requests[0].TargetLanguage == "zh"
                    && requests[0].OcrText == region.SourceText, "effective languages and corrected draft");
                return Task.FromResult<IReadOnlyList<TranslationResult>>(wrongCount ? [] : [new(true, "修正句子。", "completed")]);
            },
            (_, regions, results) =>
            {
                Require(regions.Count == 2 && regions[1].Id == sibling.Id
                    && results[1].Status == "machine-preserved", "siblings retained without translating them");
                throw new InvalidOperationException("layout unavailable");
            });
        var output = await orchestrator.TranslateWorkbenchRegionAsync(source, input, [region, sibling], default);
        Require(ocrCalls == 0 && translationCalls == 1 && output.RegionId == id
            && output.Result.Text == "修正句子。" && output.LayoutFailure == "layout-failed", "retry bypasses OCR and retains readable result");
        wrongCount = true;
        output = await orchestrator.TranslateWorkbenchRegionAsync(source, input, [region, sibling], default);
        Require(!output.Result.IsSuccess && output.Result.Status == "translation-count-mismatch", "missing result rejected");
        wrongCount = false;
        region = region with { Role = VisualTextRole.IdentityMetadata, SourceText = "Someone @someone · 2h" };
        output = await orchestrator.TranslateWorkbenchRegionAsync(source, input with { Region = region }, [region, sibling], default);
        Require(output.Layout is null && output.LayoutFailure == "identity-pixels-preserved"
            && output.Result.Text == "修正句子。", "identity pixels stay original while requested translation remains readable");
        var recognition = await orchestrator.RecognizeWorkbenchAsync(source, blocks => blocks, default);
        Require(ocrCalls == 1 && !recognition.IsSuccess && recognition.Status == "ocr-empty", "empty OCR explicit");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

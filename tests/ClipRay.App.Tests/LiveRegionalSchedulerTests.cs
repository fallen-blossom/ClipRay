using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveRegionalSchedulerTests
{
    public static void SharedOcrGateUnknownCannotCancelStableBody()
    {
        VerifySharedGate(changeBody: false);
        VerifySharedGate(changeBody: true);
    }

    private static void VerifySharedGate(bool changeBody)
    {
        using var gate = new SemaphoreSlim(1, 1);
        var captures = 0;
        var phase = 0;
        var calls = 0;
        var canceled = 0;
        var unknown = 0;
        var invalidations = 0;
        Task? delayedStop = null;
        var translated = new List<string>();
        var body = new PixelRect(10, 10, 110, 25);
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new(0, 0, 180, 160), _ =>
        {
            var bitmap = new Bitmap(180, 160);
            bitmap.SetPixel(20, 20, Volatile.Read(ref phase) == 0 ? Color.White : Color.Red);
            using var graphics = Graphics.FromImage(bitmap);
            using var brush = new SolidBrush(Color.FromArgb(20 + Interlocked.Increment(ref captures) % 180, 70, 90));
            foreach (var y in new[] { 55, 90, 125 }) graphics.FillRectangle(brush, 5, y, 160, 10);
            return bitmap;
        }, async (bitmap, token) =>
        {
            var source = bitmap.GetPixel(20, 20).R == 255 && bitmap.GetPixel(20, 20).G == 255 ? "A" : "B";
            var call = Interlocked.Increment(ref calls);
            await gate.WaitAsync(token);
            try
            {
                if (call == 1 && changeBody) Interlocked.Exchange(ref phase, 1);
                // Production main OCR owns the same semaphore requested by the
                // 300ms semantic check. The verifier cannot execute in that time.
                await Task.Delay(call == 1 ? 1200 : 30, token);
            }
            catch (OperationCanceledException) { Interlocked.Increment(ref canceled); throw; }
            finally { gate.Release(); }
            return Frame(Item(source, "译" + source, body));
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
        verifySource: async (bitmap, token) =>
        {
            await gate.WaitAsync(token);
            try
            {
                await Task.Delay(400, token); // Slow semantic OCR after it actually acquires the production gate.
                var source = bitmap.GetPixel(20, 20).G == 255 ? "A" : "B";
                return new OcrRecognitionResult(true, [Block(source, body)], "completed");
            }
            finally { gate.Release(); }
        }, scheduleRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7)))
        {
            session.CaptureDecision += (_, args) =>
            {
                if (args.Kind == "semantic_unknown_during_worker") Interlocked.Increment(ref unknown);
            };
            session.FrameReady += (_, args) =>
            {
                translated.AddRange(args.Frame.LayoutItems.Select(item => item.Text));
                if (changeBody) session.Stop();
                else delayedStop ??= Task.Run(async () => { await Task.Delay(750); session.Stop(); });
            };
            session.FrameInvalidated += (_, _) => Interlocked.Increment(ref invalidations);
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        delayedStop?.GetAwaiter().GetResult();
        var expected = changeBody ? "译B" : "译A";
        if (unknown == 0 || canceled != 0 || !translated.SequenceEqual(new[] { expected })
            || calls != (changeBody ? 2 : 1) || !changeBody && invalidations != 0)
            throw new Exception($"Shared OCR queue retired a stable worker or treated unknown as same: changed={changeBody}, "
                + $"unknown={unknown}, canceled={canceled}, calls={calls}, clears={invalidations}, published={string.Join(',', translated)}.");
    }

    public static void RegionalRefreshKeepsSiblingAndUsesCropCoordinates()
    {
        var firstBounds = new PixelRect(30, 20, 110, 25);
        var secondBounds = new PixelRect(300, 110, 120, 25);
        var first = Item("stable body", "稳定正文", firstBounds);
        var second = Item("old body", "旧正文", secondBounds);
        var phase = 0;
        var calls = 0;
        var publications = 0;
        LiveTranslationFrame? latest = null;
        var seenCrop = false;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new(0, 0, 500, 200), _ =>
        {
            var bitmap = new Bitmap(500, 200);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.FillRectangle(Brushes.White, 30, 20, 110, 25);
            graphics.FillRectangle(Volatile.Read(ref phase) == 0 ? Brushes.White : Brushes.Red, 300, 110, 120, 25);
            return bitmap;
        }, (bitmap, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) return Task.FromResult(Frame(first, second));
            seenCrop = bitmap.Width < 500 && bitmap.Height < 200;
            var points = new List<Point>();
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).ToArgb() == Color.Red.ToArgb()) points.Add(new(x, y));
            if (points.Count == 0) throw new Exception("Regional worker did not receive current changed pixels.");
            var localBounds = new PixelRect(points.Min(point => point.X), points.Min(point => point.Y),
                points.Max(point => point.X) - points.Min(point => point.X) + 1,
                points.Max(point => point.Y) - points.Min(point => point.Y) + 1);
            return Task.FromResult(Frame(Item("new body", "新正文", localBounds)));
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
        verifySource: (_, _) => Task.FromResult(new OcrRecognitionResult(true,
            [Block("stable body", firstBounds), Block(phase == 0 ? "old body" : "new body", secondBounds)], "completed")),
        scheduleRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            session.FrameReady += (_, args) =>
            {
                if (Interlocked.Increment(ref publications) == 1) { Interlocked.Exchange(ref phase, 1); return; }
                if (!args.Frame.LayoutItems.Any(item => item.Text == "新正文")) return;
                latest = args.Frame;
                session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (!seenCrop || calls != 2 || latest?.RegionWork is not { SourceVersion.Length: 64 } work
            || work.Bounds.Width >= 500 || latest.LayoutItems.Count != 2
            || latest.LayoutItems.Single(item => item.Text == "稳定正文").Bounds != firstBounds
            || latest.LayoutItems.Single(item => item.Text == "新正文").SourceRegion?.Bounds != secondBounds
            || latest.ExpectedLayoutCount != 2 || latest.ResultState != LiveTranslationViewportResultState.Ready)
            throw new Exception("A regional refresh rebuilt/lost an unrelated sibling or committed crop-local geometry.");
    }

    public static void UnknownPixelChangeAndRealScrollCannotReuseOldBody()
    {
        using var sourceBitmap = new Bitmap(200, 160);
        using (var graphics = Graphics.FromImage(sourceBitmap))
            foreach (var y in new[] { 10, 55, 100 }) graphics.FillRectangle(Brushes.White, 20, y, 120, 15);
        var items = new[] { Item("one body", "正文一", new(20, 10, 120, 15)),
            Item("two body", "正文二", new(20, 55, 120, 15)), Item("three body", "正文三", new(20, 100, 120, 15)) };
        var source = new LiveTranslationPixelSnapshot(sourceBitmap);
        using var scrolledBitmap = new Bitmap(200, 160);
        using (var graphics = Graphics.FromImage(scrolledBitmap))
            foreach (var y in new[] { 25, 70, 115 }) graphics.FillRectangle(Brushes.White, 20, y, 120, 15);
        var current = new LiveTranslationPixelSnapshot(scrolledBitmap);
        var commit = LiveTranslationViewportCommitPolicy.Commit(source, current,
            items.Select(item => item.SourceRegion!).ToArray(), LiveTranslationRegionWitness.Create(source, items), Frame(items));
        if (commit.Witness.Count != 0 || LiveTranslationViewportResultPolicy.CanPresent(commit.ResultState))
            throw new Exception("A real scroll retained old body pixels without evidence.");

        var oldEvidence = LiveTranslationSemanticEvidence.FromRecognition(new(true,
            items.Select(item => item.SourceBlock).ToArray(), "completed"), sourceBitmap)!;
        var extra = LiveTranslationSemanticEvidence.FromRecognition(new(true,
            items.Select(item => item.SourceBlock).Append(Block("video fake", new(160, 130, 30, 15))).ToArray(),
            "completed"), sourceBitmap)!;
        if (extra.SameSourceStableGeometry(oldEvidence) || extra.ConfirmsViewportReplacement(oldEvidence))
            throw new Exception("An extra OCR candidate became same-source evidence or a viewport cancellation.");
    }

    public static void RegionalExitRemovesOnlyDepartedBody()
    {
        var a = Item("fixed paragraph", "保留正文", new(30, 20, 110, 25));
        var b = Item("departing paragraph", "退出正文", new(300, 110, 120, 25));
        using var patchBitmap = new Bitmap(110, 25);
        using var patchStream = new System.IO.MemoryStream();
        patchBitmap.Save(patchStream, System.Drawing.Imaging.ImageFormat.Png);
        var patch = patchStream.ToArray();
        a = a with { MaskedPatchPng = patch };
        var phase = 0;
        var calls = 0;
        var stale = false;
        LiveTranslationFrame? final = null;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new(0, 0, 500, 200), _ =>
        {
            var bitmap = new Bitmap(500, 200);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.FillRectangle(Brushes.White, 30, 20, 110, 25);
            if (Volatile.Read(ref phase) == 0) graphics.FillRectangle(Brushes.White, 300, 110, 120, 25);
            return bitmap;
        }, (bitmap, _) =>
        {
            if (++calls == 1) return Task.FromResult(Frame(a, b));
            if (bitmap.Width >= 500) throw new Exception("A departed local paragraph rebuilt the entire viewport.");
            return Task.FromResult(new LiveTranslationFrame("Machine", "partial-or-failed", [], "Scope:partial-or-failed"));
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
        verifySource: (_, _) => Task.FromResult(new OcrRecognitionResult(true,
            phase == 0 ? [a.SourceBlock, b.SourceBlock] : [a.SourceBlock], "completed")), scheduleRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            session.FrameReady += (_, args) =>
            {
                if (Interlocked.CompareExchange(ref phase, 1, 0) == 0) return;
                if (args.Frame.LayoutItems.Any(item => item.Text == b.Text)) stale = true;
                if (args.Frame.ExpectedLayoutCount != 1 || args.Frame.ResultState != LiveTranslationViewportResultState.Ready) return;
                final = args.Frame;
                session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (stale || calls != 2 || final?.LayoutItems.Count != 1 || final.LayoutItems[0].Text != a.Text
            || !ReferenceEquals(final.LayoutItems[0].MaskedPatchPng, patch))
            throw new Exception("Regional exit retained departed text or replaced a stable sibling's erasure patch.");
    }

    public static void CropMovesOriginalOcrEvidenceAndKeepsRegionIdsUnique()
    {
        var item = Item("same positional id", "译文", new(5, 6, 40, 20));
        var word = item.SourceBlock.Words.Single();
        var spanStyle = new SourceVisualStyle(Color.Red, 18, 24, SourceFontWeight.Bold,
            SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High, 20);
        var region = item.SourceRegion! with { OriginalOcrWords = [word], SharesParentOcrEvidence = true,
            SourceStyleSpans = [new(0, 4, "same", word.Bounds, spanStyle)] };
        var frame = Frame(item with { SourceRegion = region });
        var shifted = LiveTranslationFrameCoordinates.Offset(frame, 100, 200);
        var shiftedRegion = shifted.LayoutItems[0].SourceRegion!;
        if (shiftedRegion.OriginalOcrWords.Single().Bounds != new PixelRect(105, 206, 40, 20)
            || shiftedRegion.OriginalOcrWords.Single().Bounds != shiftedRegion.Lines[0].Words[0].Bounds
            || shiftedRegion.Id == region.Id || shiftedRegion.Id != shifted.SourceRegions[0].Id
            || shifted.ExpectedLayoutRegions?.Single().Id != shiftedRegion.Id || !shiftedRegion.SharesParentOcrEvidence
            || shiftedRegion.SourceStyleSpans.Single().Bounds != new PixelRect(105, 206, 40, 20)
            || shiftedRegion.SourceStyleSpans.Single().Style != spanStyle
            || region.OriginalOcrWords.Single().Bounds != word.Bounds)
            throw new Exception("A crop left parent OCR mask evidence in local coordinates or collided with a full-frame region identity.");
    }

    public static void SourceStyleVersionCommitsCurrentTypography()
    {
        var source = Item("same source", "原译文", new(10, 10, 300, 70));
        var oldStyle = new SourceVisualStyle(Color.Red, 20, 26, SourceFontWeight.Normal,
            SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High, 20);
        var oldLayout = new TranslationTextLayout(true, 20, 26, oldStyle.FontWeight,
            oldStyle.FontCategory, false, oldStyle.Alignment, 0, "Segoe UI");
        var first = source with { SourceStyle = oldStyle, TextLayout = oldLayout, ForegroundColor = Color.Red };
        var newStyle = oldStyle with { ForegroundColor = Color.Blue, EstimatedFontSizePixels = 32,
            GlyphHeightPixels = 32, LineHeightPixels = 40, FontWeight = SourceFontWeight.Bold, Alignment = SourceTextAlignment.Center };
        var newLayout = oldLayout with { FontSizePixels = 32, LineHeightPixels = 40,
            FontWeight = newStyle.FontWeight, Alignment = newStyle.Alignment };
        var second = source with { Text = "同源另一译文", SourceStyle = newStyle, TextLayout = newLayout, ForegroundColor = Color.Blue };
        var cache = new LiveTranslationLayoutCache();
        var window = new Rectangle(0, 0, 400, 100);
        cache.Update(Frame(first), [window]);
        var actual = cache.Update(Frame(second), [window]).LayoutItems.Single();
        if (actual.Text != second.Text || actual.TextLayout != newLayout || actual.SourceStyle != newStyle
            || actual.ForegroundColor != Color.Blue)
            throw new Exception("Same text froze an older font/color/alignment source style version.");
    }

    public static void SemanticCancellationDoesNotPoisonAppExit()
    {
        RunAsync().GetAwaiter().GetResult();
        static async Task RunAsync()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var returned = 0;
            var phase = 0;
            var body = Item("stable body", "正文", new(10, 10, 110, 25));
            using var session = new LiveTranslationSession(new(0, 0, 180, 100), _ =>
            {
                var bitmap = new Bitmap(180, 100);
                bitmap.SetPixel(150, 70, Volatile.Read(ref phase) == 0 ? Color.Red : Color.Blue);
                return bitmap;
            }, (_, _) => Task.FromResult(Frame(body)), TranslationRouteKind.Machine,
                validateLayoutRegions: true, verifySource: async (bitmap, ignoredToken) =>
                {
                    entered.TrySetResult();
                    await release.Task.ConfigureAwait(false);
                    // Simulate native OCR ignoring cancellation while it still
                    // owns and reads its bitmap for another 500ms.
                    await Task.Delay(500).ConfigureAwait(false);
                    _ = bitmap.GetPixel(20, 20);
                    Interlocked.Exchange(ref returned, 1);
                    return new OcrRecognitionResult(true, [body.SourceBlock], "completed");
                });
            session.FrameReady += (_, _) => Interlocked.Exchange(ref phase, 1);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var running = session.StartAsync(timeout.Token);
            var app = BackgroundTask3LifetimeTests.FakeApp();
            BackgroundTask3LifetimeTests.Set(app, "_liveTranslationSession", session);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                await Task.Delay(350).ConfigureAwait(false); // Pass the semantic observation budget.
                app.RequestTranslationRetirement();
                var pending = await app.DrainTranslationAsync(TimeSpan.FromMilliseconds(30)).ConfigureAwait(false);
                if (pending.Completed || pending.Pending == 0 || pending.Failures.Count != 0 || returned != 0)
                    throw new Exception("App exit lost a native semantic owner or treated waiting as failed retirement.");
                release.TrySetResult();
                await running.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                var completed = await app.DrainTranslationAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                var repeated = await app.DrainTranslationAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                if (!completed.Completed || !repeated.Completed || returned != 1)
                    throw new Exception("Semantic business cancellation permanently poisoned App retirement: "
                        + string.Join(',', completed.Failures.Concat(repeated.Failures)));
            }
            finally
            {
                release.TrySetResult();
                session.Stop();
                await running.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                await LiveTranslationWorkerRegistry.DrainAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
    }

    public static void CleanupObserverSeparatesBusinessAndReleaseFailures()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        LiveTranslationWorkerRegistry.ObserveCleanupAsync(Task.FromCanceled(canceled.Token), Task.CompletedTask)
            .GetAwaiter().GetResult();
        LiveTranslationWorkerRegistry.ObserveCleanupAsync(Task.FromException(new InvalidOperationException("business")),
            Task.CompletedTask).GetAwaiter().GetResult();
        try
        {
            LiveTranslationWorkerRegistry.ObserveCleanupAsync(Task.FromCanceled(canceled.Token),
                Task.FromException(new System.IO.IOException("resource-release"))).GetAwaiter().GetResult();
            throw new Exception("A true semantic resource cleanup failure was swallowed.");
        }
        catch (System.IO.IOException) { }
    }

    public static void SourceSpanVersionAndFontTierKeepTargetRunsCoherent()
    {
        var item = Item("same source", "旧译文", new(10, 10, 300, 70));
        var style = new SourceVisualStyle(Color.Red, 20, 26, SourceFontWeight.Normal,
            SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High, 20);
        var redSpan = new SourceVisualStyleSpan(0, 4, "same", new(10, 10, 70, 25), style);
        var oldLayout = new TranslationTextLayout(true, 20, 26, style.FontWeight, style.FontCategory,
            false, style.Alignment, 0, "Segoe UI") { SourceFontSizePixels = 20,
            StyleRuns = [new(0, 1, style, TranslationStyleAlignmentEvidence.ModelAttention)] };
        var first = item with { SourceStyle = style, TextLayout = oldLayout,
            SourceRegion = item.SourceRegion! with { SourceStyleSpans = [redSpan] },
            TextAlignments = [new(0, 4, 0, 1)] };
        var blueStyle = style with { ForegroundColor = Color.Blue };
        var currentLayout = oldLayout with { StyleRuns = [new(2, 2, blueStyle, TranslationStyleAlignmentEvidence.ModelAttention)] };
        var second = first with { Text = "全新译文", TextLayout = currentLayout,
            SourceRegion = first.SourceRegion! with { SourceStyleSpans = [redSpan with { Style = blueStyle }] },
            TextAlignments = [new(0, 4, 2, 2)] };
        var cache = new LiveTranslationLayoutCache();
        var window = new Rectangle(0, 0, 500, 200);
        cache.Update(Frame(first), [window]);
        var changed = cache.Update(Frame(second), [window]).LayoutItems.Single();
        if (changed.Text != second.Text || changed.TextLayout!.StyleRuns.Single().Style.ForegroundColor != Color.Blue
            || !changed.TextAlignments.SequenceEqual(second.TextAlignments)
            || changed.TextLayout.StyleRuns.Single().Start + changed.TextLayout.StyleRuns.Single().Length > changed.Text.Length)
            throw new Exception("A changed source span reused stale target text or style offsets.");

        var next = second with { Bounds = new(10, 110, 300, 70), Text = "新段正文",
            SourceRegion = second.SourceRegion! with { Id = "next", SourceText = "next source", Bounds = new(10, 110, 300, 70) },
            TextLayout = currentLayout with { FontSizePixels = 22, StyleRuns = [new(0, 2, blueStyle, TranslationStyleAlignmentEvidence.ModelAttention)] } };
        var tiered = cache.Update(Frame(next), [new Rectangle(0, 100, 500, 100)]).LayoutItems.Single();
        if (tiered.TextLayout!.StyleRuns.Count != 1 || tiered.TextLayout.StyleRuns[0].Style.ForegroundColor != Color.Blue
            || tiered.TextLayout.SourceFontSizePixels != 20)
            throw new Exception("Font-tier stabilization discarded target style runs or their source-size basis.");
    }

    public static void SemanticBackingRefreshKeepsWordAlignmentsAndReestimatesStyles()
    {
        using var source = new Bitmap(160, 70);
        using var current = new Bitmap(160, 70);
        Draw(source, Color.Black, Color.Red, Color.White);
        Draw(current, Color.White, Color.Blue, Color.Black);
        var firstWord = new OcrWord("red", new(20, 15, 30, 25));
        var secondWord = new OcrWord("white", new(60, 15, 50, 25));
        var block = new OcrTextBlock("red white", new(20, 15, 90, 25), [firstWord, secondWord]);
        var region = VisualTextRegionBuilder.Build([block], source).Single();
        var result = new TranslationResult(true, "红白", "machine-completed")
        { TextAlignments = [new(0, 3, 0, 1), new(4, 5, 1, 1)] };
        var initial = InPlaceLayoutService.BuildRegionPlan(source, [region], [result]).Items.Single();
        var retained = LiveTranslationRegionWitness.Create(new(source), [initial])
            .RetainForViewportCommit(new(current), static _ => true, [region]);
        var actual = retained.Items.Single();
        var colors = actual.TextLayout!.StyleRuns.Select(run => run.Style.ForegroundColor.ToArgb()).ToArray();
        if (actual.Text != "红白" || !actual.TextAlignments.SequenceEqual(result.TextAlignments)
            || !colors.Contains(Color.Blue.ToArgb()) || !colors.Contains(Color.Black.ToArgb()))
            throw new Exception("Animated/theme backing refresh lost semantic alignments or kept old source-span colors.");

        static void Draw(Bitmap bitmap, Color backing, Color first, Color second)
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(backing);
            using var firstBrush = new SolidBrush(first);
            using var secondBrush = new SolidBrush(second);
            graphics.FillRectangle(firstBrush, 23, 20, 10, 10);
            graphics.FillRectangle(secondBrush, 65, 20, 10, 10);
        }
    }

    public static void UnprocessedWindowsRetainTheirSourceVersion()
    {
        using var beforeBitmap = new Bitmap(300, 180);
        using var currentBitmap = new Bitmap(beforeBitmap);
        currentBitmap.SetPixel(20, 20, Color.Red);
        currentBitmap.SetPixel(220, 120, Color.Blue);
        var before = new LiveTranslationPixelSnapshot(beforeBitmap);
        var current = new LiveTranslationPixelSnapshot(currentBitmap);
        var windows = new[] { new Rectangle(0, 0, 100, 60), new Rectangle(200, 100, 100, 60) };
        var scheduler = new LiveTranslationRegionScheduler();
        var first = scheduler.Select(windows, []);
        var second = scheduler.Select(windows, []);
        var updated = before.UpdateRegions(current, [first]);
        if (first == second || updated.CountChangedPixels(current, first) != 0
            || updated.CountChangedPixels(current, second) == 0)
            throw new Exception("An unprocessed sibling was marked as the newest source version or starved in the bounded scheduler.");
    }

    private static OcrTextBlock Block(string source, PixelRect bounds) => new(source, bounds, [new(source, bounds)]);
    private static InPlaceLayoutItem Item(string source, string translated, PixelRect bounds)
    {
        var block = Block(source, bounds);
        var region = new VisualTextRegion(source, source, [block], bounds, VisualTextRole.Body, VisualStructureConfidence.High);
        return new(block, translated, bounds, InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
    }
    private static LiveTranslationFrame Frame(params InPlaceLayoutItem[] items) => new("Machine", "completed", items)
    {
        SourceRegions = items.Select(item => item.SourceRegion!).ToArray(),
        ExpectedLayoutRegions = items.Select(item => item.SourceRegion!).ToArray(),
        ExpectedLayoutCount = items.Length,
        ExpectedPrimaryLayoutCount = items.Length,
        ExpectedPrimaryLayoutArea = items.Sum(item => item.Bounds.Width * item.Bounds.Height),
    };
}

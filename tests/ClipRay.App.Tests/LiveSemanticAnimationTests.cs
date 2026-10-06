using System.Diagnostics;
using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveSemanticAnimationTests
{
    public static void AnimatedBackingRebuildsTheErasurePatch()
    {
        using var first = new Bitmap(160, 64);
        using (var graphics = Graphics.FromImage(first)) graphics.Clear(Color.Black);
        DrawInk(first);
        var bounds = new PixelRect(20, 14, 100, 30);
        var block = new OcrTextBlock("source", bounds, [new OcrWord("source", bounds)]);
        var region = new VisualTextRegion("animated", "source", [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var initialPlan = InPlaceLayoutService.BuildRegionPlan(first, [region],
            [new TranslationResult(true, "译文", "machine-completed")]);
        var initial = initialPlan.Items.Single();
        var witness = LiveTranslationRegionWitness.Create(new LiveTranslationPixelSnapshot(first), [initial]);

        using var second = new Bitmap(first);
        using (var graphics = Graphics.FromImage(second))
            graphics.Clear(Color.FromArgb(90, 12, 18));
        DrawInk(second);
        var expectedPlan = InPlaceLayoutService.BuildRegionPlan(second, [region],
            [new TranslationResult(true, "译文", "machine-completed")]);
        var retained = witness.RetainForViewportCommit(new LiveTranslationPixelSnapshot(second),
            static _ => true, [region]);
        if (retained.Count != 1 || retained.Items[0].Text != initial.Text
            || retained.Items[0].MaskedPatchPng is not { Length: > 0 } bytes)
            throw new Exception("Unchanged OCR text lost its translation during background animation.");
        using var oldStream = new System.IO.MemoryStream(initial.MaskedPatchPng!);
        using var oldPatch = new Bitmap(oldStream);
        using var newStream = new System.IO.MemoryStream(bytes);
        using var newPatch = new Bitmap(newStream);
        if (oldPatch.GetPixel(5, 8).R >= 40 || newPatch.GetPixel(5, 8).R < 40)
            throw new Exception($"The retained translation reused an erasure patch from the old frame: "
                + $"old={oldPatch.GetPixel(5, 8)}, new={newPatch.GetPixel(5, 8)}, "
                + $"expected-items={expectedPlan.Items.Count}, expected-bg={expectedPlan.Items.FirstOrDefault()?.BackgroundColor}.");

        using var inverted = new Bitmap(160, 64);
        using (var graphics = Graphics.FromImage(inverted)) graphics.Clear(Color.White);
        DrawInk(inverted, Color.Black);
        var afterInversion = retained.RetainForViewportCommit(
            new LiveTranslationPixelSnapshot(inverted), static _ => true, [region]);
        if (afterInversion.Count != 1 || afterInversion.Items[0].ForegroundColor.R > 80)
            throw new Exception("A theme inversion left white translated text on a white backing.");

        static void DrawInk(Bitmap image, Color? ink = null)
        {
            for (var x = 25; x < 109; x += 7)
            for (var y = 21; y < 30; y++)
                image.SetPixel(x, y, ink ?? Color.White);
        }
    }

    public static void MovingBackingKeepsTextAndChangedWordReplacesIt()
    {
        var phase = 0;
        var captures = 0;
        var translated = new List<string>();
        var published = new List<string>();
        var ocrCalls = 0;
        var invalidationsBeforeChange = 0;
        var elapsed = Stopwatch.StartNew();
        var changedAt = TimeSpan.Zero;
        var replacementAt = TimeSpan.Zero;
        Task? switchSource = null;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 100, 70), _ =>
        {
            var image = new Bitmap(100, 70);
            image.SetPixel(2, 2, Volatile.Read(ref phase) == 0 ? Color.Red : Color.Blue);
            image.SetPixel(30, 25, Color.FromArgb(30 + Interlocked.Increment(ref captures) % 180, 60, 90));
            return image;
        }, (bitmap, _) =>
        {
            var source = ReadSource(bitmap);
            translated.Add(source);
            return Task.FromResult(Frame(source));
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
        verifySource: (bitmap, _) =>
        {
            Interlocked.Increment(ref ocrCalls);
            return Task.FromResult(Ocr(ReadSource(bitmap)));
        });

        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            session.FrameReady += (_, args) =>
            {
                var text = args.Frame.LayoutItems.Single().Text;
                published.Add(text);
                if (text == "译A" && changedAt == TimeSpan.Zero)
                {
                    switchSource ??= Task.Run(async () =>
                    {
                        await Task.Delay(900);
                        changedAt = elapsed.Elapsed;
                        Interlocked.Exchange(ref phase, 1);
                    });
                }
                if (text == "译B")
                {
                    replacementAt = elapsed.Elapsed;
                    session.Stop();
                }
            };
            session.FrameInvalidated += (_, _) =>
            {
                if (published.Count > 0 && Volatile.Read(ref phase) == 0)
                    Interlocked.Increment(ref invalidationsBeforeChange);
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        switchSource?.GetAwaiter().GetResult();
        if (changedAt == TimeSpan.Zero || replacementAt == TimeSpan.Zero
            || replacementAt - changedAt > TimeSpan.FromSeconds(3)
            || !published.Contains("译A") || published[^1] != "译B"
            || published.Count(text => text == "译A") != 1
            || published.Count(text => text == "译B") != 1
            || translated.Count(source => source == "A") != 1
            || translated.Count(source => source == "B") != 1
            || ocrCalls is < 2 or > 12 || invalidationsBeforeChange != 0)
            throw new Exception($"Animated A→B did not replace the old translation: "
                + $"translated={string.Join(',', translated)}, published={string.Join(',', published)}, "
                + $"ocr={ocrCalls}, earlyInvalidations={invalidationsBeforeChange}, "
                + $"elapsed={replacementAt - changedAt}.");
    }

    public static void OldWorkerCannotPublishAfterTextChanges()
    {
        var phase = 0;
        var captures = 0;
        var published = new List<string>();
        var calls = 0;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 100, 70), _ =>
        {
            var image = new Bitmap(100, 70);
            image.SetPixel(2, 2, Volatile.Read(ref phase) == 0 ? Color.Red : Color.Blue);
            image.SetPixel(30, 25, Color.FromArgb(30 + Interlocked.Increment(ref captures) % 180, 60, 90));
            return image;
        }, async (bitmap, token) =>
        {
            var source = ReadSource(bitmap);
            Interlocked.Increment(ref calls);
            if (source == "A")
            {
                Interlocked.Exchange(ref phase, 1);
                await Task.Delay(220, token);
            }
            return Frame(source);
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
        verifySource: (bitmap, _) => Task.FromResult(Ocr(ReadSource(bitmap))));

        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
        {
            session.FrameReady += (_, args) =>
            {
                published.Add(args.Frame.LayoutItems.Single().Text);
                session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (calls < 2 || !published.SequenceEqual(["译B"]))
            throw new Exception($"The old worker committed stale A after OCR saw B: "
                + $"calls={calls}, published={string.Join(',', published)}.");
    }

    public static void SourceIdentityUsesFullTextAndTolerantGeometry()
    {
        using var bitmap = new Bitmap(100, 70);
        var first = Item("A two word line", "译A", 10).Region with { Id = "r71" };
        var second = Item("C", "译C", 40).Region with { Id = "r72" };
        var close = LiveTranslationSemanticEvidence.FromRecognition(
            new OcrRecognitionResult(true,
                [Block("C", 42), Block("A two word line", 11)], "completed"), bitmap)!;
        if (!close.SameSource([first, second]))
            throw new Exception("A reordered OCR region with a small geometry drift lost its text identity.");
        if (close.SameSourceStableGeometry([first, second]))
            throw new Exception("Moved text was allowed to reuse an old visual patch or overlay geometry.");
        var slightlyMoved = LiveTranslationSemanticEvidence.FromRecognition(
            new OcrRecognitionResult(true,
                [Block("C", 40), Block("A two word line", 15)], "completed"), bitmap)!;
        if (!slightlyMoved.SameSource([first, second])
            || slightlyMoved.SameSourceStableGeometry([first, second])
            || slightlyMoved.MatchesStableGeometry(Item("A two word line", "译A", 10).Item))
            throw new Exception("A five-pixel move was not separated from stable visual geometry.");
        var changedText = LiveTranslationSemanticEvidence.FromRecognition(
            new OcrRecognitionResult(true,
                [Block("C", 42), Block("A two word linen", 11)], "completed"), bitmap)!;
        if (changedText.SameSource([first, second]))
            throw new Exception("A change at the end of full source text was matched as unchanged.");
        var moved = LiveTranslationSemanticEvidence.FromRecognition(
            new OcrRecognitionResult(true,
                [Block("C", 42), Block("A two word line", 30)], "completed"), bitmap)!;
        if (moved.SameSource([first, second]))
            throw new Exception("Text at a different position inherited an old region identity.");
        if (moved.MatchesStableGeometry(Item("A two word line", "译A", 10).Item))
            throw new Exception("A moved source word inherited an old visual patch.");
    }

    public static void UnknownOcrClearsOldTextWithinBound()
    {
        var phase = 0;
        var captures = 0;
        var published = new List<string>();
        var changedAt = TimeSpan.Zero;
        var invalidatedAt = TimeSpan.Zero;
        var elapsed = Stopwatch.StartNew();
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 100, 70), _ =>
        {
            var image = new Bitmap(100, 70);
            image.SetPixel(2, 2, Volatile.Read(ref phase) == 0 ? Color.Red : Color.Blue);
            image.SetPixel(70, 55, Color.FromArgb(30 + Interlocked.Increment(ref captures) % 180, 60, 90));
            return image;
        }, (bitmap, _) => Task.FromResult(Frame(ReadSource(bitmap))),
        TranslationRouteKind.Machine, validateLayoutRegions: true,
        verifySource: (bitmap, _) => Task.FromResult(Volatile.Read(ref phase) == 0
            ? Ocr(ReadSource(bitmap)) : new OcrRecognitionResult(false, [], "unavailable")));

        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
        {
            session.FrameReady += (_, args) =>
            {
                published.Add(args.Frame.LayoutItems.Single().Text);
                if (changedAt == TimeSpan.Zero)
                {
                    changedAt = elapsed.Elapsed;
                    Interlocked.Exchange(ref phase, 1);
                }
            };
            session.FrameInvalidated += (_, _) =>
            {
                if (changedAt != TimeSpan.Zero && invalidatedAt == TimeSpan.Zero)
                    invalidatedAt = elapsed.Elapsed;
                if (invalidatedAt != TimeSpan.Zero) session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (!published.SequenceEqual(["译A"]) || invalidatedAt == TimeSpan.Zero
            || invalidatedAt - changedAt < TimeSpan.FromSeconds(1.5)
            || invalidatedAt - changedAt > TimeSpan.FromSeconds(2.5))
            throw new Exception($"Unknown latest OCR left old text visible: "
                + $"published={string.Join(',', published)}, elapsed={invalidatedAt - changedAt}.");
    }

    public static void StopCancelsSemanticOcrWithoutLateOutput()
    {
        RunAsync().GetAwaiter().GetResult();

        static async Task RunAsync()
        {
            var captures = 0;
            var published = false;
            var latePublishes = 0;
            var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var session = new LiveTranslationSession(new Rectangle(0, 0, 100, 70), _ =>
            {
                var image = new Bitmap(100, 70);
                image.SetPixel(30, 25, Color.FromArgb(30 + Interlocked.Increment(ref captures) % 180, 60, 90));
                return image;
            }, (_, _) => Task.FromResult(Frame("A")), TranslationRouteKind.Machine,
            validateLayoutRegions: true,
            verifySource: async (_, token) =>
            {
                if (published)
                {
                    checking.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return Ocr("A");
            });
            session.FrameReady += (_, _) =>
            {
                if (published) Interlocked.Increment(ref latePublishes);
                published = true;
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var running = session.StartAsync(timeout.Token);
            await checking.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var stoppedAt = Stopwatch.StartNew();
            session.Stop();
            await running.WaitAsync(TimeSpan.FromSeconds(1));
            if (stoppedAt.Elapsed > TimeSpan.FromSeconds(1) || latePublishes != 0
                || session.State != LiveTranslationSessionState.Stopped)
                throw new Exception("Stop waited on semantic OCR or published a late frame.");
        }
    }

    public static void PartialSuccessRetriesWhileWordsAnimate()
    {
        var captures = 0;
        var calls = 0;
        var published = new List<string>();
        var invalidatedAfterPartial = 0;
        var partialPublished = false;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 100, 70), _ =>
        {
            var image = new Bitmap(100, 70);
            image.SetPixel(30, 25, Color.FromArgb(30 + Interlocked.Increment(ref captures) % 180, 60, 90));
            return image;
        }, (bitmap, _) =>
        {
            bitmap.GetPixel(30, 25); // The provider consumes the frame it was passed.
            var attempt = Interlocked.Increment(ref calls);
            var first = Item("A", "译A", 10);
            var second = Item("C", "译C", 40);
            return Task.FromResult(new LiveTranslationFrame("Machine", "partial",
                attempt == 1 ? [first.Item] : [first.Item, second.Item],
                attempt == 1 ? "temporary-provider-failure" : "none")
            {
                SourceRegions = [first.Region, second.Region],
                ExpectedLayoutCount = 2,
            });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
        verifySource: (bitmap, _) =>
        {
            bitmap.GetPixel(30, 25);
            return Task.FromResult(new OcrRecognitionResult(true,
                [Block("A", 10), Block("C", 40)], "completed"));
        });

        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
        {
            session.FrameReady += (_, args) =>
            {
                published.Add(string.Join(',', args.Frame.LayoutItems.Select(item => item.Text)));
                if (args.Frame.ResultState == LiveTranslationViewportResultState.AcceptablePartial)
                    partialPublished = true;
                if (args.Frame.ResultState == LiveTranslationViewportResultState.Ready)
                    session.Stop();
            };
            session.FrameInvalidated += (_, _) =>
            {
                if (partialPublished) invalidatedAfterPartial++;
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (calls != 2 || !published.Contains("译A") || published[^1] != "译A,译C"
            || invalidatedAfterPartial != 0)
            throw new Exception($"Animation dropped a successful partial or blocked retry: "
                + $"calls={calls}, invalidations={invalidatedAfterPartial}, "
                + $"published={string.Join(" | ", published)}.");
    }

    private static string ReadSource(Bitmap bitmap) => bitmap.GetPixel(2, 2).ToArgb() == Color.Blue.ToArgb() ? "B" : "A";

    private static OcrRecognitionResult Ocr(string source) =>
        new(true, [Block(source, 10)], "completed");

    private static OcrTextBlock Block(string source, int y)
    {
        var bounds = new PixelRect(10, y, 40, 20);
        return new OcrTextBlock(source, bounds, [new OcrWord(source, bounds)]);
    }

    private static (VisualTextRegion Region, InPlaceLayoutItem Item) Item(
        string source, string translation, int y)
    {
        var block = Block(source, y);
        var region = new VisualTextRegion("r0", source, [block], block.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var item = new InPlaceLayoutItem(block, translation, block.Bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
            SourceRegion: region);
        return (region, item);
    }

    private static LiveTranslationFrame Frame(string source)
    {
        var (region, item) = Item(source, "译" + source, 10);
        return new LiveTranslationFrame("Machine", "completed", [item])
        {
            SourceRegions = [region],
            ExpectedLayoutCount = 1,
        };
    }
}

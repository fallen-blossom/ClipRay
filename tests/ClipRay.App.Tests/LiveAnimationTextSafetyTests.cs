using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveAnimationTextSafetyTests
{
    public static void AdjacentAnimationInCropPaddingDoesNotStarveChangedText()
    {
        var phase = 0;
        var captures = 0;
        var calls = 0;
        var published = new List<string>();
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 600, 400), _ =>
        {
            var image = new Bitmap(600, 400);
            image.SetPixel(240, 50, phase == 0 ? Color.White : Color.Red);
            // This pixel is outside the source text and rendered item. The
            // paragraph merges its dirty tile with the glyph's OCR crop.
            image.SetPixel(252, 70, Color.FromArgb(++captures % 200 + 1, 80, 120));
            return image;
        }, (_, _) =>
        {
            calls++;
            var (region, item) = Content(phase == 0 ? "A" : "B", phase == 0 ? "旧译文" : "新译文");
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [item])
                { SourceRegions = [region], ExpectedLayoutCount = 1 });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);

        using (session)
        using (var timeout = new CancellationTokenSource(2500))
        {
            session.FrameReady += (_, args) =>
            {
                published.Add(args.Frame.LayoutItems.Single().Text);
                if (phase == 0) phase = 1;
                else session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (calls != 2 || !published.SequenceEqual(["旧译文", "新译文"]))
            throw new Exception("Animation outside the text evidence starved a stable changed word.");

        AnimatedOldWordCannotStarveIndependentChangedRegionInOneCrop();
    }

    private static void AnimatedOldWordCannotStarveIndependentChangedRegionInOneCrop()
    {
        var phase = 0;
        var captures = 0;
        var calls = 0;
        var published = new List<IReadOnlyList<string>>();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var aBounds = new PixelRect(60, 55, 60, 25);
        var bBounds = new PixelRect(60, 80, 60, 25);
        var aBlock = new OcrTextBlock("A", aBounds,
            [new OcrWord("A", new PixelRect(65, 70, 18, 8))]);
        var aRegion = new VisualTextRegion("a", "A", [aBlock], aBounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 160, 160), _ =>
        {
            var image = new Bitmap(160, 160);
            image.SetPixel(70, 74, Volatile.Read(ref phase) == 0
                ? Color.White : Color.FromArgb(30 + Interlocked.Increment(ref captures) % 180, 80, 120));
            image.SetPixel(70, 85, Volatile.Read(ref phase) == 0 ? Color.White : Color.Red);
            return image;
        }, (_, _) =>
        {
            var afterChange = Interlocked.Increment(ref calls) > 1;
            var bSource = afterChange ? "C" : "B";
            var currentBBlock = new OcrTextBlock(bSource, bBounds,
                [new OcrWord(bSource, new PixelRect(65, 82, 18, 8))]);
            var currentBRegion = new VisualTextRegion("b", bSource, [currentBBlock], bBounds,
                VisualTextRole.Body, VisualStructureConfidence.High);
            var aItem = new InPlaceLayoutItem(aBlock, afterChange ? "A-新" : "A-旧", aBounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: aRegion);
            var bItem = new InPlaceLayoutItem(currentBBlock, afterChange ? "B-新" : "B-旧", bBounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: currentBRegion);
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [aItem, bItem])
            {
                SourceRegions = [aRegion, currentBRegion],
                ExpectedLayoutCount = 2,
            });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(3500))
        {
            session.FrameReady += (_, args) =>
            {
                var texts = args.Frame.LayoutItems.Select(item => item.Text).ToArray();
                published.Add(texts);
                if (texts.Contains("B-旧")) Interlocked.Exchange(ref phase, 1);
                if (texts.Contains("B-新")) session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (calls != 2 || published.Count < 2
            || !published[0].Contains("A-旧") || !published[0].Contains("B-旧")
            || !published[^1].SequenceEqual(["B-新"])
            || elapsed.Elapsed > TimeSpan.FromSeconds(3))
            throw new Exception("A continuously animated old word starved or contaminated an independent stable changed region in the same crop: "
                + $"calls={calls}, published={string.Join(" | ", published.Select(items => string.Join(",", items)))}, "
                + $"elapsed={elapsed.Elapsed}.");
    }

    public static void AnimationThroughTextCannotPublishOldTranslation()
    {
        var phase = 0;
        var captures = 0;
        var calls = 0;
        var published = 0;
        var invalidated = false;
        var waiting = false;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 600, 400), _ =>
        {
            var image = new Bitmap(600, 400);
            image.SetPixel(240, 50, phase == 0 ? Color.White : Color.Red);
            if (phase != 0)
                image.SetPixel(241, 50, Color.FromArgb(++captures % 200 + 1, 80, 120));
            return image;
        }, (_, _) =>
        {
            calls++;
            var (region, item) = Content("A", "旧译文");
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [item])
                { SourceRegions = [region], ExpectedLayoutCount = 1 });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);

        using (session)
        using (var timeout = new CancellationTokenSource(1200))
        {
            session.FrameReady += (_, _) => { published++; phase = 1; };
            session.FrameInvalidated += (_, _) => invalidated = true;
            session.CaptureDecision += (_, decision) =>
                waiting |= decision.Kind == "waiting_for_stability";
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (published != 1 || calls != 1 || !invalidated || !waiting)
            throw new Exception("Motion across the changed glyph published stale text or skipped invalidation.");
    }

    public static void OneChangedGlyphPixelCannotPassViewportCommit()
    {
        using var original = new Bitmap(300, 150);
        using var changed = new Bitmap(original);
        changed.SetPixel(240, 50, Color.White);
        var source = new LiveTranslationPixelSnapshot(original);
        var current = new LiveTranslationPixelSnapshot(changed);
        var (region, item) = Content("A", "旧译文");
        var frame = new LiveTranslationFrame("Machine", "completed", [item])
            { SourceRegions = [region], ExpectedLayoutCount = 1 };
        var candidate = LiveTranslationRegionWitness.Create(source, [item]);

        var commit = LiveTranslationViewportCommitPolicy.Commit(
            source, current, [region], candidate, frame);
        if (commit.Witness.Count != 0
            || LiveTranslationViewportResultPolicy.CanPresent(commit.ResultState))
            throw new Exception("A one-pixel glyph edit was accepted under the old area-based tolerance.");
    }

    public static void IncidentalChangeWithoutWordGeometryCannotAdvanceWitness()
    {
        using var original = new Bitmap(64, 64);
        using var changed = new Bitmap(original);
        // A thin new glyph and a caret have the same pixel shape here. Without
        // word geometry the source identity is ambiguous, so clear the item.
        using (var graphics = Graphics.FromImage(changed))
            graphics.FillRectangle(Brushes.White, 10, 10, 1, 28);
        var bounds = new PixelRect(4, 4, 50, 40);
        var block = new OcrTextBlock("body", bounds, []);
        var region = new VisualTextRegion("body", "body", [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var item = new InPlaceLayoutItem(block, "旧译文", bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
            SourceRegion: region);
        var witness = LiveTranslationRegionWitness.Create(new LiveTranslationPixelSnapshot(original), [item]);

        if (witness.AdvanceIncidentalChange(new LiveTranslationPixelSnapshot(changed)).Count != 0)
            throw new Exception("A caret-shaped edit with no word geometry advanced a stale witness.");
    }

    private static (VisualTextRegion Region, InPlaceLayoutItem Item) Content(string source, string translation)
    {
        var bounds = new PixelRect(30, 30, 220, 70);
        var block = new OcrTextBlock(source, bounds,
            [new OcrWord(source, new PixelRect(230, 45, 18, 20))]);
        var region = new VisualTextRegion(source, source, [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var item = new InPlaceLayoutItem(block, translation, bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
            SourceRegion: region);
        return (region, item);
    }
}

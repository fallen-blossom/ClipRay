using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveHoverTests
{
    public static void BackgroundHoverDoesNotScheduleTextAgain()
    {
        using var before = Picture(0);
        using var hovered = Picture(12);
        if (LiveTranslationChangePlanner.Plan(before, hovered, []).Count != 0)
            throw new Exception("A small background hover scheduled unchanged glyphs again.");
        using var changed = Picture(12);
        changed.SetPixel(15, 15, Color.Black);
        if (LiveTranslationChangePlanner.Plan(before, changed, []).Count == 0)
            throw new Exception("Background tolerance swallowed a changed text stroke.");
    }

    public static void HoverKeepsTranslationAndUpdatesBacking()
    {
        var hover = false;
        var calls = 0;
        var invalidated = false;
        var sawHoverBacking = false;
        var bounds = new PixelRect(8, 8, 36, 24);
        var block = new OcrTextBlock("not 18", bounds, []);
        using var session = new LiveTranslationSession(new(0, 0, 64, 64), _ => Picture(hover ? 12 : 0),
            (_, _) => {
                calls++;
                return Task.FromResult(new LiveTranslationFrame("Machine", "completed",
                    [new(block, "不是18", bounds, InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White)]));
            }, TranslationRouteKind.Machine, validateLayoutRegions: true);
        session.FrameInvalidated += (_, _) => invalidated = true;
        session.FrameReady += (_, args) => {
            if (args.Frame.LayoutItems.Count != 1 || args.Frame.LayoutItems[0].Text != "不是18")
                throw new Exception("Hover removed or replaced stable translation.");
            if (!hover) hover = true;
            else {
                sawHoverBacking = args.Frame.LayoutItems[0].BackgroundColor.R == 12;
                session.Stop();
            }
        };
        using var timeout = new CancellationTokenSource(1800);
        session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (calls != 1 || invalidated || !sawHoverBacking)
            throw new Exception("Hover did not retain the first translation with refreshed background.");
    }

    public static void CaretBlinkDoesNotClearOrRetranslateBody()
    {
        var phase = 0;
        var caretCaptures = 0;
        var calls = 0;
        var invalidated = false;
        using var stop = new CancellationTokenSource(1200);
        // A real blink returns to the original pixels. A persistent thin stroke
        // outside the old OCR word box is a separate, ambiguous case below.
        var block = new OcrTextBlock("body", new PixelRect(4,4,50,40),
            [new OcrWord("body", new PixelRect(4,4,45,3))]);
        var region = new VisualTextRegion("body", "body", [block], block.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        using var session = new LiveTranslationSession(new(0,0,64,64), _ =>
        {
            var image = new Bitmap(64,64);
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Black);
            graphics.FillRectangle(Brushes.White, 4, 4, 45, 3);
            if (Volatile.Read(ref phase) == 1
                && Interlocked.Increment(ref caretCaptures) <= 2)
                graphics.FillRectangle(Brushes.White, 10, 10, 1, 28);
            else if (Volatile.Read(ref phase) == 1)
                Interlocked.Exchange(ref phase, 2);
            return image;
        }, (_, _) =>
        {
            calls++;
            var item = new InPlaceLayoutItem(block, "正文", block.Bounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [item]) { SourceRegions=[region] });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);
        session.FrameInvalidated += (_, _) => invalidated = true;
        session.FrameReady += (_, _) => Interlocked.CompareExchange(ref phase, 1, 0);
        session.StartAsync(stop.Token).GetAwaiter().GetResult();
        if (calls != 1 || invalidated || Volatile.Read(ref phase) != 2 || caretCaptures < 3)
            throw new Exception("A returning one-pixel caret blink cleared or retransmitted a stable translated paragraph.");

        PersistentNarrowGlyphOutsideKnownWordClearsAndRetries();
    }

    private static void PersistentNarrowGlyphOutsideKnownWordClearsAndRetries()
    {
        var changed = 0;
        var calls = 0;
        var invalidated = false;
        var publications = new List<string>();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var bounds = new PixelRect(4, 4, 50, 40);
        var block = new OcrTextBlock("body", bounds,
            [new OcrWord("body", new PixelRect(4, 4, 45, 3))]);
        var region = new VisualTextRegion("body", "body", [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new(0, 0, 64, 64), _ =>
        {
            var image = new Bitmap(64, 64);
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Black);
            graphics.FillRectangle(Brushes.White, 4, 4, 45, 3);
            if (Volatile.Read(ref changed) != 0)
                graphics.FillRectangle(Brushes.White, 10, 10, 1, 28);
            return image;
        }, (_, _) =>
        {
            var source = Interlocked.Increment(ref calls) == 1 ? "body" : "body I";
            var currentBlock = block with { Text = source };
            var currentRegion = region with { SourceText = source, Lines = [currentBlock] };
            var item = new InPlaceLayoutItem(currentBlock,
                source == "body" ? "旧译文" : "新译文", bounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
                SourceRegion: currentRegion);
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [item])
                { SourceRegions = [currentRegion], ExpectedLayoutCount = 1 });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(3500))
        {
            session.FrameInvalidated += (_, _) => invalidated = true;
            session.FrameReady += (_, args) =>
            {
                publications.Add(args.Frame.LayoutItems.Single().Text);
                if (Interlocked.CompareExchange(ref changed, 1, 0) != 0) session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (!invalidated || calls != 2
            || !publications.SequenceEqual(["旧译文", "新译文"])
            || elapsed.Elapsed > TimeSpan.FromSeconds(3))
            throw new Exception("A persistent narrow glyph outside known word boxes kept a stale translation or missed the three-second refresh budget.");
    }

    public static void CaretShapedChangeInsideWordClearsAndRetries()
    {
        var changed = false;
        var calls = 0;
        var invalidated = false;
        var publications = new List<string>();
        var bounds = new PixelRect(4, 4, 50, 40);
        var block = new OcrTextBlock("body", bounds,
            [new OcrWord("body", new PixelRect(4, 4, 45, 35))]);
        var region = new VisualTextRegion("body", "body", [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new(0, 0, 64, 64), _ =>
        {
            var image = new Bitmap(64, 64);
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Black);
            graphics.FillRectangle(Brushes.White, 4, 4, 45, 3);
            if (changed) graphics.FillRectangle(Brushes.White, 10, 10, 1, 28);
            return image;
        }, (_, _) =>
        {
            var text = ++calls == 1 ? "旧译文" : "新译文";
            var currentBlock = changed
                ? block with { Text = "body changed" }
                : block;
            var currentRegion = changed
                ? region with { SourceText = "body changed", Lines = [currentBlock] }
                : region;
            var item = new InPlaceLayoutItem(currentBlock, text, bounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
                SourceRegion: currentRegion);
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [item])
                { SourceRegions = [currentRegion], ExpectedLayoutCount = 1 });
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(2500))
        {
            session.FrameInvalidated += (_, _) => invalidated = true;
            session.FrameReady += (_, args) =>
            {
                publications.Add(args.Frame.LayoutItems.Single().Text);
                if (!changed) changed = true;
                else session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (!invalidated || calls != 2 || !publications.SequenceEqual(["旧译文", "新译文"]))
            throw new Exception("A caret-shaped edit inside text evidence kept or failed to refresh the old translation.");
    }

    private static Bitmap Picture(int backing)
    {
        var image = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(backing, backing, backing));
        graphics.FillRectangle(Brushes.White, 12, 12, 4, 16);
        graphics.FillRectangle(Brushes.White, 12, 12, 24, 3);
        return image;
    }
}

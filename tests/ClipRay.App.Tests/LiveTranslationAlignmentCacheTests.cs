using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using System.Drawing;

internal static class LiveTranslationAlignmentCacheTests
{
    public static void UniformSourceGainingMeasuredStyleRequestsFreshAlignment()
    {
        using var uniform = MixedFontStyleTests.CreateSource(out var uniformLine, out _, mixed: false);
        using var styled = MixedFontStyleTests.CreateSource(out var styledLine, out var styledWords);
        var ordinaryRegion = VisualTextRegionBuilder.Build([uniformLine], uniform).Single();
        var styledRegion = VisualTextRegionBuilder.Build(OcrStyleGeometryRefiner.Refine([styledLine], styledWords), styled).Single();
        var ordinary = VisualTranslationRequestFactory.Create([ordinaryRegion], "zh", DeepSeekModels.Flash).Single();
        var annotated = VisualTranslationRequestFactory.Create([styledRegion], "zh", DeepSeekModels.Flash).Single();
        if (ordinary.OcrText != annotated.OcrText || ordinary.SourceSpans.Count != 0 || annotated.SourceSpans.Count == 0)
            throw new Exception("The production Builder fixture did not change uniform text into a measured mixed-size annotation.");
        var calls = 0;
        var cache = new LiveTranslationRequestCache((route, requests, _) =>
        {
            calls++;
            return Task.FromResult(new TranslationBatchOutcome(requests.Select(request =>
                new TranslationResult(true, MixedFontStyleTests.TargetText, "controlled-model")
                {
                    TextAlignments = request.SourceSpans.Select(span =>
                        new TranslationTextAlignment(span.Start, span.Length, 0, 1,
                            Evidence: TranslationStyleAlignmentEvidence.ModelSemanticSpan)).ToArray(),
                }).ToArray(), new(route, 0, 0, 0)));
        });
        TranslationResult Read(TranslationRequest request) => cache.TranslateAsync(TranslationRouteKind.Machine,
            [request], default).GetAwaiter().GetResult().Results.Single();
        var first = Read(ordinary);
        var second = Read(annotated);
        var repeatedOrdinary = Read(ordinary);
        var repeatedStyled = Read(annotated);
        if (calls != 2 || first.TextAlignments.Count != 0 || repeatedOrdinary.TextAlignments.Count != 0
            || second.TextAlignments.Count == 0 || !repeatedStyled.TextAlignments.SequenceEqual(second.TextAlignments))
            throw new Exception("A uniform semantic cache hit suppressed the first request for actual source-style alignment.");
    }

    public static void StyledWhitespaceUsesExactUtf16SourceVersion()
    {
        var cache = Cache(out var count);
        var spaced = Request("A  😀 B", "s0");
        var normalized = Request("A 😀 B", "s0");
        var first = Read(cache, spaced);
        var second = Read(cache, normalized);
        var again = Read(cache, spaced);
        if (count() != 2 || first.TextAlignments.Single().SourceStart != spaced.OcrText.IndexOf('B')
            || second.TextAlignments.Single().SourceStart != normalized.OcrText.IndexOf('B')
            || !again.TextAlignments.SequenceEqual(first.TextAlignments))
            throw new Exception("A styled cache result reused source offsets from another whitespace/UTF-16 input.");
    }

    public static void SpanIdsRangesTextAndStructureDefineAlignmentIdentity()
    {
        var cache = Cache(out var count);
        const string source = "red green blue";
        var request = new TranslationRequest("en", "zh", source, textKind: TranslationTextKind.Body)
        { SourceSpans = [new("s0", 0, 3, "red")] };
        Read(cache, request);
        Read(cache, request with { SourceSpans = [new("s1", 0, 3, "red")] });
        Read(cache, request with { SourceSpans = [new("s0", 0, 3, "RED")] });
        var green = request with { SourceSpans = [new("s0", 4, 5, "green")] };
        Read(cache, green);
        Read(cache, green with { SourceSpans = [new("s0", 4, 5, "green")] }); // Equal values, new list/record instances.
        var two = request with { SourceSpans = [new("s0", 0, 3, "red"), new("s1", 4, 5, "green")] };
        Read(cache, two);
        Read(cache, two with { SourceSpans = two.SourceSpans.Reverse().ToArray() });
        if (count() != 6)
            throw new Exception("Alignment cache identity ignored span IDs, ranges, count/order, or compared list references instead of values.");
    }

    public static void PlainSemanticHitsNeverReturnAnotherSourcesAlignments()
    {
        foreach (var reverse in new[] { false, true })
        {
            var cache = Cache(out var count);
            var precise = new TranslationRequest("en", "zh", reverse ? "A 😀 B" : "A  😀 B", textKind: TranslationTextKind.Body);
            var alias = new TranslationRequest("EN", "ZH", reverse ? "A  😀 B" : "A 😀 B", textKind: TranslationTextKind.Body);
            var batch = cache.TranslateAsync(TranslationRouteKind.Machine, [precise, alias], default).GetAwaiter().GetResult();
            var cachedAlias = Read(cache, alias);
            var exactAgain = Read(cache, precise);
            if (count() != 1 || batch.Results[0].TextAlignments.Single().SourceStart != precise.OcrText.IndexOf('B')
                || batch.Results[1].TextAlignments.Count != 0 || cachedAlias.TextAlignments.Count != 0
                || exactAgain.TextAlignments.Single().SourceStart != precise.OcrText.IndexOf('B')
                || batch.Results[1].Text != batch.Results[0].Text)
                throw new Exception("Plain semantic dedup leaked UTF-16 mappings across whitespace or destroyed the original exact-version mapping.");
        }
    }

    public static void CropRetainsPrimaryRecognitionEvidenceCoordinates()
    {
        var primaryBounds = new PixelRect(2, 3, 80, 22);
        var primary = new OcrWord("canread", primaryBounds);
        var repaired = new OcrTextBlock("can read", primaryBounds,
            [new("can", new(2, 3, 30, 22)), new("read", new(38, 3, 44, 22))])
        { PrimaryRecognitionText = "canread", PrimaryRecognitionWords = [primary], WhitespaceRepair = "windows-word-boundaries",
            SelectionBounds = new(0, 0, 90, 30) };
        var region = new VisualTextRegion("r0", repaired.Text, [repaired], repaired.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High) { OriginalOcrWords = [primary] };
        var item = new InPlaceLayoutItem(repaired, "能阅读", repaired.Bounds, InPlaceBackgroundMode.FlatSampled,
            Color.White, Color.Black, SourceRegion: region);
        var shifted = LiveTranslationFrameCoordinates.Offset(new("Machine", "completed", [item])
        { SourceRegions = [region], ExpectedLayoutRegions = [region] }, 100, 200);
        var expected = new PixelRect(102, 203, 80, 22);
        var shiftedBlock = shifted.LayoutItems.Single().SourceBlock;
        if (shiftedBlock.PrimaryRecognitionWords.Single().Bounds != expected
            || shifted.SourceRegions.Single().Lines.Single().PrimaryRecognitionWords.Single().Bounds != expected
            || shifted.SourceRegions.Single().OriginalOcrWords.Single().Bounds != expected
            || shifted.ExpectedLayoutRegions!.Single().Lines.Single().PrimaryRecognitionWords.Single().Bounds != expected
            || shiftedBlock.PrimaryRecognitionText != "canread" || shiftedBlock.WhitespaceRepair != "windows-word-boundaries"
            || shiftedBlock.SelectionBounds != new PixelRect(100, 200, 90, 30)
            || primary.Bounds != primaryBounds)
            throw new Exception("A crop offset lost or moved original primary-recognition evidence more than once.");
    }

    private static TranslationRequest Request(string text, string id) =>
        new("en", "zh", text, textKind: TranslationTextKind.Body)
        { SourceSpans = [new(id, text.IndexOf('B'), 1, "B")] };
    private static TranslationResult Read(LiveTranslationRequestCache cache, TranslationRequest request) =>
        cache.TranslateAsync(TranslationRouteKind.Machine, [request], default).GetAwaiter().GetResult().Results.Single();
    private static LiveTranslationRequestCache Cache(out Func<int> count)
    {
        var calls = 0;
        count = () => calls;
        return new((route, requests, _) =>
        {
            calls++;
            return Task.FromResult(new TranslationBatchOutcome(requests.Select(request =>
                new TranslationResult(true, "甲乙", "controlled-model")
                { TextAlignments = [new(request.SourceSpans.Count > 0 ? request.SourceSpans[0].Start : request.OcrText.IndexOf('B'),
                    request.SourceSpans.Count > 0 ? request.SourceSpans[0].Length : 1, 1, 1)] }).ToArray(), new(route, 0, 0, 0)));
        });
    }
}

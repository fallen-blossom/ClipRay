using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class TranslationReadingFallbackTests
{
    public static void OnlyMissingSuccessfulTranslationsAreReadable()
    {
        var bounds = new PixelRect(0, 0, 120, 30);
        var block = new OcrTextBlock("source", bounds, []);
        var regions = Enumerable.Range(0, 5).Select(index => new VisualTextRegion(
            index.ToString(), "source", [block], bounds, VisualTextRole.Body,
            VisualStructureConfidence.High)).ToArray();
        TranslationResult[] results = [new(true, "visible", "completed"), new(true, "readable", "completed"),
            new(false, "failure detail", "failed"), new(true, "preserved", "source-preserved"),
            new(true, "source", "completed")];
        var item = new InPlaceLayoutItem(block, "visible", bounds, InPlaceBackgroundMode.FlatSampled,
            System.Drawing.Color.White, System.Drawing.Color.Black, SourceRegion: regions[0]);
        var outcome = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Layout,
            "partial", [block], regions, results, [item]);
        var fallback = TranslationReadingFallback.Create(outcome);
        if (fallback.Count != 1 || fallback.Text != "readable")
            throw new Exception("Reader included failures, preserved source or already rendered translations.");
        fallback = TranslationReadingFallback.Create(outcome with { LayoutItems = [] });
        if (fallback.Count != 2 || !fallback.Text.Contains("readable"))
            throw new Exception("A single rejected region or an entirely empty layout lost its successful text.");
        fallback = TranslationReadingFallback.Create(outcome with { Results = results.Take(1).ToArray() });
        if (fallback.Count != 0) throw new Exception("Unbound positional results were shown as translations.");
    }
}

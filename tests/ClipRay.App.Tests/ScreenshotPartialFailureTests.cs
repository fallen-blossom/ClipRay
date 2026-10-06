using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using ClipRay.App.Acceptance;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class ScreenshotPartialFailureTests
{
    public static void RealXFailureKeepsUntranslatedPixels(string imagePath)
    {
        const string expectedSha256 = "bf9b8123b434c0e98db2cd2a0641b36b0738e6e5ac45176d60500f9da3176c8a";
        var actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath)))
            .ToLowerInvariant();
        if (actualSha256 != expectedSha256)
            throw new Exception("The partial-failure input does not match the recorded X image.");
        using var source = new Bitmap(imagePath);
        const string successText = "found the perfect use case for @typesafeai Jev:";
        const string failedText = "in 2026, why is compaction still a summarization";
        var translatedBounds = new PixelRect(679, 269, 443, 23);
        var failedBounds = new PixelRect(675, 373, 463, 30);
        var translatedBlock = new OcrTextBlock(successText, translatedBounds,
            [new(successText, translatedBounds)]);
        var failedBlock = new OcrTextBlock(failedText, failedBounds,
            [new(failedText, failedBounds)]);
        var regions = new[]
        {
            new VisualTextRegion("translated", successText, [translatedBlock], translatedBounds,
                VisualTextRole.Body, VisualStructureConfidence.High),
            new VisualTextRegion("failed", failedText, [failedBlock], failedBounds,
                VisualTextRole.Body, VisualStructureConfidence.High),
        };
        var results = new[]
        {
            new TranslationResult(true, "找到了 @typesafeai Jev 的完美用例：", "completed"),
            new TranslationResult(false, string.Empty, "provider-failed"),
        };
        var plan = InPlaceLayoutService.BuildRegionPlan(source, regions, results);
        var outcome = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Translation,
            "translation-failed", [translatedBlock, failedBlock], regions, results, plan.Items,
            plan.Rejections);
        if (!TranslationAcceptancePolicy.HasDisplayableResult(outcome)
            || plan.Items.Count != 1 || plan.Items[0].SourceRegion?.Id != "translated")
            throw new Exception("One provider failure discarded a different region's translation.");

        using var rendered = TranslatedSelectionRenderer.Render(source,
            new PixelRect(0, 0, source.Width, source.Height), plan.Items);
        var changedSuccessPixels = 0;
        for (var y = (int)translatedBounds.Top; y < translatedBounds.Bottom; y++)
        for (var x = (int)translatedBounds.Left; x < translatedBounds.Right; x++)
            if (rendered.GetPixel(x, y) != source.GetPixel(x, y)) changedSuccessPixels++;
        if (changedSuccessPixels == 0)
            throw new Exception("The successful X region was not translated in the rendered image.");
        for (var y = (int)failedBounds.Top; y < failedBounds.Bottom; y++)
        for (var x = (int)failedBounds.Left; x < failedBounds.Right; x++)
            if (rendered.GetPixel(x, y) != source.GetPixel(x, y))
                throw new Exception("A failed X region lost its original English pixels.");
        Console.WriteLine($"REAL_X_PARTIAL successful_changed_pixels={changedSuccessPixels} "
            + "failed_region_unchanged=true");
    }
}

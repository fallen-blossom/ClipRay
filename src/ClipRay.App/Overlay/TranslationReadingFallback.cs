using ClipRay.App.Translation;

namespace ClipRay.App.Overlay;

/// <summary>Only successful, changed translations absent from the rendered layer.</summary>
public sealed record TranslationReadingFallback(int Count, string Text)
{
    public static TranslationReadingFallback Create(ApplicationTranslationOutcome outcome)
    {
        // Positional provider results are only meaningful with the complete region binding.
        if (outcome.Results.Count != outcome.Regions.Count) return new(0, string.Empty);
        var texts = new List<string>();
        for (var index = 0; index < outcome.Regions.Count; index++)
        {
            var region = outcome.Regions[index];
            var result = outcome.Results[index];
            if (result.SemanticOutcome != TranslationSemanticOutcome.Translated
                || string.IsNullOrWhiteSpace(result.Text)
                || string.Equals(region.SourceText.Trim(), result.Text.Trim(), StringComparison.Ordinal)) continue;
            if (outcome.LayoutItems.Any(item => item.SourceRegion?.Id == region.Id
                && string.Equals(item.Text.Trim(), result.Text.Trim(), StringComparison.Ordinal))) continue;
            texts.Add(result.Text.Trim());
        }
        return new(texts.Count, string.Join(Environment.NewLine + Environment.NewLine, texts));
    }
}

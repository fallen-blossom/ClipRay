using ClipRay.App.Translation;

namespace ClipRay.App.Diagnostics;

public sealed record TranslationRunClassification(
    int TranslatedCount,
    int PreservedCount,
    int ProviderFailedCount,
    int LayoutRejectedCount);

public static class TranslationRunClassifier
{
    public static bool TryClassify(
        ApplicationTranslationOutcome outcome,
        out TranslationRunClassification classification)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Regions.Count != outcome.Results.Count)
        {
            classification = new TranslationRunClassification(0, 0, 0, 0);
            return false;
        }

        classification = Classify(outcome);
        return true;
    }

    public static TranslationRunClassification Classify(ApplicationTranslationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Regions.Count != outcome.Results.Count)
        {
            throw new ArgumentException(
                "Each visual region must retain one translation result.",
                nameof(outcome));
        }

        var preserved = outcome.Regions.Zip(outcome.Results).Count(static pair =>
            pair.Second.IsSuccess
            && (string.IsNullOrWhiteSpace(pair.Second.Text)
                || string.Equals(
                    pair.First.SourceText.Trim(),
                    pair.Second.Text.Trim(),
                    StringComparison.Ordinal)));
        var providerFailed = outcome.Results.Count(static result => !result.IsSuccess);
        var layoutRejected = outcome.LayoutRejections?
            .Select(static rejection => rejection.Region)
            .Distinct()
            .Count() ?? 0;
        return new TranslationRunClassification(
            outcome.LayoutItems.Count,
            preserved,
            providerFailed,
            layoutRejected);
    }
}

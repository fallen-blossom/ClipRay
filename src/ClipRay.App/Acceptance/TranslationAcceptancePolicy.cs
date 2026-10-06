using ClipRay.App.Translation;
using ClipRay.App.LiveTranslation;

namespace ClipRay.App.Acceptance;

public static class TranslationAcceptancePolicy
{
    public static string LiveErrorCategory(LiveTranslationSession session, LiveTranslationFrame latest) =>
        session.State == LiveTranslationSessionState.StoppedAfterConsecutiveFailures
            ? "Live:consecutive-failures" : latest.ErrorCategory;

    public static string ErrorCategory(ApplicationTranslationOutcome outcome) => outcome.IsSuccess
        ? "none"
        : $"{outcome.FailureStage}:partial-or-failed";

    public static bool HasDisplayableResult(ApplicationTranslationOutcome outcome) =>
        outcome.FailureStage is not (TranslationPipelineStage.Ocr or TranslationPipelineStage.Scope
            or TranslationPipelineStage.Credential)
        && (outcome.IsSuccess || outcome.LayoutItems.Count > 0);

    public static int ExpectedLayoutCount(ApplicationTranslationOutcome outcome) =>
        outcome.Regions.Zip(outcome.Results).Count(static pair => IsExpectedLayout(pair.First, pair.Second));

    public static int ExpectedPrimaryLayoutCount(ApplicationTranslationOutcome outcome) =>
        outcome.Regions.Zip(outcome.Results).Count(static pair =>
            IsPrimary(pair.First) && IsExpectedLayout(pair.First, pair.Second));

    public static double ExpectedPrimaryLayoutArea(ApplicationTranslationOutcome outcome) =>
        outcome.Regions.Zip(outcome.Results).Where(static pair =>
                IsPrimary(pair.First) && IsExpectedLayout(pair.First, pair.Second))
            .Sum(static pair => pair.First.Bounds.Width * pair.First.Bounds.Height);

    private static bool IsPrimary(VisualTextRegion region) =>
        region.Role is VisualTextRole.Body or VisualTextRole.Heading;

    private static bool IsExpectedLayout(VisualTextRegion region, TranslationResult result) =>
        !result.IsSuccess
        || result.Status != "machine-preserved"
            && !string.IsNullOrWhiteSpace(result.Text)
            && !string.Equals(region.SourceText.Trim(), result.Text.Trim(), StringComparison.Ordinal);
}

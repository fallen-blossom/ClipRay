using ClipRay.App.Translation;

namespace ClipRay.TestLab;

public sealed record TestLabEvidenceAssessment(
    string StructuralExpectation,
    string ContentQuality,
    string VisualReadability)
{
    public static TestLabEvidenceAssessment Create(
        bool expectationMet,
        bool artifactWritten,
        ApplicationTranslationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var contentQuality = outcome.Regions.Count == 0
            ? "unavailable"
            : outcome.Results.Count != outcome.Regions.Count
                || outcome.Results.Any(static result => !result.IsSuccess)
                ? "known-incomplete"
                : "unverified";
        var visualReadability = !artifactWritten
            ? "unavailable"
            : outcome.FailureStage == TranslationPipelineStage.Layout
                ? "known-incomplete"
                : "unreviewed";
        return new TestLabEvidenceAssessment(
            expectationMet && artifactWritten ? "met" : "not-met",
            contentQuality,
            visualReadability);
    }

    public static TestLabEvidenceAssessment InputFailure() =>
        new("not-met", "unavailable", "unavailable");
}

public static class TestLabRunStatus
{
    public static string From(IReadOnlyList<TestLabCaseReport> cases)
    {
        var routes = cases.SelectMany(static item => item.Routes).ToArray();
        if (routes.Any(static route => route.Oracle?.Findings.Any(
                finding => finding.Code != "meaning-signal-missing") == true))
            return "quality-failed";
        if (routes.Any(static route => route.Oracle?.Findings.Count > 0))
            return "quality-needs-review";
        return routes.Length > 0 && routes.All(static route => route.ExpectationMet)
            ? "expectation-met" : "failed";
    }
}

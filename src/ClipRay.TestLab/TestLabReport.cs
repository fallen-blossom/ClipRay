using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using ClipRay.App.Translation;

namespace ClipRay.TestLab;

public static class TestLabExpectation
{
    public static bool IsMet(string expectation, ApplicationTranslationOutcome outcome,
        bool allowIntentionalPreservation = false)
    {
        if (expectation == "all-translated")
        {
            return outcome.IsSuccess
                && outcome.Regions.Count > 0
                && outcome.Results.Count == outcome.Regions.Count
                && outcome.Results.All(static result => result.IsSuccess)
                && (allowIntentionalPreservation
                    || !(outcome.LayoutRejections?.Any(static item =>
                        item.Reason == "identity-pixels-preserved") ?? false))
                && InPlaceLayoutService.HasCompleteCoverage(outcome.Regions, outcome.Results,
                    outcome.LayoutItems, outcome.LayoutRejections);
        }
        if (expectation != "protected-fallback" || outcome.IsSuccess) return false;
        return outcome.Results.Any(static result => result.Diagnostic?.Kind is
            TranslationDiagnosticKind.ProtectedContentChanged
            or TranslationDiagnosticKind.ProtectedAccountChanged
            or TranslationDiagnosticKind.ProtectedUrlChanged
            or TranslationDiagnosticKind.ProtectedNumericChanged
            or TranslationDiagnosticKind.ProtectedOtherChanged);
    }
}

public sealed record TestLabChunkReport(
    int Chunk,
    int CharacterCount,
    string Stage,
    string ResultCategory,
    int ExpectedProtectedTokenCount,
    int ReturnedProtectedTokenCount);

public sealed record TestLabFailureReport(
    int Result,
    string Category,
    string Diagnostic,
    string Stage,
    int? RetryChunk,
    string? SourceLanguage = null,
    int? SourceCharacterCount = null,
    int? SourceLineCount = null,
    string? SourceRole = null,
    string? LocalLmtInputMode = null,
    string? IntegrityBoundary = null,
    int? ExpectedProtectedTokenCount = null,
    int? RestoredProtectedTokenCount = null,
    int? SourceInvariantNumberCount = null,
    int? TranslatedInvariantNumberCount = null,
    int? ModelResponseCharacterCount = null,
    int? MaximumAllowedResponseCharacterCount = null,
    bool? ModelResponseContainsSourceText = null);

public sealed record TestLabDetailTraceReport(
    string Path,
    bool Complete,
    string? Problem = null);

public sealed record TestLabRouteReport(
    string Route,
    bool IsSuccess,
    bool ExpectationMet,
    string FailureStage,
    string ResultCategory,
    int RegionCount,
    int TranslatedCount,
    int RetainedCount,
    string AfterPath,
    IReadOnlyList<TestLabChunkReport> Chunks,
    IReadOnlyList<TestLabFailureReport>? Failures = null,
    IReadOnlyList<InPlaceLayoutRejection>? LayoutRejections = null,
    TestLabEvidenceAssessment? Evidence = null,
    TestLabOracleAssessment? Oracle = null,
    IReadOnlyList<string>? OcrSources = null,
    TestLabDetailTraceReport? DetailTrace = null);

public sealed record TestLabCaseReport(
    string Id,
    string Image,
    string ImageSha256,
    string BeforePath,
    string Expectation,
    IReadOnlyList<TestLabRouteReport> Routes);

public sealed record TestLabRunReport(
    string RunId,
    DateTimeOffset StartedUtc,
    string Mode,
    string Status,
    IReadOnlyList<string> Errors,
    IReadOnlyList<TestLabCaseReport> Cases,
    int SchemaVersion = 3,
    string? AppAssemblySha256 = null,
    string? TestLabAssemblySha256 = null,
    string? LocalModelSha256 = null);

public static class TestLabReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write(string reportsRoot, TestLabRunReport report)
    {
        Directory.CreateDirectory(reportsRoot);
        File.WriteAllText(Path.Combine(reportsRoot, report.RunId + ".json"),
            JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(reportsRoot, report.RunId + ".md"), ToMarkdown(report));
    }

    private static string ToMarkdown(TestLabRunReport report)
    {
        var lines = new List<string>
        {
            "# ClipRay TestLab " + report.RunId,
            string.Empty,
            $"- mode: `{report.Mode}`",
            $"- status: `{report.Status}`",
            $"- schema-version: `{report.SchemaVersion}`",
        };
        if (report.AppAssemblySha256 is not null) lines.Add($"- app-assembly-sha256: `{report.AppAssemblySha256}`");
        if (report.TestLabAssemblySha256 is not null) lines.Add($"- testlab-assembly-sha256: `{report.TestLabAssemblySha256}`");
        if (report.LocalModelSha256 is not null) lines.Add($"- local-model-sha256: `{report.LocalModelSha256}`");
        foreach (var error in report.Errors) lines.Add($"- error: `{error}`");
        foreach (var item in report.Cases)
        {
            lines.Add(string.Empty);
            lines.Add("## " + item.Id);
            lines.Add(string.Empty);
            lines.Add($"- before: `{item.BeforePath}`");
            foreach (var route in item.Routes)
            {
                lines.Add($"- {route.Route}: `{route.ResultCategory}`, after `{route.AfterPath}`, translated {route.TranslatedCount}, retained {route.RetainedCount}");
                if (route.DetailTrace is { } trace)
                    lines.Add($"  - detail-trace: `{trace.Path}`, evidence={(trace.Complete ? "complete" : "incomplete")}" +
                        (trace.Problem is null ? string.Empty : $", problem={trace.Problem}"));
                if (route.Evidence is { } evidence)
                    lines.Add($"  - structural-expectation={evidence.StructuralExpectation}, content-quality={evidence.ContentQuality}, visual-readability={evidence.VisualReadability}");
                if (route.OcrSources is { Count: > 0 })
                    lines.Add($"  - ocr-sources: {string.Join(", ", route.OcrSources)}");
                if (route.Oracle is { } oracle)
                {
                    lines.Add($"  - oracle-sha256={oracle.OracleSha256}, content={oracle.ContentQuality}, layout={oracle.LayoutQuality}");
                    foreach (var finding in oracle.Findings)
                        lines.Add($"    - region={finding.Region}, stage={finding.Stage}, code={finding.Code}, expected={JsonSerializer.Serialize(finding.Expected)}, actual={JsonSerializer.Serialize(finding.Actual)}");
                }
                foreach (var chunk in route.Chunks)
                {
                    lines.Add($"  - chunk={chunk.Chunk}, chars={chunk.CharacterCount}, stage={chunk.Stage}, result={chunk.ResultCategory}, protected={chunk.ReturnedProtectedTokenCount}/{chunk.ExpectedProtectedTokenCount}");
                }
                foreach (var failure in route.Failures ?? [])
                {
                    lines.Add($"  - result={failure.Result}, category={failure.Category}, diagnostic={failure.Diagnostic}, stage={failure.Stage}, retry-chunk={failure.RetryChunk?.ToString() ?? "none"}, source={failure.SourceLanguage ?? "unknown"}/{failure.SourceCharacterCount?.ToString() ?? "unknown"} chars/{failure.SourceLineCount?.ToString() ?? "unknown"} lines/{failure.SourceRole ?? "unknown"}");
                }
                foreach (var rejection in route.LayoutRejections ?? [])
                {
                    lines.Add($"  - region={rejection.Region}, layout={rejection.Reason}");
                }
            }
        }
        lines.Add(string.Empty);
        lines.Add("> 结构预期匹配不证明 OCR、译文语义或视觉可读性；未评估项保留原状。");
        lines.Add("> 离屏视觉工件不替代真实前台 Desktop 验收。");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static string Categorize(ApplicationTranslationOutcome outcome)
    {
        if (outcome.IsSuccess) return "completed";
        if (outcome.Status == "runtime-failed") return "runtime-failed";
        if (outcome.Status == "layout-incomplete") return "layout-incomplete";
        if (outcome.Status == "layout-failed") return "layout-failed";
        if (outcome.Results.Any(static result => result.Status == "translation-pending")) return "provider-pending";
        if (outcome.Results.Any(static result => result.Status == "credential-missing")) return "credential-missing";
        if (outcome.Results.Any(static result => result.Status == "network-failed")) return "network-failed";
        if (outcome.Results.Any(static result => result.Status == "Translation timed out."
            || result.Status == "Visual translation timed out; original text was retained.")) return "timeout";
        if (outcome.Results.Any(static result => result.Status.Contains("401", StringComparison.Ordinal))) return "authentication-failed";
        if (outcome.Results.Any(static result => result.Status.Contains("429", StringComparison.Ordinal))) return "rate-limited";
        if (outcome.Results.Any(static result => result.Status == "DeepSeek changed protected translation content."))
            return "protected-token-missing";
        if (outcome.Results.Any(static result => result.Status == "DeepSeek returned no translation blocks."
            || result.Status == "DeepSeek returned an invalid translation block response."
            || result.Status == "DeepSeek response was truncated at the output limit."
            || result.Status == "DeepSeek batch recovery reached its bounded attempt limit.")) return "model-output";
        if (outcome.Results.Any(static result => result.Status == "DeepSeek provider internal failure.")) return "provider-internal";
        if (outcome.Results.Any(static result => result.Status.StartsWith("DeepSeek returned", StringComparison.Ordinal)
            || result.Status.StartsWith("DeepSeek added", StringComparison.Ordinal))) return "semantic-validation";
        if (outcome.Results.SelectMany(static result => result.ChunkDiagnostics ?? [])
            .Any(static item => item.ResultCategory == "chunking-failed")) return "chunking-failed";
        var diagnostic = outcome.Results.Select(static result => result.Diagnostic).FirstOrDefault(static item => item is not null);
        if (diagnostic is not null) return diagnostic.Kind switch
        {
            TranslationDiagnosticKind.ModelUnavailable => "model-unavailable",
            TranslationDiagnosticKind.ProcessStartupFailed => "model-process-startup",
            TranslationDiagnosticKind.ProcessSessionExited => "model-process-output",
            TranslationDiagnosticKind.ProcessFailure => "model-process-output",
            TranslationDiagnosticKind.ResponseBoundaryLost or TranslationDiagnosticKind.ResetBoundaryLost => "model-process-output",
            TranslationDiagnosticKind.EmptyTranslation or TranslationDiagnosticKind.PromptEcho => "model-output",
            TranslationDiagnosticKind.SourceUnchanged or TranslationDiagnosticKind.SourceLanguageOnly
                or TranslationDiagnosticKind.MissingTargetLanguage => "semantic-validation",
            TranslationDiagnosticKind.ProtectedContentChanged or TranslationDiagnosticKind.ProtectedAccountChanged
                or TranslationDiagnosticKind.ProtectedUrlChanged or TranslationDiagnosticKind.ProtectedNumericChanged
                or TranslationDiagnosticKind.ProtectedOtherChanged => "protected-token-missing",
            TranslationDiagnosticKind.NumericInvariantChanged or TranslationDiagnosticKind.ModelTokenInvariantChanged
                or TranslationDiagnosticKind.HotkeyInvariantChanged => "semantic-validation",
            _ => "translation-failed",
        };
        if (outcome.Results.Any(static result => !result.IsSuccess
            && !string.Equals(result.Status, "translation-failed", StringComparison.Ordinal)))
            return "unclassified-provider-failure";
        return outcome.FailureStage switch
        {
            TranslationPipelineStage.Ocr => "ocr-failed",
            TranslationPipelineStage.Scope => "ocr-empty",
            TranslationPipelineStage.Layout => "layout-failed",
            _ => "translation-failed",
        };
    }

    public static IReadOnlyList<TestLabFailureReport> CreateFailureReports(
        ApplicationTranslationOutcome outcome) =>
        outcome.Results
            .Select((result, index) => (
                Result: result,
                Index: index + 1,
                Region: index < outcome.Regions.Count ? outcome.Regions[index] : null))
            .Where(static item => !item.Result.IsSuccess)
            .Select(static item => new TestLabFailureReport(
                item.Index,
                CategorizeResult(item.Result),
                item.Result.Diagnostic is null ? "none" : ToKebabCase(item.Result.Diagnostic.Kind.ToString()),
                item.Result.Diagnostic is null ? "none" : ToKebabCase(item.Result.Diagnostic.Stage.ToString()),
                item.Result.Diagnostic?.RetryChunkIndex,
                item.Region is null ? null : LocalLmtLanguage.NormalizeSource("auto", item.Region.SourceText),
                item.Region?.SourceText.Length,
                item.Region?.Lines.Count,
                item.Region?.Role.ToString().ToLowerInvariant(),
                item.Result.Diagnostic?.LocalLmtInputMode,
                item.Result.Diagnostic?.IntegrityBoundary,
                item.Result.Diagnostic?.ExpectedProtectedTokenCount,
                item.Result.Diagnostic?.RestoredProtectedTokenCount,
                item.Result.Diagnostic?.SourceInvariantNumberCount,
                item.Result.Diagnostic?.TranslatedInvariantNumberCount,
                item.Result.Diagnostic?.ModelResponseCharacterCount,
                item.Result.Diagnostic?.MaximumAllowedResponseCharacterCount,
                item.Result.Diagnostic?.ModelResponseContainsSourceText))
            .ToArray();

    private static string CategorizeResult(TranslationResult result)
    {
        if (result.Status == "credential-missing") return "credential-missing";
        if (result.Status == "network-failed") return "network-failed";
        if (result.Status == "translation-pending") return "provider-pending";
        if (result.Status.Contains("401", StringComparison.Ordinal)) return "authentication-failed";
        if (result.Status.Contains("429", StringComparison.Ordinal)) return "rate-limited";
        if (result.Status == "DeepSeek provider internal failure.") return "provider-internal";
        if (result.Status == "DeepSeek batch recovery reached its bounded attempt limit.") return "model-output";
        if (result.Diagnostic is null)
            return string.Equals(result.Status, "translation-failed", StringComparison.Ordinal)
                ? "translation-failed"
                : "unclassified-provider-failure";
        return result.Diagnostic.Kind switch
        {
            TranslationDiagnosticKind.ModelUnavailable => "model-unavailable",
            TranslationDiagnosticKind.ProcessStartupFailed => "model-process-startup",
            TranslationDiagnosticKind.ProcessSessionExited or TranslationDiagnosticKind.ProcessFailure
                or TranslationDiagnosticKind.ResponseBoundaryLost or TranslationDiagnosticKind.ResetBoundaryLost => "model-process-output",
            TranslationDiagnosticKind.EmptyTranslation or TranslationDiagnosticKind.PromptEcho => "model-output",
            TranslationDiagnosticKind.ProtectedContentChanged or TranslationDiagnosticKind.ProtectedAccountChanged
                or TranslationDiagnosticKind.ProtectedUrlChanged or TranslationDiagnosticKind.ProtectedNumericChanged
                or TranslationDiagnosticKind.ProtectedOtherChanged => "protected-token-missing",
            _ => "semantic-validation",
        };
    }

    private static string ToKebabCase(string value)
    {
        var output = new System.Text.StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character)) output.Append('-');
            output.Append(char.ToLowerInvariant(character));
        }
        return output.ToString();
    }
}

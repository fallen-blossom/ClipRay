namespace ClipRay.App.Diagnostics;

public sealed record TranslationStageTiming(
    long CaptureMilliseconds,
    long OcrMilliseconds,
    long TranslationMilliseconds,
    long LayoutMilliseconds,
    long TotalMilliseconds);

public sealed record TranslationRunLog(
    string SchemaVersion,
    string RunId,
    DateTimeOffset StartedUtc,
    string ProfileId,
    string AppVersion,
    string ExecutableSha256,
    string Route,
    string Status,
    string FailureStage,
    int SelectionWidth,
    int SelectionHeight,
    bool TouchesDisplayEdge,
    int OcrBlockCount,
    int RegionCount,
    int TranslatedCount,
    int PreservedCount,
    int ProviderFailedCount,
    int LayoutRejectedCount,
    int FallbackTranslatedCount,
    TranslationStageTiming Timing,
    string ErrorCategory,
    string OverlayDecision = "not-attempted")
{
    public const string CurrentSchemaVersion = "clipray.translation-run.v2";
}

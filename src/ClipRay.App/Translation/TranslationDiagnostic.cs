namespace ClipRay.App.Translation;

public sealed record TranslationChunkDiagnostic(
    int ChunkIndex,
    int CharacterCount,
    string Stage,
    string ResultCategory,
    int ExpectedProtectedTokenCount,
    int ReturnedProtectedTokenCount);

public enum TranslationDiagnosticKind
{
    ModelUnavailable,
    ProcessFailure,
    ProcessStartupFailed,
    ProcessSessionExited,
    ResponseBoundaryLost,
    ResetBoundaryLost,
    EmptyTranslation,
    OutputTooLong,
    PromptEcho,
    SourceUnchanged,
    SourceLanguageOnly,
    MissingTargetLanguage,
    ProtectedContentChanged,
    ProtectedAccountChanged,
    ProtectedUrlChanged,
    ProtectedNumericChanged,
    ProtectedOtherChanged,
    NumericInvariantChanged,
    ModelTokenInvariantChanged,
    HotkeyInvariantChanged,
}

public enum TranslationDiagnosticStage
{
    Initial,
    RetryChunk,
}

public sealed record TranslationDiagnostic(
    TranslationDiagnosticKind Kind,
    TranslationDiagnosticStage Stage,
    int? RetryChunkIndex = null,
    string? LocalLmtInputMode = null,
    string? IntegrityBoundary = null,
    int? ExpectedProtectedTokenCount = null,
    int? RestoredProtectedTokenCount = null,
    int? SourceInvariantNumberCount = null,
    int? TranslatedInvariantNumberCount = null,
    int? ModelResponseCharacterCount = null,
    int? MaximumAllowedResponseCharacterCount = null,
    bool? ModelResponseContainsSourceText = null);

public enum LocalLmtProcessFailureKind
{
    StartupFailed,
    SessionExited,
    ResponseBoundaryLost,
    ResetBoundaryLost,
}

public sealed class LocalLmtProcessException(LocalLmtProcessFailureKind failure) : Exception
{
    public LocalLmtProcessFailureKind Failure { get; } = failure;
}

public sealed class LocalLmtOutputException(
    TranslationDiagnosticKind kind,
    int? responseCharacterCount = null,
    int? maximumAllowedResponseCharacterCount = null,
    bool? responseContainsSourceText = null) : Exception
{
    public TranslationDiagnosticKind Kind { get; } = kind;
    public int? ResponseCharacterCount { get; } = responseCharacterCount;
    public int? MaximumAllowedResponseCharacterCount { get; } = maximumAllowedResponseCharacterCount;
    public bool? ResponseContainsSourceText { get; } = responseContainsSourceText;
}

public static class LocalLmtProcessDiagnostics
{
    public static TranslationDiagnosticKind ToDiagnosticKind(LocalLmtProcessFailureKind failure) => failure switch
    {
        LocalLmtProcessFailureKind.StartupFailed => TranslationDiagnosticKind.ProcessStartupFailed,
        LocalLmtProcessFailureKind.SessionExited => TranslationDiagnosticKind.ProcessSessionExited,
        LocalLmtProcessFailureKind.ResponseBoundaryLost => TranslationDiagnosticKind.ResponseBoundaryLost,
        LocalLmtProcessFailureKind.ResetBoundaryLost => TranslationDiagnosticKind.ResetBoundaryLost,
        _ => TranslationDiagnosticKind.ProcessFailure,
    };
}

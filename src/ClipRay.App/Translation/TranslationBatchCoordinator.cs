namespace ClipRay.App.Translation;

internal enum TranslationFailureKind
{
    None,
    MalformedJson,
    MissingIds,
    DuplicateOrUnknownIds,
    Truncated,
    InvalidItem,
    PlaceholderMismatch,
    UnchangedNaturalLanguage,
    Transport,
}
internal sealed record TranslationBatchAttempt(
    IReadOnlyList<TranslationResult> Results,
    TranslationFailureKind FailureKind,
    bool IsRecoverableStructural)
{
    public static TranslationBatchAttempt Succeeded(IReadOnlyList<TranslationResult> results) =>
        new(results, TranslationFailureKind.None, false);

    public static TranslationBatchAttempt Failed(
        int count,
        string status,
        TranslationFailureKind kind,
        bool recoverableStructural = false) =>
        new(
            Enumerable.Range(0, count)
                .Select(_ => new TranslationResult(false, string.Empty, status))
                .ToArray(),
            kind,
            recoverableStructural);
}

internal static class TranslationBatchCoordinator
{
    internal const int MaximumSplitDepth = 3;
    internal const int MaximumAttempts = 7;

    public static Task<IReadOnlyList<TranslationResult>> TranslateAsync(
        IReadOnlyList<TranslationBlock> blocks,
        Func<IReadOnlyList<TranslationBlock>, CancellationToken, Task<TranslationBatchAttempt>> attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(attempt);
        var budget = new AttemptBudget();
        return TranslateRangeAsync(blocks, attempt, depth: 0, budget, cancellationToken);
    }

    private static async Task<IReadOnlyList<TranslationResult>> TranslateRangeAsync(
        IReadOnlyList<TranslationBlock> blocks,
        Func<IReadOnlyList<TranslationBlock>, CancellationToken, Task<TranslationBatchAttempt>> attempt,
        int depth,
        AttemptBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!budget.TryTake())
        {
            return Failed(blocks.Count, budget.ExhaustedStatus());
        }

        var outcome = await attempt(blocks, cancellationToken).ConfigureAwait(false);
        budget.Observe(outcome.FailureKind);
        if (!outcome.IsRecoverableStructural
            || blocks.Count <= 1
            || depth >= MaximumSplitDepth)
        {
            return outcome.Results;
        }

        var midpoint = blocks.Count / 2;
        var leftBlocks = blocks.Take(midpoint).ToArray();
        var rightBlocks = blocks.Skip(midpoint).ToArray();
        var left = await TranslateRangeAsync(leftBlocks, attempt, depth + 1, budget, cancellationToken)
            .ConfigureAwait(false);
        var right = await TranslateRangeAsync(rightBlocks, attempt, depth + 1, budget, cancellationToken)
            .ConfigureAwait(false);
        return left.Concat(right).ToArray();
    }

    private static IReadOnlyList<TranslationResult> Failed(int count, string status) =>
        Enumerable.Range(0, count)
            .Select(_ => new TranslationResult(false, string.Empty, status))
            .ToArray();

    private sealed class AttemptBudget
    {
        private int _attempts;
        private TranslationFailureKind _lastFailureKind;

        public bool TryTake()
        {
            if (_attempts >= MaximumAttempts) return false;
            _attempts++;
            return true;
        }

        public void Observe(TranslationFailureKind failureKind)
        {
            if (failureKind != TranslationFailureKind.None) _lastFailureKind = failureKind;
        }

        public string ExhaustedStatus() => _lastFailureKind switch
        {
            TranslationFailureKind.Truncated => "DeepSeek response was truncated at the output limit.",
            TranslationFailureKind.MalformedJson or TranslationFailureKind.MissingIds
                or TranslationFailureKind.DuplicateOrUnknownIds or TranslationFailureKind.InvalidItem
                or TranslationFailureKind.PlaceholderMismatch => "DeepSeek returned an invalid translation block response.",
            TranslationFailureKind.UnchangedNaturalLanguage => "DeepSeek returned text without target-language content.",
            _ => "DeepSeek batch recovery reached its bounded attempt limit.",
        };
    }
}

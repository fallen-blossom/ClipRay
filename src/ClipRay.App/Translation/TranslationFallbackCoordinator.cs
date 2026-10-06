namespace ClipRay.App.Translation;

public sealed record TranslationFallbackDiagnostic(string Code, int Count);

public sealed record TranslationFallbackOutcome(
    IReadOnlyList<TranslationResult> Results,
    int RecoveredCount,
    int FailedCount,
    IReadOnlyList<TranslationFallbackDiagnostic> Diagnostics);

public sealed class TranslationFallbackCoordinator(
    Func<TranslationRouteKind, IReadOnlyList<TranslationRequest>, CancellationToken,
        Task<IReadOnlyList<TranslationResult>>> primary,
    IMachineTranslationProvider machine)
{
    private readonly Func<TranslationRouteKind, IReadOnlyList<TranslationRequest>, CancellationToken,
        Task<IReadOnlyList<TranslationResult>>> _primary = primary ?? throw new ArgumentNullException(nameof(primary));
    private readonly IMachineTranslationProvider _machine = machine ?? throw new ArgumentNullException(nameof(machine));

    public async Task<TranslationFallbackOutcome> TranslateAsync(
        TranslationRouteKind route,
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return new([], 0, 0, []);

        if (route == TranslationRouteKind.Machine)
        {
            var directResults = await _machine.TranslateManyAsync(requests, cancellationToken).ConfigureAwait(false);
            EnsureCount(requests.Count, directResults.Count);
            return new(
                directResults,
                0,
                directResults.Count(static result => !result.IsSuccess),
                [new TranslationFallbackDiagnostic("machine-route", requests.Count)]);
        }

        IReadOnlyList<TranslationResult> primaryResults;
        var diagnostics = new List<TranslationFallbackDiagnostic>();
        try
        {
            primaryResults = await _primary(route, requests, cancellationToken).ConfigureAwait(false);
            EnsureCount(requests.Count, primaryResults.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            primaryResults = requests.Select(static _ =>
                new TranslationResult(false, string.Empty, "primary-provider-failed")).ToArray();
            diagnostics.Add(new("primary-provider-failed", requests.Count));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var failedIndices = primaryResults
            .Select(static (result, index) => (result, index))
            .Where(static item => !item.result.IsSuccess)
            .Select(static item => item.index)
            .ToArray();
        if (failedIndices.Length == 0)
            return new(primaryResults, 0, 0, []);

        IReadOnlyList<TranslationResult> machineResults;
        try
        {
            machineResults = await _machine.TranslateManyAsync(
                failedIndices.Select(index => requests[index]).ToArray(),
                cancellationToken).ConfigureAwait(false);
            EnsureCount(failedIndices.Length, machineResults.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            diagnostics.Add(new("machine-fallback-failed", failedIndices.Length));
            return new(
                primaryResults,
                0,
                failedIndices.Length,
                diagnostics);
        }

        var merged = primaryResults.ToArray();
        var recovered = 0;
        var preserved = 0;
        var failed = 0;
        for (var index = 0; index < failedIndices.Length; index++)
        {
            switch (machineResults[index].SemanticOutcome)
            {
                case TranslationSemanticOutcome.Translated:
                    merged[failedIndices[index]] = machineResults[index];
                    recovered++;
                    break;
                case TranslationSemanticOutcome.IntentionallyPreserved:
                    merged[failedIndices[index]] = machineResults[index];
                    preserved++;
                    break;
                default:
                    failed++;
                    break;
            }
        }
        diagnostics.Add(new("machine-fallback", failedIndices.Length));
        if (preserved > 0) diagnostics.Add(new("machine-fallback-preserved", preserved));
        return new(
            merged,
            recovered,
            failed,
            diagnostics);
    }

    private static void EnsureCount(int expected, int actual)
    {
        if (expected != actual)
            throw new InvalidOperationException("Translation provider result count mismatch.");
    }
}

namespace ClipRay.App.Translation;

using ClipRay.App.Security;

public static class VisualTranslationExecutionPolicy
{
    public static readonly TimeSpan CloudBudget = TimeSpan.FromSeconds(12);

    public static IReadOnlyList<TranslationResult> TimeoutResults(int count) =>
        Enumerable.Range(0, count)
            .Select(static _ => new TranslationResult(
                false,
                string.Empty,
                "Visual translation timed out; original text was retained."))
            .ToArray();

    public static async Task<IReadOnlyList<TranslationResult>> ExecuteCloudAsync(
        IReadOnlyList<TranslationRequest> requests,
        ICredentialVault vault,
        IDeepSeekTranslationProvider provider,
        Action<DeepSeekCostEstimate?>? recordCost,
        CancellationToken cancellationToken,
        TimeSpan? budget = null,
        string? credentialName = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        credentialName ??= CredentialVault.FixedDeepSeekSlot;
        if (provider is DeepSeekTranslationProvider real && !real.IsNetworkEnabled)
            return requests.Select(static _ => new TranslationResult(false, string.Empty, "local-mode-network-disabled")).ToArray();
        if (provider is not DeepSeekTranslationProvider && vault.GetForUse(credentialName) is null)
        {
            return requests.Select(static _ =>
                new TranslationResult(false, string.Empty, "credential-missing")).ToArray();
        }

        using var deadline = new CancellationTokenSource();
        deadline.CancelAfter(budget ?? CloudBudget);
        try
        {
            TranslationPipelineTrace.Record("cloud_provider_dispatched", new
            {
                request_count = requests.Count
            });
            var results = await provider.TranslateBlocksAsync(
                requests,
                credentialName,
                cancellationToken,
                deadline.Token).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            TranslationCostRecording.TryRecord(results.Select(static result => result.CostEstimate)
                .FirstOrDefault(static estimate => estimate is not null), recordCost);
            return results;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TimeoutResults(requests.Count);
        }
    }
}

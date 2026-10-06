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
        TimeSpan? budget = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(provider);
        if (vault.GetForUse(CredentialVault.FixedDeepSeekSlot) is null)
        {
            return requests.Select(static _ =>
                new TranslationResult(false, string.Empty, "credential-missing")).ToArray();
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget ?? CloudBudget);
        try
        {
            TranslationPipelineTrace.Record("cloud_provider_dispatched", new
            {
                request_count = requests.Count
            });
            var results = await provider.TranslateBlocksAsync(
                requests,
                CredentialVault.FixedDeepSeekSlot,
                deadline.Token).ConfigureAwait(false);
            recordCost?.Invoke(results.Select(static result => result.CostEstimate)
                .FirstOrDefault(static estimate => estimate is not null));
            return results;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TimeoutResults(requests.Count);
        }
    }
}

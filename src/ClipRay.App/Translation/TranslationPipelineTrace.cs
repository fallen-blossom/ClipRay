using System.Threading;

namespace ClipRay.App.Translation;

// Local test tooling can observe the exact values crossing production stage and
// provider boundaries. Without a scope, this has no persistence or side effect.
internal static class TranslationPipelineTrace
{
    private static readonly AsyncLocal<Action<string, object?>?> Sink = new();
    private static int _nextCallId;

    internal static bool Enabled => Sink.Value is not null;

    internal static IDisposable Push(Action<string, object?> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var previous = Sink.Value;
        Sink.Value = write;
        return new Restore(previous);
    }

    internal static void Record(string kind, object? data)
    {
        try { Sink.Value?.Invoke(kind, data); }
        catch { /* Diagnostic storage must not change translation. */ }
    }

    internal static void RecordProjected(string kind, Func<object?> project)
    {
        if (!Enabled) return;
        try { Record(kind, project()); }
        catch { /* Diagnostic projection must not change translation. */ }
    }

    internal static async Task<IReadOnlyList<TranslationResult>> ObserveProviderAsync(
        string role, TranslationRouteKind route, IReadOnlyList<TranslationRequest> requests,
        Func<Task<IReadOnlyList<TranslationResult>>> invoke)
    {
        if (!Enabled) return await invoke().ConfigureAwait(false);
        var callId = Interlocked.Increment(ref _nextCallId);
        RecordProjected("provider_call_started", () => new
        {
            call_id = callId, role, route = route.ToString(), requests = ProjectRequests(requests)
        });
        try
        {
            var results = await invoke().ConfigureAwait(false);
            RecordProjected("provider_call_completed", () => new
            {
                call_id = callId, role, results = ProjectResults(results)
            });
            return results;
        }
        catch (Exception exception)
        {
            Record("provider_call_failed", new
            {
                call_id = callId, role, category = exception.GetType().Name
            });
            throw;
        }
    }

    internal static IMachineTranslationProvider ObserveMachineProvider(
        IMachineTranslationProvider provider, string role) =>
        Enabled ? new ObservedMachineProvider(provider, role) : provider;

    internal static object[] ProjectRequests(IReadOnlyList<TranslationRequest> requests) =>
        requests.Select((request, index) => (object)new
        {
            index, request.SourceLanguage, request.TargetLanguage, request.OcrText,
            request.Model, text_kind = request.TextKind.ToString(),
            request.Scene, request.TranslationHint
        }).ToArray();

    internal static object[] ProjectResults(IReadOnlyList<TranslationResult> results) =>
        results.Select((result, index) => (object)new
        {
            index, result.IsSuccess, result.Text, result.Status,
            semantic_outcome = result.SemanticOutcome.ToString(),
            result.Diagnostic, result.ChunkDiagnostics, result.ExecutionEvidence
        }).ToArray();

    private sealed class ObservedMachineProvider(IMachineTranslationProvider provider, string role)
        : IMachineTranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
            IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken) =>
            ObserveProviderAsync(role, TranslationRouteKind.Machine, requests,
                () => provider.TranslateManyAsync(requests, cancellationToken));
    }

    private sealed class Restore(Action<string, object?>? previous) : IDisposable
    {
        public void Dispose() => Sink.Value = previous;
    }
}

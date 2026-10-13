using System.IO;
using ClipRay.App.Infrastructure;
using ClipRay.App.Translation;
using static BackgroundTask3Tests;
using static BackgroundTask3LifetimeTests;

internal static class BackgroundTask3ChunkTests
{
    private const string Source = "This is an ordinary sentence about a clear afternoon. Another ordinary sentence describes the garden and the river. A final sentence explains the plan for the following day.";
    private static TranslationRequest Request(string text = Source) =>
        new("English", "简体中文", text, DeepSeekModels.Flash, TranslationTextKind.Body);

    internal static void ChildFailure() => ChildFailureAsync(recovery: false).GetAwaiter().GetResult();
    internal static void RecoveryChildFailure() => ChildFailureAsync(recovery: true).GetAwaiter().GetResult();
    private static async Task ChildFailureAsync(bool recovery)
    {
        var released = false;
        var child = new TranslationChildLifetime(() => { },
            () => Task.FromException(new IOException("synthetic exit observation failed")),
            Task.CompletedTask, () => released = true);
        var failureCall = recovery ? 3 : 2;
        var runner = new OneShot(async (call, _) =>
        {
            if (call == 1) return Source;
            if (recovery && call == 2) return string.Empty;
            if (call == failureCall) await child.RetireAsync();
            return "这是另一句普通的中文句子。";
        });
        var provider = Provider(runner);
        try
        {
            var first = await ObserveFailure(() => provider.TranslateManyAsync([Request()], default));
            var callsAfterFirst = runner.Calls;
            var second = await ObserveFailure(() => provider.TranslateManyAsync([Request("A different ordinary sentence.")], default));
            var retirement = await ObserveFailure(provider.RetireAsync);
            Console.WriteLine($"chunk-child-failure: recovery={recovery}; firstThrew={first is TranslationChildRetirementException}; callsAfterFirst={callsAfterFirst}; callsAfterSecond={runner.Calls}; retirementSuccess={provider.RetireAsync().IsCompletedSuccessfully}; childFault={child.RetireAsync().IsFaulted}; handleReleased={released}");
            Check(first is TranslationChildRetirementException f && ReferenceEquals(f.Owner, child), "Chunk failure lost its precise child owner.");
            Check(ReferenceEquals(first, second) && ReferenceEquals(first, retirement), "Next request or retirement lost the retained failure.");
            Check(callsAfterFirst == failureCall && runner.Calls == failureCall && !released, "Failed child allowed another runner call or early handle release.");
            Check(ReferenceEquals(Field<TranslationChildRetirementException>(provider, "_failedChildRetirement"), first), "Provider did not retain the exact failure.");
            var app = FakeApp(); Set(app, "_localTranslationProvider", provider); var finalized = 0;
            Check(!await app.RequestManagedShutdownAsync(finalShutdown: () => finalized++), "Failed chunk child allowed final shutdown.");
            var drain = await app.DrainTranslationAsync(TimeSpan.FromSeconds(2));
            Check(drain.Pending == 0 && drain.Failures.Count == 1 && finalized == 0,
                "Managed drain confused failed exit observation with successful retirement.");
        }
        finally { await ObserveFailure(provider.RetireAsync); await DrainRegistry(); }
    }

    internal static void Cancellation() => CancellationAsync().GetAwaiter().GetResult();
    private static async Task CancellationAsync()
    {
        foreach (var retireProvider in new[] { false, true })
        {
            var entered = NewSignal(); var release = NewSignal();
            using var cancellation = new CancellationTokenSource();
            var runner = new OneShot(async (call, token) =>
            {
                if (call == 1) return Source;
                entered.TrySetResult(); await release.Task.WaitAsync(token); return "分块译文。";
            });
            var provider = Provider(runner);
            var work = provider.TranslateManyAsync([Request()], cancellation.Token);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if (retireProvider) _ = provider.RetireAsync(); else cancellation.Cancel();
                var failure = await ObserveFailure(() => work);
                Check(failure is OperationCanceledException && work.IsCanceled && runner.Calls == 2,
                    "Chunk conversion swallowed cancellation or started more work.");
            }
            finally
            {
                release.TrySetResult(); await ObserveFailure(() => work);
                await provider.RetireAsync().WaitAsync(TimeSpan.FromSeconds(3)); await DrainRegistry();
            }
        }
    }

    internal static void OrdinaryOutcomes() => OrdinaryOutcomesAsync().GetAwaiter().GetResult();
    private static async Task OrdinaryOutcomesAsync()
    {
        foreach (var ordinaryFailure in new[] { false, true })
        {
            var runner = new OneShot((call, _) => call == 1 ? Task.FromResult(Source)
                : ordinaryFailure ? Task.FromException<string>(new IOException("fixture private detail"))
                : Task.FromResult("这是分块翻译的中文内容。"));
            var provider = Provider(runner);
            try
            {
                var result = (await provider.TranslateManyAsync([Request()], default).WaitAsync(TimeSpan.FromSeconds(3)))[0];
                Check(result.IsSuccess != ordinaryFailure && result.ChunkDiagnostics is { Count: > 0 }, "Ordinary chunk result/diagnostics changed.");
                Check(!result.Status.Contains("fixture private detail"), "Private exception detail entered the result.");
                if (ordinaryFailure)
                    Check(result.Status == "Local LMT chunk retry failed without exposing private text."
                        && result.Diagnostic?.Stage == TranslationDiagnosticStage.RetryChunk, "Ordinary failure lost its safe chunk diagnostic.");
            }
            finally { await provider.RetireAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        }
    }

    private static LocalTranslationProvider Provider(ILocalLmtOneShotRunner runner)
    {
        // Synthetic backend: only inspect nonexistent paths under the OS temp root.
        // Mark the matching synthetic artifact snapshot verified; never open a model.
        var root = Path.Combine(Path.GetTempPath(), $"clipray-task3-chunk-{Guid.NewGuid():N}");
        var provider = new LocalTranslationProvider(new(Path.Combine(root, "model"), Path.Combine(root, "runtime")),
            oneShotRunner: runner, batchRunner: new NoBatch(), collectLocalDiagnosticContext: true);
        Set(provider, "_artifactsVerified", true);
        Set(provider, "_verifiedArtifactSnapshot", Invoke(provider, "CaptureArtifactSnapshot")!);
        return provider;
    }

    private static async Task<Exception?> ObserveFailure(Func<Task> work)
    {
        try { await work().WaitAsync(TimeSpan.FromSeconds(3)); return null; }
        catch (Exception failure) when (failure is not TimeoutException) { return failure; }
    }
    private sealed class OneShot(Func<int, CancellationToken, Task<string>> run) : ILocalLmtOneShotRunner
    {
        internal int Calls;
        public Task<string> TranslateAsync(LocalLmtSessionStart start, string prompt, CancellationToken token) => run(++Calls, token);
    }
    private sealed class NoBatch : ILocalLmtBatchRunner
    {
        public Task<IReadOnlyList<string>> TranslateAsync(LocalLmtSessionStart start, IReadOnlyList<string> prompts, CancellationToken token) =>
            throw new Exception("Batch not expected for this single-body fixture.");
    }
}

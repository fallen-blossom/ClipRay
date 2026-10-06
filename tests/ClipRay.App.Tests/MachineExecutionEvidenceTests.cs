using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ClipRay.App.Translation;

internal static class MachineExecutionEvidenceTests
{
    public static void Run()
    {
        using var provider = new LocalMachineTranslationProvider(new EvidenceProcess());
        var results = provider.TranslateManyAsync([
            new("en", "zh", "Hello world"),
            new("en", "zh", "account", textKind: TranslationTextKind.IdentityMetadata)
        ], default).GetAwaiter().GetResult();
        if (results[0].ExecutionEvidence?.ActualBackend != "cpu / int8"
            || results[0].ExecutionEvidence?.BackendFallbackReason != "cuda-inference-failed"
            || results[1].ExecutionEvidence is not null)
            throw new Exception("Machine execution evidence was lost or attributed to a bypass.");

        using var retryProvider = new LocalMachineTranslationProvider(new EvidenceProcess(failBatch: true));
        var retries = retryProvider.TranslateManyAsync([new("en", "zh", "Hello world"), new("en", "zh", "Good morning")], default)
            .GetAwaiter().GetResult();
        if (retries.Any(result => result.ExecutionEvidence?.ActualBackend != "cpu / int8"))
            throw new Exception("Individual retry evidence was lost.");
        var unavailable = new UnavailableProcess();
        using var unavailableProvider = new LocalMachineTranslationProvider(unavailable);
        var failed = unavailableProvider.TranslateManyAsync([new("en", "zh", "Hello world")], default).GetAwaiter().GetResult()[0];
        if (failed.Status != "machine-dependency-unavailable" || unavailable.Calls != 1 || failed.ExecutionEvidence is not null)
            throw new Exception("Missing dependencies must not be retried or attributed a completed backend.");
        var retryUnavailable = new UnavailableProcess(failBatchFirst: true);
        using var retryUnavailableProvider = new LocalMachineTranslationProvider(retryUnavailable);
        var unavailableResults = retryUnavailableProvider.TranslateManyAsync([
            new("en", "zh", "Hello world"), new("en", "zh", "Good morning"), new("en", "zh", "See you later")], default)
            .GetAwaiter().GetResult();
        if (retryUnavailable.Calls != 2 || unavailableResults.Any(result => result.Status != "machine-dependency-unavailable"))
            throw new Exception("A fatal startup error during segment retry must stop remaining retries.");
        Console.WriteLine("Machine result evidence and bypass/retry attribution checks passed.");
    }

    public static int Probe(string[] args)
    {
        if (args.Length is not (5 or 6)) throw new ArgumentException("Expected python worker model fresh-report-path [gpu-directory]");
        var reportPath = Path.GetFullPath(args[4]);
        if (File.Exists(reportPath)) throw new IOException("Report already exists");
        using var worker = new BundledMachineTranslationProcess(args[1], args[2], args[3], TimeSpan.FromSeconds(60), keepAlive: true,
            gpuLibraryDirectory: args.Length == 6 ? args[5] : null);
        using var provider = new LocalMachineTranslationProvider(worker,
            supportsCommonLanguages: MachineTranslationModelPaths.IsCommonLanguageModel(args[3]));
        var watch = Stopwatch.StartNew();
        var first = provider.TranslateManyAsync([new("en", "zh", "Hello world")], default).GetAwaiter().GetResult()[0];
        var firstMs = watch.ElapsedMilliseconds;
        watch.Restart();
        var second = provider.TranslateManyAsync([new("en", "zh", "Good morning")], default).GetAwaiter().GetResult()[0];
        var secondMs = watch.ElapsedMilliseconds;
        var bypass = provider.TranslateManyAsync([new("en", "zh", "account", textKind: TranslationTextKind.IdentityMetadata)], default)
            .GetAwaiter().GetResult()[0];
        var passed = first.IsSuccess && second.IsSuccess && first.ExecutionEvidence?.ActualBackend is not null
            && second.ExecutionEvidence?.ActualBackend is not null && bypass.ExecutionEvidence is null && worker.ProcessStartCount == 1;
        using (var output = new FileStream(reportPath, FileMode.CreateNew))
            JsonSerializer.Serialize(output, new { Passed = passed, first, second, bypass, firstMs, secondMs,
                worker.ProcessStartCount, Scope = "Synthetic local runtime evidence; not translation quality acceptance" },
                new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Machine runtime evidence: passed={passed}; report={reportPath}");
        return passed ? 0 : 1;
    }

    public static int StartupFailureProbe(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Expected python-without-dependencies worker model");
        foreach (var persistent in new[] { false, true })
        {
            using var worker = new BundledMachineTranslationProcess(args[1], args[2], args[3], TimeSpan.FromSeconds(10), keepAlive: persistent);
            try
            {
                worker.TranslateAsync(new("en", "zh", [new string('x', 2_000_000)]), default).GetAwaiter().GetResult();
                throw new Exception("Expected a startup dependency failure");
            }
            catch (MachineTranslationRuntimeException failure) when (failure.Status == "machine-dependency-unavailable")
            {
                if (worker.ProcessStartCount != 1) throw new Exception("Startup probe unexpectedly restarted worker");
                Console.WriteLine($"Large-input startup error recovered; persistent={persistent}; starts=1");
            }
        }
        return 0;
    }

    private sealed class EvidenceProcess(bool failBatch = false) : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            throw new Exception("Provider must consume evidence returned with the response.");

        public Task<MachineTranslationProcessResult> TranslateWithEvidenceAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            if (failBatch && request.Texts.Count > 1) throw new IOException("fixture batch failure");
            return Task.FromResult(new MachineTranslationProcessResult(request.Texts.Select(_ => "你好世界").ToArray(),
                new("ctranslate2", "fixture-model", null, "auto", "cpu / int8", "cuda-inference-failed")));
        }
    }

    private sealed class UnavailableProcess(bool failBatchFirst = false) : IMachineTranslationProcess
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            Calls++;
            if (failBatchFirst && Calls == 1) throw new IOException("fixture initial batch failure");
            throw new MachineTranslationRuntimeException("machine-dependency-unavailable");
        }
    }
}

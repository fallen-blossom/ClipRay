using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class WorkbenchRecoveryEvidence
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 8 && !(args.Length == 9 && args[8] is "merge" or "split"))
            throw new ArgumentException("Expected image report source-language python worker model gpu-directory [merge|split]");
        if (File.Exists(args[2])) throw new IOException("Evidence report already exists");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var source = new Bitmap(args[1]);
        using var ocr = OcrService.CreateForCommonMachine();
        using var worker = new BundledMachineTranslationProcess(args[4], args[5], args[6], TimeSpan.FromSeconds(60),
            keepAlive: true, gpuLibraryDirectory: args[7]);
        using var provider = new LocalMachineTranslationProvider(worker, supportsCommonLanguages: true);
        // Warm the actual process, then cancel a different, uncached workbench request.
        var warm = (await provider.TranslateManyAsync([new("en", "zh", "Good morning")], timeout.Token))[0];
        if (!warm.IsSuccess || warm.ExecutionEvidence?.ActualBackend is null)
            throw new InvalidOperationException("Real model warmup did not return backend evidence.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator = new TranslationApplicationOrchestrator(ocr.RecognizeAsync, async (_, requests, token) =>
        {
            var pending = provider.TranslateManyAsync(requests, token);
            entered.TrySetResult();
            return await pending.ConfigureAwait(false);
        });
        var bounds = new PixelRect(0, 0, source.Width, source.Height);
        using var document = new TranslationWorkbenchDocument(source, bounds, bounds,
            TranslationRouteKind.Machine, "zh", orchestrator, blocks => blocks, commonMachineModel: true);
        await document.RecognizeAsync(timeout.Token);
        var session = document.Session ?? throw new InvalidOperationException("OCR did not produce a session");
        var selected = session.Regions.OrderByDescending(region => region.Draft.Length).First();
        Guid[] mergedInputs = [];
        Guid? splitParent = null;
        if (args.Length == 9 && args[8] == "merge")
        {
            var second = session.Regions.Where(region => region.Id != selected.Id && region.Original.Role == VisualTextRole.Body
                && Math.Abs(region.Original.Bounds.Left - selected.Original.Bounds.Left) < 20)
                .OrderBy(region => Math.Abs(region.Original.Bounds.Top - selected.Original.Bounds.Top)).First();
            mergedInputs = [selected.Id, second.Id];
            var merged = session.Merge(mergedInputs);
            selected = session.Regions.Single(region => region.Id == merged);
        }
        if (args.Length == 9 && args[8] == "split")
        {
            splitParent = selected.Id;
            var split = session.Split(selected.Id, 1, selected.Draft.IndexOf('\n') + 1);
            selected = session.Regions.Single(region => region.Id == split[0]);
        }
        session.Configure(selected.Id, args[3], "zh", TranslationRouteKind.Machine);
        var pendingTranslation = document.TranslateAsync(selected.Id, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        await Task.Delay(20, timeout.Token);
        var pendingAtCancel = !pendingTranslation.IsCompleted;
        var watch = Stopwatch.StartNew();
        session.Cancel(selected.Id);
        await pendingTranslation.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
        var cancelMilliseconds = watch.ElapsedMilliseconds;
        var cancelled = session.Regions.Single(region => region.Id == selected.Id);
        watch.Restart();
        await document.TranslateAsync(selected.Id, timeout.Token);
        var retryMilliseconds = watch.ElapsedMilliseconds;
        var retry = session.Regions.Single(region => region.Id == selected.Id);
        watch.Restart();
        await document.TranslateAsync(selected.Id, timeout.Token);
        var repeatMilliseconds = watch.ElapsedMilliseconds;
        var repeat = session.Regions.Single(region => region.Id == selected.Id);
        var groupingRestored = true;
        if (mergedInputs.Length > 0 || splitParent.HasValue)
        {
            session.UndoGrouping();
            groupingRestored = splitParent.HasValue ? session.Regions.Any(region => region.Id == splitParent.Value)
                : mergedInputs.All(id => session.Regions.Any(region => region.Id == id));
            session.RedoGrouping();
            var restored = session.Regions.Single(region => region.Id == selected.Id);
            groupingRestored &= restored.Translation == repeat.Translation && ReferenceEquals(restored.Layout, repeat.Layout);
        }
        var recovered = retry.State is WorkbenchRegionState.Translated or WorkbenchRegionState.LayoutRejected;
        var passed = groupingRestored && pendingAtCancel && cancelled.State == WorkbenchRegionState.Cancelled
            && cancelled.Translation.Length == 0 && cancelled.Draft == selected.Draft
            && recovered && repeat.State == retry.State && repeat.Translation == retry.Translation
            && session.Attempts.Last().ExecutionEvidence?.ActualBackend is not null;
        using (var output = new FileStream(args[2], FileMode.CreateNew))
            JsonSerializer.Serialize(output, new
            {
                Passed = passed, MergedInputs = mergedInputs, SplitParent = splitParent, GroupingRestored = groupingRestored,
                PendingAtCancel = pendingAtCancel, cancelMilliseconds, retryMilliseconds, repeatMilliseconds,
                worker.ProcessStartCount, Warm = warm, Cancelled = cancelled, Retry = retry, Repeat = repeat,
                Attempts = session.Attempts, ImageSha256 = Hash(args[1]), WorkerSha256 = Hash(args[5]),
                ModelSha256 = Hash(Path.Combine(args[6], "model.bin")), AppSha256 = Hash(typeof(OcrService).Assembly.Location),
                Scope = "Real local OCR/provider/workbench cancellation and retry; cancellation observed while provider task pending, not proof of GPU dispatch; no foreground or semantic quality acceptance."
            }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Workbench runtime recovery: passed={passed}; pendingAtCancel={pendingAtCancel}; retryState={retry.State}; report={args[2]}");
        return passed ? 0 : 1;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

using ClipRay.App.Infrastructure;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using static BackgroundTask3LifetimeTests;

internal static class MachineLiveStartupEntryTests
{
    internal static void Run()
    {
        MachineWarmupStartsDuringSelectionAndGatesSessionAsync().GetAwaiter().GetResult();
        StoppedEntryCannotResumeAfterMachineWarmupAsync().GetAwaiter().GetResult();
        Console.WriteLine("Machine App startup entry cases passed (controlled provider; no native model or screen capture).");
    }

    private static async Task MachineWarmupStartsDuringSelectionAndGatesSessionAsync()
    {
        var app = FakeApp();
        var machine = new HeldMachine();
        Set(app, "_machineTranslationProvider", machine);
        var selectionEntered = NewSignal();
        var selected = new TaskCompletionSource<PixelRect?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var region = new PixelRect(0, 0, 120, 80);
        var starts = 0;
        app.LiveSelectionFactory = _ =>
        {
            Check(machine.Entered.Task.IsCompleted && machine.Calls == 1,
                "The actual Machine route did not begin model warmup before region selection.");
            selectionEntered.TrySetResult();
            return selected.Task;
        };
        app.LiveSessionFactory = (_, actualRegion) =>
        {
            Check(actualRegion == region, "The Machine startup entry changed the selected region.");
            Interlocked.Increment(ref starts);
            return Task.CompletedTask;
        };

        var running = app.RunLiveTranslationEntryAsync(null, TranslationRouteKind.Machine);
        try
        {
            await machine.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await selectionEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!running.IsCompleted && starts == 0,
                "A Machine session started while required warmup and region selection were pending.");
            await app.RunLiveTranslationEntryAsync(region, TranslationRouteKind.Machine);
            Check(machine.Calls == 1 && starts == 0,
                "A duplicate Machine entry started another warmup or session.");
            selected.SetResult(region);
            Check(!running.IsCompleted && starts == 0,
                "A selected Machine region bypassed the held model warmup.");
            machine.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(2));
            Check(machine.Calls == 1 && starts == 1,
                "Completing actual Machine warmup did not start exactly one session.");
        }
        finally
        {
            selected.TrySetResult(null);
            machine.Release.TrySetResult();
            await CleanUpAsync(app, running);
        }
    }

    private static async Task StoppedEntryCannotResumeAfterMachineWarmupAsync()
    {
        var app = FakeApp();
        var machine = new HeldMachine();
        Set(app, "_machineTranslationProvider", machine);
        var starts = 0;
        app.LiveSessionFactory = (_, _) =>
        {
            Interlocked.Increment(ref starts);
            return Task.CompletedTask;
        };
        var running = app.RunLiveTranslationEntryAsync(new(0, 0, 120, 80), TranslationRouteKind.Machine);
        try
        {
            await machine.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(starts == 0 && !running.IsCompleted,
                "A fixed Machine region started before its required warmup.");
            Invoke(app, "StopLiveTranslation");
            await running.WaitAsync(TimeSpan.FromSeconds(2));
            Check(starts == 0 && Field<LiveTranslationStartupCoordinator>(app, "_liveStartup").Current is null,
                "Stopping the real Machine entry did not revoke the pending session.");
            Check(!machine.Release.Task.IsCompleted && LiveTranslationWorkerRegistry.HasUnresolvedWorker,
                "The stopped entry lost ownership of its unfinished warmup.");
            machine.Release.TrySetResult();
            await Field<Task>(app, "_machineTranslationWarmup").WaitAsync(TimeSpan.FromSeconds(2));
            await LiveTranslationWorkerRegistry.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Check(starts == 0, "Late actual Machine warmup started an obsolete session.");
        }
        finally
        {
            machine.Release.TrySetResult();
            await CleanUpAsync(app, running);
        }
    }

    private static async Task CleanUpAsync(ClipRay.App.App app, Task running)
    {
        Invoke(app, "StopLiveTranslation");
        await running.WaitAsync(TimeSpan.FromSeconds(2));
        if (Field<Task?>(app, "_machineTranslationWarmup") is { } warmup)
            await warmup.WaitAsync(TimeSpan.FromSeconds(2));
        await LiveTranslationWorkerRegistry.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await Field<OcrService>(app, "_machineOcr").DisposeAsync();
        await Field<TranslationRuntimeServices>(app, "_translationServices").RetireAsync();
        await Field<AsyncResourceLifetime>(app, "_translationRequests").RetireAsync(() => Task.CompletedTask);
        await Field<AsyncResourceLifetime>(app, "_localProviderOwners").RetireAsync(() => Task.CompletedTask);
        Field<SemaphoreSlim>(app, "_ocrGate").Dispose();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class HeldMachine : IMachineTranslationProvider
    {
        internal TaskCompletionSource Entered { get; } = NewSignal();
        internal TaskCompletionSource Release { get; } = NewSignal();
        internal int Calls { get; private set; }

        public async Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
            IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken)
        {
            Check(requests.Count == 1 && requests[0].OcrText == "Ready."
                && requests[0].SourceLanguage == "en" && requests[0].TargetLanguage == "zh",
                "The actual Machine warmup did not use the expected non-user bootstrap request.");
            Calls++;
            Entered.TrySetResult();
            await Release.Task;
            return [new(true, "准备。", "machine-completed")];
        }
    }
}

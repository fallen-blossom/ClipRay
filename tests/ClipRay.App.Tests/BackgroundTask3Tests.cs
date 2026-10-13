using System.Diagnostics;
using System.Drawing;
using System.IO;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class BackgroundTask3Tests
{
    internal static (string Name, Action Test)[] CreateCases() => [
            ("held-semantic", () => HeldSemanticExpiresChangedText().GetAwaiter().GetResult()),
            ("capture-expiry", () => CaptureFailureExpiresDisplay().GetAwaiter().GetResult()),
            ("plain-stop", () => PlainStopKeepsPhysicalSource().GetAwaiter().GetResult()),
            ("plain-change", () => PlainStopKeepsPhysicalSource(changeDuringWork: true).GetAwaiter().GetResult()),
            ("common-context", BackgroundTask3LifetimeTests.CommonContextRetirement),
            ("service-close", BackgroundTask3LifetimeTests.OcrServiceClosesWithoutRebuild),
            ("entry-stop", BackgroundTask3LifetimeTests.EntryStopAtEveryAwait),
            ("cloud-warmup", BackgroundTask3LifetimeTests.CloudSkipsMachineWarmup),
            ("settings", BackgroundTask3LifetimeTests.SettingsPreserveUnrelatedLiveOwner),
            ("managed-exit", BackgroundTask3LifetimeTests.ManagedExitDecidesBeforeRetirement),
            ("app-retained-frame", BackgroundTask3LifetimeTests.RetainedFrameReachesAppGuard),
            ("common-failure-recognition", BackgroundTask3LifetimeTests.FailedInitializationAndHeldRecognition),
            ("local-model-retirement", BackgroundTask3LifetimeTests.LocalModelSnapshotDefersRetirement),
            ("request-snapshot", BackgroundTask3LifetimeTests.RequestKeepsSelectedCredentialAndFallback),
            ("queued-clear", () => BackgroundTask3ReviewTests.QueuedDisplay(false)),
            ("queued-retain", () => BackgroundTask3ReviewTests.QueuedDisplay(true)),
            ("queued-publish-failure", () => { BackgroundTask3ReviewTests.QueuedDisplay(false, true); BackgroundTask3ReviewTests.QueuedDisplay(true, true); }),
            ("idle-release", BackgroundTask3ReviewTests.IdleRelease),
            ("failed-model-admission", BackgroundTask3ReviewTests.FailedModelAdmission),
            ("machine-child-retirement", () => BackgroundTask3ReviewTests.ChildRetirement(false)),
            ("local-child-retirement", () => BackgroundTask3ReviewTests.ChildRetirement(true)),
            ("child-stop-failure", () => { BackgroundTask3ReviewTests.ChildRetirement(false, true); BackgroundTask3ReviewTests.ChildRetirement(true, true); }),
            ("failed-child-fallback", BackgroundTask3ReviewTests.FailedChildBlocksFallback),
            ("chunk-child-retirement", BackgroundTask3ChunkTests.ChildFailure),
            ("chunk-recovery-retirement", BackgroundTask3ChunkTests.RecoveryChildFailure),
            ("chunk-cancellation", BackgroundTask3ChunkTests.Cancellation),
            ("chunk-one-shot-outcomes", BackgroundTask3ChunkTests.OrdinaryOutcomes),
        ];

    internal static int Run(string[] args)
    {
        var cases = CreateCases();
        var names = args.Where(a => !a.StartsWith("--")).ToHashSet();
        var selected = cases.Where(c => names.Count == 0 || names.Contains(c.Name)).ToArray();
        if (selected.Length == 0 || names.Except(cases.Select(c => c.Name)).Any()) return 2;
        var failures = 0;
        foreach (var test in selected)
            try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception e) { failures++; Console.WriteLine($"FAIL {test.Name}: {e}"); }
        Console.WriteLine($"TASK3 {selected.Length - failures}/{selected.Length}");
        return failures == 0 ? 0 : 1;
    }

    private static async Task HeldSemanticExpiresChangedText()
    {
        var phase = 0;
        var entered = NewSignal(); var release = NewSignal(); var sourceRead = NewSignal();
        var first = NewSignal(); var retained = NewSignal();
        var late = 0; var clears = 0; var probes = 0;
        using var session = new LiveTranslationSession(new(0, 0, 120, 80), _ => Picture(phase),
            (_, _) => Task.FromResult(Frame()), TranslationRouteKind.Machine, validateLayoutRegions: true,
            staleWorkerCancelTimeout: TimeSpan.FromMilliseconds(150), verifySource: async (bitmap, _) =>
            {
                Interlocked.Increment(ref probes); entered.TrySetResult();
                await release.Task;
                bitmap.GetPixel(15, 15); sourceRead.TrySetResult();
                return new(true, [], "late");
            });
        session.FrameReady += (_, e) => {
            if (phase == 0) { phase = 1; first.TrySetResult(); }
            else if (e.Frame.LayoutItems.Any(i => i.Text == "translated A")) Interlocked.Increment(ref late);
            else if (e.Frame.LayoutItems.Any(i => i.Text == "translated B")) retained.TrySetResult();
        };
        session.FrameInvalidated += (_, _) => { if (phase != 0) Interlocked.Increment(ref clears); };
        var running = session.StartAsync(default);
        try
        {
            await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await retained.Task.WaitAsync(TimeSpan.FromSeconds(2.5));
            Check(!release.Task.IsCompleted && late == 0 && clears == 0, "A expired without retaining current static B.");
            Check(probes == 1, "A held semantic owner accumulated queued OCR work.");
            session.Stop(); await running.WaitAsync(TimeSpan.FromSeconds(1));
            Check(LiveTranslationWorkerRegistry.HasUnresolvedWorker, "Held semantic owner lost its physical registration.");
        }
        finally
        {
            session.Stop(); release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(3));
            await sourceRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await DrainRegistry();
        }
        Check(late == 0, "Late OCR republished obsolete text.");
    }

    private static async Task CaptureFailureExpiresDisplay()
    {
        var fail = false; var shown = NewSignal(); var cleared = NewSignal();
        using var session = new LiveTranslationSession(new(0, 0, 120, 80),
            _ => fail ? throw new IOException("synthetic capture failure") : Picture(0),
            (_, _) => Task.FromResult(Frame()), TranslationRouteKind.Machine, validateLayoutRegions: true);
        session.FrameReady += (_, _) => { fail = true; shown.TrySetResult(); };
        session.FrameInvalidated += (_, _) => { if (fail) cleared.TrySetResult(); };
        var running = session.StartAsync(default);
        try { await shown.Task.WaitAsync(TimeSpan.FromSeconds(2)); await cleared.Task.WaitAsync(TimeSpan.FromSeconds(2.5)); }
        finally { session.Stop(); await running.WaitAsync(TimeSpan.FromSeconds(3)); await DrainRegistry(); }
    }

    private static async Task PlainStopKeepsPhysicalSource(bool changeDuringWork = false)
    {
        var entered = NewSignal(); var release = NewSignal(); var read = NewSignal(); var published = 0; var phase = 0; var calls = 0;
        using var session = new LiveTranslationSession(new(0, 0, 120, 80), _ => Picture(phase), async (bitmap, _) => {
            Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task; bitmap.GetPixel(15, 15); read.TrySetResult(); return Frame();
        }, TranslationRouteKind.Machine, staleWorkerCancelTimeout: TimeSpan.FromMilliseconds(100));
        session.FrameReady += (_, _) => published++;
        var running = session.StartAsync(default);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); if (changeDuringWork) phase = 1; else session.Stop();
            await running.WaitAsync(TimeSpan.FromSeconds(1)); Check(LiveTranslationWorkerRegistry.HasUnresolvedWorker && calls == 1, "Plain worker lost ownership or overlapped replacement."); }
        finally { session.Stop(); release.TrySetResult(); await running.WaitAsync(TimeSpan.FromSeconds(3)); await read.Task.WaitAsync(TimeSpan.FromSeconds(2)); await DrainRegistry(); }
        Check(published == 0, "Plain late work published after Stop.");
    }

    internal static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task DrainRegistry()
    {
        var until = Stopwatch.StartNew();
        while (LiveTranslationWorkerRegistry.HasUnresolvedWorker && until.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(10);
        Check(!LiveTranslationWorkerRegistry.HasUnresolvedWorker, "Worker registry did not return to zero.");
    }
    internal static Bitmap Picture(int phase)
    {
        var b = new Bitmap(120, 80); using var g = Graphics.FromImage(b); g.Clear(Color.Black);
        g.FillRectangle(phase == 0 ? Brushes.White : Brushes.Red, 12, 12, 20, 8);
        g.FillRectangle(Brushes.White, 12, 52, 20, 8); return b;
    }
    internal static LiveTranslationFrame Frame()
    {
        var a = Item("A", 10); var b = Item("B", 50);
        return new("Machine", "completed", [a, b]) { SourceRegions = [a.SourceRegion!, b.SourceRegion!], ExpectedLayoutCount = 2 };
    }
    internal static InPlaceLayoutItem Item(string name, int y)
    {
        var r = new PixelRect(10, y, 30, 14); var block = new OcrTextBlock(name, r, []);
        var region = new VisualTextRegion(name, name, [block], r, VisualTextRole.Body, VisualStructureConfidence.High);
        return new(block, "translated " + name, r, InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
    }
}

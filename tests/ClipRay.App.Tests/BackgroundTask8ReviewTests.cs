using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using ClipRay.App.Diagnostics;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static partial class BackgroundTask8Tests
{
    private static void UiRetireActive() => OnDispatcher(async () =>
    {
        using var hold = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var fixture = new OverlayFixture(new FakeElements(() =>
        {
            Interlocked.Increment(ref calls); entered.TrySetResult(); hold.Wait();
            return [new(20,20,20,20)];
        }));
        // Use the production Shared owner and actual App accepted/cancelled Exit path once.
        var scheduler = fixture.Overlay.SnapQueryScheduler;
        fixture.Overlay.SnapQueryBudget = TimeSpan.FromSeconds(15);
        Set(fixture.Overlay,"_snapDetectionMode",SnapDetectionMode.ElementFirst);
        var before = Field<PixelRect>(fixture.Overlay,"_selection");
        var revision = Field<long>(fixture.Overlay,"_captureInputRevision");
        var app = BackgroundTask3LifetimeTests.FakeApp();
        var shutdowns = 0;
        var refinement = Refresh(fixture.Overlay);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!await app.RequestManagedShutdownAsync(() => Task.FromResult(false), () => shutdowns++),
                "Cancelled Exit committed.");
            Check(!Field<bool>(app,"_translationClosing"),"Cancelled Exit closed UIA admission.");
            Check(!await app.RequestManagedShutdownAsync(() => Task.FromResult(true), () => shutdowns++,TimeSpan.FromMilliseconds(30)),
                "Accepted Exit reported a held physical UIA query drained.");
            var drain = await app.DrainTranslationAsync(TimeSpan.Zero);
            Check(!drain.Completed && drain.Pending > 0 && shutdowns == 0 && calls == 1,
                "Accepted Exit dropped its physical UIA drain.");
            var rejectedCalls = 0;
            Check((await scheduler.QueryAsync(() => { rejectedCalls++; return [new(1,1,2,2)]; },TimeSpan.FromSeconds(1),default)).Count == 0
                && rejectedCalls == 0,"Retired UIA owner admitted a new provider.");
        }
        finally { hold.Set(); }
        await refinement.WaitAsync(TimeSpan.FromSeconds(2));
        Check(Field<PixelRect>(fixture.Overlay,"_selection") == before
            && Field<long>(fixture.Overlay,"_captureInputRevision") == revision,
            "Accepted retirement applied an active UIA result before its deadline.");
        Check((await app.DrainTranslationAsync(TimeSpan.FromSeconds(2))).Completed,
            "Physical UIA return never completed the actual App drain.");
        Check(await app.RequestManagedShutdownAsync(finalShutdown: () => shutdowns++) && shutdowns == 1,
            "Exit retry did not complete exactly once after physical UIA return.");
    });

    private static void UiRetireReady() => OnDispatcher(() =>
    {
        using var fixture = new OverlayFixture(new FakeElements(() => [new(20,20,20,20)]));
        var scheduler = new UiQueryScheduler();
        fixture.Overlay.SnapQueryScheduler = scheduler;
        fixture.Overlay.SnapQueryBudget = TimeSpan.FromSeconds(15);
        Set(fixture.Overlay,"_snapDetectionMode",SnapDetectionMode.ElementFirst);
        var before = Field<PixelRect>(fixture.Overlay,"_selection");
        var revision = Field<long>(fixture.Overlay,"_captureInputRevision");
        var prior = SynchronizationContext.Current;
        using var queued = new QueuedApplyContext();
        SynchronizationContext.SetSynchronizationContext(queued);
        try
        {
            var refinement = Refresh(fixture.Overlay);
            Check(queued.Ready.Wait(2000) && !refinement.IsCompleted,
                "Ready-before-retire oracle did not hold the actual overlay continuation.");
            scheduler.RetireAsync().GetAwaiter().GetResult();
            queued.ApplyOne();
            refinement.GetAwaiter().GetResult();
            Check(Field<PixelRect>(fixture.Overlay,"_selection") == before
                && Field<long>(fixture.Overlay,"_captureInputRevision") == revision,
                "A ready UIA result applied after owner retirement.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior);scheduler.RetireAsync().GetAwaiter().GetResult(); }
        return Task.CompletedTask;
    });

    private sealed class QueuedApplyContext : SynchronizationContext,IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback,object? State)> _queue = new();
        internal readonly ManualResetEventSlim Ready = new();
        public override void Post(SendOrPostCallback callback,object? state) { _queue.Enqueue((callback,state));Ready.Set(); }
        internal void ApplyOne() { Check(_queue.TryDequeue(out var item),"No ready overlay apply was queued.");item.Callback(item.State); }
        public void Dispose() => Ready.Dispose();
    }

    private static void DiagnosticStatus()
    {
        var root = TempRoot();
        var oldRoot = Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT");
        var oldMode = Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_MODE");
        try
        {
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT",root);
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_MODE","data");
            using var evidence = ScreenshotTranslationEvidence.StartForRuntime(root,Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow)
                ?? throw new InvalidOperationException("Fake-root screenshot writer did not start.");
            var writer = new JsonlTranslationRunLogWriter(root);
            string[] fixedStatuses = ["ocr-empty","translation-count-mismatch","layout-failed","layout-incomplete","translation-failed"];
            using var source = Source();
            var block = new OcrTextBlock("PRIVATE-original",new(10,10,60,20),[Word("PRIVATE-original",.9)]);
            foreach (var expected in fixedStatuses)
            {
                var orchestrator = new TranslationApplicationOrchestrator(
                    (_,_) => Task.FromResult(new OcrRecognitionResult(true,expected == "ocr-empty" ? [] : [block],"fixture")),
                    (_,requests,_) => Task.FromResult<IReadOnlyList<TranslationResult>>(expected == "translation-count-mismatch" ? []
                        : requests.Select(_ => new TranslationResult(expected != "translation-failed","PRIVATE-translation","PRIVATE-provider-status")).ToArray()),
                    (_,_,_) => expected == "layout-failed" ? throw new InvalidOperationException("fake layout") : []);
                var outcome = orchestrator.ExecuteAsync(source,TranslationRouteKind.Machine,"English",blocks => blocks,default).GetAwaiter().GetResult();
                Check(outcome.Status == expected,"Actual producer did not reach the intended empty/failure status.");
                evidence.RecordOutcome(outcome);
                writer.Write(new(TranslationRunLog.CurrentSchemaVersion,Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,"fixture","version","hash","local",
                    outcome.Status,outcome.FailureStage.ToString(),100,80,false,outcome.OcrBlocks.Count,outcome.Regions.Count,0,0,0,0,0,new(0,0,0,0,0),"none"));
            }
            foreach (var privateStatus in new[] { "PRIVATE-ascii-secret","私密 PRIVATE https://local/secret" })
            {
                writer.Write(new(TranslationRunLog.CurrentSchemaVersion,"private",DateTimeOffset.UtcNow,"fixture","version","hash","local",privateStatus,"Ocr",
                    100,80,false,0,0,0,0,0,0,0,new(0,0,0,0,0),"none"));
                evidence.RecordOutcome(new(false,TranslationPipelineStage.Ocr,privateStatus,[block],[],[],[]));
            }
            var logLines = File.ReadAllLines(Directory.GetFiles(root,"translation-*.jsonl").Single());
            var events = File.ReadAllLines(Path.Combine(evidence.DirectoryPath,"events.jsonl"));
            for (var index=0;index<fixedStatuses.Length;index++)
            {
                using var log = JsonDocument.Parse(logLines[index]);
                using var item = JsonDocument.Parse(events.Where(e => e.Contains("pipeline_outcome")).ElementAt(index));
                Check(log.RootElement.GetProperty("schemaVersion").GetString() == TranslationRunLog.CurrentSchemaVersion
                    && log.RootElement.GetProperty("status").ValueKind == JsonValueKind.String
                    && log.RootElement.GetProperty("status").GetString() == fixedStatuses[index]
                    && item.RootElement.GetProperty("data").GetProperty("Status").ValueKind == JsonValueKind.String
                    && item.RootElement.GetProperty("data").GetProperty("Status").GetString() == fixedStatuses[index],
                    "Default sibling writers lost a fixed status value/string schema.");
            }
            Check(logLines.Concat(events).All(line => !line.Contains("PRIVATE") && !line.Contains("https://local/secret")),
                "Fixed-status repair disclosed arbitrary private status or OCR/translation content.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT",oldRoot);
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_MODE",oldMode);
        }
    }
}

using System.Drawing;
using System.IO;
using System.Reflection;
using ClipRay.App.Ocr;
using ClipRay.App.Diagnostics;
using ClipRay.Core.Geometry;

internal static partial class BackgroundTask8Tests
{
    internal static (string Name, Action Test)[] CreateCases() => [
        ("quality-fallback", QualityFallback), ("default-private", DefaultPrivate), ("entry-budget", EntryBudget),
        ("raw-lines",RawLines),("quality-guards",QualityGuards),("service-trace",ServiceTrace),
        ("demand-idle",DemandIdle),("thread-policy",ThreadPolicy),("preview-cache",PreviewCache),
        ("mosaic-workload",MosaicWorkload),("preview-entry",PreviewEntry),("uia-overlays",UiOverlays),
        ("uia-latest-deadline",UiLatestDeadline),("uia-traversal",UiTraversal),("uia-fallback-version",UiFallbackVersion),
        ("extraction-entry",ExtractionEntry),("extraction-late",ExtractionLate),
        ("diagnostic-budgets",DiagnosticBudgets),("diagnostic-writers",DiagnosticWriters),("diagnostic-optin",DiagnosticOptin),
        ("diagnostic-failure",DiagnosticFailure),
        ("uia-retire-active",UiRetireActive),("uia-retire-ready",UiRetireReady),
        ("diagnostic-status",DiagnosticStatus)];
    internal static int Run(string[] args)
    {
        var cases = CreateCases();
        var index=Array.IndexOf(args,"--case");
        if(index<0&&args.Length!=1 || index>=0&&(index!=1||args.Length!=3)) {Console.Error.WriteLine("Invalid task-8 arguments.");return 2;}
        var terms = index<0?[]:args[index+1].Split([',','|']);
        if (terms.Any(t => !cases.Any(c => c.Name == t))) { Console.Error.WriteLine("Unknown task-8 case."); return 2; }
        var selected = cases.Where(c => terms.Length == 0 || terms.Contains(c.Name)).ToArray();
        var failures = 0;
        foreach (var item in selected)
        {
            var prior=SynchronizationContext.Current;
            var auto=System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall;
            var sink=ClipRay.App.Capture.MediaDeliveryTrace.SinkOverride;
            try { System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=false;
                SynchronizationContext.SetSynchronizationContext(null);
                ClipRay.App.Capture.MediaDeliveryTrace.SinkOverride=(_,_,_)=>{};
                item.Test(); Console.WriteLine("PASS task-8/" + item.Name); }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL task-8/" + item.Name + ": " + ex); }
            finally { ClipRay.App.Capture.MediaDeliveryTrace.SinkOverride=sink;SynchronizationContext.SetSynchronizationContext(prior);
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=auto; }
        }
        Console.WriteLine($"TASK_8_BACKGROUND {selected.Length-failures}/{selected.Length} passed");
        return failures == 0 ? 0 : 1;
    }
    internal static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static OcrWord Word(string text, double? score, PixelRect? bounds=null) => new(text, bounds ?? new(10,10,80,20),
        new("fake", "fixture", "latin", score)) { IsDetectedLine = true };
    private static void QualityFallback()
    {
        using var bitmap = new Bitmap(200,100);
        var fast = new FakeOcr([Word("http://local/ab", .30)]);
        var fallback = new FakeOcr([Word("http://local/ab", .94)]);
        var result = new FastFirstOcrRuntime(fast,fallback).RecognizeAsync(bitmap,default).GetAwaiter().GetResult();
        Check(fast.Calls==1 && fallback.Calls==1 && result[0].Evidence!.RawScore==.94,
            "A nonempty low-confidence candidate bypassed the bounded independent fallback.");
    }
    private static void DefaultPrivate()
    {
        var root = TempRoot();
        var ctor=typeof(ScreenshotTranslationEvidence).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,
            [typeof(string),typeof(string),typeof(DateTimeOffset),typeof(bool)],null)!;
        using var evidence=(ScreenshotTranslationEvidence)ctor.Invoke([root,Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,true]);
        evidence.Record("translation_inputs",new { OcrText="PRIVATE-URL-http://local/secret", width=23, scale=2,
            source_lines=new[] {new {text="PRIVATE-URL-line",bounds=new {width=18,height=4},confidence=.8}} });
        var text=File.ReadAllText(Path.Combine(evidence.DirectoryPath,"events.jsonl"));
        Check(!text.Contains("PRIVATE-URL") && text.Contains("bounds") && text.Contains("confidence"),"Default data-only evidence retained content or lost raw geometry.");
    }
    private static void EntryBudget()
    {
        var root=TempRoot();
        var entry=new TranslationRunLog("fixture","run",DateTimeOffset.UtcNow,"profile","version","hash","local",
            new string('x',200_000),"none",20,30,false,1,1,1,0,0,0,0,new(0,0,0,0,0),"none");
        new JsonlTranslationRunLogWriter(root).Write(entry);
        Check(new FileInfo(Directory.GetFiles(root).Single()).Length<=65_536,"Diagnostic entry exceeded its budget.");
    }
    internal static string TempRoot() { var root=Path.Combine(Path.GetTempPath(),"clipray-task8-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    internal sealed class FakeOcr(IReadOnlyList<OcrWord> words):IOcrRuntime,IOcrWarmup,IAsyncDisposable
    {
        internal int Calls,Warms,Releases;
        internal Color? PixelAfterHold;
        internal Func<CancellationToken,Task>? Hold;
        public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap,CancellationToken token) { Calls++;if(Hold is not null){await Hold(token);PixelAfterHold=bitmap.GetPixel(0,0);} return words; }
        public Task WarmupAsync(CancellationToken token) { Warms++;return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Releases++;return ValueTask.CompletedTask; }
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ClipRay.App.Capture;
using ClipRay.App.Scrolling;
using ClipRay.App.Security;
using ClipRay.App.Translation;

internal static partial class BackgroundTask6Tests
{
    public static int Run(string[] args)
    {
        var tests = CreateCases();
        var selected = args.SkipWhile(a => a != "--background-task-6-tests").Skip(1).ToArray();
        if (selected.Length > 0)
        {
            if (selected.Any(name => !tests.Any(t => t.Name == name))) return 2;
            tests = tests.Where(t => selected.Contains(t.Name)).ToArray();
        }
        var failed = 0;
        foreach (var test in tests)
        {
            var context=SynchronizationContext.Current;
            var autoInstall=System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall;
            try
            {
                System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=false;
                SynchronizationContext.SetSynchronizationContext(test.Name is "app-recovery" or "tray-recovery" or "app-quick" or "app-region" or "overlay-owner" or "app-cost" or "output-exit" or "recovery-exit"
                    ? new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher) : null);
                test.Run(); Console.WriteLine("PASS task-6/" + test.Name);
            }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL task-6/" + test.Name + ": " + error); }
            finally { SynchronizationContext.SetSynchronizationContext(context);System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall=autoInstall; }
        }
        Console.WriteLine($"TASK_6_BACKGROUND {tests.Length-failed}/{tests.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    internal static (string Name, Action Run)[] CreateCases() =>
    [
        ("unmarked", UnmarkedChunksSurvive),
        ("edge", EdgeChangesAreNotRepeated),
        ("first-pause", FirstNoProgressPauses),
        ("cost", LedgerFailureKeepsSuccessfulProvider),
        ("persist-restart", PersistenceAndRestartPixels),
        ("manifest-fault", FailedManifestRetainsIndexedSubset),
        ("strict-manifest", RejectsUnsafeManifests),
        ("legacy", LegacyAndCorruptMaterialsStay),
        ("reparse", ReparseMaterialsStay),
        ("manual-continuation", ManualContinuationAfterPause),
        ("source-input", NativeInputIdentityAndCoordinates),
        ("source-pause", InvalidSourcePausesWithPieces),
        ("edges-pixels", NarrowEdgesComposeExactPixels),
        ("noise-controls", NoiseAndStaticControlsRemainSupported),
        ("app-recovery", AppRecoveryOutcomes),
        ("tray-recovery", ActualTrayRecoveryEntry),
        ("app-quick", ActualQuickCaptureOutcomes),
        ("app-region", ActualRegionDelivery),
        ("overlay-owner", ActualOverlayOwnsAsyncBitmap),
        ("app-cost", ActualAppCostFailureKeepsText),
        ("output-exit", OutputSaveExitOwnership),
        ("recovery-exit", RecoveryExitOwnership),
        ("pipeline", LongCapturePipelineTests.Run),
        ("previous-recovery", PreviousRecoveryCases),
    ];

    private static void UnmarkedChunksSurvive()
    {
        using var temp = new Temp();
        var session = Path.Combine(temp.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
        using var image = Pattern(24, 24, 0);
        var path = Path.Combine(session, "000.png");
        image.Save(path, ImageFormat.Png);
        var bytes = File.ReadAllBytes(path);
        ScrollCaptureTempStore.CleanupOrphans(temp.Root);
        Check(File.Exists(path) && bytes.SequenceEqual(File.ReadAllBytes(path)), "Startup removed a crash's unmarked chunks.");
    }

    private static void EdgeChangesAreNotRepeated()
    {
        using var first = Edge(300, 80, 0);
        using var next = Edge(300, 80, 20);
        var analysis = new FrameStitcher().Analyze(first, next);
        Check(analysis.Disposition != FrameStitchDisposition.Repeated, "Changed narrow edge content was reported as high-confidence repeated.");
    }

    private static void FirstNoProgressPauses()
    {
        using var temp = new Temp();
        var input = new Input();
        var settled = 0;
        ScrollCaptureSession? session = null;
        var settler = new Settler(() => { if (++settled == 12) session!.RequestCompletion(); });
        using (session = new(new(0, 0, 24, 24), ScrollCaptureMode.Automatic, new FixtureCapture(), input, settler, temp.Root,
            new ScrollCaptureOptions(SettleDelay: TimeSpan.Zero)))
        using (var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            Check(input.Calls <= 8 && !session.AutomaticScrollingEnabled, "First no-progress kept posting wheel input instead of pausing.");
            Check(result.AcceptedFrameCount == 1 && result.StopReason == ScrollCaptureStopReason.CompletedByUser, "Paused first frame/manual completion was lost.");
            using var composite = result.CreateComposite();
            Check(composite.GetPixel(4, 9) == PatternPixel(4, 9), "First frame changed after the pause.");
        }
    }

    private static void LedgerFailureKeepsSuccessfulProvider()
    {
        var provider = new Provider();
        var fallback = new Machine();
        var request = new TranslationRequest("en", "zh-CN", "Hello", DeepSeekModels.Flash, TranslationTextKind.Body, "test", "Translate faithfully.");
        var result = new TranslationFallbackCoordinator(
            (route, requests, token) => VisualTranslationExecutionPolicy.ExecuteCloudAsync(requests, new Vault(), provider,
                _ => throw new IOException("fake ledger unavailable"), token), fallback)
            .TranslateAsync(TranslationRouteKind.DeepSeekFlash, [request], CancellationToken.None).GetAwaiter().GetResult();
        Check(result.Results[0] is { IsSuccess: true, Text: "你好" } && fallback.Calls == 0, "Ledger failure discarded translation or dispatched a secondary provider.");
    }

    internal static Bitmap Pattern(int width, int height, int offset)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y=0; y<height; y++) for (var x=0; x<width; x++) image.SetPixel(x,y,PatternPixel(x,y+offset));
        return image;
    }
    internal static Color PatternPixel(int x, int y) => Color.FromArgb(255,(x*17+y*13)%256,(x*3+y*31)%256,(x*37+y*7)%256);
    private static Bitmap Edge(int width,int height,int offset)
    {
        var image = new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using (var g=Graphics.FromImage(image)) g.Clear(Color.White);
        for(var y=0;y<height;y++) for(var x=0;x<12;x++) image.SetPixel(x,y,PatternPixel(x,y+offset));
        return image;
    }
    internal static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    internal sealed class Temp : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"clipray-task6-"+Guid.NewGuid().ToString("N"));
        public Temp()=>Directory.CreateDirectory(Root);
        public void Dispose() { if(Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
    private sealed class Input : IScrollInputService { public int Calls; public Task SendWheelAsync(Point target,int delta,CancellationToken token) { Calls++;return Task.CompletedTask; } }
    private sealed class Settler(Action? step=null) : IScrollSettler { public Task WaitForSettleAsync(TimeSpan delay,CancellationToken token) { step?.Invoke();return Task.CompletedTask; } }
    private sealed class FixtureCapture : IScrollRegionCapture { public Bitmap Capture(Rectangle region)=>Pattern(region.Width,region.Height,0); }
    private sealed class Vault : ICredentialVault
    {
        public string? GetForUse(string name)=>"fixture";
        public void Save(string name,string provider,string value)=>throw new NotSupportedException();
        public void Delete(string name)=>throw new NotSupportedException();
        public IReadOnlyList<CredentialMetadata> List()=>[];
    }
    private sealed class Provider : IDeepSeekTranslationProvider
    {
        private readonly TranslationResult _result = new(true,"你好","completed",new(0.01m,0,10,5,1,DateTimeOffset.UtcNow,"fixture"));
        public Task<IReadOnlyList<TranslationResult>> TranslateBlocksAsync(IReadOnlyList<TranslationRequest> requests,string name,CancellationToken token)=>Task.FromResult<IReadOnlyList<TranslationResult>>(requests.Select(_=>_result).ToArray());
        public Task<TranslationResult> TranslateAsync(TranslationRequest request,string name,CancellationToken token)=>Task.FromResult(_result);
        public Task<TranslationResult> TestConnectionAsync(string name,string model,CancellationToken token)=>throw new NotSupportedException();
        public Task<TranslationResult> TestCandidateConnectionAsync(string key,string model,CancellationToken token)=>throw new NotSupportedException();
    }
    private sealed class Machine : IMachineTranslationProvider
    {
        public int Calls;
        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<TranslationRequest> requests,CancellationToken token) { Calls++;return Task.FromResult<IReadOnlyList<TranslationResult>>(requests.Select(_=>new TranslationResult(true,"fallback","machine")).ToArray()); }
    }
}

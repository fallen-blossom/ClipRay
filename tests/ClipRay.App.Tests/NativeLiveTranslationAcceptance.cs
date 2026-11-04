using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

// Deliberately small native acceptance: two generated sources, one failed Apply,
// actual machine translation and actual WPF raster/OCR readback. No screen capture.
internal static class NativeLiveTranslationAcceptance
{
    private static readonly string[] Lines = ["The red bird is flying.", "The blue fish is swimming."];
    private const int Width = 720, Height = 180;

    public static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected output-directory install-root.");
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew();
        var evidence = new List<object>();
        try
        {
            RunCore(output, Path.GetFullPath(args[2]), clock, evidence);
            WriteReport(output, clock, evidence, true, null);
            Console.WriteLine($"NATIVE_LIVE_TRANSLATION_ACCEPTANCE_PASSED elapsed={clock.ElapsedMilliseconds}ms");
            return 0;
        }
        catch (Exception error)
        {
            WriteReport(output, clock, evidence, false, error.GetType().Name + ": " + error.Message);
            Console.Error.WriteLine($"NATIVE_LIVE_TRANSLATION_ACCEPTANCE_FAILED: {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }

    private static void RunCore(string output, string root, Stopwatch clock, List<object> evidence)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(42));
        using var nativeGate = new SemaphoreSlim(1, 1);
        using var ocr = OcrService.CreateForCommonMachine();
        var model = MachineTranslationModelPaths.Resolve(root);
        using var process = new BundledMachineTranslationProcess(
            Path.Combine(root, "machine-translation", "runtime", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "machine-translation", "machine_translation_worker.py"),
            model, workerTimeout: TimeSpan.FromSeconds(10), keepAlive: true,
            startupTimeout: TimeSpan.FromSeconds(20));
        using var machine = new LocalMachineTranslationProvider(process,
            supportsCommonLanguages: MachineTranslationModelPaths.IsCommonLanguageModel(model));
        using var renderer = new StaRasterRenderer();
        using var sourceA = CreateSource(0);
        using var sourceB = CreateSource(1);
        sourceA.Save(Path.Combine(output, "source-A.png"), ImageFormat.Png);
        sourceB.Save(Path.Combine(output, "source-B.png"), ImageFormat.Png);
        var phase = 0;
        var pipelineCalls = 0; var pipelineOcrCalls = 0; var semanticOcrCalls = 0;
        var nativeRecognitionCalls = 0;
        var readbackCalls = 0; var providerCalls = 0; var attempts = 0; var presented = 0;
        var completeFrames = 0; var intermediateBFrames = 0;
        var nativeActive = 0; var nativeMaximum = 0;
        var frames = new List<LiveTranslationFrameReadyEventArgs>();
        var rasterPaths = new List<(string Path, string Text, int Phase)>();
        Exception? observerFailure = null;
        LiveTranslationFrameReadyEventArgs? failedApply = null;
        (int Pipeline, int Ocr, int Provider, int Semantic, int NativeOcr) failedCounts = default;

        async Task<T> Native<T>(Func<Task<T>> action, CancellationToken token)
        {
            await nativeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                nativeMaximum = Math.Max(nativeMaximum, Interlocked.Increment(ref nativeActive));
                return await action().ConfigureAwait(false);
            }
            finally { Interlocked.Decrement(ref nativeActive); nativeGate.Release(); }
        }

        var liveRecognition = new LiveTranslationRecognitionCache(
            LiveTranslationRecognitionCache.CommonMachineProfile, new Size(Width, Height),
            (bitmap, token) => Native(async () => {
                Interlocked.Increment(ref nativeRecognitionCalls);
                return await ocr.RecognizeAsync(bitmap, token).ConfigureAwait(false);
            }, token));
        var orchestrator = new TranslationApplicationOrchestrator(
            (bitmap, token) => {
                Interlocked.Increment(ref pipelineOcrCalls);
                return liveRecognition.RecognizeAsync(bitmap, token);
            },
            (_, requests, token) => {
                Interlocked.Increment(ref providerCalls);
                return Native(() => machine.TranslateManyAsync(requests, token), token);
            });
        LiveTranslationSession? session = null;
        session = new(new Rectangle(0, 0, Width, Height),
            _ => new Bitmap(Volatile.Read(ref phase) == 0 ? sourceA : sourceB),
            async (bitmap, token) => {
                var call = Interlocked.Increment(ref pipelineCalls);
                var bounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height);
                var outcome = await orchestrator.ExecuteAsync(bitmap, TranslationRouteKind.Machine,
                    "简体中文", blocks => SelectionCapturePolicy.ConstrainToSelection(blocks, bounds, bounds),
                    token).ConfigureAwait(false);
                evidence.Add(new { Kind = "native_pipeline", Call = call, AtMilliseconds = clock.ElapsedMilliseconds,
                    outcome.IsSuccess, outcome.Status, outcome.OcrBlocks, outcome.Regions, outcome.Results,
                    outcome.LayoutItems, outcome.LayoutRejections });
                Require(outcome.IsSuccess && outcome.Results.Count > 0
                    && outcome.Results.All(result => result.IsSuccess), "Native pipeline did not translate every region.");
                return LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine, outcome);
            }, TranslationRouteKind.Machine, validateLayoutRegions: true,
            verifySource: (bitmap, token) => {
                Interlocked.Increment(ref semanticOcrCalls);
                return liveRecognition.RecognizeAsync(bitmap, token);
            }, scheduleRegions: true);
        using (session)
        {
            session.FrameReady += (_, candidate) => {
                try
                {
                    attempts++;
                    Require(session.CanPresent(candidate), "Session published an ineligible candidate.");
                    var currentPhase = Volatile.Read(ref phase);
                    var source = currentPhase == 0 ? sourceA : sourceB;
                    var frame = candidate.Frame;
                    var sourceText = string.Concat(frame.SourceRegions.OrderBy(region => region.Bounds.Top)
                        .ThenBy(region => region.Bounds.Left).Select(region => region.SourceText));
                    var text = string.Concat(frame.LayoutItems.OrderBy(item => item.Bounds.Top)
                        .ThenBy(item => item.Bounds.Left).Select(item => item.Text));
                    var complete = Normalize(sourceText) == Normalize(Lines[currentPhase])
                        && frame.ResultState == LiveTranslationViewportResultState.Ready
                        && frame.LayoutItems.Count == 1 && frame.ExpectedLayoutCount == 1;
                    if (currentPhase == 1)
                    {
                        evidence.Add(new { Kind = "B_candidate", candidate.Generation,
                            frame.RegionWork, frame.ResultState, SourceText = sourceText, Text = text,
                            SourceRegions = frame.SourceRegions.Select(region => new { region.SourceText, region.Bounds }),
                            LayoutSources = frame.LayoutItems.Select(item => new {
                                SourceText = item.SourceRegion?.SourceText ?? item.SourceBlock.Text, item.Text, item.Bounds }),
                            Complete = complete, AtMilliseconds = clock.ElapsedMilliseconds,
                            pipelineCalls, pipelineOcrCalls, providerCalls, semanticOcrCalls, nativeRecognitionCalls });
                        Require(!Normalize(sourceText).Contains(Normalize(Lines[0]), StringComparison.Ordinal)
                            && frame.LayoutItems.All(item => Normalize(item.SourceRegion?.SourceText ?? item.SourceBlock.Text)
                                != Normalize(Lines[0]))
                            && !(text.Contains('红') && text.Contains('鸟')),
                            "A stale A source or translation was presented after the source changed to B.");
                    }
                    else Require(complete, "OCR/source regions omitted or changed the generated A line.");
                    Require(frame.LayoutItems.Count > 0 && text.Any(IsHan)
                        && Normalize(text) != Normalize(Lines[currentPhase]),
                        "Native provider did not return nonempty Chinese target text.");
                    // A small semantic oracle distinguishes these two native results.
                    if (complete) Require(currentPhase == 0 ? text.Contains('红') && text.Contains('鸟')
                        : text.Contains('蓝') && text.Contains('鱼'), "Native result lost the fixture's color or animal.");
                    Require(frame.LayoutItems.All(item => item.Bounds.Left >= 0 && item.Bounds.Top >= 0
                        && item.Bounds.Right <= Width && item.Bounds.Bottom <= Height
                        && item.Bounds.Width > 0 && item.Bounds.Height > 0
                        && item.TextLayout is { ShouldRender: true }), "Layout is outside the fixture or cannot render.");
                    var rasterPath = Path.Combine(output, currentPhase == 0 ? "rendered-A.png"
                        : complete ? "rendered-B.png" : $"rendered-B-intermediate-{attempts}.png");
                    renderer.Invoke(() => RenderAndCheck(source, frame.LayoutItems, rasterPath));
                    if (failedApply is null)
                    {
                        failedApply = candidate;
                        failedCounts = (pipelineCalls, pipelineOcrCalls, providerCalls, semanticOcrCalls, nativeRecognitionCalls);
                        evidence.Add(new { Kind = "injected_apply_failure", candidate.Generation,
                            AtMilliseconds = clock.ElapsedMilliseconds, pipelineCalls, pipelineOcrCalls, providerCalls,
                            nativeRecognitionCalls });
                        // The raster exists, but Apply intentionally does not acknowledge it.
                        session.ReportDisplayNotApplied(candidate);
                        return;
                    }
                    if (currentPhase == 0)
                    {
                        Require(ReferenceEquals(failedApply.Frame, frame), "Display recovery rebuilt the native frame.");
                        Require(failedCounts == (pipelineCalls, pipelineOcrCalls, providerCalls, semanticOcrCalls, nativeRecognitionCalls),
                            "Apply recovery repeated OCR or translation on unchanged pixels.");
                    }
                    frames.Add(candidate);
                    if (complete)
                    {
                        completeFrames++;
                        rasterPaths.Add((rasterPath, text, currentPhase));
                    }
                    else intermediateBFrames++;
                    session.MarkDisplayApplied(candidate.Generation);
                    session.MarkDisplayPresented(candidate.Generation);
                    presented++;
                    evidence.Add(new { Kind = "offscreen_raster_ack", Phase = currentPhase,
                        candidate.Generation, Text = text, Complete = complete,
                        AtMilliseconds = clock.ElapsedMilliseconds });
                    if (currentPhase == 0) Volatile.Write(ref phase, 1);
                    else if (complete) session.Stop();
                }
                catch (Exception error) { observerFailure = error; session.Stop(); }
            };
            session.StartAsync(deadline.Token).GetAwaiter().GetResult();
            if (observerFailure is not null) throw observerFailure;
            Require(!deadline.IsCancellationRequested, "Native acceptance exceeded the 42-second runtime budget.");
            Require(completeFrames == 2 && presented == completeFrames + intermediateBFrames
                && attempts == presented + 1 && pipelineCalls >= 2 && pipelineOcrCalls == pipelineCalls
                && providerCalls == pipelineCalls && semanticOcrCalls > 0,
                $"Unexpected lifecycle counts: presented={presented}, attempts={attempts}, pipelines={pipelineCalls}, ocr={pipelineOcrCalls}, provider={providerCalls}, semantic={semanticOcrCalls}.");
            Require(nativeRecognitionCalls > 0 && liveRecognition.CompletedReuseCount > 0,
                "The real pipeline and semantic verification did not reuse any completed native OCR.");
            Require(frames.Select(frame => frame.Generation).Distinct().Count() == frames.Count
                && frames.Zip(frames.Skip(1)).All(pair => pair.Second.Generation > pair.First.Generation)
                && session.State == LiveTranslationSessionState.Stopped && session.LastErrorCategory == "none",
                "Latest B/Stop did not retire A cleanly.");
            var stoppedCounts = (attempts, pipelineCalls, pipelineOcrCalls, providerCalls, semanticOcrCalls, nativeRecognitionCalls);
            Task.Delay(300, deadline.Token).GetAwaiter().GetResult();
            Require(stoppedCounts == (attempts, pipelineCalls, pipelineOcrCalls, providerCalls, semanticOcrCalls, nativeRecognitionCalls)
                && frames.All(frame => !session.CanPresent(frame)) && !session.CanPresent(failedApply!),
                "A late native result or display candidate survived Stop.");
            foreach (var raster in rasterPaths)
            {
                using var bitmap = new Bitmap(raster.Path);
                readbackCalls++;
                var readback = Native(() => ocr.RecognizeAsync(bitmap, deadline.Token), deadline.Token)
                    .GetAwaiter().GetResult();
                var recognized = string.Concat(readback.Blocks.OrderBy(block => block.Bounds.Top)
                    .ThenBy(block => block.Bounds.Left).Select(block => block.Text));
                evidence.Add(new { Kind = "native_raster_readback", raster.Phase, Expected = raster.Text,
                    Actual = recognized, readback.IsSuccess, readback.Status, readback.Blocks });
                Require(readback.IsSuccess && Normalize(recognized) == Normalize(raster.Text),
                    "Native OCR of actual rendered glyphs differs from returned translation.");
            }
            Require(nativeMaximum == 1 && readbackCalls == 2 && !deadline.IsCancellationRequested,
                "Native gate or bounded raster readback failed.");
            evidence.Add(new { Kind = "lifecycle_summary", attempts, presented, pipelineCalls, pipelineOcrCalls,
                providerCalls, semanticOcrCalls, readbackCalls, nativeMaximum, process.ProcessStartCount,
                nativeRecognitionCalls, NativeOcrCallsIncludingReadback = nativeRecognitionCalls + readbackCalls,
                CompletedRecognitionReuses = liveRecognition.CompletedReuseCount,
                completeFrames, intermediateBFrames,
                StopRejectsAllCandidates = true, NoLateActivityObservationMilliseconds = 300,
                ApplyRecoveryReusedNativeFrame = true, NativeBudgetSeconds = 42 });
        }
    }

    private static Bitmap CreateSource(int phase)
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Arial", 36, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.DrawString(Lines[phase], font, Brushes.Black, 28, 58, StringFormat.GenericTypographic);
        graphics.FillRectangle(Brushes.Teal, 660, 130, 30, 20);
        return bitmap;
    }

    private static void RenderAndCheck(Bitmap source, IReadOnlyList<InPlaceLayoutItem> items, string path)
    {
        var overlay = TranslatedSelectionRenderer.RenderOverlay(Width, Height, items);
        var bytes = new byte[Width * Height * 4];
        overlay.CopyPixels(bytes, Width * 4, 0);
        var painted = 0;
        using var rendered = TranslatedSelectionRenderer.Render(source, new(0, 0, Width, Height), items);
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            // Fractional WPF edges may cover part of a pixel. Only pixels wholly
            // outside every layout rectangle count as protected neighbors.
            var inside = items.Any(item => x + 1 > item.Bounds.Left && x < item.Bounds.Right
                && y + 1 > item.Bounds.Top && y < item.Bounds.Bottom);
            var alpha = bytes[(y * Width + x) * 4 + 3];
            if (alpha > 0) painted++;
            if (!inside)
            {
                Require(alpha == 0, "Overlay changed an adjacent pixel outside layout bounds.");
                Require(source.GetPixel(x, y).ToArgb() == rendered.GetPixel(x, y).ToArgb(),
                    "Composited rendering changed an adjacent source pixel.");
            }
        }
        Require(painted > 100, "Actual overlay raster is empty.");
        rendered.Save(path, ImageFormat.Png);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(overlay));
        using var stream = File.Create(Path.ChangeExtension(path, ".overlay.png"));
        encoder.Save(stream);
    }

    private static string Normalize(string text) => string.Concat(text.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static bool IsHan(char value) => value is >= '\u3400' and <= '\u9fff';
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void WriteReport(string output, Stopwatch clock, List<object> evidence, bool passed, string? error)
    {
        File.WriteAllText(Path.Combine(output, "native-live-acceptance.json"), JsonSerializer.Serialize(new {
            Passed = passed, Error = error, ElapsedMilliseconds = clock.ElapsedMilliseconds,
            Scope = "Controlled generated bitmap -> native CommonMachine OCR/local machine provider -> real live session -> offscreen WPF RenderOverlay/Render -> native OCR raster readback. No desktop, clipboard, credential or network access. No physical foreground/window capture acceptance.",
            AppSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(OcrService).Assembly.Location))),
            TestsSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(NativeLiveTranslationAcceptance).Assembly.Location))),
            Evidence = evidence
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class StaRasterRenderer : IDisposable
    {
        private readonly BlockingCollection<Action> _actions = new();
        private readonly Thread _thread;
        public StaRasterRenderer()
        {
            _thread = new Thread(() => { foreach (var action in _actions.GetConsumingEnumerable()) action(); })
                { IsBackground = true, Name = "ClipRay controlled native acceptance raster" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        public void Invoke(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Add(() => { try { action(); completion.SetResult(); } catch (Exception error) { completion.SetException(error); } });
            completion.Task.GetAwaiter().GetResult();
        }
        public void Dispose() { _actions.CompleteAdding(); _thread.Join(); _actions.Dispose(); }
    }
}

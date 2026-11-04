using System.Diagnostics;
using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveCropBoundaryRecoveryTests
{
    public static void InteriorCropEdgeRetriesFullWindowWithoutCommittingHalfLine()
    {
        const int width = 320, height = 128;
        const string sourceA = "The red bird is flying.";
        const string sourceB = "The blue fish is swimming.";
        const string clippedB = "The blue fish is swimmin";
        var oldBounds = new PixelRect(32, 32, 64, 32);
        var fullBounds = new PixelRect(32, 32, 224, 32);
        var expectedCrop = new Rectangle(8, 8, 112, 80);
        using var before = Picture(false);
        using var after = Picture(true);
        var initial = Frame(sourceA, "旧译文", oldBounds);
        var beforePixels = new LiveTranslationPixelSnapshot(before);
        var afterPixels = new LiveTranslationPixelSnapshot(after);
        var originalPlan = LiveTranslationChangePlanner.Analyze(beforePixels, afterPixels, initial.SourceRegions);
        var incorrectlyAdvanced = beforePixels.UpdateRegions(afterPixels, [expectedCrop]);
        var advancedPlan = LiveTranslationChangePlanner.Analyze(incorrectlyAdvanced, afterPixels, initial.SourceRegions);
        // This gives the baseline assertion an observable distinction: preserving
        // A leaves two dirty windows; prematurely committing the clipped crop
        // would leave only the independent suffix window.
        if (originalPlan.Windows.Count != 2 || !originalPlan.Windows.Contains(expectedCrop)
            || advancedPlan.Windows.Count != 1)
            throw new Exception("The fixed crop fixture no longer distinguishes baseline preservation from premature advancement.");

        var phase = 0; var calls = 0; var sawIncomplete = false;
        int? windowsAfterIncomplete = null;
        string? problem = null;
        var workers = new List<Size>();
        var published = new List<string>();
        var elapsed = Stopwatch.StartNew();
        LiveTranslationSession? session = null;
        session = new(new(0, 0, width, height), _ => new Bitmap(phase == 0 ? before : after), (bitmap, _) =>
        {
            var call = ++calls;
            workers.Add(bitmap.Size);
            if (call == 1) return Task.FromResult(initial);
            if (call == 2)
            {
                if (bitmap.Size != expectedCrop.Size)
                    problem ??= $"The intended first regional worker received {bitmap.Size} instead of {expectedCrop.Size}.";
                // Exactly one pixel inside the internal right edge: exercise the
                // fixed tolerance without changing padding or inventing cases.
                var clippedBounds = new PixelRect(8, 24, bitmap.Width - 9, 24);
                return Task.FromResult(Frame(clippedB, "半句译文", clippedBounds));
            }
            if (call != 3 || bitmap.Size != new Size(width, height))
                problem ??= "Boundary recovery did not select the full viewport as the very next worker.";
            else if (bitmap.GetPixel(64, 48).ToArgb() != Color.Red.ToArgb()
                || bitmap.GetPixel(224, 48).ToArgb() != Color.Blue.ToArgb())
                problem ??= "The full recovery worker did not receive the newest B pixels inside and outside the rejected crop.";
            if (call > 3) session!.Stop();
            return Task.FromResult(Frame(sourceB, "完整译文", fullBounds));
        }, TranslationRouteKind.Machine, validateLayoutRegions: true, verifySource: null, scheduleRegions: true);
        using (session)
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
        {
            session.Diagnostic += (_, diagnostic) =>
            {
                if (diagnostic.ErrorCategory != "crop-edge-incomplete") return;
                sawIncomplete = true;
                if (diagnostic.ResultState != LiveTranslationViewportResultState.Pending)
                    problem ??= "A clipped crop was classified as a completed viewport.";
            };
            session.CaptureDecision += (_, decision) =>
            {
                if (sawIncomplete && windowsAfterIncomplete is null && decision.Kind == "pipeline_scheduled")
                    windowsAfterIncomplete = decision.WindowCount;
            };
            session.FrameReady += (_, candidate) =>
            {
                var source = string.Concat(candidate.Frame.SourceRegions.Select(region => region.SourceText));
                published.Add(source);
                if (source == clippedB)
                    problem ??= "The rejected half-line escaped through FrameReady.";
                if (source != sourceA && source != sourceB)
                    problem ??= "Recovery published an unexpected source fragment.";
                if (candidate.Frame.ResultState != LiveTranslationViewportResultState.Ready)
                    problem ??= "The final complete source was not ready.";
                session.MarkDisplayApplied(candidate.Generation);
                session.MarkDisplayPresented(candidate.Generation);
                if (source == sourceA && phase == 0) phase = 1;
                else session.Stop();
            };
            session.StartAsync(deadline.Token).GetAwaiter().GetResult();
            if (problem is not null) throw new Exception(problem);
            if (deadline.IsCancellationRequested || elapsed.Elapsed > TimeSpan.FromSeconds(5)
                || calls != 3 || !sawIncomplete || windowsAfterIncomplete != 2
                || !published.SequenceEqual([sourceA, sourceB])
                || !workers.SequenceEqual([new Size(width, height), expectedCrop.Size, new Size(width, height)])
                || session.State != LiveTranslationSessionState.Stopped)
                throw new Exception($"Crop boundary recovery did not preserve the baseline and finish full B: calls={calls}, pending={sawIncomplete}, nextPlanWindows={windowsAfterIncomplete}, published={string.Join('|', published)}, elapsed={elapsed.Elapsed}.");
        }

        static Bitmap Picture(bool changed)
        {
            var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            bitmap.SetPixel(40, 40, Color.White);
            if (changed)
            {
                bitmap.SetPixel(64, 48, Color.Red);
                bitmap.SetPixel(224, 48, Color.Blue);
            }
            return bitmap;
        }

        static LiveTranslationFrame Frame(string source, string translated, PixelRect bounds)
        {
            var block = new OcrTextBlock(source, bounds, [new OcrWord(source, bounds)]);
            var region = new VisualTextRegion(source, source, [block], bounds,
                VisualTextRole.Body, VisualStructureConfidence.High);
            var item = new InPlaceLayoutItem(block, translated, bounds,
                InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
            return new("Machine", "completed", [item])
            {
                SourceRegions = [region], ExpectedLayoutRegions = [region], ExpectedLayoutCount = 1,
                ExpectedPrimaryLayoutCount = 1, ExpectedPrimaryLayoutArea = bounds.Width * bounds.Height,
            };
        }
    }
}

using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Diagnostics;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class ScreenshotEvidenceTests
{
    public static void ScreenshotSwitchIsIndependentOfLiveTrace()
    {
        var oldScreenshot = Environment.GetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT");
        var oldLive = Environment.GetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT");
        var root = NewRoot();
        try
        {
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT", null);
            Environment.SetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT", root);
            if (ScreenshotTranslationEvidence.StartFromEnvironment(
                Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow) is not null)
                throw new Exception("The live translation switch enabled screenshot evidence.");
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT", @"\\server\share");
            if (ScreenshotTranslationEvidence.StartFromEnvironment(
                Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow) is not null)
                throw new Exception("A network path enabled private screenshot evidence.");
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT", root);
            using var evidence = ScreenshotTranslationEvidence.StartFromEnvironment(
                Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
            if (evidence is null)
                throw new Exception("The screenshot switch did not start a local run.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLIPRAY_SCREENSHOT_EVIDENCE_ROOT", oldScreenshot);
            Environment.SetEnvironmentVariable("CLIPRAY_LIVE_ACCEPTANCE_ROOT", oldLive);
            Directory.Delete(root, recursive: true);
        }
    }

    public static void RecordsOnePrivateRunWithSourceRequestsOutcomeAndLayer()
    {
        var root = NewRoot();
        try
        {
            var runId = Guid.NewGuid().ToString("N");
            var request = new TranslationRequest("auto", "简体中文", "Hello Codex");
            var result = new TranslationResult(true, "你好 Codex", "completed");
            var block = new OcrTextBlock("Hello Codex", new PixelRect(0, 0, 4, 4),
                [new OcrWord("Hello Codex", new PixelRect(0, 0, 4, 4))]);
            var region = new VisualTextRegion("region-1", block.Text, [block], block.Bounds,
                VisualTextRole.Body, VisualStructureConfidence.High);
            var layout = new InPlaceLayoutItem(block, result.Text, block.Bounds,
                InPlaceBackgroundMode.FlatSampled, System.Drawing.Color.White, System.Drawing.Color.Black,
                SourceRegion: region,
                TextLayout: new TranslationTextLayout(true, 12, 14,
                    SourceFontWeight.Normal, SourceFontCategory.SansSerif, false,
                    SourceTextAlignment.Left, 0, "Segoe UI"));
            var outcome = new ApplicationTranslationOutcome(true, TranslationPipelineStage.None,
                "completed", [block], [region], [result], [layout]);
            string directory;
            using (var evidence = ScreenshotTranslationEvidence.StartForTest(
                root, runId, DateTimeOffset.UtcNow))
            {
                directory = evidence.DirectoryPath;
                evidence.RecordContext(new { route = "Machine", selection = new { x = 1, y = 2 } });
                using var source = new Bitmap(4, 4);
                evidence.SaveSource(source);
                using (evidence.Push())
                {
                    if (!ReferenceEquals(ScreenshotTranslationEvidence.Current, evidence))
                        throw new Exception("Screenshot evidence did not enter its own async scope.");
                    evidence.RecordTranslationInputs(TranslationRouteKind.Machine, [request]);
                    evidence.RecordTranslationOutputs(new TranslationBatchOutcome([result],
                        new TranslationExecutionTrace(TranslationRouteKind.Machine, 0, 0, 0)));
                    using (TranslationWireTrace.Push(evidence.RecordWire))
                        TranslationWireTrace.Record("provider_request", new { payload = "text only" });
                }
                if (ScreenshotTranslationEvidence.Current is not null)
                    throw new Exception("Screenshot evidence escaped its async scope.");
                evidence.RecordOutcome(outcome);
                evidence.RecordOverlayDecision(true, "accepted-by-overlay", 1);
                evidence.SaveRenderedLayer(() => SelectionTranslationLayerRenderer.RenderOverlay(4, 4, [layout]));
                if (evidence.WriteFailures != 0)
                    throw new Exception("Evidence failed to write its artifacts.");
            }

            var manifestPath = Path.Combine(directory, "manifest.json");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var value = manifest.RootElement;
            if (!value.GetProperty("complete").GetBoolean()
                || value.GetProperty("run_id").GetString() != runId
                || value.GetProperty("visibility_claim").GetString() != "offscreen-only"
                || value.GetProperty("source_sha256").GetString() != HashFile(Path.Combine(directory, "source.png"))
                || value.GetProperty("rendered_layer_sha256").GetString()
                    != HashFile(Path.Combine(directory, "rendered-layer.png"))
                || File.ReadAllText(Path.Combine(directory, "manifest.sha256")).Trim()
                    != HashFile(manifestPath))
                throw new Exception("Screenshot evidence manifest lost a file or made a desktop claim.");
            var eventsPath = Path.Combine(directory, "events.jsonl");
            if (value.GetProperty("events_sha256").GetString() != HashFile(eventsPath))
                throw new Exception("Screenshot evidence event hash is wrong.");
            var events = File.ReadAllLines(eventsPath).Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                if (events.Length != value.GetProperty("event_count").GetInt32()
                    || events.Where((item, index) => item.RootElement.GetProperty("sequence").GetInt32()
                        != index + 1).Any())
                    throw new Exception("Screenshot evidence sequence is incomplete.");
                var kinds = events.Select(item => item.RootElement.GetProperty("kind").GetString()).ToArray();
                if (!kinds.Contains("translation_inputs") || !kinds.Contains("translation_outputs")
                    || !kinds.Contains("wire_provider_request") || !kinds.Contains("pipeline_outcome")
                    || !kinds.Contains("overlay_applied") || !kinds.Contains("rendered_layer_saved"))
                    throw new Exception("Screenshot evidence lost a required stage.");
                var projected = events.Single(item => item.RootElement.GetProperty("kind").GetString()
                    == "pipeline_outcome").RootElement.GetProperty("data");
                if (projected.GetProperty("ocr_blocks")[0].GetProperty("Text").GetString() != "Hello Codex"
                    || projected.GetProperty("regions")[0].GetProperty("result")
                        .GetProperty("Text").GetString() != "你好 Codex"
                    || projected.GetProperty("layouts")[0].GetProperty("TextLayout")
                        .GetProperty("FontSizePixels").GetDouble() != 12)
                    throw new Exception("Screenshot outcome projection lacks OCR, translation or typography.");
            }
            finally
            {
                foreach (var item in events) item.Dispose();
            }

            var ordinaryLogRoot = Path.Combine(root, "ordinary-log");
            new JsonlTranslationRunLogWriter(ordinaryLogRoot).Write(new TranslationRunLog(
                TranslationRunLog.CurrentSchemaVersion, runId, DateTimeOffset.UtcNow,
                "local", "1.0.0", "abc", "Machine", "completed", "None",
                4, 4, false, 1, 1, 1, 0, 0, 0, 0,
                new TranslationStageTiming(0, 1, 1, 1, 3), "none", "overlay-refused"));
            using var ordinaryLog = JsonDocument.Parse(File.ReadAllText(
                Directory.GetFiles(ordinaryLogRoot, "translation-*.jsonl").Single()));
            if (ordinaryLog.RootElement.GetProperty("status").GetString() != "completed"
                || ordinaryLog.RootElement.GetProperty("overlayDecision").GetString() != "overlay-refused")
                throw new Exception("The ordinary run log conflated pipeline completion with overlay acceptance.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static void RecordsActualFallbackSubsetAndMarksBrokenLayerIncomplete()
    {
        var root = NewRoot();
        try
        {
            string directory;
            using (var evidence = ScreenshotTranslationEvidence.StartForTest(
                root, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow))
            {
                directory = evidence.DirectoryPath;
                evidence.RecordContext(new { route = "DeepSeekFlash" });
                using var source = new Bitmap(4, 4);
                evidence.SaveSource(source);
                var fallback = evidence.ObserveMachineProvider(new FakeMachineProvider(), "machine-fallback");
                var failedSibling = new TranslationRequest("auto", "简体中文", "Only failed sibling");
                var results = fallback.TranslateManyAsync([failedSibling], CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (results.Count != 1 || results[0].Text != "兜底译文")
                    throw new Exception("Observed machine provider changed its result.");
                evidence.RecordOutcome(new ApplicationTranslationOutcome(false,
                    TranslationPipelineStage.Translation, "translation-failed", [], [], [], []));
                evidence.RecordOverlayDecision(true, "accepted-by-overlay", 1);
                Directory.CreateDirectory(Path.Combine(directory, "rendered-layer.png"));
                evidence.SaveRenderedLayer(CreateLayer);
                if (evidence.WriteFailures == 0)
                    throw new Exception("A failed layer write was not counted.");
            }
            using var manifest = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(directory, "manifest.json")));
            if (manifest.RootElement.GetProperty("complete").GetBoolean()
                || manifest.RootElement.GetProperty("write_failures").GetInt32() == 0)
                throw new Exception("Broken screenshot evidence was claimed complete.");
            var startedLine = File.ReadAllLines(Path.Combine(directory, "events.jsonl"))
                .First(line => line.Contains("\"kind\":\"provider_call_started\"", StringComparison.Ordinal));
            using var started = JsonDocument.Parse(startedLine);
            var data = started.RootElement.GetProperty("data");
            if (data.GetProperty("role").GetString() != "machine-fallback"
                || data.GetProperty("requests").GetArrayLength() != 1
                || data.GetProperty("requests")[0].GetProperty("OcrText").GetString()
                    != "Only failed sibling")
                throw new Exception("Fallback evidence did not preserve the actual request subset.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BitmapSource CreateLayer() => BitmapSource.Create(4, 4, 96, 96,
        PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 4 * 4);

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClipRay-screenshot-evidence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class FakeMachineProvider : IMachineTranslationProvider
    {
        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
            IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranslationResult>>(
                requests.Select(_ => new TranslationResult(true, "兜底译文", "completed")).ToArray());
    }
}

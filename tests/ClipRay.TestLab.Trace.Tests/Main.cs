using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using ClipRay.TestLab;

internal static class TraceEvidenceTests
{
    [STAThread]
    private static int Main()
    {
        CapturesRequestsFromTheRealOrchestrator().GetAwaiter().GetResult();
        FiltersWireSecretsAndMarksMissingArtifactIncomplete();
        RejectsNetworkRoots();
        Console.WriteLine("TESTLAB_DETAIL_TRACE_TESTS_PASSED");
        return 0;
    }

    private static async Task CapturesRequestsFromTheRealOrchestrator()
    {
        var root = NewRoot();
        try
        {
            var runId = "r" + Guid.NewGuid().ToString("N");
            var caseId = "synthetic";
            var before = TestLabArtifactPaths.Create(root, runId, caseId).BeforePath;
            Directory.CreateDirectory(Path.GetDirectoryName(before)!);
            using var source = new Bitmap(220, 90);
            using (var drawing = Graphics.FromImage(source)) drawing.Clear(Color.White);
            source.Save(before, ImageFormat.Png);
            var bounds = new PixelRect(12, 12, 180, 36);
            var word = new OcrWord("Hello, Codex", bounds);
            var block = new OcrTextBlock(word.Text, bounds, [word]);
            IReadOnlyList<TranslationRequest>? actualRequests = null;
            var orchestrator = new TranslationApplicationOrchestrator(
                (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "ocr-completed")),
                (route, requests, _) =>
                {
                    actualRequests = requests;
                    var results = requests.Select((_, index) =>
                        new TranslationResult(true, "你好，Codex " + index, "completed")).ToArray();
                    return Task.FromResult(new TranslationBatchOutcome(results,
                        new TranslationExecutionTrace(route, 0, 0, 0)));
                },
                (_, regions, results) => regions.Select((region, index) =>
                    new InPlaceLayoutItem(region.Lines[0], results[index].Text, region.Bounds,
                        InPlaceBackgroundMode.FlatSampled, Color.White, Color.Black,
                        TextLayout: new TranslationTextLayout(true, 18, 22,
                            SourceFontWeight.Normal, SourceFontCategory.SansSerif, false,
                            SourceTextAlignment.Left, 0, "Segoe UI"), SourceRegion: region)).ToArray());
            string directory;
            using (var trace = TestLabDetailTrace.Start(root, runId, caseId, "local", DateTimeOffset.UtcNow))
            {
                directory = trace.DirectoryPath;
                trace.RecordContext("fixture.png", HashFile(before), before, source.Width, source.Height,
                    "简体中文", new string('a', 64), new string('b', 64));
                ApplicationTranslationOutcome outcome;
                using (trace.ObservePipeline())
                    outcome = await orchestrator.ExecuteAsync(source, TranslationRouteKind.LocalLmt,
                        "简体中文", static blocks => blocks, CancellationToken.None);
                if (actualRequests is null || actualRequests.Count == 0)
                    throw new Exception("The production orchestrator emitted no translation request.");
                trace.RecordOutcome(outcome);
                var after = Path.Combine(Path.GetDirectoryName(before)!, "after-local.png");
                TestLabVisualArtifactWriter.Write(source, outcome.LayoutItems, outcome.IsSuccess,
                    "synthetic", after);
                trace.RecordRenderedArtifact(after);
            }
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            var summary = manifest.RootElement;
            var afterPng = Path.Combine(Path.GetDirectoryName(before)!, "after-local.png");
            if (!summary.GetProperty("complete").GetBoolean()
                || summary.GetProperty("visibility_claim").GetString() != "offscreen-only"
                || summary.GetProperty("source_png_sha256").GetString() != HashFile(before)
                || summary.GetProperty("rendered_png_sha256").GetString() != HashFile(afterPng)
                || File.ReadAllText(Path.Combine(directory, "manifest.sha256")).Trim()
                    != HashFile(Path.Combine(directory, "manifest.json")))
                throw new Exception("A complete local trace lost its image or manifest binding.");
            var assessed = TestLabRunner.AssessDetailTrace(root, runId, caseId, "local", before, afterPng);
            if (!assessed.Complete || assessed.Path != $"runs/{runId}/{caseId}/local")
                throw new Exception("A complete detailed trace was not linked to its route report.");
            var failedQualityRoute = new TestLabRouteReport("local", true, false, "none", "completed",
                1, 1, 0, Path.GetRelativePath(root, afterPng), [], DetailTrace: assessed);
            var failedQualityCase = new TestLabCaseReport(caseId, "fixture.png", HashFile(before),
                Path.GetRelativePath(root, before), "all-translated", [failedQualityRoute]);
            if (TestLabRunner.ResolveRunOutcome([failedQualityCase], true).Status != "failed")
                throw new Exception("Complete trace was incorrectly promoted to a quality pass.");
            var events = ReadEvents(directory);
            try
            {
                var requests = Event(events, "pipeline_translation_requests")
                    .GetProperty("data").GetProperty("requests");
                if (requests.GetArrayLength() != actualRequests!.Count
                    || requests[0].GetProperty("OcrText").GetString() != actualRequests[0].OcrText
                    || requests[0].GetProperty("text_kind").GetString()
                        != actualRequests[0].TextKind.ToString())
                    throw new Exception("Detail trace reconstructed requests instead of recording actual inputs.");
                var ocr = Event(events, "pipeline_ocr_completed").GetProperty("data")
                    .GetProperty("blocks")[0];
                if (ocr.GetProperty("Words")[0].GetProperty("Text").GetString() != word.Text
                    || Event(events, "pipeline_regions_grouped").GetProperty("data").GetArrayLength() == 0
                    || Event(events, "pipeline_outcome").GetProperty("data")
                        .GetProperty("layouts")[0].GetProperty("TextLayout")
                        .GetProperty("FontSizePixels").GetDouble() != 18)
                    throw new Exception("OCR, grouped regions or actual layout were omitted.");
                if (summary.GetProperty("events_sha256").GetString()
                    != HashFile(Path.Combine(directory, "events.jsonl")))
                    throw new Exception("Event hash does not bind the detailed trace.");
            }
            finally { foreach (var item in events) item.Dispose(); }
            File.WriteAllText(Path.Combine(directory, "manifest.sha256"), new string('0', 64));
            if (TestLabRunner.AssessDetailTrace(root, runId, caseId, "local", before, afterPng).Complete)
                throw new Exception("A changed manifest digest was accepted as complete evidence.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void FiltersWireSecretsAndMarksMissingArtifactIncomplete()
    {
        var root = NewRoot();
        try
        {
            var runId = "r" + Guid.NewGuid().ToString("N");
            var before = TestLabArtifactPaths.Create(root, runId, "wire").BeforePath;
            Directory.CreateDirectory(Path.GetDirectoryName(before)!);
            using (var source = new Bitmap(2, 2)) source.Save(before, ImageFormat.Png);
            string directory;
            using (var trace = TestLabDetailTrace.Start(root, runId, "wire", "deepseek", DateTimeOffset.UtcNow))
            {
                directory = trace.DirectoryPath;
                trace.RecordContext("fixture.png", HashFile(before), before, 2, 2,
                    "简体中文", new string('a', 64), new string('b', 64));
                trace.RecordWire("provider_request", new
                {
                    Provider = "DeepSeek", BlockCount = 1, Payload = "request body",
                    Authorization = "Bearer do-not-record-me"
                });
                trace.RecordWire("provider_response", new
                {
                    Provider = "DeepSeek", Content = "response body", FinishReason = "stop",
                    Usage = new { input_tokens = 10 }, ApiKey = "do-not-record-me"
                });
                trace.RecordOutcome(new ApplicationTranslationOutcome(false,
                    TranslationPipelineStage.Translation, "runtime-failed", [], [], [], []));
                trace.RecordRenderedArtifact(Path.Combine(root, "missing-render.png"));
                if (trace.WriteFailures == 0)
                    throw new Exception("Missing rendered evidence did not count as a write failure.");
            }
            var events = File.ReadAllText(Path.Combine(directory, "events.jsonl"));
            if (events.Contains("do-not-record-me", StringComparison.Ordinal)
                || !events.Contains("request body", StringComparison.Ordinal)
                || !events.Contains("response body", StringComparison.Ordinal))
                throw new Exception("Wire evidence leaked credentials or lost payload and response.");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            if (manifest.RootElement.GetProperty("complete").GetBoolean()
                || manifest.RootElement.GetProperty("write_failures").GetInt32() == 0)
                throw new Exception("An incomplete trace was claimed complete.");
            var after = Path.Combine(Path.GetDirectoryName(before)!, "after-deepseek.png");
            File.Copy(before, after);
            var assessed = TestLabRunner.AssessDetailTrace(root, runId, "wire", "deepseek", before, after);
            if (assessed.Complete || assessed.Problem != "manifest-incomplete")
                throw new Exception("The route report missed an incomplete trace manifest.");
            var route = new TestLabRouteReport("deepseek", true, true, "none", "completed",
                1, 1, 0, Path.GetRelativePath(root, after), [], DetailTrace: assessed);
            var testCase = new TestLabCaseReport("wire", "fixture.png", HashFile(before),
                Path.GetRelativePath(root, before), "all-translated", [route]);
            var (status, errors) = TestLabRunner.ResolveRunOutcome([testCase], true);
            if (status != "evidence-incomplete" || errors.Count != 1
                || !errors[0].Contains("wire/deepseek", StringComparison.Ordinal))
                throw new Exception("Incomplete requested evidence left the TestLab run successful.");
            if (TestLabRunner.ResolveRunOutcome([testCase], false).Status != "expectation-met")
                throw new Exception("An ordinary report started requiring optional detail evidence.");
            var report = new TestLabRunReport(runId, DateTimeOffset.UtcNow, "deepseek",
                status, errors, [testCase]);
            TestLabReportWriter.Write(Path.Combine(root, "reports"), report);
            var reportJson = File.ReadAllText(Path.Combine(root, "reports", runId + ".json"));
            var reportMarkdown = File.ReadAllText(Path.Combine(root, "reports", runId + ".md"));
            if (!reportJson.Contains("\"DetailTrace\"", StringComparison.Ordinal)
                || !reportJson.Contains("\"evidence-incomplete\"", StringComparison.Ordinal)
                || !reportMarkdown.Contains("evidence=incomplete", StringComparison.Ordinal))
                throw new Exception("Run report did not show the incomplete detailed evidence.");
            var ordinaryRunId = runId + "-ordinary";
            TestLabReportWriter.Write(Path.Combine(root, "reports"), new TestLabRunReport(
                ordinaryRunId, DateTimeOffset.UtcNow, "deepseek", "expectation-met", [],
                [testCase with { Routes = [route with { DetailTrace = null }] }]));
            if (File.ReadAllText(Path.Combine(root, "reports", ordinaryRunId + ".json"))
                    .Contains("\"DetailTrace\"", StringComparison.Ordinal)
                || File.ReadAllText(Path.Combine(root, "reports", ordinaryRunId + ".md"))
                    .Contains("detail-trace:", StringComparison.Ordinal))
                throw new Exception("Ordinary reports gained a detail trace field.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void RejectsNetworkRoots()
    {
        try
        {
            _ = TestLabDetailTrace.RequireLocalRoot(@"\\server\share");
            throw new Exception("UNC root was accepted for private detail evidence.");
        }
        catch (ArgumentException exception) when (exception.Message.Contains("detail-trace", StringComparison.Ordinal))
        {
        }
    }

    private static JsonDocument[] ReadEvents(string directory) =>
        File.ReadAllLines(Path.Combine(directory, "events.jsonl"))
            .Select(static line => JsonDocument.Parse(line)).ToArray();

    private static JsonElement Event(JsonDocument[] events, string kind) =>
        events.Single(item => item.RootElement.GetProperty("kind").GetString() == kind).RootElement;

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-testlab-detail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

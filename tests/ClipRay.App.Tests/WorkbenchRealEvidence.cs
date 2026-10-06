using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using ClipRay.TestLab;

internal static class WorkbenchRealEvidence
{
    // Explicit paths only; private source and translated text stay in the requested local output.
    public static int Run(string[] args)
    {
        var commonOcr = args[0] == "--workbench-real-common";
        var chatTemplate = args[0] == "--workbench-real-lmt-chat";
        var hyMt2 = args[0] is "--workbench-real-hymt2" or "--workbench-real-hymt2-terms";
        var candidateProtected = args[0] is "--workbench-real-qwen-protected" or "--workbench-real-hymt2-terms";
        var candidateHttp = hyMt2 || candidateProtected || args[0] == "--workbench-real-qwen-http";
        var candidateContext = candidateHttp || args[0] == "--workbench-real-qwen-context";
        var candidate = candidateContext || args[0] == "--workbench-real-qwen";
        var useLmt = candidate || args[0] is "--workbench-real-lmt" or "--workbench-real-lmt-chat";
        if (useLmt ? args.Length != 7 : args.Length is not (8 or 9)) throw new ArgumentException("Machine: root output case sourceLanguage python worker model [gpu-directory]; LMT: root output case sourceLanguage model cli");
        var root = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
        var manifest = TestLabManifest.Load(root);
        if (manifest.Errors.Count != 0) throw new InvalidDataException("Invalid manifest");
        var test = manifest.Cases.Single(item => item.Id == args[3]);
        var oracle = TestLabQualityOracle.Load(root) ?? throw new InvalidDataException("Independent oracle required");
        var imagePath = Path.Combine(root, test.Image);
        if (!useLmt && !MachineTranslationModelPaths.IsCommonLanguageModel(args[7]))
            throw new InvalidDataException("This evidence entry requires the complete common-language model set.");
        using var source = new Bitmap(imagePath);
        using var ocr = commonOcr ? OcrService.CreateForCommonMachine() : new OcrService();
        var machineTrace = useLmt ? null : new RecordingMachineProcess(new BundledMachineTranslationProcess(
            args[5], args[6], args[7], TimeSpan.FromSeconds(60), keepAlive: true, gpuLibraryDirectory: args.Length == 9 ? args[8] : null));
        using var provider = useLmt ? null : new LocalMachineTranslationProvider(machineTrace!, supportsCommonLanguages: true);
        var promptProbe = chatTemplate ? new ChatTemplateProbe() : null;
        using var lmt = useLmt && !candidate ? new LocalTranslationProvider(new LocalLmtOptions(args[5], args[6],
            ExpectedModelSha256: LocalLmtModelCatalog.ExpectedSha256For(args[5])),
            oneShotRunner: promptProbe) : null;
        var route = useLmt ? TranslationRouteKind.LocalLmt : TranslationRouteKind.Machine;
        using var http = candidateHttp ? new QwenHttpWorkbenchProbe(args[5], args[6], output, candidateProtected, hyMt2) : null;
        var qwen = candidate && !candidateHttp ? new QwenWorkbenchProbe(args[5], args[6], output, candidateContext) : null;
        var orchestrator = new TranslationApplicationOrchestrator(ocr.RecognizeAsync,
            (_, requests, token) => candidateHttp ? http!.TranslateManyAsync(requests, token) : candidate ? qwen!.TranslateManyAsync(requests, token)
                : useLmt ? lmt!.TranslateManyAsync(requests, token) : provider!.TranslateManyAsync(requests, token));
        var bounds = new PixelRect(0, 0, source.Width, source.Height);
        using var document = new TranslationWorkbenchDocument(source, bounds, bounds,
            route, test.TargetLanguage, orchestrator, blocks => blocks, commonMachineModel: !useLmt);
        var watch = Stopwatch.StartNew();
        document.RecognizeAsync().GetAwaiter().GetResult();
        var recognitionMilliseconds = watch.ElapsedMilliseconds;
        long? warmRecognitionMilliseconds = null;
        if (commonOcr)
        {
            watch.Restart();
            var warm = ocr.RecognizeAsync(source, CancellationToken.None).GetAwaiter().GetResult();
            warmRecognitionMilliseconds = watch.ElapsedMilliseconds;
            if (!warm.IsSuccess) throw new InvalidOperationException("Warm common OCR failed.");
        }
        var regionMilliseconds = new Dictionary<Guid, long>();
        var session = document.Session ?? throw new InvalidOperationException($"Recognition: {document.State}");
        foreach (var region in session.Regions)
        {
            session.Configure(region.Id, args[4], test.TargetLanguage, route);
            watch.Restart();
            document.TranslateAsync(region.Id).GetAwaiter().GetResult();
            regionMilliseconds[region.Id] = watch.ElapsedMilliseconds;
        }
        Directory.CreateDirectory(output);
        using var rendered = WorkbenchImageExport.Render(document);
        rendered.Save(Path.Combine(output, test.Id + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        var regions = session.Regions;
        var results = regions.Select(region => new TranslationResult(
            region.State is WorkbenchRegionState.Translated or WorkbenchRegionState.LayoutRejected or WorkbenchRegionState.Preserved,
            region.State == WorkbenchRegionState.Preserved ? region.Draft : region.Translation,
            region.State == WorkbenchRegionState.Preserved ? "machine-preserved" : region.Detail)).ToArray();
        var translated = results.All(result => result.IsSuccess);
        var layoutComplete = regions.All(region => region.State != WorkbenchRegionState.LayoutRejected);
        var outcome = new ApplicationTranslationOutcome(translated && layoutComplete,
            !translated ? TranslationPipelineStage.Translation : !layoutComplete ? TranslationPipelineStage.Layout : TranslationPipelineStage.None,
            "workbench-evidence", regions.SelectMany(region => region.Original.Lines).ToArray(),
            regions.Select(region => region.Original).ToArray(), results,
            regions.Where(region => region.Layout is not null).Select(region => region.Layout!).ToArray());
        var assessment = oracle.Assess(test.Id, Hash(imagePath), test.TargetLanguage, outcome, true);
        File.WriteAllText(Path.Combine(output, test.Id + ".json"), JsonSerializer.Serialize(new
        {
            Route = route.ToString(), CandidateProbe = candidate, CandidateContext = candidateContext && !hyMt2, CandidateHttp = candidateHttp, CandidateProtected = candidateProtected, ChatTemplateProbe = chatTemplate, PromptProbeCalls = promptProbe?.Calls,
            CandidateProfile = hyMt2 ? (candidateProtected ? "Hy-MT2-1.8B-Q8_0-source-terms-official-sampling" : "Hy-MT2-1.8B-Q8_0-official-template-and-sampling") : null,
            SourceLanguage = args[4], ImageSha256 = Hash(imagePath),
            RecognitionMilliseconds = recognitionMilliseconds,
            OcrProfile = commonOcr ? "live-common-machine" : "screenshot-workbench-multilingual",
            WarmRecognitionMilliseconds = warmRecognitionMilliseconds,
            TranslationAndLayoutMilliseconds = regionMilliseconds.Values.Sum(),
            AppSha256 = Hash(typeof(TranslationWorkbenchDocument).Assembly.Location), WorkerSha256 = Hash(args[6]),
            ModelSha256 = Hash(useLmt ? args[5] : Path.Combine(args[7], "model.bin")),
            OcrEvidence = outcome.OcrBlocks.SelectMany(block => block.Words).Select(word => word.Evidence).Distinct(),
            Regions = regions.Select(region => new { region.Id, region.Draft, region.Translation, State = region.State.ToString(), region.Detail, region.Original.Bounds, Milliseconds = regionMilliseconds[region.Id] }),
            Attempts = session.Attempts,
            MachineProcessCalls = machineTrace?.Calls,
            Assessment = assessment
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{test.Id}: {regions.Count} regions; content={assessment?.ContentQuality}; layout={assessment?.LayoutQuality}; evidence={output}");
        return assessment?.ContentQuality == "screened-needs-human-review" && assessment.LayoutQuality != "failed" ? 0 : 1;
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private sealed class RecordingMachineProcess(IMachineTranslationProcess inner) : IMachineTranslationProcess, IDisposable
    {
        // Private input/output remains in the explicitly selected local evidence directory.
        public List<object> Calls { get; } = [];
        public async Task<MachineTranslationProcessResult> TranslateWithEvidenceAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            var result = await inner.TranslateWithEvidenceAsync(request, token).ConfigureAwait(false);
            Calls.Add(new { Request = request, Result = result, Milliseconds = watch.ElapsedMilliseconds });
            return result;
        }
        public async Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            (await TranslateWithEvidenceAsync(request, token).ConfigureAwait(false)).Texts;
        public void Dispose() { if (inner is IDisposable disposable) disposable.Dispose(); }
    }
    private sealed class ChatTemplateProbe : ILocalLmtOneShotRunner
    {
        public int Calls { get; private set; }
        public Task<string> TranslateAsync(LocalLmtSessionStart start, string prompt, CancellationToken token)
        {
            Calls++;
            return new LocalLmtOneShotRunner().TranslateAsync(start with
            {
                Arguments = start.Arguments.Replace("--no-conversation", "--conversation", StringComparison.Ordinal)
            }, prompt, token);
        }
    }
}

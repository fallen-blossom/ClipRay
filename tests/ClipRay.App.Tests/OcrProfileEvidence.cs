using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using RapidOcrNet;
using SkiaSharp;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.TestLab;

internal static class OcrProfileEvidence
{
    public static int ProbeCrop(string[] args)
    {
        if (args.Length != 7 || args[6] is not ("visual" or "common")
            || !int.TryParse(args[2], out var left) || !int.TryParse(args[3], out var top)
            || !int.TryParse(args[4], out var width) || !int.TryParse(args[5], out var height)
            || width <= 0 || height <= 0)
            throw new ArgumentException("Expected --ocr-crop <image> <left> <top> <width> <height> <visual|common>.");
        using var source = new Bitmap(Path.GetFullPath(args[1]));
        var crop = new Rectangle(left, top, width, height);
        if (!new Rectangle(0, 0, source.Width, source.Height).Contains(crop))
            throw new ArgumentOutOfRangeException(nameof(args), "OCR crop must stay inside its source image.");
        using var bitmap = source.Clone(crop, source.PixelFormat);
        using var ocr = args[6] == "visual" ? new OcrService() : OcrService.CreateForCommonMachine();
        var result = ocr.RecognizeAsync(bitmap, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine(JsonSerializer.Serialize(new { profile = args[6], crop, result.IsSuccess,
            result.Status, blocks = result.Blocks.Select(block => new { block.Text, block.Bounds,
                words = block.Words.Select(word => new { word.Text, word.Bounds }) }) }));
        return result.IsSuccess ? 0 : 1;
    }

    public static int ReplayRegions(string[] args)
    {
        if (args.Length != 4 && !(args.Length == 5 && args[4] == "detector-lines"))
            throw new ArgumentException("Expected OCR report image output [detector-lines]");
        using var report = JsonDocument.Parse(File.ReadAllText(args[1]));
        if (report.RootElement.GetProperty("ImageSha256").GetString() != Hash(args[2]))
            throw new InvalidDataException("Image differs from the OCR evidence.");
        var blocks = report.RootElement.GetProperty("Rows")[0].GetProperty("Blocks")
            .Deserialize<OcrTextBlock[]>() ?? throw new InvalidDataException("Missing OCR blocks");
        var preserveDetectorLines = args.Length == 5;
        if (preserveDetectorLines)
        {
            if (blocks.Any(block => block.Words.Count == 0 || block.Words.Any(word => word.Evidence?.EngineId != "rapidocr-common-script")))
                throw new InvalidDataException("Detector-line experiment requires common-script line evidence for every word.");
            blocks = blocks.SelectMany(block => block.Words)
                .Select(word => new OcrTextBlock(word.Text, word.Bounds, [word])).ToArray();
        }
        using var bitmap = new Bitmap(args[2]);
        var regions = VisualTextRegionBuilder.Build(blocks, bitmap);
        File.WriteAllText(args[3], JsonSerializer.Serialize(new
        {
            SourceReportSha256 = Hash(args[1]), ImageSha256 = Hash(args[2]),
            AppSha256 = Hash(typeof(VisualTextRegionBuilder).Assembly.Location), Regions = regions,
            PreserveDetectorLinesExperiment = preserveDetectorLines,
            Scope = preserveDetectorLines
                ? "Experimental ungrouped detector lines passed to production region builder; not production OCR behavior; no translation."
                : "Production region builder replay over recorded OCR and its original local image; no translation."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Region replay completed: {blocks.Length} OCR blocks -> {regions.Count} regions.");
        return 0;
    }

    public static int Run(string[] args)
    {
        if (args.Length != 5 || args[4] is not ("production" or "common" or "windows-en" or "common-korean" or "multilingual" or "multilingual4" or "multilingual-no-spin" or "traced" or "traced-no-spin" or "traced4-no-spin" or "primary-only"))
            throw new ArgumentException("Expected root output case and a supported OCR profile (including traced, traced-no-spin, traced4-no-spin, primary-only).");
        var root = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        var manifest = TestLabManifest.Load(root);
        if (manifest.Errors.Count != 0) throw new InvalidDataException("Invalid manifest");
        var test = manifest.Cases.Single(item => item.Id == args[3]);
        var image = Path.Combine(root, test.Image);
        Directory.CreateDirectory(output);
        using var bitmap = new Bitmap(image);
        var stages = new List<object>();
        void Stage(string runtime, OcrRuntimeStageTrace stage) => stages.Add(new { Runtime = runtime, stage.Name, stage.Milliseconds, stage.Count });
        void Input(string runtime, SKBitmap input, RapidOcrOptions options) => stages.Add(DetectorInput(runtime, input, options));
        RapidOcrRuntime Rapid(string runtime, bool korean = false, int? threads = null, bool noSpin = true,
            Action<OcrResult>? native = null) => new(useKoreanSupplement: korean, threadCountOverride: threads,
                disableSpinning: noSpin, observeResult: native, observeStage: stage => Stage(runtime, stage),
                observeDetectorInput: (input, options) => Input(runtime, input, options));
        using var ocr = args[4] == "production" ? new OcrService()
            : args[4] == "common" ? new OcrService(() => new CommonScriptOcrRuntime(
                observeStage: stage => Stage("common", stage), observeDetectorInput: (input, options) => Input("common", input, options)))
            : args[4] == "windows-en" ? OcrService.CreateForWindowsLanguage("en-US")
            : args[4] == "common-korean" ? new OcrService(() => new CommonScriptOcrRuntime(includeKorean: true,
                observeCandidates: (bounds, choices) => stages.Add(new { Name = "script-candidates", Bounds = bounds, Choices = choices }),
                observeStage: stage => Stage("common", stage), observeDetectorInput: (input, options) => Input("common", input, options)), "Experimental shared detection with Latin, CJK and Korean recognition.")
            : args[4] == "primary-only" ? new OcrService(() => new TimedRuntime("primary", Rapid("primary", native: result =>
                {
                    stages.Add(new { Name = "native-primary", result.DbNetTime, result.DetectTime,
                        CrnnTimes = result.TextBlocks.Select(block => block.CrnnTime).ToArray(),
                        BlockTimes = result.TextBlocks.Select(block => block.BlockTime).ToArray() });
                    Console.WriteLine($"  native-primary: DbNetTime={result.DbNetTime}, DetectTime={result.DetectTime}, CrnnTimeSum={result.TextBlocks.Sum(block => block.CrnnTime)}");
                }), stages), "Isolated primary OCR experiment; no Korean supplement or Windows fallback.")
            : args[4] is "traced" or "traced-no-spin" or "traced4-no-spin" ? new OcrService(() => new FastFirstOcrRuntime(
                new ScriptSupplementalOcrRuntime(
                    new TimedRuntime("primary", Rapid("primary", threads: args[4] == "traced4-no-spin" ? 4 : null, noSpin: args[4] != "traced"), stages),
                    new TimedRuntime("korean", Rapid("korean", korean: true, threads: args[4] == "traced4-no-spin" ? 4 : null, noSpin: args[4] != "traced"), stages),
                    ScriptSupplementalOcr.ContainsHangul), new WindowsOcrRuntime()))
            : new OcrService(() => new FastFirstOcrRuntime(
                new ScriptSupplementalOcrRuntime(Rapid("primary", threads: args[4] == "multilingual4" ? 4 : null,
                        noSpin: args[4] != "multilingual4"),
                    Rapid("korean", korean: true, threads: args[4] == "multilingual4" ? 4 : null,
                        noSpin: args[4] != "multilingual4"), ScriptSupplementalOcr.ContainsHangul),
                new TimedRuntime("windows-fallback", new WindowsOcrRuntime(), stages)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var rows = new List<object>();
        var success = true;
        for (var index = 0; index < 4; index++)
        {
            stages.Clear();
            var watch = Stopwatch.StartNew();
            OcrRecognitionTrace? serviceTrace = null;
            var result = ocr.RecognizeAsync(bitmap, timeout.Token, trace => serviceTrace = trace).GetAwaiter().GetResult();
            watch.Stop();
            success &= result.IsSuccess;
            rows.Add(new { Index = index, ColdService = index == 0, Milliseconds = watch.ElapsedMilliseconds,
                result.IsSuccess, result.Status, result.Blocks, Quality = AssessSyntheticTruth(root, test.Id, result.Blocks),
                Service = serviceTrace, Stages = stages.ToArray() });
            Console.WriteLine($"{args[4]} iteration {index}: {watch.ElapsedMilliseconds} ms; success={result.IsSuccess}; blocks={result.Blocks.Count}");
        }
        File.WriteAllText(Path.Combine(output, "ocr-profile.json"), JsonSerializer.Serialize(new
        {
            Profile = args[4], Case = test.Id, ImageSha256 = Hash(image),
            AppSha256 = Hash(typeof(OcrService).Assembly.Location), TestSha256 = Hash(typeof(OcrProfileEvidence).Assembly.Location),
            RapidOcrAssemblySha256 = Hash(typeof(RapidOcr).Assembly.Location),
            Models = ModelFiles(), CpuLogicalCount = Environment.ProcessorCount,
            Threads = OcrExecutionPolicy.SelectThreads(Environment.ProcessorCount), SpinningDisabled = args[4] is not ("traced" or "multilingual4"),
            bitmap.Width, bitmap.Height, Rows = rows,
            Scope = "Local synthetic/supplied image OCR only; first service call is process/service cold, not machine cold-cache. Input tensor shape uses the exact pinned library preparation method, not interception of native ONNX execution. Its extra probe cost is recorded separately. No translation or screen capture; App global gate/foreground commit not measured."
        }, new JsonSerializerOptions { WriteIndented = true }));
        ocr.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return success ? 0 : 1;
    }

    private static object? AssessSyntheticTruth(string root, string caseId, IReadOnlyList<OcrTextBlock> blocks)
    {
        var truthFile = Path.Combine(root, "truth.json");
        if (!File.Exists(truthFile)) return null;
        using var truth = JsonDocument.Parse(File.ReadAllText(truthFile));
        var item = truth.RootElement.GetProperty("Cases").EnumerateArray().Single(test => test.GetProperty("Id").GetString() == caseId);
        var words = blocks.SelectMany(block => block.Words).ToArray();
        var lines = item.GetProperty("Lines").EnumerateArray().Select(line =>
        {
            var expected = line.GetProperty("Text").GetString()!;
            var left = line.GetProperty("X").GetDouble(); var top = line.GetProperty("Y").GetDouble();
            var right = left + line.GetProperty("Width").GetDouble(); var bottom = top + line.GetProperty("Height").GetDouble();
            var matching = words.Where(word => word.Bounds.Top + word.Bounds.Height / 2 >= top - 3
                && word.Bounds.Top + word.Bounds.Height / 2 <= bottom + 3
                && word.Bounds.Right > left && word.Bounds.Left < right).OrderBy(word => word.Bounds.Left).ToArray();
            var actual = string.Join(" ", matching.Select(word => word.Text));
            static string Normalize(string text) => string.Concat(text.Where(char.IsLetterOrDigit)).ToLowerInvariant();
            return new { Expected = expected, Actual = actual, ExactAlphanumericMatch = Normalize(expected) == Normalize(actual),
                HasGeometricCoverage = matching.Length > 0, WordCount = matching.Length };
        }).ToArray();
        return new { ExpectedLines = lines.Length, CoveredLines = lines.Count(line => line.HasGeometricCoverage),
            ExactAlphanumericLines = lines.Count(line => line.ExactAlphanumericMatch), Lines = lines,
            Scope = "Independent generated truth and intended line bands; exact letters/digits ignore punctuation/spacing, so inspect actual strings and fixture image before claiming typography or full character correctness." };
    }

    private static object[] ModelFiles() => new[] { "v5", "v6" }.SelectMany(version =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "models", version);
        return Directory.Exists(root) ? Directory.EnumerateFiles(root).Where(path => Path.GetExtension(path) is ".onnx" or ".txt")
            .OrderBy(path => path).Select(path => (object)new { File = version + "/" + Path.GetFileName(path), Sha256 = Hash(path) }).ToArray() : [];
    }).ToArray();

    private static object DetectorInput(string runtime, SKBitmap bitmap, RapidOcrOptions options)
    {
        // Diagnostic-only inspection of the exact installed 3.0.0 preparation path.
        // Repeating it produces no inference and avoids a second hand-copied size algorithm.
        var watch = Stopwatch.StartNew();
        var prepare = typeof(RapidOcr).GetMethod("PrepareDetectorInput", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Pinned RapidOcr detector preparation method changed.");
        var input = prepare.Invoke(null, [bitmap, options])
            ?? throw new InvalidOperationException("Pinned detector input probe returned null.");
        try
        {
            var scale = (ScaleParam)(input.GetType().GetField("Scale", BindingFlags.Public | BindingFlags.Instance)?.GetValue(input)
                ?? throw new InvalidOperationException("Pinned detector scale field changed."));
            return new { Name = "detector-input", Runtime = runtime, bitmap.Width, bitmap.Height,
                TensorShape = new[] { 1, 3, scale.DstHeight, scale.DstWidth },
                TensorFloatElements = 3L * scale.DstHeight * scale.DstWidth,
                options.Padding, options.ImgResize, options.LimitSideLen, options.UnClipRatio,
                ProbeMilliseconds = watch.Elapsed.TotalMilliseconds };
        }
        finally { ((IDisposable)input).Dispose(); }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class TimedRuntime(string name, IOcrRuntime inner, List<object> stages) : IOcrRuntime, IDisposable, IAsyncDisposable
    {
        public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(Bitmap bitmap, CancellationToken token)
        {
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            try { return await inner.RecognizeAsync(bitmap, token).ConfigureAwait(false); }
            finally
            {
                var milliseconds = watch.ElapsedMilliseconds;
                var cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                stages.Add(new { Name = name, Milliseconds = milliseconds, ProcessCpuMilliseconds = cpuMilliseconds });
                Console.WriteLine($"  {name}: wall={milliseconds} ms, process CPU={cpuMilliseconds:F0} ms");
            }
        }
        public void Dispose() => _ = DisposeAsync();
        public ValueTask DisposeAsync() => new(OcrRuntimeRelease.ReleaseAsync(inner));
    }
}

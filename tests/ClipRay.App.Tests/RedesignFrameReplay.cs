using System.Drawing;
using System.IO;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.App.Overlay;
using ClipRay.App.Acceptance;
using ClipRay.App.LiveTranslation;
using ClipRay.Core.Geometry;

internal static class RedesignFrameReplay
{
    public static int ProbeCommonTranslation(string model, string fixtures)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(fixtures));
        using var provider = new LocalMachineTranslationProvider(new BundledMachineTranslationProcess(
            Path.Combine(Environment.CurrentDirectory,"artifacts","machine-translation","runtime","python.exe"),
            Path.Combine(Environment.CurrentDirectory,"src","ClipRay.App","Translation","machine_translation_worker.py"),
            model, workerTimeout: TimeSpan.FromSeconds(20), keepAlive: true), supportsCommonLanguages: true);
        var failures = 0;
        foreach(var item in cases.RootElement.EnumerateArray())
        {
            var source = item.GetProperty("text").GetString()!;
            var language = item.GetProperty("language").GetString()!;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = provider.TranslateManyAsync([new TranslationRequest(language,"zh",source,
                textKind: TranslationTextKind.Body)],default).GetAwaiter().GetResult()[0];
            var matches = result.IsSuccess && (language == "zh" ? result.Text == source
                : source.Contains("18")
                    ? result.Text.Contains("不要删除") && result.Text.Contains("18") && result.Text.Contains("24")
                    : System.Text.RegularExpressions.Regex.IsMatch(result.Text, @"今天.*可以.*明天(?:我)?(?:不能|无法)"));
            if(!matches) failures++;
            Console.WriteLine(JsonSerializer.Serialize(new {language,source,result.Text,result.Status,matches,milliseconds=timer.ElapsedMilliseconds}));
        }
        return failures == 0 ? 0 : 1;
    }
    public static int ProbeCommonOcr(string fixtures)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(fixtures));
        using var ocr = OcrService.CreateForEnglishMachine();
        var failures = 0;
        foreach(var item in cases.RootElement.EnumerateArray()) {
            var text = item.GetProperty("text").GetString()!;
            var language = item.GetProperty("language").GetString()!;
            using var bitmap = new Bitmap(1800,180);
            using(var graphics = Graphics.FromImage(bitmap)) using(var font = new Font("Segoe UI",24)) {
                graphics.Clear(Color.White);
                using var format = new StringFormat();
                if(language == "ar") format.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
                graphics.DrawString(text,font,Brushes.Black,new RectangleF(30,30,1740,140),format);
            }
            var result = ocr.RecognizeAsync(bitmap,default).GetAwaiter().GetResult();
            var actual = string.Join(" ",result.Blocks.Select(block=>block.Text));
            static string Normalize(string value) => new(value.Normalize(System.Text.NormalizationForm.FormKC)
                .Where(character => char.IsLetterOrDigit(character) && character != '\u0640').ToArray());
            var matches = Normalize(text) == Normalize(actual);
            if(!matches) failures++;
            Console.WriteLine(JsonSerializer.Serialize(new {language,text,actual,matches}));
        }
        return failures == 0 ? 0 : 1;
    }
    public static int ExtractOnnxDictionary(string input, string output)
    {
        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions();
        options.IntraOpNumThreads = 1;
        using var model = new Microsoft.ML.OnnxRuntime.InferenceSession(input, options);
        var metadata = model.ModelMetadata.CustomMetadataMap;
        Console.WriteLine(JsonSerializer.Serialize(new {keys=metadata.Keys}));
        if(!metadata.TryGetValue("character", out var dictionary)) return 2;
        var lines = dictionary.StartsWith('[')
            ? JsonSerializer.Deserialize<string[]>(dictionary)!
            : dictionary.TrimEnd('\r', '\n').Split('\n');
        if(lines.Length is < 100 or > 10000 || lines.Any(line => line.Length > 64)) return 3;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllLines(output, lines, new System.Text.UTF8Encoding(false));
        Console.WriteLine(JsonSerializer.Serialize(new {characters=lines.Length, output}));
        return 0;
    }
    public static int ProbeRapidApi()
    {
        foreach(var type in typeof(RapidOcrNet.RapidOcr).Assembly.GetExportedTypes()) {
            if(!type.FullName!.StartsWith("RapidOcrNet")) continue;
            Console.WriteLine(type.FullName);
            foreach(var property in type.GetProperties()) Console.WriteLine("  property "+property);
            foreach(var field in type.GetFields()) Console.WriteLine("  field "+field);
            foreach(var member in type.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.DeclaredOnly))
                if(!member.IsSpecialName) Console.WriteLine("  "+member);
        }
        return 0;
    }
    public static int ProbeBilingual()
    {
        using var bitmap=new Bitmap(1000,400);
        using(var g=Graphics.FromImage(bitmap)) using(var font=new Font("Microsoft YaHei",24)) {
            g.Clear(Color.Black); g.DrawString("中文直接不翻译，保留原文。",font,Brushes.White,20,40);
            g.DrawString("This English sentence should still translate quickly.",font,Brushes.White,20,140);
        }
        foreach(var tag in Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(language=>language.LanguageTag)) {
            var type=typeof(OcrService).Assembly.GetType("ClipRay.App.Ocr.WindowsOcrRuntime")!;
            var runtime=(IOcrRuntime)Activator.CreateInstance(type,[tag,false,false])!;
            var words=runtime.RecognizeAsync(bitmap,default).GetAwaiter().GetResult();
            Console.WriteLine(JsonSerializer.Serialize(new {tag,words}));
        }
        return 0;
    }
    public static int Run(string path)
    {
        using var source=new Bitmap(Path.GetFullPath(path));
        using var ocr=OcrService.CreateForEnglishMachine();
        var recognition=ocr.RecognizeAsync(source,default).GetAwaiter().GetResult();
        foreach(var block in recognition.Blocks) Console.WriteLine(JsonSerializer.Serialize(new {ocr=block}));
        var regions=VisualTextRegionBuilder.Build(recognition.Blocks,source);
        var requests=VisualTranslationRequestFactory.Create(regions,"简体中文",DeepSeekModels.Flash);
        var workspace = ClipRay.App.Infrastructure.WorkspaceLocator.Find(AppContext.BaseDirectory);
        var model = Environment.GetEnvironmentVariable("CLIPRAY_REPLAY_MODEL_PATH")
            ?? Path.Combine(workspace, "artifacts", "machine-translation", "models", "translate-en_zh-1_9");
        using var provider=new LocalMachineTranslationProvider(new BundledMachineTranslationProcess(
            Path.Combine(workspace, "artifacts", "machine-translation", "runtime", "python.exe"),
            Path.Combine(workspace, "src", "ClipRay.App", "Translation", "machine_translation_worker.py"),
            model));
        var results=provider.TranslateManyAsync(requests,default).GetAwaiter().GetResult();
        var plan=InPlaceLayoutService.BuildRegionPlan(source,regions,results);
        for(var i=0;i<regions.Count;i++) Console.WriteLine(JsonSerializer.Serialize(new {region=regions[i],request=requests[i],result=results[i],layout=plan.Items.FirstOrDefault(item=>item.SourceRegion?.Id==regions[i].Id)}));
        using var rendered=TranslatedSelectionRenderer.Render(source,new PixelRect(0,0,source.Width,source.Height),plan.Items);
        rendered.Save(Path.ChangeExtension(path,"replay.png"),System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine(JsonSerializer.Serialize(new {rejections=plan.Rejections}));
        return recognition.IsSuccess?0:1;
    }

    public static int RunLiveGate(IReadOnlyList<string> paths)
    {
        if (paths.Count < 2)
        {
            Console.Error.WriteLine("live-replay-gate-requires-at-least-two-frames");
            return 2;
        }

        using var ocr = OcrService.CreateForCommonMachine();
        var modelPath = MachineTranslationModelPaths.Resolve(
            Path.Combine(Environment.CurrentDirectory, "artifacts"));
        using var provider = new LocalMachineTranslationProvider(new BundledMachineTranslationProcess(
            Path.Combine(Environment.CurrentDirectory, "artifacts", "machine-translation", "runtime", "python.exe"),
            Path.Combine(Environment.CurrentDirectory, "src", "ClipRay.App", "Translation", "machine_translation_worker.py"),
            modelPath,
            workerTimeout: TimeSpan.FromSeconds(20), keepAlive: true),
            supportsCommonLanguages: MachineTranslationModelPaths.IsCommonLanguageModel(modelPath));

        var providerRequests = 0;
        var cache = new LiveTranslationRequestCache(async (route, requests, token) =>
        {
            providerRequests += requests.Count;
            var results = await provider.TranslateManyAsync(requests, token).ConfigureAwait(false);
            return new TranslationBatchOutcome(results, new TranslationExecutionTrace(route, 0, 0, 0));
        });
        var orchestrator = new TranslationApplicationOrchestrator(
            (bitmap, token) => ocr.RecognizeAsync(bitmap, token), cache.TranslateAsync);

        var failures = 0;
        var presentableFrames = 0;
        var requested = 0;
        foreach (var path in paths)
        {
            using var source = new Bitmap(Path.GetFullPath(path));
            TranslationPipelineTelemetry? timing = null;
            var outcome = orchestrator.ExecuteAsync(
                source,
                TranslationRouteKind.Machine,
                "简体中文",
                static blocks => blocks,
                progress: null,
                telemetry: value => timing = value,
                CancellationToken.None).GetAwaiter().GetResult();
            requested += outcome.Results.Count;
            var frame = LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine, outcome);
            var metrics = LiveTranslationViewportCommitPolicy.Measure(
                new LiveTranslationPixelSnapshot(source), frame, frame.LayoutItems.Count, frame.LayoutItems);
            var state = LiveTranslationViewportResultPolicy.Classify(frame, metrics);
            var presentable = LiveTranslationViewportResultPolicy.CanPresent(state)
                && metrics.ExpectedPrimaryLayouts > 0
                && metrics.RetainedPrimaryLayouts > 0;
            if (presentable) presentableFrames++;
            // Partial display is a valid product fallback, but it cannot pass a
            // replay gate that claims all expected layouts are present.
            var pass = presentable && state == LiveTranslationViewportResultState.Ready
                && metrics.ExpectedLayouts == metrics.RetainedLayouts
                && metrics.RejectedLayouts == 0;
            if (!pass) failures++;

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                frame = Path.GetFileName(path),
                pass,
                presentable,
                state = state.ToString(),
                outcome.Status,
                ocrBlocks = outcome.OcrBlocks.Count,
                regions = outcome.Regions.Count,
                expectedLayouts = metrics.ExpectedLayouts,
                layouts = metrics.RetainedLayouts,
                expectedPrimaryLayouts = metrics.ExpectedPrimaryLayouts,
                primaryLayouts = metrics.RetainedPrimaryLayouts,
                coverage = Math.Round(metrics.Coverage, 3),
                rejectionRate = Math.Round(metrics.RejectionRate, 3),
                failedResults = outcome.Results.Select((result, index) => new { result, index })
                    .Where(pair => !pair.result.IsSuccess)
                    .Select(pair => new { pair.index, pair.result.Status,
                        diagnostic = pair.result.Diagnostic?.Kind.ToString() }).ToArray(),
                rejectionReasons = outcome.LayoutRejections?.GroupBy(item => item.Reason)
                    .ToDictionary(group => group.Key, group => group.Count()),
                timing,
            }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            pass = failures == 0,
            frames = paths.Count,
            completeFrames = paths.Count - failures,
            presentableFrames,
            requested,
            providerRequests,
            semanticCacheHits = requested - providerRequests,
            model = Path.GetFileName(modelPath),
            semanticQuality = "not-assessed",
        }));
        return failures == 0 ? 0 : 1;
    }
}

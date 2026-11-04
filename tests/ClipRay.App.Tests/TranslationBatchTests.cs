using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ClipRay.App.Diagnostics;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.Core.Geometry;

internal static class TranslationBatchTests
{
    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Output directory required.");
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        OrdinaryUppercaseIsTranslated();
        StatusDistinguishesMissingTranslation();
        OptionalStyleCannotHideReadableText();
        DiagnosticStylesDoNotExposeContent();
        RenderSourceStyles(output);
        SemanticStyleTests.Run(output);
        MachineLexicalStyleTests.Run();
        ParentLineErasesWordBoundaryInk();
        Console.WriteLine("TRANSLATION_BATCH_TESTS_PASSED");
        return 0;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    public static int RunProduction(string[] args)
    {
        try { return RunProductionCore(args); }
        catch(Exception error)
        {
            Console.Error.WriteLine($"PRODUCTION_FIXTURE_FAILED: {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }

    private static int RunProductionCore(string[] args)
    {
        if (args.Length is < 4 or > 5 || args[3] is not ("machine" or "deepseek"))
            throw new ArgumentException("Expected output-directory install-root machine|deepseek.");
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var fixture = args.Length == 5 ? args[4] : "controls";
        if (fixture == "colored") SemanticStyleTests.Run(output);
        else if (fixture == "controls") RenderSourceStyles(output);
        IReadOnlyList<string> expectedLines=[];
        using var source = fixture == "daily-layout" ? DailyTranslationPageFixture.Create(out expectedLines) : fixture == "mixed-font"
            ? MixedFontStyleTests.CreateSource(out _,out _)
            : new Bitmap(Path.Combine(output, fixture == "colored" ? "semantic-colored-i-source.png" : "source.png"));
        if(fixture is "mixed-font" or "daily-layout") source.Save(Path.Combine(output,"source.png"),ImageFormat.Png);
        using var ocr = new OcrService();
        var root = Path.GetFullPath(args[2]);
        var model = MachineTranslationModelPaths.Resolve(root);
        using var process = new BundledMachineTranslationProcess(
            Path.Combine(root,"machine-translation","runtime","python.exe"),
            Path.Combine(AppContext.BaseDirectory,"machine-translation","machine_translation_worker.py"),
            model,workerTimeout:TimeSpan.FromSeconds(10),keepAlive:true,startupTimeout:TimeSpan.FromSeconds(60));
        using var machine = new LocalMachineTranslationProvider(process,
            supportsCommonLanguages:MachineTranslationModelPaths.IsCommonLanguageModel(model));
        var vault = new CredentialVault();
        var deepseek = new DeepSeekTranslationProvider(vault);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var route = args[3] == "machine" ? TranslationRouteKind.Machine : TranslationRouteKind.DeepSeekFlash;
        var orchestrator = new TranslationApplicationOrchestrator(
            (bitmap,token)=>ocr.RecognizeAsync(bitmap,token),
            async (_,requests,token)=> route == TranslationRouteKind.Machine
                ? await machine.TranslateManyAsync(requests,token)
                : await VisualTranslationExecutionPolicy.ExecuteCloudAsync(requests,vault,deepseek,null,token));
        TranslationPipelineTelemetry? timing=null;
        var outcome = orchestrator.ExecuteAsync(source,route,"简体中文",blocks=>SelectionCapturePolicy.ConstrainToSelection(
            blocks,new(0,0,source.Width,source.Height),new(0,0,source.Width,source.Height)),
            progress:null,telemetry:value=>timing=value,stop.Token).GetAwaiter().GetResult();
        using var translated=TranslatedSelectionRenderer.Render(source,new(0,0,source.Width,source.Height),outcome.LayoutItems);
        translated.Save(Path.Combine(output,"production-translated.png"),ImageFormat.Png);
        AssertFlatFixtureErased(source, outcome.LayoutItems,
            fixture == "controls" ? Color.FromArgb(24,24,24) : Color.White);
        var readback=ocr.RecognizeAsync(translated,stop.Token).GetAwaiter().GetResult();
        static string Readable(string value)=>string.Concat(value.Where(char.IsLetterOrDigit));
        // Reading order can change across columns when glyph tops differ by a
        // pixel. Associate actual raster recognition with its spatial region.
        var rasterAssignments=readback.Blocks.Select(block=>(Block:block,Index:outcome.LayoutItems
            .Select((item,index)=>(Index:index,Area:Overlap(item.Bounds,block.Bounds)))
            .Where(pair=>pair.Area>0).OrderByDescending(pair=>pair.Area).Select(pair=>pair.Index).DefaultIfEmpty(-1).First())).ToArray();
        var rasterByLayout=outcome.LayoutItems.Select((item,index)=>Readable(string.Concat(rasterAssignments
            .Where(pair=>pair.Index==index).OrderBy(pair=>pair.Block.Bounds.Top).ThenBy(pair=>pair.Block.Bounds.Left)
            .Select(pair=>pair.Block.Text)))).ToArray();
        var renderMatches=readback.IsSuccess && rasterAssignments.All(pair=>pair.Index>=0)
            && outcome.LayoutItems.Select((item,index)=>Readable(item.Text)==rasterByLayout[index]).All(value=>value);
        var sourceText=Readable(string.Concat(outcome.OcrBlocks.Select(block=>block.Text)));
        var sourceMismatches=expectedLines.Where(line=>!sourceText.Contains(Readable(line),StringComparison.Ordinal)).ToArray();
        File.WriteAllText(Path.Combine(output,"production-outcome.json"),JsonSerializer.Serialize(new {
            Route=route.ToString(),outcome,timing,Readback=readback,
            RenderedTextMatches=renderMatches,RasterByLayout=rasterByLayout,SourceExactTextMismatches=sourceMismatches,
            Scope="Generated non-sensitive image; actual production OCR/provider/layout/export and OCR readback. No desktop or clipboard observation.",
            AppSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(OcrService).Assembly.Location)))
        },new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"PRODUCTION_TRANSLATION {route}: success={outcome.IsSuccess}; OCR={outcome.OcrBlocks.Count}; regions={outcome.Regions.Count}; layouts={outcome.LayoutItems.Count}; timing={JsonSerializer.Serialize(timing)}");
        Check(outcome.IsSuccess && outcome.OcrBlocks.Count>=(fixture=="daily-layout"?12:fixture=="controls"?6:1)
            && outcome.Regions.Count>=(fixture=="daily-layout"?10:fixture=="controls"?4:1)
            && outcome.Results.All(result=>result.IsSuccess) && outcome.LayoutItems.Count==outcome.Regions.Count,
            "Actual production translation did not cover every source region; inspect production-outcome.json.");
        foreach(var expectedLine in expectedLines)
            Check(sourceText.Contains(Readable(expectedLine),StringComparison.Ordinal),
                "Production OCR omitted or changed a generated source line: "+expectedLine);
        Check(renderMatches,
            "Rendered glyph OCR differs from the returned translation; inspect output image and readback before accepting.");
        if(fixture=="colored") Check(outcome.Regions.Count==1
            && outcome.LayoutItems[0].TextLayout!.StyleRuns.Any(run=>run.Style.ForegroundColor.R>150
                && run.Style.ForegroundColor.G<100 && run.Style.ForegroundColor.B<110),
            "Actual production colored sentence lost semantic coverage or its aligned red span.");
        if(fixture=="mixed-font") Check(outcome.Regions.Count==1
            && outcome.LayoutItems[0].TextLayout!.StyleRuns.Any(run=>run.Style.EstimatedFontSizePixels>30),
            "Actual production mixed font sentence lost the aligned larger source font.");
        return 0;
    }

    private static double Overlap(PixelRect a,PixelRect b)=>
        Math.Max(0,Math.Min(a.Right,b.Right)-Math.Max(a.Left,b.Left))
        * Math.Max(0,Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Top,b.Top));

    private static void ParentLineErasesWordBoundaryInk()
    {
        using var source = new Bitmap(120,40);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.White);
            graphics.FillRectangle(Brushes.Black,10,12,80,14);
        }
        var bounds = new PixelRect(8,8,86,24);
        OcrWord[] children = [new("first",new(8,8,30,24)),new("second",new(52,8,42,24))];
        var parent = new OcrWord("first second",bounds) {IsDetectedLine=true,RecognitionWords=children};
        var line = new OcrTextBlock(parent.Text,bounds,children) {PrimaryRecognitionWords=[parent]};
        var region = new VisualTextRegion("ctc-gap",line.Text,[line],bounds,
            VisualTextRole.Body,VisualStructureConfidence.High);
        using var mask = TextReplacementMask.Build(source,region,[new(80,8,14,24)]);
        Check(mask.Mask.GetPixel(45,18).A==255,"A CTC word interval gap left original source ink behind.");
        Check(mask.Mask.GetPixel(85,18).A==0,"Parent line erasure damaged a protected neighboring fragment.");
        var mismatched = line with {Text="different text"};
        using var mismatchedMask=TextReplacementMask.Build(source,region with {Lines=[mismatched]});
        Check(mismatchedMask.Mask.GetPixel(45,18).A==0,"Unmatched parent recognition broadened the erasure area.");
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.White);
            graphics.FillRectangle(Brushes.Black,12,12,20,14);
            graphics.FillRectangle(Brushes.Black,58,12,20,14);
            graphics.FillRectangle(Brushes.Black,44,12,3,14);
        }
        using var isolatedIconMask=TextReplacementMask.Build(source,region);
        Check(isolatedIconMask.Mask.GetPixel(45,18).A==0,
            "An isolated, unrecognized icon in a CTC word gap was treated as source text.");
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.White);
            graphics.FillRectangle(Brushes.Black,44,12,3,14);
        }
        using var controlMask=TextReplacementMask.Build(source,region with {Role=VisualTextRole.Control});
        Check(controlMask.Mask.GetPixel(45,18).A==0,"Empty control seeds restored a full parent erasure fallback.");
        using (var graphics = Graphics.FromImage(source))
            graphics.FillRectangle(Brushes.Black,35,12,12,14);
        using var protectedBridge=TextReplacementMask.Build(source,region,[new(34,8,9,24)]);
        Check(protectedBridge.Mask.GetPixel(45,18).A==0,
            "Protected neighboring ink became a seed or bridge for erasing an unrecognized icon.");
    }

    private static void AssertFlatFixtureErased(Bitmap source,IReadOnlyList<InPlaceLayoutItem> items,Color background)
    {
        var checkedInk=0;
        foreach(var item in items)
        {
            using var stream=new MemoryStream(item.MaskedPatchPng ?? throw new Exception("Missing source erasure patch."));
            using var patch=new Bitmap(stream);
            for(var y=(int)Math.Floor(item.Bounds.Top);y<(int)Math.Ceiling(item.Bounds.Bottom);y++)
            for(var x=(int)Math.Floor(item.Bounds.Left);x<(int)Math.Ceiling(item.Bounds.Right);x++)
            {
                if(x<0||y<0||x>=source.Width||y>=source.Height) continue;
                var pixel=source.GetPixel(x,y);
                if(Math.Abs(pixel.R-background.R)+Math.Abs(pixel.G-background.G)+Math.Abs(pixel.B-background.B)<90) continue;
                checkedInk++;
                var erased=patch.GetPixel(x-(int)Math.Floor(item.Bounds.Left),y-(int)Math.Floor(item.Bounds.Top));
                Check(erased.A==255 && Math.Abs(erased.R-background.R)+Math.Abs(erased.G-background.G)+Math.Abs(erased.B-background.B)<24,
                    $"Source glyph was not erased at {x},{y}; OCR readback alone cannot prove clean replacement.");
            }
        }
        Check(checkedInk>20,"Erasure oracle did not inspect real source ink.");
    }

    private static void OrdinaryUppercaseIsTranslated()
    {
        var labels = new[] { "SAVE", "CANCEL", "DELETE", "SETTINGS", "DOWNLOAD", "CONTINUE", "WARNING", "WELCOME", "Search", "LEVEL1", "CHAPTER2", "CPU", "GPU", "API", "OpenAI", "GPT-6" };
        var regions = labels.Select((text, index) => new VisualTextRegion($"r{index}", text,
            [new(text, new(10, 10 + index * 35, 170, 28), [])], new(10, 10 + index * 35, 170, 28),
            VisualTextRole.Control, VisualStructureConfidence.High)).ToArray();
        var requests = VisualTranslationRequestFactory.Create(regions, "zh", DeepSeekModels.Flash);
        for (var i = 0; i < labels.Length; i++)
            Check((requests[i].TextKind == TranslationTextKind.BrandOrIdentifier) == (i >= 11),
                $"Wrong identifier classification for {labels[i]}: {requests[i].TextKind}");
    }

    private static void StatusDistinguishesMissingTranslation()
    {
        var region = new VisualTextRegion("r", "Save", [new("Save", new(0, 0, 50, 20), [])],
            new(0, 0, 50, 20), VisualTextRole.Control, VisualStructureConfidence.High);
        var outcome = new ApplicationTranslationOutcome(false, TranslationPipelineStage.Layout,
            "layout-incomplete", region.Lines, [region], [new(true, "保存", "completed")], [],
            [new(1, "text-does-not-fit")]);
        var status = TranslationStatusText.FormatOverlayOutcome(TranslationRouteKind.Machine, outcome);
        Check(status.Contains("排版未显示 1 处") && !status.Contains("按规则保留"),
            "A rejected translation was described as intentional preservation.");
    }

    private static void OptionalStyleCannotHideReadableText()
    {
        var source=new SourceVisualStyle(Color.Black,14,24,SourceFontWeight.Normal,SourceFontCategory.SansSerif,
            false,SourceTextAlignment.Left,0,VisualStyleConfidence.High,20);
        var bounds=new PixelRect(0,0,90,25);
        var basic=TranslationTextLayoutPolicy.Fit("完整译文",bounds,source);
        Check(basic.ShouldRender,"Base fixture must fit independently of optional typography.");
        var styled=TranslationTextLayoutPolicy.WithStyleRuns("完整译文",bounds,source,basic,
            [new(0,4,source with {EstimatedFontSizePixels=80},TranslationStyleAlignmentEvidence.ModelAttention)]);
        Check(styled.ShouldRender && styled.StyleRuns.Count==0,
            "Optional source typography hid the entire readable translation.");
        var compact=new TranslationTextLayout(true,9,11,SourceFontWeight.Normal,SourceFontCategory.SansSerif,
            false,SourceTextAlignment.Left,0,"Segoe UI, Microsoft YaHei UI");
        var colored=TranslationTextLayoutPolicy.WithStyleRuns("译文",new(0,0,30,12),source,compact,
            [new(0,2,source with { ForegroundColor=Color.Red },TranslationStyleAlignmentEvidence.ModelSemanticSpan)]);
        Check(colored.ShouldRender && colored.StyleRuns.Count==1,
            "An accepted compact control was discarded before testing its color-only run.");
    }

    private static void DiagnosticStylesDoNotExposeContent()
    {
        var json = DiagnosticProjection.Project(new { foreground_argb = "FF24B0E8", alignment = "Center",
            confidence = "High", text = "private-source", font_weight = "private-value" }, false)!.ToJsonString();
        Check(json.Contains("FF24B0E8") && json.Contains("Center") && !json.Contains("private-source")
            && !json.Contains("private-value"), "Style diagnostics must retain closed structural values only.");
    }

    private static void RenderSourceStyles(string output)
    {
        using var source = new Bitmap(920, 400);
        var examples = new (string Text, string Translation, Rectangle Box, Color Color, float Size)[]
        {
            ("SAVE CHANGES", "保存更改", new(24, 24, 300, 42), Color.FromArgb(36,176,232), 28),
            ("DELETE THIS FILE", "删除此文件", new(24, 100, 310, 36), Color.FromArgb(238,77,91), 24),
            ("Keep every line readable.", "保持每行文字清晰。", new(24, 168, 420, 36), Color.FromArgb(230,230,230), 24),
            ("A centered paragraph", "居中段落第一行", new(540, 230, 300, 36), Color.FromArgb(255,193,74), 24),
            ("with a shorter line", "第二行稍短", new(565, 270, 250, 36), Color.FromArgb(255,193,74), 24),
            ("and the final sentence.", "这是最后一行。", new(530, 310, 320, 36), Color.FromArgb(255,193,74), 24),
        };
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.FromArgb(24,24,24));
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            for (var index = 0; index < examples.Length; index++)
            {
                var example = examples[index];
                using var font = new Font("Segoe UI", example.Size, FontStyle.Regular, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(example.Color);
                if (index >= 3)
                {
                    var width = (int)Math.Ceiling(graphics.MeasureString(example.Text, font).Width);
                    example.Box = new Rectangle(690-width/2, example.Box.Y, width, example.Box.Height);
                    examples[index] = example;
                }
                graphics.DrawString(example.Text, font, brush, example.Box.Location);
            }
        }
        var blocks = examples.Select(example => new OcrTextBlock(example.Text,
            new(example.Box.X, example.Box.Y, example.Box.Width, example.Box.Height),
            [new(example.Text, new(example.Box.X, example.Box.Y, example.Box.Width, example.Box.Height))])).ToArray();
        var regions = blocks.Take(3).Select((block,i) => new VisualTextRegion($"r{i}", block.Text,
            [block], block.Bounds, VisualTextRole.Control, VisualStructureConfidence.High)).ToList();
        regions.Add(new("paragraph", string.Join('\n', blocks.Skip(3).Select(block => block.Text)),
            blocks.Skip(3).ToArray(), new(blocks.Skip(3).Min(block=>block.Bounds.Left),230,
                blocks.Skip(3).Max(block=>block.Bounds.Right)-blocks.Skip(3).Min(block=>block.Bounds.Left),116),
            VisualTextRole.Body, VisualStructureConfidence.High));
        var results = examples.Take(3).Select(example => new TranslationResult(true,example.Translation,"completed")).ToList();
        results.Add(new(true, string.Join('\n', examples.Skip(3).Select(example => example.Translation)), "completed"));
        var plan = InPlaceLayoutService.BuildRegionPlan(source, regions, results);
        Check(plan.Items.Count == 4 && plan.Rejections.Count == 0,
            "Clean source lost translated regions: " + string.Join(',', plan.Rejections));
        foreach (var item in plan.Items)
        {
            var index = regions.FindIndex(region => region.Id == item.SourceRegion?.Id);
            var expected = examples[index == 3 ? 3 : index];
            Check(Math.Abs(item.ForegroundColor.R-expected.Color.R) <= 8
                && Math.Abs(item.ForegroundColor.G-expected.Color.G) <= 8
                && Math.Abs(item.ForegroundColor.B-expected.Color.B) <= 8,
                $"Source color shifted for {index}: {item.ForegroundColor} expected {expected.Color}.");
            Check(item.TextLayout!.FontSizePixels >= expected.Size * .70
                && item.TextLayout.FontSizePixels <= expected.Size * 1.25,
                $"Source font scale changed for {index}: {item.TextLayout.FontSizePixels}.");
        }
        Check(plan.Items.Single(item => item.SourceRegion?.Id == "paragraph").TextLayout!.Alignment == SourceTextAlignment.Center,
            "A centered paragraph lost its source alignment.");
        Check(plan.Items.Single(item=>item.SourceRegion?.Id=="r0").TextLayout!.FontSizePixels
            > plan.Items.Single(item=>item.SourceRegion?.Id=="r1").TextLayout!.FontSizePixels + 2,
            "Different source font sizes were normalized to the smaller peer.");
        source.Save(Path.Combine(output,"source.png"), ImageFormat.Png);
        using var rendered = TranslatedSelectionRenderer.Render(source, new(0,0,source.Width,source.Height), plan.Items);
        rendered.Save(Path.Combine(output,"translated.png"), ImageFormat.Png);
        var overlay = SelectionTranslationLayerRenderer.RenderOverlay(source.Width,source.Height,plan.Items);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(overlay));
        using (var stream = File.Create(Path.Combine(output,"overlay.png"))) encoder.Save(stream);
        using var interactive = new Bitmap(Path.Combine(output,"overlay.png"));
        var compared = 0; var different = 0;
        foreach (var item in plan.Items)
        for (var y=(int)item.Bounds.Top;y<item.Bounds.Bottom;y++)
        for (var x=(int)item.Bounds.Left;x<item.Bounds.Right;x++)
        {
            var a=rendered.GetPixel(x,y);var b=interactive.GetPixel(x,y);
            if (b.A != 255) continue;
            compared++;
            if (Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B)>12) different++;
        }
        Check(compared>1000 && different < compared*.01,
            $"Interactive/export renderer mismatch: {different}/{compared} pixels.");
        File.WriteAllText(Path.Combine(output,"layout.json"),JsonSerializer.Serialize(plan.Items.Select(item => new {
            id=item.SourceRegion?.Id, color=item.ForegroundColor.ToArgb().ToString("X8"), item.TextLayout,
            expectedSourceSize=examples[regions.FindIndex(region=>region.Id==item.SourceRegion?.Id)].Size,
            item.Bounds }),new JsonSerializerOptions {WriteIndented=true}));
    }
}

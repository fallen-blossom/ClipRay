using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class WorkbenchManualEvidence
{
    public static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected source-image fresh-output");
        if (Directory.Exists(args[2])) throw new IOException("Use a fresh output directory");
        Directory.CreateDirectory(args[2]);
        using var source = new Bitmap(args[1]);
        using var ocr = OcrService.CreateForCommonMachine();
        var providerCalls = 0;
        var orchestrator = new TranslationApplicationOrchestrator(ocr.RecognizeAsync,
            (_, _, _) => { providerCalls++; return Task.FromException<IReadOnlyList<TranslationResult>>(
                new InvalidOperationException("Manual edits must not call a provider.")); });
        var bounds = new PixelRect(0, 0, source.Width, source.Height);
        using var document = new TranslationWorkbenchDocument(source, bounds, bounds,
            TranslationRouteKind.LocalLmt, "zh", orchestrator, blocks => blocks);
        document.RecognizeAsync().GetAwaiter().GetResult();
        var region = document.Session?.Regions.OrderByDescending(item => item.Draft.Length).FirstOrDefault()
            ?? throw new InvalidDataException("OCR did not produce a region");
        const string corrected = "在游戏厅，每款游戏需要两个筹码。你有七个筹码，足够玩三场游戏，还剩一个筹码。";
        if (!document.EditTranslation(region.Id, corrected)) throw new InvalidDataException("Manual edit was superseded");
        var result = document.Session!.Regions.Single(item => item.Id == region.Id);
        using var rendered = WorkbenchImageExport.Render(document);
        var outputImage = Path.Combine(args[2], "manual-correction.png");
        rendered.Save(outputImage, ImageFormat.Png);
        var report = new
        {
            SourceSha256 = Hash(args[1]), OutputSha256 = Hash(outputImage),
            AppSha256 = Hash(typeof(TranslationWorkbenchDocument).Assembly.Location),
            TestSha256 = Hash(typeof(WorkbenchManualEvidence).Assembly.Location),
            RegionSource = region.Draft, result.Translation, State = result.State.ToString(),
            result.Detail, HasLayout = result.Layout is not null,
            BaselineState = result.ManualBaseline?.State.ToString(), providerCalls,
            Scope = "Offscreen OCR and manual translation layout/export; no model, foreground, clipboard or user acceptance"
        };
        File.WriteAllText(Path.Combine(args[2], "manual-evidence.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { report.State, report.HasLayout, providerCalls }));
        return result.State == WorkbenchRegionState.Translated && result.Layout is not null
            && report.SourceSha256 != report.OutputSha256 && providerCalls == 0 ? 0 : 1;
    }

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
}

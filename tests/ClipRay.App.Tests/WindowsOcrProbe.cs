using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class WindowsOcrProbe
{
    public static int Run(string[] args)
    {
        var installed = OcrService.InstalledWindowsLanguages();
        if (args.Length == 1) { Console.WriteLine(string.Join(", ", installed)); return 0; }
        if (args.Length != 4) throw new ArgumentException("image exact-language fresh-report-file");
        if (File.Exists(args[3])) throw new IOException("Use a fresh report path");
        using var image = new Bitmap(args[1]);
        var translationCalls = 0;
        var defaultOcrCalls = 0;
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => { defaultOcrCalls++; throw new InvalidOperationException("Explicit OCR must not call default runtime"); },
            (_, _, _) => { translationCalls++; return Task.FromResult<IReadOnlyList<TranslationResult>>([]); });
        var bounds = new PixelRect(0, 0, image.Width, image.Height);
        using var document = new TranslationWorkbenchDocument(image, bounds, bounds, TranslationRouteKind.Machine,
            "zh", orchestrator, blocks => blocks);
        document.ConfigureOcrLanguage(args[2]);
        var watch = Stopwatch.StartNew();
        document.RecognizeAsync().GetAwaiter().GetResult();
        var elapsed = watch.ElapsedMilliseconds;
        var regions = document.Session?.Regions ?? [];
        var evidence = regions.SelectMany(region => region.Original.Lines).SelectMany(line => line.Words)
            .Select(word => word.Evidence).Distinct().ToArray();
        using var stream = File.OpenRead(args[1]);
        File.WriteAllText(args[3], JsonSerializer.Serialize(new
        {
            InstalledLanguages = installed, RequestedLanguage = args[2], State = document.State.ToString(), document.RecognitionDetail,
            Milliseconds = elapsed, DefaultOcrCalls = defaultOcrCalls, TranslationCalls = translationCalls,
            ImageSha256 = Convert.ToHexString(SHA256.HashData(stream)),
            Evidence = evidence, Regions = regions.Select(region => new { region.Draft, region.Original.Bounds })
        }, new JsonSerializerOptions { WriteIndented = true }));
        var valid = regions.Count > 0 && evidence.Length > 0 && evidence.All(item => item is not null
            && item.EngineId == "windows-ocr" && item.ModelId.Equals(args[2], StringComparison.OrdinalIgnoreCase))
            && defaultOcrCalls == 0 && translationCalls == 0;
        Console.WriteLine($"Windows OCR matched requested language={valid}; state={document.State}; regions={regions.Count}");
        return valid ? 0 : 1;
    }
}

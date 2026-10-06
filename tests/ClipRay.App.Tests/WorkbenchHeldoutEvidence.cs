using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class WorkbenchHeldoutEvidence
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();
    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is not (5 or 6 or 7)) throw new ArgumentException("Expected model server fixture fresh-output [prior-images] [rounds]");
        var rounds = args.Length == 7 && int.TryParse(args[6], out var requestedRounds) && requestedRounds is >= 1 and <= 3
            ? requestedRounds : args.Length == 7 ? throw new ArgumentOutOfRangeException(nameof(args), "rounds must be 1..3") : 1;
        using var fixture = JsonDocument.Parse(File.ReadAllText(args[3]));
        using var probe = new QwenHttpWorkbenchProbe(args[1], args[2], args[4], protectSource: true);
        using var ocr = OcrService.CreateForCommonMachine();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4 * rounds));
        var orchestrator = new TranslationApplicationOrchestrator(ocr.RecognizeAsync,
            (_, requests, token) => probe.TranslateManyAsync(requests, token));
        var records = new List<object>();
        var index = 0;
        for (var round = 1; round <= rounds; round++)
        foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var text = item.GetProperty("sourceText").GetString()!;
            var caseIndex = index++ % fixture.RootElement.GetProperty("cases").GetArrayLength() + 1;
            var prefix = rounds == 1 ? caseIndex.ToString() : $"r{round}-{caseIndex}";
            var timer = Stopwatch.StartNew();
            using var source = args.Length >= 6 ? new Bitmap(Path.Combine(args[5], $"{caseIndex}.source.png")) : new Bitmap(1100, 320);
            if (args.Length == 5)
            {
                using var graphics = Graphics.FromImage(source);
                using var font = new Font("Segoe UI", 24);
                graphics.Clear(Color.White);
                graphics.DrawString(text, font, Brushes.Black, new RectangleF(30, 30, 1040, 260));
            }
            var imagePath = Path.Combine(args[4], $"{prefix}.source.png");
            source.Save(imagePath, ImageFormat.Png);
            var bounds = new PixelRect(0, 0, source.Width, source.Height);
            using var document = new TranslationWorkbenchDocument(source, bounds, bounds,
                TranslationRouteKind.LocalLmt, "zh", orchestrator, blocks => blocks);
            await document.RecognizeAsync(timeout.Token);
            var ocrMilliseconds = timer.ElapsedMilliseconds;
            var session = document.Session ?? throw new InvalidOperationException("OCR produced no session");
            foreach (var region in session.Regions)
            {
                session.Configure(region.Id, "en", "zh", TranslationRouteKind.LocalLmt);
                await document.TranslateAsync(region.Id, timeout.Token);
            }
            var translationMilliseconds = timer.ElapsedMilliseconds - ocrMilliseconds;
            using var rendered = WorkbenchImageExport.Render(document);
            rendered.Save(Path.Combine(args[4], $"{prefix}.translated.png"), ImageFormat.Png);
            var renderMilliseconds = timer.ElapsedMilliseconds - ocrMilliseconds - translationMilliseconds;
            records.Add(new
            {
                Id = item.GetProperty("id").GetString(), Round = round, Source = text, ImageSha256 = Hash(imagePath),
                OcrMilliseconds = ocrMilliseconds, TranslationMilliseconds = translationMilliseconds,
                RenderMilliseconds = renderMilliseconds, TotalMilliseconds = timer.ElapsedMilliseconds,
                ManagedMemoryBytes = GC.GetTotalMemory(false), ProcessWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64,
                Regions = session.Regions.Select(region => new { region.Draft, region.Translation,
                    State = region.State.ToString(), region.Detail, region.Original.Bounds }), Attempts = session.Attempts
            });
        }
        File.WriteAllText(Path.Combine(args[4], "workbench-results.json"), JsonSerializer.Serialize(new
        {
            FixtureSha256 = Hash(args[3]), AppSha256 = Hash(typeof(OcrService).Assembly.Location),
            TestSha256 = Hash(typeof(WorkbenchHeldoutEvidence).Assembly.Location), ServerSha256 = Hash(args[2]),
            Rounds = rounds, Records = records, Scope = "Authored offscreen raster tasks through actual OCR/workbench/candidate/layout; stage timing includes source image save in OCR stage. Sequential runs, not distribution or foreground acceptance. Semantic review separate."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Held-out workbench execution completed: {records.Count} tasks; semantic review pending.");
        return 0;
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

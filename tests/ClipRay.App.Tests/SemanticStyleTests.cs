using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Security;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class SemanticStyleTests
{
    private const string Sentence = "I can read the room.";
    private const string Translated = "我能察言观色。";

    public static void Run(string output)
    {
        using var source = new Bitmap(720, 110);
        var words = new List<OcrWord>();
        var red = Color.FromArgb(210, 40, 50);
        using (var graphics = Graphics.FromImage(source))
        using (var font = new Font("Segoe UI", 28, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var x = 24f;
            foreach (var text in Sentence.Split(' '))
            {
                var width = graphics.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
                using var brush = new SolidBrush(words.Count == 0 ? red : Color.FromArgb(35, 35, 35));
                graphics.DrawString(text, font, brush, x, 25, StringFormat.GenericTypographic);
                words.Add(new(text, new(x, 25, width, 36), new("windows-ocr", "controlled-word-geometry", null, null)));
                x += width + font.Size * .30f;
            }
        }
        var bounds = new PixelRect(24, 25, words[^1].Bounds.Right - 24, 36);
        Check(words.Zip(words.Skip(1)).All(pair => pair.Second.Bounds.Left - pair.First.Bounds.Right >= 28 * .30 - .01),
            "Source fixture has no independently specified visible word gap.");
        var primaryText = Sentence.Replace(" ", "", StringComparison.Ordinal);
        var primaryWord = new OcrWord(primaryText, bounds, new("rapidocr-common-script", "controlled-line", "latin", .9994)) { IsDetectedLine = true };
        var lines = OcrBlockGrouping.GroupWords([primaryWord]);
        Check(lines.Count == 1 && lines[0].Words.Count == 1, "Fixture did not model production detected-line geometry.");
        Check(OcrStyleGeometryRefiner.NeedsRefinement(source, lines), "A materially colored I did not request local word geometry.");
        var badGeometry = words.Select((word, index) => index == 1 ? word with { Text = "cannot" } : word).ToArray();
        Check(OcrStyleGeometryRefiner.Refine(lines, badGeometry)[0].Words.Count == 1,
            "A disagreeing secondary OCR replaced primary words.");
        lines = OcrStyleGeometryRefiner.Refine(lines, words);
        Check(lines[0].Text == Sentence && lines[0].Bounds == bounds && lines[0].Words.Count == 5,
            "Geometry refinement failed a character-identical whitespace repair or changed primary bounds.");
        Check(lines[0].PrimaryRecognitionText == primaryText && lines[0].WhitespaceRepair == "windows-word-boundaries"
            && lines[0].PrimaryRecognitionWords.Single().Evidence?.EngineId == "rapidocr-common-script"
            && lines[0].PrimaryRecognitionWords.Single().Text == primaryText,
            "Whitespace repair lost the original fused primary text or model evidence.");
        var regions = VisualTextRegionBuilder.Build(lines, source);
        Check(regions.Count == 1 && regions[0].SourceText == Sentence,
            "Color boundaries fragmented a semantic sentence and left the isolated I untranslated.");
        Check(regions[0].OriginalOcrWords.Single().Text == primaryText,
            "Visual region replaced primary recognition evidence with borrowed word geometry.");
        var requests = VisualTranslationRequestFactory.Create(regions, "zh", DeepSeekModels.Flash);
        Check(requests.Length == 1 && requests[0].OcrText == Sentence && requests[0].SourceSpans.Any(span => span.Text == "I"),
            "Factory lost the full sentence or exact styled I annotation.");
        using var machine = new LocalMachineTranslationProvider(new WholeSentenceProcess());
        var results = machine.TranslateManyAsync(requests, CancellationToken.None).GetAwaiter().GetResult();
        Check(results.Single().IsSuccess && results[0].Text == Translated && results[0].Status != "machine-preserved",
            "Full-sentence machine route preserved a fragmented source word.");
        var resolution = TranslationStyleResolver.Resolve(regions[0], requests[0], results[0]);
        Check(resolution.Runs.Any(run => run.Start == 0 && run.Length == 1 && Near(run.Style.ForegroundColor, red)),
            "Validated machine alignment failed to preserve the styled I on 我.");
        Check(resolution.UnresolvedSourceSpanCount > 0, "Unaligned idiomatic phrase falsely claimed exact style alignment.");
        var plan = InPlaceLayoutService.BuildRegionPlan(source, regions, results);
        Check(plan.Items.Count == 1 && plan.Rejections.Count == 0 && plan.Items[0].Text == Translated
            && InPlaceLayoutService.HasCompleteCoverage(regions, results, plan.Items, plan.Rejections),
            "Complete translated idiom failed the actual layout coverage path.");
        Directory.CreateDirectory(output);
        source.Save(Path.Combine(output, "semantic-colored-i-source.png"), ImageFormat.Png);
        using var rendered = TranslatedSelectionRenderer.Render(source, new(0, 0, source.Width, source.Height), plan.Items);
        rendered.Save(Path.Combine(output, "semantic-colored-i-machine.png"), ImageFormat.Png);

        DeepSeekSemanticSpans(requests[0], regions[0]);
        ProtectedTokensKeepTheirWhitespace();
        MixedFontStyleTests.Run(output);
        var malformed = results[0] with { TextAlignments = [new(-1, 9, 5000, 1)] };
        Check(TranslationStyleResolver.Resolve(regions[0], requests[0], malformed).Runs.Count == 0
            && malformed.Text == Translated, "Bad alignment discarded translation or assigned arbitrary colors.");
        Console.WriteLine("Semantic sentence/style tests passed (controlled process/HTTP; native provider quality not asserted).");
    }

    private static void ProtectedTokensKeepTheirWhitespace()
    {
        var cases = new (string Source, string[] Words)[] {
            ("OpenAI", ["Open", "AI"]), ("ChatGPT", ["Chat", "GPT"]),
            ("getValue", ["get", "Value"]), ("HTTPRequest", ["HTTP", "Request"]),
            ("RTX4090", ["RTX", "4090"]), ("GPT-6.1", ["GP", "T-6.1"]),
            ("some_identifier", ["some_id", "entifier"]),
            ("@alice_name", ["@alice_", "name"]),
            ("https://example.com/myPage", ["https://example.com/my", "Page"]),
            ("foo@example.com", ["foo@ex", "ample.com"]),
            ("C:\\LocalData\\file.txt", ["C:\\Local", "Data\\file.txt"]),
            ("object.methodName()", ["object.method", "Name()"]),
        };
        foreach (var item in cases)
        {
            var x = 10d;
            var geometry = item.Words.Select(text => { var word = new OcrWord(text, new(x, 10, text.Length * 7, 20)); x += text.Length * 7 + 2; return word; }).ToArray();
            var bounds = new PixelRect(10, 10, geometry[^1].Bounds.Right - 10, 20);
            var primary = new OcrTextBlock(item.Source, bounds, [new(item.Source, bounds,
                new("rapidocr-common-script", "controlled-line", "latin", .99)) { IsDetectedLine = true }]);
            var result = OcrStyleGeometryRefiner.Refine([primary], geometry).Single();
            Check(result.Words.Count == geometry.Length && result.Text == item.Source && result.WhitespaceRepair is null,
                $"Borrowed geometry inserted whitespace inside protected token {item.Source}.");
            Check(result.PrimaryRecognitionText == item.Source
                && result.PrimaryRecognitionWords.Single().Evidence?.EngineId == "rapidocr-common-script",
                "Protected token geometry lost original recognition evidence.");
        }
        var positive = "Icanreadtheroom.";
        var parts = new[] { "I", "can", "read", "the", "room." };
        var currentX = 10d;
        var wordBoxes = parts.Select(text => { var word = new OcrWord(text, new(currentX,10,text.Length*7,20)); currentX += text.Length*7+2; return word; }).ToArray();
        var rect = new PixelRect(10,10,wordBoxes[^1].Bounds.Right-10,20);
        var repaired = OcrStyleGeometryRefiner.Refine([new(positive,rect,[new(positive,rect)])],wordBoxes).Single();
        Check(repaired.Text == Sentence && repaired.WhitespaceRepair == "windows-word-boundaries",
            "Literal protection also disabled ordinary fused-sentence whitespace repair.");
    }

    private static void DeepSeekSemanticSpans(TranslationRequest request, VisualTextRegion region)
    {
        foreach (var ambiguous in new[] { false, true })
        {
            using var handler = new WholeSentenceHandler(ambiguous);
            var provider = new DeepSeekTranslationProvider(new FixtureVault(), handler);
            var result = provider.TranslateBlocksAsync([request], "fixture", CancellationToken.None).GetAwaiter().GetResult().Single();
            Check(result.IsSuccess && result.Text == (ambiguous ? "我我能察言观色。" : Translated),
                "Optional semantic styles blocked a complete DeepSeek translation.");
            var resolution = TranslationStyleResolver.Resolve(region, request, result);
            Check(ambiguous ? resolution.Runs.Count == 0 : resolution.Runs.Any(run => run.Start == 0 && run.Length == 1
                && run.Evidence == TranslationStyleAlignmentEvidence.ModelSemanticSpan),
                "DeepSeek accepted ambiguous target text or lost a unique validated semantic span.");
        }
    }

    private static bool Near(Color a, Color b) => Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B) < 45;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class WholeSentenceProcess : IMachineTranslationProcess
    {
        public Task<IReadOnlyList<string>> TranslateAsync(MachineTranslationProcessRequest request, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<string>>([Translated]);
        public Task<MachineTranslationProcessResult> TranslateWithEvidenceAsync(MachineTranslationProcessRequest request, CancellationToken token)
        {
            Check(request.Texts.Count == 1 && request.Texts[0] == Sentence && request.ReturnAlignments,
                "Production machine provider sent colored fragments rather than the complete sentence.");
            return Task.FromResult(new MachineTranslationProcessResult([Translated], TextAlignments: [[new(0, 1, 0, 1)]]));
        }
    }
    private sealed class WholeSentenceHandler(bool ambiguous) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var envelope = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            using var input = JsonDocument.Parse(envelope.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var item = input.RootElement.GetProperty("b0");
            Check(item.GetProperty("text").GetString() == Sentence, "DeepSeek provider received a fragmented sentence.");
            var source = item.GetProperty("source_spans").EnumerateArray().Single(span => span.GetProperty("source_text").GetString() == "I");
            Check(source.EnumerateObject().Count() == 2, "Provider exposed source pixels, colors, or local geometry.");
            var translated = ambiguous ? "我我能察言观色。" : Translated;
            var contract = JsonSerializer.Serialize(new { b0 = new { translation = translated,
                spans = new[] { new { id = source.GetProperty("id").GetString(), target_text = "我" } } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                choices = new[] { new { message = new { content = contract } } } }), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class FixtureVault : ICredentialVault
    {
        public void Save(string name, string provider, string value) => throw new NotSupportedException();
        public IReadOnlyList<CredentialMetadata> List() => [];
        public void Delete(string name) => throw new NotSupportedException();
        public string? GetForUse(string name) => "controlled-local-handler-no-network";
    }
}

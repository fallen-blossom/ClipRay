using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class MixedFontStyleTests
{
    public const string SourceText = "Keep every word readable.";
    public const string TargetText = "让每个词都清晰可读。";

    // Root can use this real raster directly with the production OCR/provider probe.
    public static Bitmap CreateSource(out OcrTextBlock primaryLine, out IReadOnlyList<OcrWord> knownWordGeometry, bool mixed = true)
    {
        var image = new Bitmap(920, 150);
        var words = new List<OcrWord>();
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var x = 26f;
        foreach (var pair in SourceText.Split(' ').Select((text, index) => (text, index)))
        {
            var size = mixed && pair.index is 1 or 2 ? 36f : 20f;
            using var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(35, 35, 35));
            var width = graphics.MeasureString(pair.text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
            var ascent = size * font.FontFamily.GetCellAscent(FontStyle.Regular) / font.FontFamily.GetEmHeight(FontStyle.Regular);
            var y = 78 - ascent;
            graphics.DrawString(pair.text, font, brush, x, y, StringFormat.GenericTypographic);
            words.Add(new(pair.text, new(x, y, width, font.GetHeight(graphics)),
                new("windows-ocr", "controlled-word-geometry", null, null)));
            x += width + font.Size * .30f;
        }
        var left = words.Min(static word => word.Bounds.Left) - 2;
        var top = words.Min(static word => word.Bounds.Top) - 2;
        var right = words.Max(static word => word.Bounds.Right) + 2;
        var bottom = words.Max(static word => word.Bounds.Bottom) + 2;
        var bounds = new PixelRect(left, top, right-left, bottom-top);
        primaryLine = new(SourceText, bounds, [new OcrWord(SourceText, bounds,
            new("rapidocr-common-script", "controlled-line", "latin", .99)) { IsDetectedLine = true }]);
        knownWordGeometry = words;
        Check(words.Zip(words.Skip(1)).All(pair => pair.Second.Bounds.Left - pair.First.Bounds.Right >= 20 * .30 - .01),
            "Mixed-size source fixture has no independently specified visible word gap.");
        return image;
    }

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var source = CreateSource(out var line, out var words);
        source.Save(Path.Combine(output, "semantic-same-color-mixed-size-source.png"), ImageFormat.Png);
        Check(!OcrStyleGeometryRefiner.HasMaterialInkColors(source, line.Bounds), "Same-color fixture acquired a color difference.");
        Check(OcrStyleGeometryRefiner.NeedsRefinement(source, [line]),
            "An entire same-color line with 20/36px source fonts did not request real word geometry.");
        var fused = SourceText.Replace(" ", "", StringComparison.Ordinal);
        var fusedLine = line with { Text = fused, Words = [line.Words[0] with { Text = fused }] };
        Check(OcrStyleGeometryRefiner.NeedsRefinement(source, [fusedLine]),
            "A missing-whitespace primary line bypassed the same-color mixed-font geometry check.");
        var repaired = OcrStyleGeometryRefiner.Refine([fusedLine], words).Single();
        Check(repaired.Text == SourceText && repaired.PrimaryRecognitionText == fused
            && repaired.WhitespaceRepair == "windows-word-boundaries",
            "Mixed-font whitespace repair lost original evidence or changed non-whitespace characters.");
        var uncertain = VisualTextRegionBuilder.Build([line], source).Single();
        Check(uncertain.SourceText == SourceText && uncertain.SourceStyleSpans.Any(span => span.Style.Confidence == VisualStyleConfidence.Low),
            "Missing mixed-font geometry was silently described as a uniform style.");
        var refined = OcrStyleGeometryRefiner.Refine([line], words);
        var regions = VisualTextRegionBuilder.Build(refined, source);
        Check(regions.Count == 1 && regions[0].SourceText == SourceText,
            "A font boundary fragmented a semantic sentence.");
        var spans = regions[0].SourceStyleSpans.Where(span => span.Style.EstimatedFontSizePixels is > 0).ToArray();
        Check(spans.Length >= 3 && spans.Max(span => span.Style.EstimatedFontSizePixels!.Value)
            >= spans.Min(span => span.Style.EstimatedFontSizePixels!.Value) * 1.4,
            "Real source glyphs lost their mixed sizes during Builder estimation.");
        var request = VisualTranslationRequestFactory.Create(regions, "zh", DeepSeekModels.Flash).Single();
        var middle = request.SourceSpans.Where(span => span.Start >= 5 && span.Start + span.Length <= 15).ToArray();
        Check(middle.Length > 0, "Large source phrase had no provider style annotation.");
        var result = new TranslationResult(true, TargetText, "controlled-model") { TextAlignments = middle.Select(span =>
            new TranslationTextAlignment(span.Start, span.Length, span.Text == "word" ? 3 : 1,
                span.Text == "every word" ? 3 : span.Text == "word" ? 1 : 2,
                Evidence: TranslationStyleAlignmentEvidence.ModelSemanticSpan)).ToArray() };
        var resolution = TranslationStyleResolver.Resolve(regions[0], request, result);
        Check(resolution.Runs.Any(run => run.Start == 1 && run.Style.EstimatedFontSizePixels is > 28),
            "Target style run did not retain the large source phrase's measured font size.");
        var plan = InPlaceLayoutService.BuildRegionPlan(source, regions, [result]);
        Check(plan.Items.Count == 1 && plan.Rejections.Count == 0
            && plan.Items[0].TextLayout!.StyleRuns.Any(run => run.Start == 1 && run.Style.EstimatedFontSizePixels is > 28),
            "Measured large-font target run disappeared before actual drawing.");
        using var rendered = TranslatedSelectionRenderer.Render(source, new(0,0,source.Width,source.Height), plan.Items);
        rendered.Save(Path.Combine(output, "semantic-same-color-mixed-size-translated.png"), ImageFormat.Png);
        using var uniform = CreateSource(out var uniformLine, out _, mixed: false);
        Check(!OcrStyleGeometryRefiner.NeedsRefinement(uniform, [uniformLine]),
            "Uniform 20px text unnecessarily triggered a second OCR for normal ascenders/descenders.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

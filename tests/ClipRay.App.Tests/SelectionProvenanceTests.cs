using System.Drawing;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class SelectionProvenanceTests
{
    internal static void Run()
    {
        using var source = new Bitmap(120, 40);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.White);
            graphics.FillRectangle(Brushes.Black, 10, 12, 80, 14);
        }
        OcrWord[] children = [new("first", new(8, 8, 30, 24)), new("second", new(52, 8, 42, 24))];
        var parent = new OcrWord("first second", new(8, 8, 86, 24))
            { IsDetectedLine = true, RecognitionWords = children };
        var block = new OcrTextBlock(parent.Text, parent.Bounds, children)
        {
            PrimaryRecognitionText = parent.Text,
            PrimaryRecognitionWords = [parent],
            WhitespaceRepair = "windows-word-boundaries",
        };
        var capture = new PixelRect(100, 200, 120, 40);
        var full = SelectionCapturePolicy.ConstrainToSelection([block], new(100, 200, 120, 40), capture).Single();
        Check(ReferenceEquals(full.PrimaryRecognitionWords.Single(), parent)
            && full.PrimaryRecognitionText == parent.Text && full.WhitespaceRepair == block.WhitespaceRepair,
            "Actual screenshot scope discarded source provenance.");
        var region = new VisualTextRegion("scope", full.Text, [full], full.Bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        using var fullMask = TextReplacementMask.Build(source, region);
        Check(fullMask.Mask.GetPixel(45, 18).A == 255, "Actual scope left connected source ink in a CTC gap.");

        var half = SelectionCapturePolicy.ConstrainToSelection([block], new(100, 200, 48, 40), capture).Single();
        Check(half.Text == "first" && half.PrimaryRecognitionWords.Single().Text == "first second",
            "Half selection lost either selected text or original detector text.");
        using var halfMask = TextReplacementMask.Build(source,
            region with { SourceText = half.Text, Lines = [half], Bounds = half.Bounds });
        for (var y = 0; y < source.Height; y++)
        for (var x = 48; x < source.Width; x++)
            Check(halfMask.Mask.GetPixel(x, y).A == 0, "Clipped detector evidence erased outside the user's selection.");
        var repaired = MaskedBackgroundRepair.Repair(source, halfMask);
        Check(repaired.PatchPng is not null && halfMask.Bounds.Right == 48,
            "Half selection omitted selected detector edge ink from its exported patch bounds.");
        using (var stream = new System.IO.MemoryStream(repaired.PatchPng!))
        using (var patch = new Bitmap(stream))
            Check(patch.GetPixel(45 - (int)halfMask.Bounds.Left, 18 - (int)halfMask.Bounds.Top).ToArgb() == Color.White.ToArgb(),
                "Half selection exported patch left connected original ink behind.");
        var plan = InPlaceLayoutService.BuildRegionPlan(source,
            [region with { SourceText = half.Text, Lines = [half], Bounds = half.Bounds }],
            [new TranslationResult(true, "首个", "completed")]);
        Check(plan.Items.Count == 1, "Half selection could not produce an ordinary in-place result.");
        using var rendered = TranslatedSelectionRenderer.Render(source, new(0, 0, source.Width, source.Height), plan.Items);
        for (var y = 0; y < source.Height; y++)
        for (var x = 48; x < source.Width; x++)
            Check(rendered.GetPixel(x, y).ToArgb() == source.GetPixel(x, y).ToArgb(),
                "Half selection final renderer changed a neighboring unselected pixel.");
        OcrWord[] overlapping = [new("first", new(8, 8, 50, 24)), new("second", new(52, 8, 42, 24))];
        var overlappingParent = parent with { RecognitionWords = overlapping };
        var overlappingBlock = block with { Words = overlapping, PrimaryRecognitionWords = [overlappingParent] };
        var safeHalf = SelectionCapturePolicy.ConstrainToSelection([overlappingBlock], new(100, 200, 70, 40), capture).Single();
        using var overlapMask = TextReplacementMask.Build(source,
            region with { SourceText = safeHalf.Text, Lines = [safeHalf], Bounds = safeHalf.Bounds });
        for (var y = 8; y < 32; y++)
        for (var x = 52; x < 94; x++)
            Check(overlapMask.Mask.GetPixel(x, y).A == 0, "Parent evidence erased an excluded sibling with overlapping OCR boxes.");

        var action = new VisualTextRegion("mention-action", "Ask @Alice for help",
            [new("Ask @Alice for help", new(0, 0, 240, 24), [])], new(0, 0, 240, 24),
            VisualTextRole.Control, VisualStructureConfidence.High);
        Check(VisualTranslationRequestFactory.Create([action], "zh", DeepSeekModels.Flash)[0].TextKind
            != TranslationTextKind.IdentityMetadata, "Action text containing a handle was hidden as identity metadata.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}

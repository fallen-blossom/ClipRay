using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ClipRay.App.Ocr;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class ReadableParagraphLayoutTests
{
    private const string Original = "Save changes to this project.";
    private const string Translated = "请确认是否保存当前项目中尚未保存的全部更改。";

    public static void Run(string? output = null)
    {
        BlankSpaceKeepsReadableSourceSize(output);
        GraphicAndSelectionBoundariesRemainProtected();
        ControlsKeepTheirOriginalContainer();
        LargerStyleRunsHaveUnclippedLineHeight();
        LiveRefreshRebuildsGeometryAndPixelWitness();
        Console.WriteLine("READABLE_PARAGRAPH_LAYOUT_PASSED");
    }

    private static void BlankSpaceKeepsReadableSourceSize(string? output)
    {
        using var source = Create(out var region);
        var plan = InPlaceLayoutService.BuildRegionPlan(source, [region], [new(true, Translated, "controlled")]);
        Check(plan.Items.Count == 1, "Normal paragraph with blank space had no visible translation.");
        var item = plan.Items[0];
        var sourceSize = item.SourceStyle!.EstimatedFontSizePixels!.Value;
        Check(item.TextLayout!.FontSizePixels >= sourceSize * .95,
            $"Blank space was ignored: source {sourceSize:F2}px, target {item.TextLayout.FontSizePixels:F2}px.");
        Check(item.Bounds.Height > region.Bounds.Height && item.Bounds.Bottom <= source.Height,
            "Paragraph did not use bounded blank space to wrap.");
        using var patchStream = new System.IO.MemoryStream(item.MaskedPatchPng!);
        using var patch = new Bitmap(patchStream);
        Check(patch.Width == (int)item.Bounds.Width && patch.Height == (int)item.Bounds.Height,
            "Expanded text container would stretch the original erasure patch.");
        for (var y = (int)region.Bounds.Height; y < patch.Height; y++)
        for (var x = 0; x < patch.Width; x++)
            Check(patch.GetPixel(x, y).A == 0, "Borrowed blank space acquired an opaque erasure.");
        using var rendered = TranslatedSelectionRenderer.Render(source,
            new(0, 0, source.Width, source.Height), plan.Items);
        Check(CountInk(rendered, item.Bounds, region.Bounds.Bottom + 1) > 12,
            "Wrapped translation was absent from the actual raster.");
        Check(rendered.GetPixel(5, 5).ToArgb() == source.GetPixel(5, 5).ToArgb(),
            "Pixels outside the translation changed.");
        if (output is not null)
        {
            System.IO.Directory.CreateDirectory(output);
            source.Save(System.IO.Path.Combine(output, "paragraph-source.png"), ImageFormat.Png);
            rendered.Save(System.IO.Path.Combine(output, "paragraph-translated.png"), ImageFormat.Png);
            Console.WriteLine($"PARAGRAPH_FONT source={sourceSize:F2} target={item.TextLayout.FontSizePixels:F2} height={item.Bounds.Height:F0}");
        }
    }

    private static void GraphicAndSelectionBoundariesRemainProtected()
    {
        using var source = Create(out var region);
        var barrierY = (int)region.Bounds.Bottom + 3;
        using (var graphics = Graphics.FromImage(source))
            graphics.FillRectangle(Brushes.Blue, (float)region.Bounds.X + 8, barrierY, 12, 8);
        var result = new TranslationResult(true, Translated, "controlled");
        var graphicPlan = InPlaceLayoutService.BuildRegionPlan(source, [region], [result]);
        Check(graphicPlan.Items.All(item => item.Bounds.Bottom <= barrierY),
            "Paragraph wrapped into an unrecognized graphic.");
        using (var graphics = Graphics.FromImage(source))
        using (var faint = new SolidBrush(Color.FromArgb(250, 250, 250)))
        {
            graphics.FillRectangle(Brushes.White, (float)region.Bounds.Left, barrierY, (float)region.Bounds.Width, 8);
            graphics.FillRectangle(faint, (float)region.Bounds.Left, barrierY, (float)region.Bounds.Width, 1);
        }
        var borderPlan = InPlaceLayoutService.BuildRegionPlan(source, [region], [result]);
        Check(borderPlan.Items.All(item => item.Bounds.Bottom <= barrierY),
            "Paragraph treated a low-contrast control border as blank space.");
        var selectedBottom = region.Bounds.Bottom + 2;
        var constrained = region with { Lines = region.Lines.Select(line => line with
            { SelectionBounds = new(region.Bounds.X, region.Bounds.Y, region.Bounds.Width, selectedBottom-region.Bounds.Y) }).ToArray() };
        var selectionPlan = InPlaceLayoutService.BuildRegionPlan(source, [constrained], [result]);
        Check(selectionPlan.Items.All(item => item.Bounds.Bottom <= selectedBottom),
            "Paragraph expanded outside the actual screenshot selection.");
        var neighborBounds = new PixelRect(region.Bounds.X, region.Bounds.Bottom + 3, region.Bounds.Width, 22);
        var neighbor = new VisualTextRegion("neighbor", "Other text stays here.",
            [new("Other text stays here.", neighborBounds, [new("Other text stays here.", neighborBounds)])],
            neighborBounds, VisualTextRole.Body, VisualStructureConfidence.High);
        var siblingPlan = InPlaceLayoutService.BuildRegionPlan(source, [region, neighbor],
            [result, new(false, "", "controlled-failure")]);
        Check(siblingPlan.Items.All(item => item.Bounds.Bottom <= neighbor.Bounds.Top),
            "Paragraph expanded over a preserved sibling.");
    }

    private static void ControlsKeepTheirOriginalContainer()
    {
        using var source = Create(out var region);
        region = region with { Role = VisualTextRole.Control };
        var plan = InPlaceLayoutService.BuildRegionPlan(source, [region], [new(true, Translated, "controlled")]);
        Check(plan.Items.All(item => item.Bounds == region.Bounds),
            "A control label expanded beyond its existing container.");
    }

    private static void LargerStyleRunsHaveUnclippedLineHeight()
    {
        const string text = "Keep words readable.";
        var bounds = new PixelRect(0, 0, 320, 64);
        var style = new SourceVisualStyle(Color.Black, 14, 24, SourceFontWeight.Normal,
            SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0, VisualStyleConfidence.High, 20);
        var large = style with { ForegroundColor = Color.Red, EstimatedFontSizePixels = 36, GlyphHeightPixels = 26, LineHeightPixels = 43 };
        var layout = TranslationTextLayoutPolicy.Fit(text, bounds, style);
        layout = TranslationTextLayoutPolicy.WithStyleRuns(text, bounds, style, layout,
            [new(5, 5, large, TranslationStyleAlignmentEvidence.ModelSemanticSpan)]);
        var formatted = TranslationTextLayoutPolicy.Format(text, bounds.Width, layout, System.Windows.Media.Brushes.Black);
        var ink = formatted.BuildGeometry(new System.Windows.Point()).Bounds;
        Check(layout.StyleRuns.Count == 1 && ink.Top >= -.5 && ink.Bottom <= bounds.Height + .5,
            $"A retained large source style was clipped by the base line height: {ink}.");
    }

    private static void LiveRefreshRebuildsGeometryAndPixelWitness()
    {
        using var source = Create(out var region);
        var initial = InPlaceLayoutService.BuildRegionPlan(source, [region], [new(true, Translated, "controlled")]).Items.Single();
        Check(initial.Bounds.Height > region.Bounds.Height, "Live refresh fixture did not begin expanded.");
        using var current = new Bitmap(source);
        var icon = new Rectangle((int)region.Bounds.Left + 8, (int)region.Bounds.Bottom + 3, 12, 8);
        using (var graphics = Graphics.FromImage(current)) graphics.FillRectangle(Brushes.Blue, icon);
        var snapshot = new LiveTranslationPixelSnapshot(current);
        var retained = LiveTranslationRegionWitness.Create(new(source), [initial])
            .RetainForViewportCommit(snapshot, static _ => true, [region]);
        Check(retained.Count == 1 && retained.Items[0].Bounds == region.Bounds,
            "Live backing refresh kept old expanded geometry after blank space acquired a graphic.");
        using var stream = new System.IO.MemoryStream(retained.Items[0].MaskedPatchPng!);
        using var patch = new Bitmap(stream);
        Check(patch.Height == (int)retained.Items[0].Bounds.Height, "Refreshed patch would be stretched to an old container.");
        using var raster = TranslatedSelectionRenderer.Render(current, new(0, 0, current.Width, current.Height), retained.Items);
        Check(raster.GetPixel(icon.Left + 2, icon.Top + 2).ToArgb() == Color.Blue.ToArgb(), "Live refresh covered the new graphic.");
        Check(retained.RetainUnchanged(snapshot).Count == 1, "Refreshed geometry did not retain its matching pixel witness.");
    }

    private static Bitmap Create(out VisualTextRegion region)
    {
        var source = new Bitmap(420, 200, PixelFormat.Format32bppArgb);
        var bounds = new PixelRect(20, 20, 270, 28);
        using (var graphics = Graphics.FromImage(source))
        using (var font = new Font("Segoe UI", 20, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(Original, font, Brushes.Black, 20, 20, StringFormat.GenericTypographic);
        }
        var line = new OcrTextBlock(Original, bounds, [new(Original, bounds)])
            { SelectionBounds = new(0, 0, source.Width, source.Height) };
        region = new("paragraph", Original, [line], bounds, VisualTextRole.Body, VisualStructureConfidence.High);
        return source;
    }

    private static int CountInk(Bitmap image, PixelRect bounds, double below)
    {
        var count = 0;
        for (var y = (int)Math.Ceiling(below); y < Math.Min(image.Height, bounds.Bottom); y++)
        for (var x = (int)bounds.Left; x < Math.Min(image.Width, bounds.Right); x++)
        {
            var color = image.GetPixel(x, y);
            if (color.R < 140 && color.G < 140 && color.B < 140) count++;
        }
        return count;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

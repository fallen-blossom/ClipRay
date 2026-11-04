using System.Drawing;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class MortSourceColorTests
{
    internal static void Run()
    {
        foreach (var (background, ink) in new[]
        {
            (Color.White, Color.FromArgb(192, 32, 48)),
            (Color.FromArgb(24, 24, 24), Color.FromArgb(100, 210, 250)),
        })
        {
            using var bitmap = new Bitmap(160, 50);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(ink))
            {
                graphics.Clear(background);
                graphics.FillRectangle(brush, 20, 12, 36, 20);
                graphics.FillRectangle(brush, 78, 12, 40, 20);
            }
            OcrWord[] words = [new("first", new(16, 8, 44, 28)), new("second", new(74, 8, 48, 28))];
            var block = new OcrTextBlock("first second", new(16, 8, 106, 28), words);
            var result = MortSourceColorAdapter.Analyze(bitmap, block);
            Check(result.Success && !result.UsedFontFallback && result.ForegroundWordSupport == 2,
                "Pinned upstream helper did not observe both word samples.");
            Check(result.Font.ToArgb() == ink.ToArgb() && result.Background.ToArgb() == background.ToArgb(),
                "Pinned upstream helper changed the exact solid source colors.");
            var style = SourceVisualStyleEstimator.Estimate(bitmap, block, [block], background);
            Check(style.ForegroundColor.ToArgb() == ink.ToArgb(), "Production style consumer lost the upstream source color.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

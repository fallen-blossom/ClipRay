using System.Drawing;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class NeutralTextRegionTests
{
    public static void NeutralShadesKeepSentenceContext()
    {
        DistantColumnsDoNotBreakWrappedProse();
        SentenceBoundaryInsideWrappedParagraph();
        using var bitmap = new Bitmap(520, 80);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        using var font = new Font("Arial", 20);
        var words = new List<OcrWord>();
        var parts = new[] { "This", "sentence", "needs", "context." };
        for (var index = 0; index < parts.Length; index++)
        {
            using var brush = new SolidBrush(index % 2 == 0 ? Color.White : Color.FromArgb(125, 125, 125));
            graphics.DrawString(parts[index], font, brush, 10 + index * 125, 15);
            words.Add(new(parts[index], new PixelRect(10 + index * 125, 15, 120, 32)));
        }
        var text = string.Join(' ', parts);
        var regions = VisualTextRegionBuilder.Build([new(text, new PixelRect(10, 15, 495, 32), words)], bitmap);
        if (regions.Count != 1 || regions[0].SourceText != text)
            throw new Exception("Neutral foreground shades split a sentence into unrelated translation requests.");
    }

    private static void SentenceBoundaryInsideWrappedParagraph()
    {
        var first = new OcrTextBlock("The library closes at six each evening.", new(37, 38, 911, 54), []);
        var second = new OcrTextBlock("Please return your borrowed books before", new(34, 103, 865, 54), []);
        var last = new OcrTextBlock("the doors close.", new(34, 169, 400, 54), []);
        if (VisualTextRegionBuilder.Build([first, second, last]).Count != 1)
            throw new Exception("A sentence boundary split a continuously wrapped paragraph.");
        if (VisualTextRegionBuilder.Build([first, second with { Bounds = new(34, 180, 865, 54) }]).Count != 2)
            throw new Exception("Separate cards were joined across a blank gap.");
        if (VisualTextRegionBuilder.Build([first, second with { Bounds = new(180, 103, 865, 54) }]).Count != 2)
            throw new Exception("Different paragraph indents were joined across a sentence boundary.");
    }

    private static void DistantColumnsDoNotBreakWrappedProse()
    {
        OcrTextBlock Block(string text, double x, double y, double width, double height = 38) =>
            new(text, new PixelRect(x, y, width, height), [new(text, new PixelRect(x, y, width, height))]);
        var regions = VisualTextRegionBuilder.Build([
            Block("This paragraph describes repeated requests, their context,", 419, 661, 738),
            Block("and their results.", 419, 692, 211, 37),
            Block("Membership", 7, 694, 144, 49),
            Block("People to follow", 1292, 676, 220, 46)]);
        var paragraph = regions.Single(region => region.SourceText.StartsWith("This paragraph"));
        if (!paragraph.SourceText.Contains("and their results."))
            throw new Exception("Unrelated columns turned a wrapped paragraph ending into an isolated control row.");
    }
}

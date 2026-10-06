namespace ClipRay.App.Ocr;

public static class OcrTextLayout
{
    public static string FormatLines(IEnumerable<OcrTextBlock> blocks) => string.Join(
        Environment.NewLine,
        blocks
            .Where(static block => !string.IsNullOrWhiteSpace(block.Text))
            .OrderBy(static block => block.Bounds.Top)
            .ThenBy(static block => block.Bounds.Left)
            .Select(static block => block.Text.Trim()));
}

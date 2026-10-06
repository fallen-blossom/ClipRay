using ClipRay.App.Ocr;

namespace ClipRay.App.Translation;

public enum TranslationBlockState
{
    Changed,
    Unchanged,
    Failed,
    SkippedVisual,
}

public sealed record TranslationOverlayBlock(
    OcrTextBlock SourceBlock,
    string Text,
    TranslationBlockState State,
    string Status);

public static class TranslationOverlayPlan
{
    public static IReadOnlyList<TranslationOverlayBlock> Create(
        IReadOnlyList<OcrTextBlock> blocks,
        IReadOnlyList<TranslationResult> results)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(results);
        if (blocks.Count != results.Count)
        {
            throw new ArgumentException("Each OCR block must retain one translation result.", nameof(results));
        }

        return blocks.Select((block, index) =>
        {
            var result = results[index];
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Text))
            {
                return new TranslationOverlayBlock(block, block.Text, TranslationBlockState.Failed, result.Status);
            }

            var unchanged = string.Equals(block.Text.Trim(), result.Text.Trim(), StringComparison.Ordinal);
            return new TranslationOverlayBlock(
                block,
                result.Text,
                unchanged ? TranslationBlockState.Unchanged : TranslationBlockState.Changed,
                result.Status);
        }).ToArray();
    }
}

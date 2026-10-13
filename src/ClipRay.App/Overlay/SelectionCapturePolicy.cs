using ClipRay.Core.Geometry;
using ClipRay.App.Ocr;

namespace ClipRay.App.Overlay;

public static class SelectionCapturePolicy
{
    private const double PaddingRatio = 0.2;
    private const double MinimumPadding = 6;
    private const double MaximumPadding = 12;

    public static PixelRect ExpandForOcr(PixelRect selection, PixelRect bounds)
    {
        if (selection.IsEmpty) throw new ArgumentException("Selection must not be empty.", nameof(selection));
        if (bounds.IsEmpty) throw new ArgumentException("Capture bounds must not be empty.", nameof(bounds));

        var padding = Math.Clamp(
            Math.Min(selection.Width, selection.Height) * PaddingRatio,
            MinimumPadding,
            MaximumPadding);
        var left = Math.Max(bounds.Left, selection.Left - padding);
        var top = Math.Max(bounds.Top, selection.Top - padding);
        var right = Math.Min(bounds.Right, selection.Right + padding);
        var bottom = Math.Min(bounds.Bottom, selection.Bottom + padding);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelRect CreateOcrCaptureBounds(PixelRect selection, PixelRect bounds)
    {
        var expanded = ExpandForOcr(selection, bounds);
        var left = Math.Max(bounds.Left, Math.Floor(expanded.Left));
        var top = Math.Max(bounds.Top, Math.Floor(expanded.Top));
        var right = Math.Min(bounds.Right, Math.Ceiling(expanded.Right));
        var bottom = Math.Min(bounds.Bottom, Math.Ceiling(expanded.Bottom));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static IReadOnlyList<OcrTextBlock> ConstrainToSelection(
        IReadOnlyList<OcrTextBlock> blocks,
        PixelRect selection,
        PixelRect captureBounds)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (selection.IsEmpty) throw new ArgumentException("Selection must not be empty.", nameof(selection));
        if (captureBounds.IsEmpty) throw new ArgumentException("Capture bounds must not be empty.", nameof(captureBounds));

        var relativeSelection = new PixelRect(
            selection.X - captureBounds.X,
            selection.Y - captureBounds.Y,
            selection.Width,
            selection.Height);
        var constrained = new List<OcrTextBlock>();
        foreach (var block in blocks)
        {
            if (block.Words.Count == 0)
            {
                if (BelongsToSelection(relativeSelection, block.Bounds)
                    && TryIntersect(block.Bounds, relativeSelection, out var clipped))
                {
                    constrained.Add(block with { Bounds = clipped });
                }
                continue;
            }

            var selectedWords = block.Words
                .Where(word => BelongsToSelection(relativeSelection, word.Bounds))
                .ToArray();
            foreach (var selectedBlock in OcrBlockGrouping.GroupWords(selectedWords))
            {
                if (TryIntersect(selectedBlock.Bounds, relativeSelection, out var clipped))
                {
                    constrained.Add(selectedBlock with { Bounds = clipped });
                }
            }
        }

        return constrained
            .OrderBy(static block => block.Bounds.Top)
            .ThenBy(static block => block.Bounds.Left)
            .ToArray();
    }

    private static bool BelongsToSelection(PixelRect selection, PixelRect candidate) => Classify(selection,candidate)=="selected";

    internal static string WordDisposition(PixelRect selection,PixelRect captureBounds,PixelRect candidate) =>
        Classify(new(selection.X-captureBounds.X,selection.Y-captureBounds.Y,selection.Width,selection.Height),candidate);

    private static string Classify(PixelRect selection, PixelRect candidate)
    {
        var x = candidate.Left + candidate.Width / 2;
        var y = candidate.Top + candidate.Height / 2;
        if (x < selection.Left || x > selection.Right || y < selection.Top || y > selection.Bottom)
            return "center-outside-selection";
        if (!TryIntersect(selection, candidate, out var overlap)) return "no-overlap";

        // Padding gives OCR context, but a neighboring row belongs to the user's
        // selection only when most of its glyph box is actually inside it.
        return overlap.Height / candidate.Height >= 0.60 ? "selected" : "height-overlap-below-0.60";
    }

    private static bool TryIntersect(PixelRect first, PixelRect second, out PixelRect intersection)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        intersection = right > left && bottom > top
            ? new PixelRect(left, top, right - left, bottom - top)
            : default;
        return !intersection.IsEmpty;
    }
}

using ClipRay.Core.Geometry;
using RapidOcrNet;

namespace ClipRay.App.Ocr;

// CTC alignment is model-derived geometry, not a measured glyph/ink boundary.
// Axis-aligned adaptation of the column-to-pixel/word grouping mechanism in
// BobLd/RapidOcrNet CalRecBoxes.cs (Apache-2.0), pinned commit
// 590d44b2efc7ecbef446c36bb6359ebbfcffeb50. No recognition is rerun here.
internal static class NativeOcrWordGeometry
{
    internal static IReadOnlyList<OcrWord> FromNativeWords(IReadOnlyList<WordBox>? boxes,
        string parentText, PixelRect parentBounds, OcrWordEvidence evidence,
        string[]? parentChars = null, float[]? parentCharScores = null)
    {
        if (boxes is null || boxes.Count == 0 || parentBounds.IsEmpty) return [];
        var words = new List<OcrWord>(boxes.Count);
        var characterOffset = 0;
        foreach (var box in boxes)
        {
            if (string.IsNullOrWhiteSpace(box.Text) || box.BoxPoints is not { Length: >= 4 }) return [];
            var left = Math.Max(parentBounds.Left, box.BoxPoints.Min(point => point.X));
            var top = Math.Max(parentBounds.Top, box.BoxPoints.Min(point => point.Y));
            var right = Math.Min(parentBounds.Right, box.BoxPoints.Max(point => point.X));
            var bottom = Math.Min(parentBounds.Bottom, box.BoxPoints.Max(point => point.Y));
            if (right <= left || bottom <= top) return [];
            var endOffset = characterOffset + Compact(box.Text).Length;
            // The pinned upstream WordBox.Score advances over nonblank glyphs
            // while CharScores still includes whitespace. Rebind each score to
            // its original recognized-token range, or keep confidence unknown.
            words.Add(new(box.Text, new(left, top, right - left, bottom - top), evidence with
            {
                RawScore = RawScoreForRange(parentText, parentChars, parentCharScores, characterOffset, endOffset)
            }));
            characterOffset = endOffset;
        }
        return Compact(parentText) == Compact(string.Concat(words.Select(word => word.Text))) ? words : [];
    }

    private static double? RawScoreForRange(string parentText, string[]? chars, float[]? scores, int start, int end)
    {
        if (chars is null || scores is null || chars.Length != scores.Length
            || chars.Any(text => text is null) || Compact(string.Concat(chars)) != Compact(parentText)) return null;
        var offset = 0;
        var matchedLength = 0;
        var matchedScores = new List<double>();
        for (var index = 0; index < chars.Length; index++)
        {
            var length = Compact(chars[index]).Length;
            var next = offset + length;
            if (length > 0 && next > start && offset < end)
            {
                // A dictionary token spanning two WordBoxes cannot supply a
                // distinct per-word confidence. Never divide or invent it.
                if (offset < start || next > end || !float.IsFinite(scores[index])) return null;
                matchedLength += length;
                matchedScores.Add(scores[index]);
            }
            offset = next;
        }
        return matchedLength == end - start && matchedScores.Count > 0 ? matchedScores.Average() : null;
    }

    internal static IReadOnlyList<OcrWord> FromCtcLine(TextLine line, PixelRect crop,
        string parentText, OcrWordEvidence evidence)
    {
        var chars = line.Chars; var columns = line.CharCols; var scores = line.CharScores;
        var totalColumns = float.IsFinite(line.LineTxtLen) && line.LineTxtLen > 0 ? line.LineTxtLen : line.ColCount;
        if (crop.IsEmpty || chars is not { Length: > 0 } || columns is null || scores is null
            || chars.Length != columns.Length || chars.Length != scores.Length || totalColumns <= 0
            || Compact(parentText) != Compact(string.Concat(chars))) return [];
        for (var index = 0; index < columns.Length; index++)
            if (chars[index] is null || columns[index] < 0 || columns[index] >= totalColumns
                || index > 0 && columns[index] <= columns[index - 1]) return [];

        var groups = new List<List<int>>();
        List<int> current = [];
        for (var index = 0; index < chars.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(chars[index])) { Flush(); continue; }
            var nativeGlyph = chars[index].Any(character => character is >= '\u3040' and <= '\u30ff'
                or >= '\u3400' and <= '\u9fff' or >= '\uac00' and <= '\ud7af');
            if (nativeGlyph) { Flush(); groups.Add([index]); continue; }
            // Match the pinned upstream's CTC gap boundary heuristic. A large
            // blank interval can delimit words even if the recognizer lost space.
            if (current.Count > 0 && columns[index] - columns[current[^1]] > 5) Flush();
            current.Add(index);
        }
        Flush();
        if (groups.Count == 0) return [];

        var pixelPerColumn = crop.Width / totalColumns;
        var averageWidths = groups.Where(group => group.Count > 1)
            .Select(group => (columns[group[^1]] - columns[group[0]]) * pixelPerColumn / (group.Count - 1)).ToArray();
        var glyphWidth = averageWidths.Length > 0 ? averageWidths.Average()
            : crop.Width / Math.Max(1, chars.Count(text => !string.IsNullOrWhiteSpace(text)));
        var cells = groups.Select(group => new Cell(group,
            Math.Clamp(crop.Left + (columns[group[0]] + .5) * pixelPerColumn - glyphWidth / 2, crop.Left, crop.Right),
            Math.Clamp(crop.Left + (columns[group[^1]] + .5) * pixelPerColumn + glyphWidth / 2, crop.Left, crop.Right))).ToArray();
        for (var index = 1; index < cells.Length; index++)
        {
            if (cells[index - 1].Right <= cells[index].Left) continue;
            var boundary = (cells[index - 1].Right + cells[index].Left) / 2;
            cells[index - 1] = cells[index - 1] with { Right = boundary };
            cells[index] = cells[index] with { Left = boundary };
        }
        if (cells.Any(cell => cell.Right <= cell.Left)) return [];
        return cells.Select(cell => new OcrWord(string.Concat(cell.Indices.Select(index => chars[index])),
            new(cell.Left, crop.Top, cell.Right - cell.Left, crop.Height), evidence with
            {
                RawScore = cell.Indices.All(index => float.IsFinite(scores[index]))
                    ? cell.Indices.Average(index => (double)scores[index]) : null
            })).ToArray();

        void Flush() { if (current.Count > 0) { groups.Add(current); current = []; } }
    }

    private static string Compact(string text) => string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
    private sealed record Cell(List<int> Indices, double Left, double Right);
}

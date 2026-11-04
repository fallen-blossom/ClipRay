using System.Drawing;
using System.Text;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Ocr;

// Optional local geometry evidence. Only explicitly recorded whitespace repair
// may change primary recognition text; every other character must agree exactly.
public static class OcrStyleGeometryRefiner
{
    public static bool NeedsRefinement(Bitmap source, IReadOnlyList<OcrTextBlock> blocks) =>
        blocks.Any(block => block.Words.Count <= 1
            && (HasMaterialInkColors(source, block.Bounds)
                || HasEnoughLatinText(block.Text) && HasMaterialInkHeightVariation(source, block.Bounds)));

    public static IReadOnlyList<OcrTextBlock> Refine(IReadOnlyList<OcrTextBlock> primary, IReadOnlyList<OcrWord> geometryWords)
    {
        var result = new List<OcrTextBlock>(primary.Count);
        foreach (var block in primary)
        {
            if (block.Words.Count > 1) { result.Add(block); continue; }
            var matches = geometryWords.Where(word => !word.IsDetectedLine && !word.Bounds.IsEmpty
                && Coverage(word.Bounds, block.Bounds) >= .95
                && Math.Abs(word.Bounds.Top + word.Bounds.Height / 2 - block.Bounds.Top - block.Bounds.Height / 2)
                    <= block.Bounds.Height * .40)
                .OrderBy(static word => word.Bounds.Left).ToArray();
            // Exact character agreement, one ordered horizontal row, no borrowing
            // a subset from a nearby row or a repeated line elsewhere on screen.
            if (matches.Length < 2 || !Compact(string.Concat(matches.Select(static word => word.Text))).Equals(Compact(block.Text), StringComparison.Ordinal)
                || matches.Zip(matches.Skip(1)).Any(pair => pair.Second.Bounds.Left < pair.First.Bounds.Right - 1))
            { result.Add(block); continue; }
            var cursor = 0;
            var accepted = new List<OcrWord>();
            var wordStarts = new List<int>();
            foreach (var word in matches)
            {
                var text = word.Text.Trim();
                var start = block.Text.IndexOf(text, cursor, StringComparison.Ordinal);
                if (text.Length == 0 || start < 0 || block.Text[cursor..start].Any(static character => !char.IsWhiteSpace(character)))
                { accepted.Clear(); break; }
                accepted.Add(word with { Text = text });
                wordStarts.Add(start);
                cursor = start + text.Length;
            }
            if (accepted.Count != matches.Length || !string.IsNullOrWhiteSpace(block.Text[cursor..]))
            { result.Add(block); continue; }
            var insertions = new HashSet<int>();
            var protectedRanges = VisualTranslationRequestFactory.GetWhitespaceRepairProtectedRanges(block.Text);
            for (var index = 1; index < accepted.Count; index++)
            {
                var start = wordStarts[index];
                var previous = accepted[index - 1];
                var current = accepted[index];
                // Repair lost Latin word separators only at independently recognized
                // word boxes with a real positive pixel gap. Preserve existing source
                // whitespace; do not add spaces between CJK glyphs or punctuation.
                if (start > 0 && !char.IsWhiteSpace(block.Text[start - 1])
                    && !protectedRanges.Any(range => start > range.Start && start < range.Start + range.Length)
                    && char.IsAsciiLetterOrDigit(block.Text[start - 1]) && char.IsAsciiLetterOrDigit(block.Text[start])
                    && current.Bounds.Left - previous.Bounds.Right >= Math.Max(1, Math.Min(previous.Bounds.Height, current.Bounds.Height) * .06))
                    insertions.Add(start);
            }
            var repaired = new StringBuilder(block.Text.Length + insertions.Count);
            for (var index = 0; index < block.Text.Length; index++)
            {
                if (insertions.Contains(index)) repaired.Append(' ');
                repaired.Append(block.Text[index]);
            }
            var repairedText = repaired.ToString();
            // Primary model text/evidence stays available independently of borrowed
            // Windows geometry and the explicit word-boundary whitespace repair.
            result.Add(block with { Text = repairedText, Words = accepted,
                PrimaryRecognitionText = block.PrimaryRecognitionText ?? block.Text,
                PrimaryRecognitionWords = block.PrimaryRecognitionWords.Count > 0 ? block.PrimaryRecognitionWords : block.Words,
                WhitespaceRepair = repairedText == block.Text ? block.WhitespaceRepair : "windows-word-boundaries" });
        }
        return result;
    }

    public static bool HasMaterialInkColors(Bitmap source, PixelRect bounds)
    {
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, source.Height);
        if (right <= left || bottom <= top) return false;
        var border = new List<Color>();
        for (var x = left; x < right; x++) { border.Add(source.GetPixel(x, top)); border.Add(source.GetPixel(x, bottom - 1)); }
        for (var y = top; y < bottom; y++) { border.Add(source.GetPixel(left, y)); border.Add(source.GetPixel(right - 1, y)); }
        var backing = Color.FromArgb(Median(border.Select(static color => color.R)), Median(border.Select(static color => color.G)), Median(border.Select(static color => color.B)));
        var ink = new List<Color>();
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var pixel = source.GetPixel(x, y);
            if (Distance(pixel, backing) >= 64) ink.Add(pixel);
        }
        if (ink.Count < 16) return false;
        var clusters = new List<List<Color>>();
        foreach (var pixel in ink)
        {
            var group = clusters.FirstOrDefault(candidate => SameInkDirection(candidate[0], pixel, backing));
            if (group is null) clusters.Add([pixel]); else group.Add(pixel);
        }
        var substantial = clusters.Where(group => group.Count >= Math.Max(6, ink.Count * .025)).ToArray();
        return substantial.Any(first => substantial.Any(second => !ReferenceEquals(first, second)
            && Distance(first[0], second[0]) >= 72));
    }

    public static bool HasMaterialInkHeightVariation(Bitmap source, PixelRect bounds)
    {
        // This is only a bounded trigger for another local recognizer, never word
        // geometry or a font assignment. Real words must still agree in Refine.
        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, source.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, source.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, source.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, source.Height);
        if (right <= left || bottom <= top) return false;
        var border = new List<Color>();
        for (var x = left; x < right; x++) { border.Add(source.GetPixel(x, top)); border.Add(source.GetPixel(x, bottom - 1)); }
        for (var y = top; y < bottom; y++) { border.Add(source.GetPixel(left, y)); border.Add(source.GetPixel(right - 1, y)); }
        var backing = Color.FromArgb(Median(border.Select(static color => color.R)), Median(border.Select(static color => color.G)), Median(border.Select(static color => color.B)));
        var heights = new List<int>();
        var islandTop = bottom; var islandBottom = top - 1; var islandWidth = 0;
        void FinishIsland()
        {
            var height = islandBottom - islandTop + 1;
            // Punctuation and isolated dot noise do not suggest mixed font sizes.
            if (islandWidth >= 2 && height >= Math.Max(5, (bottom - top) * .20)) heights.Add(height);
            islandTop = bottom; islandBottom = top - 1; islandWidth = 0;
        }
        for (var x = left; x < right; x++)
        {
            var columnTop = bottom; var columnBottom = top - 1;
            for (var y = top; y < bottom; y++)
                if (Distance(source.GetPixel(x, y), backing) >= 64)
                { columnTop = Math.Min(columnTop, y); columnBottom = y; }
            if (columnBottom < columnTop) { FinishIsland(); continue; }
            islandTop = Math.Min(islandTop, columnTop); islandBottom = Math.Max(islandBottom, columnBottom); islandWidth++;
        }
        FinishIsland();
        if (heights.Count < 4) return false;
        heights.Sort();
        var small = heights[heights.Count / 4]; var large = heights[heights.Count * 3 / 4];
        return large >= small * 1.55 && large - small >= 6;
    }

    public static bool IsLatinMultiword(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Count(static word => word.Any(char.IsAsciiLetter)) >= 2;
    public static bool HasEnoughLatinText(string text) => text.Count(char.IsAsciiLetter) >= 4;

    private static string Compact(string text) => string.Concat(text.Where(static character => !char.IsWhiteSpace(character)));
    private static int Median(IEnumerable<byte> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
    private static double Distance(Color a, Color b) => Math.Sqrt((a.R-b.R)*(a.R-b.R)+(a.G-b.G)*(a.G-b.G)+(a.B-b.B)*(a.B-b.B));
    private static bool SameInkDirection(Color a, Color b, Color backing)
    {
        var ar = a.R - backing.R; var ag = a.G - backing.G; var ab = a.B - backing.B;
        var br = b.R - backing.R; var bg = b.G - backing.G; var bb = b.B - backing.B;
        return (ar*br+ag*bg+ab*bb) / Math.Max(1, Distance(a, backing) * Distance(b, backing)) >= .97;
    }
    private static double Coverage(PixelRect a, PixelRect b) => Math.Max(0, Math.Min(a.Right,b.Right)-Math.Max(a.Left,b.Left))
        * Math.Max(0,Math.Min(a.Bottom,b.Bottom)-Math.Max(a.Top,b.Top)) / Math.Max(1,a.Width*a.Height);
}

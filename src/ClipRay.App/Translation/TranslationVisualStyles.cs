using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

// All indices use .NET UTF-16 offsets. Source pixels never enter a provider request.
public sealed record SourceVisualStyleSpan(int Start, int Length, string Text, PixelRect Bounds, SourceVisualStyle Style);
public sealed record TranslationSourceSpan(string Id, int Start, int Length, string Text);
public enum TranslationStyleAlignmentEvidence { ExactText, ModelAttention, ModelSemanticSpan, LexicalAnchorAttention }
public sealed record TranslationTextAlignment(int SourceStart, int SourceLength, int TargetStart, int TargetLength,
    double Confidence = 1, TranslationStyleAlignmentEvidence Evidence = TranslationStyleAlignmentEvidence.ModelAttention);
public sealed record TranslatedVisualStyleRun(int Start, int Length, SourceVisualStyle Style,
    TranslationStyleAlignmentEvidence Evidence);
public sealed record TranslationStyleResolution(IReadOnlyList<TranslatedVisualStyleRun> Runs, int UnresolvedSourceSpanCount);

public static class TranslationStyleResolver
{
    public static TranslationRequest AttachSourceSpans(VisualTextRegion region, TranslationRequest request)
    {
        // Factory joins wrapped lines and trims whitespace. Map only when every
        // non-whitespace character is unchanged, never by relative text length.
        var original = NonWhitespacePositions(region.SourceText);
        var semantic = NonWhitespacePositions(request.OcrText);
        if (original.Length != semantic.Length || !original.Select(i => region.SourceText[i])
            .SequenceEqual(semantic.Select(i => request.OcrText[i]))) return request with { SourceSpans = [] };
        var spans = new List<TranslationSourceSpan>();
        for (var index = 0; index < region.SourceStyleSpans.Count; index++)
        {
            var span = region.SourceStyleSpans[index];
            if (span.Style.Confidence == VisualStyleConfidence.Low || !ValidRange(span.Start, span.Length, region.SourceText.Length)) continue;
            var first = Array.FindIndex(original, position => position >= span.Start);
            var last = Array.FindLastIndex(original, position => position < span.Start + span.Length);
            if (first < 0 || last < first) continue;
            var start = semantic[first];
            var length = semantic[last] - start + 1;
            spans.Add(new($"s{index}", start, length, request.OcrText.Substring(start, length)));
        }
        return request with { SourceSpans = spans };
    }

    public static TranslationStyleResolution Resolve(VisualTextRegion region, TranslationRequest request, TranslationResult result)
    {
        var effective = AttachSourceSpans(region, request);
        var candidates = new List<(TranslatedVisualStyleRun Run, string Id)>();
        if (!result.IsSuccess || result.Text.Length == 0) return new([], region.SourceStyleSpans.Count);
        foreach (var span in effective.SourceSpans)
        {
            var styleIndex = int.Parse(span.Id.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
            var style = region.SourceStyleSpans[styleIndex].Style;
            var alignments = result.TextAlignments.Where(alignment => alignment.Confidence >= .8
                && ValidRange(alignment.SourceStart, alignment.SourceLength, request.OcrText.Length)
                && ValidRange(alignment.TargetStart, alignment.TargetLength, result.Text.Length)
                && alignment.SourceStart >= span.Start && alignment.SourceStart + alignment.SourceLength <= span.Start + span.Length).ToArray();
            foreach (var alignment in alignments)
                candidates.Add((new(alignment.TargetStart, alignment.TargetLength, style, alignment.Evidence), span.Id));
            // An unchanged literal can be located exactly on either provider route.
            // Repeated text is ambiguous; no first-match or proportional coloring.
            if (alignments.Length == 0 && UniqueIndex(request.OcrText, span.Text) == span.Start
                && UniqueIndex(result.Text, span.Text) is var target && target >= 0
                && HasLiteralBoundaries(result.Text, target, span.Text))
                candidates.Add((new(target, span.Text.Length, style, TranslationStyleAlignmentEvidence.ExactText), span.Id));
        }
        // Conflicting target spans are not evidence of either source style.
        var accepted = candidates.Where(candidate => !candidates.Any(other => other.Id != candidate.Id
            && candidate.Run.Start < other.Run.Start + other.Run.Length
            && other.Run.Start < candidate.Run.Start + candidate.Run.Length)).ToArray();
        var runs = accepted.Select(static candidate => candidate.Run).Distinct().OrderBy(static run => run.Start).ToArray();
        var resolved = accepted.Select(static candidate => candidate.Id).ToHashSet(StringComparer.Ordinal);
        return new(runs, region.SourceStyleSpans.Count - resolved.Count);
    }

    internal static bool ValidRange(int start, int length, int limit) => start >= 0 && length > 0 && start <= limit - length;
    internal static int UniqueIndex(string text, string value)
    {
        if (value.Length == 0) return -1;
        var first = text.IndexOf(value, StringComparison.Ordinal);
        return first >= 0 && text.IndexOf(value, first + 1, StringComparison.Ordinal) < 0 ? first : -1;
    }
    private static bool HasLiteralBoundaries(string text, int start, string literal) =>
        (!char.IsAsciiLetterOrDigit(literal[0]) || start == 0 || !char.IsAsciiLetterOrDigit(text[start - 1]))
        && (!char.IsAsciiLetterOrDigit(literal[^1]) || start + literal.Length == text.Length
            || !char.IsAsciiLetterOrDigit(text[start + literal.Length]));
    private static int[] NonWhitespacePositions(string text) => Enumerable.Range(0, text.Length)
        .Where(index => !char.IsWhiteSpace(text[index])).ToArray();
}

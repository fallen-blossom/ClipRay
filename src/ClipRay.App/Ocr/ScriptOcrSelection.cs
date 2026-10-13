namespace ClipRay.App.Ocr;

public sealed record ScriptOcrCandidate(string Text, double Confidence, string Script)
{
    public bool HasRawConfidence { get; init; } = true;
    internal static ScriptOcrCandidate FromRecognition(string text,float[]? scores,string script)
    { var score=OcrQualityPolicy.RawConfidence(scores);return new(text,score??0,script) { HasRawConfidence=score.HasValue }; }
}
public sealed record ScriptOcrSelectionResult(ScriptOcrCandidate? Candidate, string? AlternativeText = null);

public static class ScriptOcrSelection
{
    public static string RestoreReadingOrder(string text, string script)
    {
        if (script != "arabic") return text;
        // Arabic glyphs are emitted in visual order; Latin/numeric spans keep
        // their internal left-to-right order, including version punctuation.
        var spans = System.Text.RegularExpressions.Regex.Matches(text, @"[A-Za-z0-9]+(?:[._:/+-][A-Za-z0-9]+)*|.",
            System.Text.RegularExpressions.RegexOptions.Singleline).Select(match => match.Value).ToArray();
        return string.Concat(spans.Reverse());
    }
    public static ScriptOcrCandidate? Select(IReadOnlyList<ScriptOcrCandidate> candidates) => SelectWithReview(candidates).Candidate;

    public static ScriptOcrSelectionResult SelectWithReview(IReadOnlyList<ScriptOcrCandidate> candidates)
    {
        var completeCandidates = candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text)
            && double.IsFinite(candidate.Confidence)
            && MatchesScript(candidate)).ToArray();
        var longest = completeCandidates.Select(candidate => candidate.Text.Count(char.IsLetterOrDigit)).DefaultIfEmpty().Max();
        // A recognizer returning only a fraction of a shared full-line crop
        // cannot win on the confidence of its few surviving characters.
        var baseline = completeCandidates.Where(candidate => candidate.Text.Count(char.IsLetterOrDigit) >= longest / 5)
        .OrderByDescending(candidate => candidate.Confidence)
        .ThenByDescending(candidate => candidate.Script == "latin")
        .FirstOrDefault();
        if (baseline is null || baseline.Script != "latin") return new(baseline);
        var fuller = completeCandidates.Where(candidate => candidate.Confidence >= .9
                && AddsNativeTextWithoutChangingLatin(candidate, baseline.Text))
            .OrderByDescending(candidate => candidate.Text.Count(char.IsLetterOrDigit)).FirstOrDefault();
        // The score floor is a review heuristic, not a calibrated probability.
        // Preserve both readings: extra script content must be checked against the image.
        return fuller is null ? new(baseline) : new(fuller, baseline.Text);
    }

    private static bool AddsNativeTextWithoutChangingLatin(ScriptOcrCandidate candidate, string latin)
    {
        var full = string.Concat(candidate.Text.Where(c => !char.IsWhiteSpace(c)));
        var partial = string.Concat(latin.Where(c => !char.IsWhiteSpace(c)));
        if (partial.Length == 0) return false;
        var index = full.IndexOf(partial, StringComparison.Ordinal);
        if (index < 0) return false;
        var added = (full[..index] + full[(index + partial.Length)..]).Where(char.IsLetterOrDigit).ToArray();
        return added.Length >= 2 && added.All(c => candidate.Script switch
        {
            "cjk" => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff',
            "korean" => ScriptSupplementalOcr.ContainsHangul(c.ToString()),
            _ => false,
        });
    }

    private static bool MatchesScript(ScriptOcrCandidate candidate) => candidate.Script switch
    {
        "korean" => ScriptSupplementalOcr.ContainsHangul(candidate.Text),
        "cyrillic" => candidate.Text.Count(c => c is >= '\u0400' and <= '\u052f') >= 2,
        "arabic" => candidate.Text.Count(c => c is >= '\u0600' and <= '\u06ff'
            or >= '\u0750' and <= '\u077f' or >= '\u08a0' and <= '\u08ff') >= 2,
        "cjk" => candidate.Text.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff'),
        "latin" => true,
        _ => false
    };
}

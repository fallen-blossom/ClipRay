using System.Text;
using System.Text.RegularExpressions;

namespace ClipRay.App.Translation;

internal sealed class MachineProtectedTranslationText
{
    private const string WebAddressPattern = @"(?<![A-Za-z0-9_@])(?:https?://)?(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?:[/?#][^\s<>]*)?";
    private static readonly Regex ProtectedPattern = new(
        WebAddressPattern + @"|[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]+|\{\{[^{}]+\}\}|\{[^{}]+\}|</?[^>]+>|(?<![\p{L}\p{N}_])@[A-Za-z0-9_][\p{L}\p{N}_.-]*|%(?:\d+\$)?[a-zA-Z]|\b(?:Ctrl|Alt|Shift|Win)(?:\+[A-Za-z0-9]+)+\b|\bF(?:[1-9]|1\d|2[0-4])\b|\b[A-Za-z][A-Za-z0-9]*(?:[._-]\d+)+\b|(?<![A-Za-z0-9_])\d+(?:[.,]\d+)*[xX×]|\b\d+(?:[.,]\d+)*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SentenceProtectedPattern = new(
        WebAddressPattern + @"|[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]+|\{\{[^{}]+\}\}|\{[^{}]+\}|</?[^>]+>|(?<![\p{L}\p{N}_])@[A-Za-z0-9_][\p{L}\p{N}_.-]*|%(?:\d+\$)?[a-zA-Z]|\b(?:Codex|ChatGPT|Anthropic|OpenAI|Grok|Astra)\b|\b[A-Za-z][A-Za-z0-9]*(?:[._-]\d+)+\b|\b(?:Ctrl|Alt|Shift|Win)(?:\+[A-Za-z0-9]+)+\b|\bF(?:[1-9]|1\d|2[0-4])\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private MachineProtectedTranslationText(string source, MachineTranslationTextSegment[] segments)
    {
        Source = source;
        Segments = segments;
    }

    public string Source { get; }
    public IReadOnlyList<MachineTranslationTextSegment> Segments { get; }
    private bool IsSentence { get; init; }
    private bool ExplicitJapanese { get; init; }

    public static MachineProtectedTranslationText Create(string source, bool keepSentenceContext = false, bool explicitJapanese = false)
    {
        if(keepSentenceContext)
        {
            // An independent identity line is metadata, not part of the sentence.
            // Keep OCR-corrupted handles outside the English model while leaving
            // the following complete body intact.
            var identity=Regex.Match(source,@"\A[ \t]*@[A-Za-z0-9_][\p{L}\p{N}_.-]*[ \t]*\r?\n");
            var sentenceSegments = new List<MachineTranslationTextSegment>();
            var body = source;
            if (identity.Success)
            {
                sentenceSegments.Add(new MachineTranslationTextSegment(identity.Value, false));
                body = source[identity.Length..];
            }
            foreach (var part in Regex.Split(body, @"(\r?\n+)").Where(static part => part.Length > 0))
                sentenceSegments.Add(new MachineTranslationTextSegment(part,
                    part.Any(char.IsLetter) && !Regex.IsMatch(part, @"^\r?\n+$")));
            if (sentenceSegments.Count == 0)
                sentenceSegments.Add(new MachineTranslationTextSegment(source, source.Any(char.IsLetter)));
            return new(source, sentenceSegments.ToArray()) {IsSentence=true, ExplicitJapanese=explicitJapanese};
        }
        var segments = new List<MachineTranslationTextSegment>();
        var position = 0;
        foreach (Match match in ProtectedPattern.Matches(source).Where(match => !IsJapaneseHan(source, match, explicitJapanese)))
        {
            AddUnprotectedSegment(source[position..match.Index], segments);
            segments.Add(new MachineTranslationTextSegment(match.Value, false));
            position = match.Index + match.Length;
        }

        AddUnprotectedSegment(source[position..], segments);
        if (segments.Count == 0)
            segments.Add(new MachineTranslationTextSegment(source, source.Any(char.IsLetter)));
        return new MachineProtectedTranslationText(source, segments.ToArray()) { ExplicitJapanese=explicitJapanese };
    }

    public bool TryReassemble(IReadOnlyList<string> translatedSegments,out string restored)
    {
        restored=Reassemble(translatedSegments);
        if(!IsSentence) return true;
        foreach(var group in SentenceProtectedPattern.Matches(Source).Where(match => !IsJapaneseHan(Source, match, ExplicitJapanese))
            .Select(match=>match.Value).GroupBy(value=>value,StringComparer.Ordinal))
            if(Regex.Matches(restored,Regex.Escape(group.Key),RegexOptions.CultureInvariant).Count!=group.Count()) return false;
        return true;
    }

    public string Reassemble(IReadOnlyList<string> translatedSegments)
    {
        if (translatedSegments.Count != Segments.Count)
            throw new ArgumentException("Machine translation segment count does not match the source plan.", nameof(translatedSegments));

        var result = new StringBuilder();
        for (var index = 0; index < translatedSegments.Count; index++)
        {
            var source = Segments[index];
            if (!source.ShouldTranslate)
            {
                result.Append(translatedSegments[index]);
                continue;
            }
            // The worker trims its output. Keep source separators so protected numbers
            // and model identifiers cannot merge with adjacent untranslated ASCII.
            var leading = source.Text.Length - source.Text.TrimStart().Length;
            var trailing = source.Text.Length - source.Text.TrimEnd().Length;
            result.Append(source.Text.AsSpan(0, leading));
            result.Append(translatedSegments[index].Trim());
            result.Append(source.Text.AsSpan(source.Text.Length - trailing));
        }
        return result.ToString();
    }

    private static bool IsJapaneseHan(string source, Match match, bool explicitJapanese = false)
    {
        if(!match.Value.All(c => c is >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff' or >= '\uf900' and <= '\ufaff')) return false;
        if (explicitJapanese) return true;
        char[] boundaries = ['。','！','？','.','!','?','\n'];
        var begin = source.LastIndexOfAny(boundaries, match.Index) + 1;
        var end = source.IndexOfAny(boundaries, match.Index + match.Length);
        if(end < 0) end = source.Length;
        return source[begin..end].Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9d');
    }

    private static void AddUnprotectedSegment(
        string text,
        ICollection<MachineTranslationTextSegment> segments)
    {
        if (text.Length == 0) return;
        segments.Add(new MachineTranslationTextSegment(text, text.Any(char.IsLetter)));
    }
}

internal sealed record MachineTranslationTextSegment(string Text, bool ShouldTranslate);

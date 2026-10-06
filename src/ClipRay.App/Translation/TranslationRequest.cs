using System.Text.RegularExpressions;

namespace ClipRay.App.Translation;

public static class DeepSeekModels
{
    public const string Flash = "deepseek-flash";
    public const string Pro = "deepseek-v4-pro";

    public static void Validate(string model)
    {
        if (model != Flash)
        {
            throw new ArgumentException("Only DeepSeek V4.1 Flash (deepseek-flash) is allowed.", nameof(model));
        }
    }
}

public enum TranslationTextKind
{
    General,
    Body,
    UiControl,
    Heading,
    Metadata,
    IdentityMetadata,
    Hashtag,
    BrandOrIdentifier,
}

public sealed record TranslationRequest
{
    public TranslationRequest(
        string sourceLanguage,
        string targetLanguage,
        string ocrText,
        string model = DeepSeekModels.Flash,
        TranslationTextKind textKind = TranslationTextKind.General,
        string scene = "general",
        string translationHint = "Translate natural language faithfully and concisely.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(ocrText);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(translationHint);
        DeepSeekModels.Validate(model);
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        OcrText = ocrText;
        Model = model;
        TextKind = textKind;
        Scene = scene;
        TranslationHint = translationHint;
    }

    public string SourceLanguage { get; }
    public string TargetLanguage { get; }
    public string OcrText { get; }
    public string Model { get; }
    public TranslationTextKind TextKind { get; }
    public string Scene { get; }
    public string TranslationHint { get; }
}

internal sealed class ProtectedTranslationText
{
    private static readonly Regex ProtectedPattern = new(
        @"\{\{[^{}]+\}\}|\{[^{}]+\}|</?[^>]+>|(?<![\p{L}\p{N}_])@[A-Za-z0-9_][A-Za-z0-9_.-]*|%(?:\d+\$)?[a-zA-Z]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string[] _values;
    private readonly string[] _tokens;
    private readonly string _namespace;

    private ProtectedTranslationText(
        string text,
        string[] values,
        string[] tokens,
        string tokenNamespace,
        bool hasUnprotectedLetters)
    {
        Text = text;
        _values = values;
        _tokens = tokens;
        _namespace = tokenNamespace;
        HasUnprotectedLetters = hasUnprotectedLetters;
    }

    public string Text { get; }
    public bool HasUnprotectedLetters { get; }

    public static ProtectedTranslationText Create(string source)
    {
        string tokenNamespace;
        do
        {
            tokenNamespace = Guid.NewGuid().ToString("N");
        }
        while (source.Contains($"[[CLIPRAY_{tokenNamespace}_", StringComparison.Ordinal));

        var unprotectedText = ProtectedPattern.Replace(source, string.Empty);
        var values = new List<string>();
        var tokens = new List<string>();
        var text = ProtectedPattern.Replace(source, match =>
        {
            var index = values.Count;
            values.Add(match.Value);
            var token = $"[[CLIPRAY_{tokenNamespace}_{index}]]";
            tokens.Add(token);
            return token;
        });
        return new ProtectedTranslationText(
            text,
            values.ToArray(),
            tokens.ToArray(),
            tokenNamespace,
            unprotectedText.Any(char.IsLetter));
    }

    public bool TryRestore(string translated, out string restored)
    {
        for (var index = 0; index < _tokens.Length; index++)
        {
            if (CountOccurrences(translated, _tokens[index]) != 1)
            {
                restored = string.Empty;
                return false;
            }
        }

        var namespacePattern = new Regex($@"\[\[CLIPRAY_{Regex.Escape(_namespace)}_[^\]]+\]\]", RegexOptions.CultureInvariant);
        if (namespacePattern.Matches(translated).Count != _tokens.Length)
        {
            restored = string.Empty;
            return false;
        }

        for (var index = 0; index < _values.Length; index++)
        {
            translated = translated.Replace(_tokens[index], _values[index], StringComparison.Ordinal);
        }

        restored = translated;
        return true;
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }
}

public static class TranslationContentIntegrity
{
    // OCR can join an identifier and its neighboring words. Its digits still
    // have to survive translation even when the surrounding ASCII boundaries do not.
    private static readonly Regex NumberPattern = new(@"\d+(?:[.,]\d+)*", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<string, string> WrittenNumberValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
        ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
        ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12", ["thirteen"] = "13",
        ["fourteen"] = "14", ["fifteen"] = "15", ["sixteen"] = "16", ["seventeen"] = "17",
        ["eighteen"] = "18", ["nineteen"] = "19", ["twenty"] = "20", ["thirty"] = "30",
        ["forty"] = "40", ["fifty"] = "50", ["sixty"] = "60", ["seventy"] = "70",
        ["eighty"] = "80", ["ninety"] = "90",
    };
    private static readonly Regex WrittenNumberPattern = new(
        @"(?<![\p{L}-])(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?![\p{L}-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ModelTokenPattern = new(
        @"[A-Z]{2,8}(?:[._-]\d+)+|(?<![A-Za-z0-9_])[A-Za-z][A-Za-z0-9]*(?:[._-]\d+)+(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HotkeyPattern = new(@"(?<![A-Za-z0-9_])(?:(?:Ctrl|Alt|Shift|Win)(?:\+[A-Za-z0-9]+)+|F(?:[1-9]|1\d|2[0-4]))(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] CalendarMonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly Regex[] NamedMonthPatterns = CalendarMonthNames.Select(name => new Regex(
        // Month names can also be verbs or personal names; require calendar context.
        $@"\b(?:in|of|on|since|until|by|before|after|during|through|from|to)\s+{name}\b|\b{name}\s+\d+\b|\b\d+(?:st|nd|rd|th)?\s+{name}\b|\b(?:{string.Join('|', CalendarMonthNames)})\s*(?:,|and|or)\s+{name}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)).ToArray();
    private static readonly Regex[] NumericMonthPatterns = Enumerable.Range(1, 12).Select(month => new Regex(
        $@"(?<![A-Za-z0-9_]){month}(?=\s*月)", RegexOptions.Compiled | RegexOptions.CultureInvariant)).ToArray();

    public static bool HasSameInvariants(string source, string translated) =>
        FindMismatch(source, translated) is null;

    public static int CountNumbers(string value) => NumberPattern.Matches(value).Count;

    public static TranslationInvariantMismatch? FindMismatch(string source, string translated)
    {
        var normalizedNumbers = RemoveMatchingWrittenNumberConversions(source,
            RemoveMatchingCalendarMonthNumbers(source, translated));
        if (!HasSameMatches(ModelTokenPattern, source, translated)) return TranslationInvariantMismatch.ModelToken;
        if (!HasSameMatches(HotkeyPattern, source, translated)) return TranslationInvariantMismatch.Hotkey;
        if (!HasSameMatches(NumberPattern, source, normalizedNumbers)) return TranslationInvariantMismatch.Number;
        return null;
    }

    private static string RemoveMatchingCalendarMonthNumbers(string source, string translated)
    {
        for (var index = 0; index < NamedMonthPatterns.Length; index++)
        {
            var count = NamedMonthPatterns[index].Matches(source).Count;
            if (count > 0) translated = NumericMonthPatterns[index].Replace(translated, string.Empty, count);
        }
        return translated;
    }

    private static string RemoveMatchingWrittenNumberConversions(string source, string translated)
    {
        var allowed = WrittenNumberPattern.Matches(source)
            .Select(match => WrittenNumberValues[match.Value])
            .GroupBy(static value => value, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        if (allowed.Count == 0) return translated;

        return NumberPattern.Replace(translated, match =>
        {
            if (!allowed.TryGetValue(match.Value, out var remaining) || remaining == 0)
                return match.Value;
            allowed[match.Value] = remaining - 1;
            return string.Empty;
        });
    }

    private static bool HasSameMatches(Regex pattern, string source, string translated)
    {
        var sourceValues = pattern.Matches(source).Select(static match => match.Value).Order(StringComparer.OrdinalIgnoreCase);
        var translatedValues = pattern.Matches(translated).Select(static match => match.Value).Order(StringComparer.OrdinalIgnoreCase);
        return sourceValues.SequenceEqual(translatedValues, StringComparer.OrdinalIgnoreCase);
    }
}

public enum TranslationInvariantMismatch
{
    Number,
    ModelToken,
    Hotkey,
}

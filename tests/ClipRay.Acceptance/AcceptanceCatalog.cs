using System.Collections.ObjectModel;
using System.Text;

namespace ClipRay.Acceptance.Testing;

public sealed class AcceptanceCatalog
{
    private AcceptanceCatalog(IReadOnlyList<AcceptanceCase> cases) => Cases = cases;

    public IReadOnlyList<AcceptanceCase> Cases { get; }

    public static AcceptanceCatalog Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Acceptance case directory does not exist: {root}");
        var cases = Directory.EnumerateFiles(root, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .SelectMany(path => Parse(File.ReadAllText(path), Path.GetFileName(path)).Cases)
            .ToArray();
        EnsureUniqueIds(cases, root);
        return new AcceptanceCatalog(cases);
    }

    public static AcceptanceCatalog Parse(string text, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var cases = new List<AcceptanceCase>();
        Dictionary<string, string>? fields = null;
        var lineNumber = 0;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (string.Equals(line, "[case]", StringComparison.OrdinalIgnoreCase))
            {
                AddCase(cases, fields, sourceName);
                fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (fields is null)
                throw new InvalidDataException($"{sourceName}:{lineNumber} must appear inside [case].");
            var separator = line.IndexOf('=');
            if (separator <= 0)
                throw new InvalidDataException($"{sourceName}:{lineNumber} must use key=value.");
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
                throw new InvalidDataException($"{sourceName}:{lineNumber} has an empty key or value.");
            if (!fields.TryAdd(key, value))
                throw new InvalidDataException($"{sourceName}:{lineNumber} repeats '{key}'.");
        }
        AddCase(cases, fields, sourceName);

        EnsureUniqueIds(cases, sourceName);
        return new AcceptanceCatalog(cases.AsReadOnly());
    }

    private static void EnsureUniqueIds(IEnumerable<AcceptanceCase> cases, string sourceName)
    {
        var duplicate = cases.GroupBy(static item => item.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"{sourceName} repeats case id '{duplicate.Key}'.");
    }

    private static void AddCase(ICollection<AcceptanceCase> cases, Dictionary<string, string>? fields, string sourceName)
    {
        if (fields is null) return;
        if (!fields.TryGetValue("id", out var id) || !fields.TryGetValue("kind", out var kind))
            throw new InvalidDataException($"{sourceName} case requires id and kind.");
        cases.Add(new AcceptanceCase(id, kind, new ReadOnlyDictionary<string, string>(fields)));
    }
}

public sealed class AcceptanceCase(string id, string kind, IReadOnlyDictionary<string, string> fields)
{
    public string Id { get; } = id;
    public string Kind { get; } = kind;
    public IReadOnlyDictionary<string, string> Fields { get; } = fields;

    public string GetRequired(string key) => Fields.TryGetValue(key, out var value)
        ? value
        : throw new InvalidDataException($"Case '{Id}' requires '{key}'.");
}

public static class AcceptanceCatalogReport
{
    private static readonly string[] PreferredLanguages = ["zh-Hans", "en", "ja", "fr", "ru"];

    public static string Format(AcceptanceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var languageSet = catalog.Cases
            .SelectMany(static item => new[] { GetOptional(item, "source_language"), GetOptional(item, "target_language") })
            .Where(static language => !string.IsNullOrWhiteSpace(language) && !string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var languages = PreferredLanguages.Where(languageSet.Contains)
            .Concat(languageSet.Except(PreferredLanguages, StringComparer.OrdinalIgnoreCase).OrderBy(static language => language, StringComparer.OrdinalIgnoreCase));
        var builder = new StringBuilder();
        builder.AppendLine($"CATALOG_VALID cases={catalog.Cases.Count}");
        builder.AppendLine($"translation_cases={catalog.Cases.Count(static item => item.Kind == "translation")}");
        builder.AppendLine($"mixed_translation_cases={catalog.Cases.Count(static item => item.Kind == "mixed_translation")}");
        builder.AppendLine($"scroll_cases={catalog.Cases.Count(static item => item.Kind == "scroll_capture")}");
        builder.AppendLine($"ocr_bake_off_cases={catalog.Cases.Count(static item => item.Kind == "ocr_bake_off")}");
        builder.AppendLine($"ocr_real_fixture_pending={catalog.Cases.Count(static item => item.Kind == "ocr_bake_off" && string.Equals(GetOptional(item, "fixture_status"), "real_ui_image_required", StringComparison.OrdinalIgnoreCase))}");
        builder.AppendLine($"automated_not_run={catalog.Cases.Count(static item => string.Equals(GetOptional(item, "execution"), "automated", StringComparison.OrdinalIgnoreCase))}");
        builder.AppendLine($"manual_pending={catalog.Cases.Count(static item => string.Equals(GetOptional(item, "execution"), "manual_pending", StringComparison.OrdinalIgnoreCase))}");
        builder.Append($"languages={string.Join(',', languages)}");
        return builder.ToString();
    }

    private static string? GetOptional(AcceptanceCase item, string key) => item.Fields.TryGetValue(key, out var value) ? value : null;
}

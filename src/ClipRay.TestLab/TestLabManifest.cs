using System.IO;
using System.Text.Json;

namespace ClipRay.TestLab;

public sealed record TestLabCase(
    string Id,
    string Image,
    string TargetLanguage,
    string Expectation);

public sealed record TestLabManifest(
    IReadOnlyList<TestLabCase> Cases,
    IReadOnlyList<string> Errors)
{
    public static TestLabManifest Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var path = Path.Combine(Path.GetFullPath(root), "manifest.json");
        if (!File.Exists(path)) return new TestLabManifest([], ["manifest-missing"]);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("cases", out var casesElement)
                || casesElement.ValueKind != JsonValueKind.Array)
            {
                return new TestLabManifest([], ["manifest-invalid"]);
            }
            var cases = new List<TestLabCase>();
            foreach (var item in casesElement.EnumerateArray())
            {
                if (!TryReadCase(item, out var testCase))
                    return new TestLabManifest([], ["case-invalid"]);
                cases.Add(testCase);
            }
            if (cases.Count == 0
                || cases.Select(static item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cases.Count
                || cases.Any(item => !IsValidCase(item, Path.GetFullPath(root))))
            {
                return new TestLabManifest([], ["case-invalid"]);
            }
            return new TestLabManifest(cases, []);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return new TestLabManifest([], ["manifest-invalid"]);
        }
    }

    private static bool TryReadCase(JsonElement item, out TestLabCase testCase)
    {
        testCase = new TestLabCase(string.Empty, string.Empty, string.Empty, string.Empty);
        if (item.ValueKind != JsonValueKind.Object
            || !TryReadString(item, "id", out var id)
            || !TryReadString(item, "image", out var image)
            || !TryReadString(item, "targetLanguage", out var targetLanguage)
            || !TryReadString(item, "expectation", out var expectation))
        {
            return false;
        }
        testCase = new TestLabCase(id, image, targetLanguage, expectation);
        return true;
    }

    private static bool TryReadString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        return item.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && (value = element.GetString() ?? string.Empty).Length > 0;
    }

    private static bool IsValidCase(TestLabCase testCase, string root)
    {
        if (!TestLabArtifactPaths.IsSafeSegment(testCase.Id)
            || string.IsNullOrWhiteSpace(testCase.TargetLanguage)
            || testCase.Expectation is not ("all-translated" or "protected-fallback")
            || Path.IsPathRooted(testCase.Image))
        {
            return false;
        }
        var fullRoot = Path.TrimEndingDirectorySeparator(root);
        var image = Path.GetFullPath(Path.Combine(fullRoot, testCase.Image));
        return image.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && File.Exists(image);
    }
}

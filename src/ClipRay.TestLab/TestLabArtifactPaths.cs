using System.IO;
using System.Text.RegularExpressions;

namespace ClipRay.TestLab;

public sealed record TestLabArtifactPaths(string CaseRoot, string BeforePath)
{
    private static readonly Regex SafeSegment = new(
        "^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) && SafeSegment.IsMatch(value);

    public static TestLabArtifactPaths Create(string root, string runId, string caseId)
    {
        if (!IsSafeSegment(runId)) throw new ArgumentException("run-id-invalid", nameof(runId));
        if (!IsSafeSegment(caseId)) throw new ArgumentException("case-id-invalid", nameof(caseId));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var runRoot = Path.GetFullPath(Path.Combine(fullRoot, "runs", runId));
        var caseRoot = Path.GetFullPath(Path.Combine(runRoot, caseId));
        if (!caseRoot.StartsWith(runRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("case-root-outside-run", nameof(caseId));
        return new TestLabArtifactPaths(caseRoot, Path.Combine(caseRoot, "before.png"));
    }

    public string After(string route) => Path.Combine(CaseRoot, $"after-{route}.png");
}

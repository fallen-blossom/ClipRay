using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ClipRay.App.Settings;

namespace ClipRay.App.Cleanup;

public enum CleanupFileKind
{
    CompletedImage,
    CompletedRecording,
}

public sealed record CleanupTarget(string RootDirectory, IReadOnlySet<CleanupFileKind> FileKinds);

public sealed record CleanupPlan(DateTime CutoffUtc, IReadOnlyList<CleanupTarget> Targets)
{
    public static CleanupPlan? FromSettings(AppSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.CleanupScheduleEnabled) return null;

        settings.Validate();
        var grouped = new Dictionary<string, HashSet<CleanupFileKind>>(StringComparer.OrdinalIgnoreCase);
        AddTarget(grouped, settings.ScreenshotDirectory, CleanupFileKind.CompletedImage);
        AddTarget(grouped, settings.LongCaptureDirectory, CleanupFileKind.CompletedImage);
        AddTarget(grouped, settings.RecordingDirectory, CleanupFileKind.CompletedRecording);
        var targets = grouped
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new CleanupTarget(pair.Key, pair.Value))
            .ToArray();
        return new CleanupPlan(now.UtcDateTime.AddDays(-settings.CleanupRetentionDays), targets);
    }

    private static void AddTarget(
        IDictionary<string, HashSet<CleanupFileKind>> targets,
        string rootDirectory,
        CleanupFileKind fileKind)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        if (!targets.TryGetValue(fullPath, out var kinds))
        {
            kinds = [];
            targets.Add(fullPath, kinds);
        }
        kinds.Add(fileKind);
    }
}

public sealed record CleanupResult(
    int DeletedFileCount,
    int DeletedDirectoryCount,
    int SkippedPathCount,
    int FailedPathCount)
{
    public int RecycledFileCount => DeletedFileCount;
    public IReadOnlyList<CleanupPathOutcome> Items { get; init; } = [];
}

public sealed record CleanupPathOutcome(string Path, string Status, string Reason);
public sealed record CleanupCandidate(string Root, string Path, CompletedOutputReceipt Receipt);

public static class CleanupPathPolicy
{
    public static bool CanDescend(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Directory)
        && !attributes.HasFlag(FileAttributes.ReparsePoint);
}

public sealed class CleanupService
{
    private readonly CompletedOutputStore _completed;
    private readonly ICleanupRecycleBackend _recycle;
    internal Action<CleanupCandidate>? BeforeExecution { get; set; }
    public CleanupService(CompletedOutputStore? completed = null, ICleanupRecycleBackend? recycle = null)
    {
        _completed = completed ?? new();
        _recycle = recycle ?? new WindowsCleanupRecycleBackend();
    }
    private static readonly Regex CompletedImageName = new(
        @"^(?:ClipRay_\d{6}_\d{3}(?:_\d+)?|ClipRay-(?:Screenshot|LongCapture)_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}(?:_\d+)?)\.png$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CompletedRecordingName = new(
        @"^(?:ClipRay-\d{8}-\d{6}-\d{3}-[0-9a-f]{32}|ClipRay-Recording_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}(?:_\d+)?)\.mp4$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public CleanupResult Execute(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var outcomes = new List<CleanupPathOutcome>();
        var candidates = Scan(plan, outcomes);
        outcomes.AddRange(ExecuteCandidates(plan, candidates));
        return new(outcomes.Count(x => x.Status == "recycled"), 0,
            outcomes.Count(x => x.Status == "skipped"), outcomes.Count(x => x.Status == "failed")) { Items = outcomes };
    }

    public IReadOnlyList<CleanupCandidate> Scan(CleanupPlan plan, IList<CleanupPathOutcome>? observations = null)
    {
        var candidates = new List<CleanupCandidate>();
        foreach (var target in plan.Targets)
        {
            if (!IsSafeDirectory(target.RootDirectory))
            { observations?.Add(new(target.RootDirectory, "skipped", "root-unavailable-or-reparse")); continue; }
            try
            {
                var directories = new[] { target.RootDirectory }.Concat(Directory.GetDirectories(target.RootDirectory)
                    .Where(d => IsDateDirectory(Path.GetFileName(d))));
                foreach (var directory in directories)
                {
                    if (!IsSafeDirectory(directory)) { observations?.Add(new(directory,"skipped","directory-reparse")); continue; }
                    using var lease = CompletedOutputStore.EnterDirectory(directory);
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        var kind = CompletedRecordingName.IsMatch(Path.GetFileName(file)) ? CleanupFileKind.CompletedRecording : CleanupFileKind.CompletedImage;
                        if (!IsOwnedCompletedFile(Path.GetFileName(file), target.FileKinds)
                            || !_completed.TryGetEligible(file,kind,plan.CutoffUtc,out var receipt))
                        { observations?.Add(new(file,"skipped","unknown-incomplete-recent-or-changed")); continue; }
                        candidates.Add(new(Path.GetFullPath(target.RootDirectory),Path.GetFullPath(file),receipt!));
                    }
                }
            }
            catch { observations?.Add(new(target.RootDirectory,"failed","scan-unavailable")); }
        }
        return candidates;
    }

    public IReadOnlyList<CleanupPathOutcome> ExecuteCandidates(CleanupPlan plan, IReadOnlyList<CleanupCandidate> candidates)
    {
        var results = new List<CleanupPathOutcome>();
        foreach (var candidate in candidates)
        {
            try
            {
                BeforeExecution?.Invoke(candidate);
                var target = plan.Targets.FirstOrDefault(t => string.Equals(Path.GetFullPath(t.RootDirectory),candidate.Root,StringComparison.OrdinalIgnoreCase));
                var parent = Path.GetDirectoryName(candidate.Path)!;
                var contained = string.Equals(parent,candidate.Root,StringComparison.OrdinalIgnoreCase)
                    || IsDateDirectory(Path.GetFileName(parent)) && string.Equals(Path.GetDirectoryName(parent),candidate.Root,StringComparison.OrdinalIgnoreCase);
                if (target is null || !target.FileKinds.Contains(candidate.Receipt.Kind) || !Path.IsPathFullyQualified(candidate.Path)
                    || !string.Equals(candidate.Path,Path.GetFullPath(candidate.Path),StringComparison.OrdinalIgnoreCase) || !contained || !IsSafeDirectory(parent)
                    || !IsOwnedCompletedFile(Path.GetFileName(candidate.Path),target.FileKinds))
                { results.Add(new(candidate.Path,"skipped","candidate-outside-contract")); continue; }
                using var lease = CompletedOutputStore.EnterDirectory(parent);
                if (!_completed.TryGetEligible(candidate.Path,candidate.Receipt.Kind,plan.CutoffUtc,out var current) || current != candidate.Receipt)
                { results.Add(new(candidate.Path,"skipped","candidate-changed-before-execution")); continue; }
                _recycle.RecycleFile(candidate.Path);
                results.Add(new(candidate.Path,"recycled","completed-expired"));
            }
            catch { results.Add(new(candidate.Path,"failed","recycle-unavailable")); }
        }
        return results;
    }

    internal static bool IsOwnedCompletedFile(string fileName, IReadOnlySet<CleanupFileKind> kinds) =>
        kinds.Contains(CleanupFileKind.CompletedImage) && CompletedImageName.IsMatch(fileName)
        || kinds.Contains(CleanupFileKind.CompletedRecording) && CompletedRecordingName.IsMatch(fileName);

    private static bool IsSafeDirectory(string path) => Directory.Exists(path) && CompletedOutputStore.IsOrdinaryPath(path);

    private static bool IsDateDirectory(string name) => DateOnly.TryParseExact(
        name,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out _);
}

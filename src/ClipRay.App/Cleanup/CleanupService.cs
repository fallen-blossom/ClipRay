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
    int FailedPathCount);

public static class CleanupPathPolicy
{
    public static bool CanDescend(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.Directory)
        && !attributes.HasFlag(FileAttributes.ReparsePoint);
}

public sealed class CleanupService
{
    private static readonly Regex CompletedImageName = new(
        @"^(?:ClipRay_\d{6}_\d{3}(?:_\d+)?|ClipRay-(?:Screenshot|LongCapture)_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}(?:_\d+)?)\.png$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CompletedRecordingName = new(
        @"^(?:ClipRay-\d{8}-\d{6}-\d{3}-[0-9a-f]{32}|ClipRay-Recording_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}(?:_\d+)?)\.mp4$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public CleanupResult Execute(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var deletedFiles = 0;
        var deletedDirectories = 0;
        var skippedPaths = 0;
        var failedPaths = 0;

        foreach (var target in plan.Targets)
        {
            if (!Directory.Exists(target.RootDirectory)) continue;
            if (!TryGetAttributes(target.RootDirectory, out var rootAttributes))
            {
                failedPaths++;
                continue;
            }
            if (!CleanupPathPolicy.CanDescend(rootAttributes))
            {
                skippedPaths++;
                continue;
            }

            DeleteOwnedFiles(target.RootDirectory, target.FileKinds, plan.CutoffUtc, ref deletedFiles, ref skippedPaths, ref failedPaths);

            string[] children;
            try
            {
                children = Directory.GetDirectories(target.RootDirectory);
            }
            catch
            {
                failedPaths++;
                continue;
            }

            foreach (var directory in children)
            {
                if (!IsDateDirectory(Path.GetFileName(directory))) continue;
                if (!TryGetAttributes(directory, out var attributes))
                {
                    failedPaths++;
                    continue;
                }
                if (!CleanupPathPolicy.CanDescend(attributes))
                {
                    skippedPaths++;
                    continue;
                }

                DeleteOwnedFiles(directory, target.FileKinds, plan.CutoffUtc, ref deletedFiles, ref skippedPaths, ref failedPaths);
                try
                {
                    if (Directory.EnumerateFileSystemEntries(directory).Any()) continue;
                    Directory.Delete(directory, recursive: false);
                    deletedDirectories++;
                }
                catch
                {
                    failedPaths++;
                }
            }
        }

        return new CleanupResult(deletedFiles, deletedDirectories, skippedPaths, failedPaths);
    }

    private static void DeleteOwnedFiles(
        string directory,
        IReadOnlySet<CleanupFileKind> kinds,
        DateTime cutoffUtc,
        ref int deletedFiles,
        ref int skippedPaths,
        ref int failedPaths)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory);
        }
        catch
        {
            failedPaths++;
            return;
        }

        foreach (var file in files)
        {
            if (!IsOwnedCompletedFile(Path.GetFileName(file), kinds)) continue;
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoffUtc)
                {
                    skippedPaths++;
                    continue;
                }
                File.Delete(file);
                deletedFiles++;
            }
            catch
            {
                failedPaths++;
            }
        }
    }

    internal static bool IsOwnedCompletedFile(string fileName, IReadOnlySet<CleanupFileKind> kinds) =>
        kinds.Contains(CleanupFileKind.CompletedImage) && CompletedImageName.IsMatch(fileName)
        || kinds.Contains(CleanupFileKind.CompletedRecording) && CompletedRecordingName.IsMatch(fileName);

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch
        {
            attributes = default;
            return false;
        }
    }

    private static bool IsDateDirectory(string name) => DateOnly.TryParseExact(
        name,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out _);
}

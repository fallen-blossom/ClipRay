using System.IO;

namespace ClipRay.App.Scrolling;

public sealed record ScrollCaptureTempCleanupResult(int DeletedSessionCount, int SkippedSessionCount, int FailedSessionCount);

public static class ScrollCaptureTempStore
{
    public const string RecoveryMarker = "clipray-recovery.json";
    public static ScrollCaptureTempCleanupResult CleanupOrphans(
        string root,
        IReadOnlySet<string>? activeSessionDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Directory.Exists(root)) return new ScrollCaptureTempCleanupResult(0, 0, 0);

        var active = (activeSessionDirectories ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deleted = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(directory);
            if (active.Contains(fullPath))
            {
                skipped++;
                continue;
            }

            try
            {
                if (File.Exists(Path.Combine(fullPath, RecoveryMarker)))
                {
                    skipped++;
                    continue;
                }
                var info = new DirectoryInfo(fullPath);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skipped++;
                    continue;
                }

                Directory.Delete(fullPath, recursive: true);
                deleted++;
            }
            catch
            {
                failed++;
            }
        }

        return new ScrollCaptureTempCleanupResult(deleted, skipped, failed);
    }
}

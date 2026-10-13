using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ClipRay.App.Recording;
using ClipRay.App.Settings;

namespace ClipRay.App.Capture;

public enum RecentCaptureKind { Screenshot, LongCapture, Recording }

public sealed record RecentCaptureItem(string Path, RecentCaptureKind Kind, DateTime LastWriteTimeUtc, long Length);

/// <summary>Recovers only ClipRay outputs. It never observes the system clipboard or deletes files.</summary>
public sealed class RecentCaptureService
{
    private static readonly Regex ImageName = new(
        @"^(?:ClipRay_\d{6}_\d{3}(?:_\d+)?|ClipRay-(?:Screenshot|LongCapture)_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}(?:_\d+)?)\.png$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex VideoName = new(
        @"^(?:ClipRay-\d{8}-\d{6}-\d{3}-[0-9a-f]{32}|ClipRay-Recording_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}-\d{3}(?:_\d+)?)\.mp4$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly (string Path, RecentCaptureKind Kind)[] _roots;

    public RecentCaptureService(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _roots = [(settings.ScreenshotDirectory, RecentCaptureKind.Screenshot),
            (settings.LongCaptureDirectory, RecentCaptureKind.LongCapture),
            (settings.RecordingDirectory, RecentCaptureKind.Recording)];
    }

    public IReadOnlyList<RecentCaptureItem> GetRecent(int maximumCount = 20)
    {
        if (maximumCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var candidates = new Dictionary<string, RecentCaptureItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _roots)
        {
            if (!SavedMediaFile.TryResolveDirectory(root.Path, out var directory)) continue;
            AddCandidates(directory, root.Kind, candidates);
            string[] subdirectories;
            try { subdirectories = Directory.GetDirectories(directory); }
            catch { continue; }
            foreach (var child in subdirectories)
            {
                if (!IsDateDirectory(Path.GetFileName(child)) || !SavedMediaFile.TryResolveDirectory(child, out _)) continue;
                AddCandidates(child, root.Kind, candidates);
            }
        }
        // Validate only newest candidates until the bounded result is full.
        var result = new List<RecentCaptureItem>(maximumCount);
        foreach (var item in candidates.Values.OrderByDescending(x => x.LastWriteTimeUtc).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsReadableMedia(item)) continue;
            result.Add(item);
            if (result.Count == maximumCount) break;
        }
        return result;
    }

    public bool TryCopy(RecentCaptureItem item, Action<System.Windows.DataObject>? publish = null) =>
        TryPrepareCopy(item, out var prepared) && TryPublishCopy(prepared!, publish);

    /// <summary>Run on a worker: reads/decodes a PNG once or validates the finished video.</summary>
    public bool TryPrepareCopy(RecentCaptureItem item, out PreparedMediaCopy? prepared)
    {
        ArgumentNullException.ThrowIfNull(item);
        prepared = null;
        if (!IsOwnedPath(item.Path, item.Kind)) return false;
        if (item.Kind != RecentCaptureKind.Recording)
            return SavedMediaClipboard.TryPreparePng(item.Path, out prepared, item.Kind);
        if (!SavedMediaFile.TryGetStamp(item.Path, out var before)
            || RecordingMediaProbe.TryReadDuration(item.Path) is not { Ticks: > 0 }
            || !SavedMediaFile.TryGetStamp(item.Path, out var after) || before != after) return false;
        prepared = new PreparedMediaCopy(item.Path, item.Kind, after);
        return true;
    }

    /// <summary>Run on the UI thread: verifies ownership/version and publishes the prepared payload.</summary>
    public bool TryPublishCopy(PreparedMediaCopy prepared, Action<System.Windows.DataObject>? publish = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return IsOwnedPath(prepared.Path, prepared.Kind) && SavedMediaClipboard.TryPublish(prepared, publish);
    }

    private bool IsOwnedPath(string path, RecentCaptureKind kind)
    {
        if (!SavedMediaFile.IsRegularLocalFile(path) || !MatchesName(Path.GetFileName(path), kind)) return false;
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        foreach (var root in _roots)
        {
            if ((root.Kind == RecentCaptureKind.Recording) != (kind == RecentCaptureKind.Recording)
                || !SavedMediaFile.TryResolveDirectory(root.Path, out var resolvedRoot)) continue;
            if (string.Equals(parent, resolvedRoot, StringComparison.OrdinalIgnoreCase)) return true;
            if (parent is not null && IsDateDirectory(Path.GetFileName(parent))
                && string.Equals(Path.GetDirectoryName(parent), resolvedRoot, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void AddCandidates(string directory, RecentCaptureKind rootKind, IDictionary<string, RecentCaptureItem> result)
    {
        string[] files;
        try { files = Directory.GetFiles(directory); }
        catch { return; }
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var kind = name.StartsWith("ClipRay-LongCapture_", StringComparison.OrdinalIgnoreCase) ? RecentCaptureKind.LongCapture
                : name.StartsWith("ClipRay-Screenshot_", StringComparison.OrdinalIgnoreCase) ? RecentCaptureKind.Screenshot : rootKind;
            if (!MatchesName(Path.GetFileName(path), kind) || !SavedMediaFile.IsRegularLocalFile(path)) continue;
            try
            {
                var info = new FileInfo(path);
                result.TryAdd(info.FullName, new RecentCaptureItem(info.FullName, kind, info.LastWriteTimeUtc, info.Length));
            }
            catch { /* One inaccessible file must not hide other outputs. */ }
        }
    }

    private static bool IsReadableMedia(RecentCaptureItem item) => SavedMediaFile.IsRegularLocalFile(item.Path)
        && (item.Kind == RecentCaptureKind.Recording
            ? RecordingMediaProbe.TryReadDuration(item.Path) is { Ticks: > 0 }
            : SavedMediaFile.TryReadPng(item.Path, out _, out _));

    private static bool MatchesName(string name, RecentCaptureKind kind) =>
        kind == RecentCaptureKind.Recording ? VideoName.IsMatch(name) : ImageName.IsMatch(name);

    private static bool IsDateDirectory(string name) => DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out _);
}

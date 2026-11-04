using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ClipRay.App.Cleanup;

public sealed record CleanupRunStatus(
    DateTimeOffset CompletedAtUtc,
    bool Succeeded,
    int DeletedFileCount,
    int SkippedPathCount,
    int FailedPathCount)
{
    public IReadOnlyList<CleanupPathOutcome> Items { get; init; } = [];
}

public sealed class CleanupRunStatusStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(path);

    public CleanupRunStatus? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var status=JsonSerializer.Deserialize<CleanupRunStatus>(File.ReadAllText(_path));
            return status is null ? null : status with{Items=status.Items??[]};
        }
        catch
        {
            return null;
        }
    }

    public void Write(CleanupRunStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("无法确定清理状态目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(status, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public static class CleanupRunStatusFormatter
{
    /// <summary>Aggregate diagnostics only: never expose stored paths or unknown raw reason strings.</summary>
    public static IReadOnlyList<CleanupReasonSummary> SummarizeReasons(CleanupRunStatus? status, string interfaceLanguage = "zh-CN")
    {
        if (status is null) return [];
        var items = status.Items ?? [];
        var counts = items.Where(x => x.Status is "skipped" or "failed")
            .GroupBy(x => (x.Status, x.Reason))
            .Select(x => Describe(x.Key.Status, x.Key.Reason, x.Count(), interfaceLanguage)).ToList();
        foreach (var (kind, total) in new[] { ("skipped", status.SkippedPathCount), ("failed", status.FailedPathCount) })
        {
            var missing = total - items.Count(x => x.Status == kind);
            if (missing > 0) counts.Add(Describe(kind, "details-not-recorded", missing, interfaceLanguage));
        }
        return counts.OrderBy(x => x.Status, StringComparer.Ordinal).ThenBy(x => x.Reason, StringComparer.Ordinal).ToArray();
    }

    public static string FormatReasons(CleanupRunStatus? status, string interfaceLanguage = "zh-CN") =>
        string.Join(interfaceLanguage == "en" ? "; " : "；",
            SummarizeReasons(status, interfaceLanguage).Select(x => $"{x.Description}: {x.Count}"));

    private static CleanupReasonSummary Describe(string status, string reason, int count, string language)
    {
        var (zh, en, category) = reason switch
        {
            "within-retention" => ("仍在保留期内", "Within retention period", "retained"),
            "completion-receipt-missing" => ("无完成凭据，已保留", "No completion receipt; retained", "retained"),
            "outside-completed-output-contract" => ("非受管完成文件，已保留", "Outside completed-output contract; retained", "retained"),
            "unknown-incomplete-recent-or-changed" => ("旧记录未细分跳过原因", "Legacy record has no detailed skip reason", "unknown"),
            "details-not-recorded" => ("未记录原因明细", "Reason details were not recorded", "unknown"),
            "completion-receipt-invalid" => ("完成凭据无效，已保留", "Invalid completion receipt; retained", "attention"),
            "receipt-unavailable" => ("无法读取或验证完成凭据，已保留", "Cannot read or verify completion receipt; retained", "attention"),
            "output-missing" => ("文件已不存在", "Output no longer exists", "attention"),
            "output-metadata-changed" => ("文件信息已变化，已保留", "Output metadata changed; retained", "retained"),
            "output-content-changed" => ("文件内容已变化，已保留", "Output content changed; retained", "retained"),
            "output-or-receipt-unsafe" => ("文件或凭据路径不安全，已保留", "Unsafe output or receipt path; retained", "attention"),
            "root-unavailable-or-reparse" => ("目录不可用或为重解析点", "Root unavailable or a reparse point", "attention"),
            "directory-reparse" => ("重解析目录，已保留", "Reparse directory; retained", "retained"),
            "candidate-outside-contract" => ("候选文件超出清理范围，已保留", "Candidate outside cleanup contract; retained", "attention"),
            "candidate-changed-before-execution" => ("执行前文件状态变化，已保留", "Candidate changed before execution; retained", "retained"),
            "scan-unavailable" => ("无法扫描目录", "Directory scan failed", "attention"),
            "recycle-unavailable" => ("无法移入回收站", "Recycle failed", "attention"),
            _ => ("未识别的原因", "Unrecognized reason", "unknown"),
        };
        // A failure remains a failure even if its reason is absent or unexpectedly reused.
        return new(status, category == "unknown" && reason != "unknown-incomplete-recent-or-changed" && reason != "details-not-recorded"
            ? "unrecognized" : reason, count, language == "en" ? en : zh, status == "failed" ? "failed" : category);
    }

    public static string Format(CleanupRunStatus? status, TimeZoneInfo? timeZone = null, string interfaceLanguage = "zh-CN")
    {
        var english = interfaceLanguage == "en";
        if (status is null) return english ? "Never run" : "从未执行";
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(status.CompletedAtUtc, zone);
        var outcome = english
            ? status.Succeeded
                ? $"Succeeded; recycled {status.DeletedFileCount}, skipped {status.SkippedPathCount}"
                : $"Partially failed; recycled {status.DeletedFileCount}, skipped {status.SkippedPathCount}, failed {status.FailedPathCount}"
            : status.Succeeded
            ? $"成功，回收 {status.DeletedFileCount}，跳过 {status.SkippedPathCount}"
            : $"未完全成功，回收 {status.DeletedFileCount}，跳过 {status.SkippedPathCount}，失败 {status.FailedPathCount}";
        return $"{local:yyyy-MM-dd HH:mm:ss} · {outcome}";
    }
}

public sealed record CleanupReasonSummary(string Status, string Reason, int Count, string Description, string Category);

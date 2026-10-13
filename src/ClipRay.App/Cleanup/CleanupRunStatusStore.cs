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

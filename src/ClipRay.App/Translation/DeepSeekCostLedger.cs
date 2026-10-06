using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ClipRay.App.Translation;

public sealed record DeepSeekCostSummary(
    decimal EstimatedCny,
    long PromptCacheHitTokens,
    long PromptCacheMissTokens,
    long CompletionTokens,
    int ApiRequestCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastUpdatedAtUtc)
{
    public static DeepSeekCostSummary Empty(DateTimeOffset now) =>
        new(0m, 0, 0, 0, 0, now, now);
}

public sealed class DeepSeekCostLedger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _path;

    public DeepSeekCostLedger(string path) => _path = Path.GetFullPath(path);

    public DeepSeekCostSummary Load()
    {
        lock (_sync)
        {
            return LoadUnsafe() ?? DeepSeekCostSummary.Empty(DateTimeOffset.UtcNow);
        }
    }

    public DeepSeekCostSummary Record(DeepSeekCostEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        lock (_sync)
        {
            var current = LoadUnsafe() ?? DeepSeekCostSummary.Empty(estimate.PricedAt.ToUniversalTime());
            var updated = current with
            {
                EstimatedCny = current.EstimatedCny + estimate.EstimatedCny,
                PromptCacheHitTokens = current.PromptCacheHitTokens + estimate.PromptCacheHitTokens,
                PromptCacheMissTokens = current.PromptCacheMissTokens + estimate.PromptCacheMissTokens,
                CompletionTokens = current.CompletionTokens + estimate.CompletionTokens,
                ApiRequestCount = current.ApiRequestCount + estimate.RequestCount,
                LastUpdatedAtUtc = estimate.PricedAt.ToUniversalTime(),
            };
            WriteUnsafe(updated);
            return updated;
        }
    }

    public DeepSeekCostSummary Reset(DateTimeOffset resetAt)
    {
        lock (_sync)
        {
            var empty = DeepSeekCostSummary.Empty(resetAt.ToUniversalTime());
            WriteUnsafe(empty);
            return empty;
        }
    }

    private DeepSeekCostSummary? LoadUnsafe()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<DeepSeekCostSummary>(File.ReadAllText(_path));
        }
        catch
        {
            return null;
        }
    }

    private void WriteUnsafe(DeepSeekCostSummary summary)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("无法确定 DeepSeek 费用统计目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(summary, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public static class DeepSeekCostSummaryFormatter
{
    public static string Format(DeepSeekCostSummary summary, string interfaceLanguage = "zh-CN")
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.ApiRequestCount == 0) return interfaceLanguage == "en" ? "No DeepSeek usage costs recorded" : "尚无可统计的 DeepSeek 费用";
        var amount = summary.EstimatedCny > 0m && summary.EstimatedCny < 0.000001m
            ? "< 0.000001"
            : summary.EstimatedCny.ToString("0.######", CultureInfo.InvariantCulture);
        return interfaceLanguage == "en" ? $"Estimated total ¥{amount} · {summary.ApiRequestCount} API requests" : $"累计预估费用 ¥{amount} · {summary.ApiRequestCount} 次 API 请求";
    }

    public static string Detail(DeepSeekCostSummary summary, TimeZoneInfo? timeZone = null, string interfaceLanguage = "zh-CN")
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.ApiRequestCount == 0)
            return interfaceLanguage == "en" ? "Includes only DeepSeek translations with token usage reported to ClipRay. This is not your DeepSeek account bill." : "仅统计 ClipRay 收到 token usage 的 DeepSeek 翻译；不是 DeepSeek 账户账单。";
        var local = TimeZoneInfo.ConvertTime(summary.StartedAtUtc, timeZone ?? TimeZoneInfo.Local);
        if (interfaceLanguage == "en") return $"Since {local:yyyy-MM-dd HH:mm} · Cache hits {summary.PromptCacheHitTokens}, misses {summary.PromptCacheMissTokens}, output {summary.CompletionTokens} tokens. Local cumulative estimate only; not your DeepSeek account bill.";
        return $"自 {local:yyyy-MM-dd HH:mm} 起 · 缓存命中 {summary.PromptCacheHitTokens}，未命中 {summary.PromptCacheMissTokens}，输出 {summary.CompletionTokens} token。仅为本机累计预估，不是 DeepSeek 账户账单。";
    }
}

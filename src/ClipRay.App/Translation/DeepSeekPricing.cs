namespace ClipRay.App.Translation;

public sealed record DeepSeekCostEstimate(
    decimal EstimatedCny,
    long PromptCacheHitTokens,
    long PromptCacheMissTokens,
    long CompletionTokens,
    int RequestCount,
    DateTimeOffset PricedAt,
    string PriceSchedule);

internal sealed record DeepSeekTokenUsage(
    long PromptCacheHitTokens,
    long PromptCacheMissTokens,
    long CompletionTokens);

public static class DeepSeekPricing
{
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    private static readonly DateTimeOffset ScheduledPricingEffectiveAtUtc =
        new(2026, 8, 16, 16, 0, 0, TimeSpan.Zero);

    public static DeepSeekCostEstimate Estimate(
        string model,
        long promptCacheHitTokens,
        long promptCacheMissTokens,
        long completionTokens,
        DateTimeOffset at)
    {
        if (model != DeepSeekModels.Pro && model != "deepseek-v4-flash") DeepSeekModels.Validate(model);
        ArgumentOutOfRangeException.ThrowIfNegative(promptCacheHitTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(promptCacheMissTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(completionTokens);

        if (model == "deepseek-v4-flash") model = DeepSeekModels.Flash;
        var rates = ResolveRates(model, at);
        var estimatedCny =
            (promptCacheHitTokens * rates.CacheHitPerMillion
             + promptCacheMissTokens * rates.CacheMissPerMillion
             + completionTokens * rates.OutputPerMillion) / 1_000_000m;
        return new DeepSeekCostEstimate(
            estimatedCny,
            promptCacheHitTokens,
            promptCacheMissTokens,
            completionTokens,
            1,
            at,
            rates.Schedule);
    }

    private static DeepSeekRates ResolveRates(string model, DateTimeOffset at)
    {
        if (model == DeepSeekModels.Flash && at.ToUniversalTime() >=
            new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.Zero))
        {
            var day = at.ToOffset(BeijingOffset).DayOfWeek;
            var currentPeak = day is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && IsPeakPeriod(at);
            return currentPeak
                ? new DeepSeekRates(0.04m, 2m, 8m, "2026-09-10 起 V4.1 Flash 高峰价")
                : new DeepSeekRates(0.02m, 1m, 4m, "2026-09-10 起 V4.1 Flash 空闲价");
        }
        if (at.ToUniversalTime() < ScheduledPricingEffectiveAtUtc)
        {
            return model == DeepSeekModels.Flash
                ? new DeepSeekRates(0.02m, 1m, 2m, "2026-08-17 前基准价")
                : new DeepSeekRates(0.025m, 3m, 6m, "2026-08-17 前基准价");
        }

        var peak = IsPeakPeriod(at);
        if (model == DeepSeekModels.Flash)
        {
            return peak
                ? new DeepSeekRates(0.10m, 3m, 9m, "2026-08-17 起高峰价")
                : new DeepSeekRates(0.05m, 1.5m, 4.5m, "2026-08-17 起低谷价");
        }
        return peak
            ? new DeepSeekRates(0.30m, 9m, 27m, "2026-08-17 起高峰价")
            : new DeepSeekRates(0.15m, 4.5m, 13.5m, "2026-08-17 起低谷价");
    }

    public static bool IsPeakPeriod(DateTimeOffset at)
    {
        var beijingTime = at.ToOffset(BeijingOffset).TimeOfDay;
        return beijingTime >= TimeSpan.FromHours(9) && beijingTime < TimeSpan.FromHours(12)
            || beijingTime >= TimeSpan.FromHours(14) && beijingTime < TimeSpan.FromHours(18);
    }

    private sealed record DeepSeekRates(
        decimal CacheHitPerMillion,
        decimal CacheMissPerMillion,
        decimal OutputPerMillion,
        string Schedule);
}

internal sealed class DeepSeekCostAccumulator
{
    private readonly object _sync = new();
    private decimal _estimatedCny;
    private long _promptCacheHitTokens;
    private long _promptCacheMissTokens;
    private long _completionTokens;
    private int _requestCount;
    private DateTimeOffset _lastPricedAt;
    private string? _priceSchedule;

    public void Add(string model, DeepSeekTokenUsage? usage, DateTimeOffset pricedAt)
    {
        if (usage is null) return;
        var estimate = DeepSeekPricing.Estimate(
            model,
            usage.PromptCacheHitTokens,
            usage.PromptCacheMissTokens,
            usage.CompletionTokens,
            pricedAt);
        lock (_sync)
        {
            _estimatedCny += estimate.EstimatedCny;
            _promptCacheHitTokens += estimate.PromptCacheHitTokens;
            _promptCacheMissTokens += estimate.PromptCacheMissTokens;
            _completionTokens += estimate.CompletionTokens;
            _requestCount++;
            _lastPricedAt = pricedAt;
            _priceSchedule = _priceSchedule is null || string.Equals(_priceSchedule, estimate.PriceSchedule, StringComparison.Ordinal)
                ? estimate.PriceSchedule
                : "按每次请求对应的公开价";
        }
    }

    public DeepSeekCostEstimate? Snapshot()
    {
        lock (_sync)
        {
            return _requestCount == 0
                ? null
                : new DeepSeekCostEstimate(
                    _estimatedCny,
                    _promptCacheHitTokens,
                    _promptCacheMissTokens,
                    _completionTokens,
                    _requestCount,
                    _lastPricedAt,
                    _priceSchedule!);
        }
    }
}

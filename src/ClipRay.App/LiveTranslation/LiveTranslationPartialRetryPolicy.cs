using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

// A partial frame keeps its successful items visible while failed requests
// remain retryable. The deadline uses monotonic time, never wall-clock time.
internal sealed class LiveTranslationPartialRetryPolicy(TimeProvider clock)
{
    private int _consecutivePartials;
    private long _scheduledAt;
    private TimeSpan _interval;

    public bool IsPending => _consecutivePartials > 0;
    public bool IsDue => IsPending && Remaining == TimeSpan.Zero;
    public TimeSpan Remaining
    {
        get
        {
            if (!IsPending) return TimeSpan.Zero;
            var elapsed = clock.GetElapsedTime(_scheduledAt);
            return elapsed >= _interval ? TimeSpan.Zero : _interval - elapsed;
        }
    }

    public TimeSpan RecordPartial(TranslationRouteKind route)
    {
        _consecutivePartials = Math.Min(_consecutivePartials + 1, 32);
        _interval = DelayFor(route, _consecutivePartials);
        _scheduledAt = clock.GetTimestamp();
        return _interval;
    }

    public TimeSpan NextPollDelay() => TimeSpan.FromMilliseconds(
        Math.Min(500, Math.Max(1, Remaining.TotalMilliseconds)));

    public void Reset()
    {
        _consecutivePartials = 0;
        _interval = TimeSpan.Zero;
    }

    public static TimeSpan DelayFor(TranslationRouteKind route, int consecutivePartials)
    {
        if (consecutivePartials <= 0) throw new ArgumentOutOfRangeException(nameof(consecutivePartials));
        if (route is TranslationRouteKind.DeepSeekFlash or TranslationRouteKind.DeepSeekPro)
        {
            var multiplier = 1 << Math.Min(consecutivePartials - 1, 4);
            return TimeSpan.FromSeconds(Math.Min(60, 6 * multiplier));
        }
        return LiveTranslationRefreshPolicy.NextDelay(route, Math.Min(consecutivePartials, 3));
    }
}

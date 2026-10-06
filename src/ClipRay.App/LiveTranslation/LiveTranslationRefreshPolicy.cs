using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

public static class LiveTranslationRefreshPolicy
{
    public static TimeSpan NextDelay(TranslationRouteKind route, int consecutiveFailures)
    {
        var baseline = route == TranslationRouteKind.Machine ? 150 : route == TranslationRouteKind.LocalLmt ? 1200 : 2500;
        var multiplier = 1 << Math.Clamp(consecutiveFailures, 0, 3);
        return TimeSpan.FromMilliseconds(Math.Min(6000, baseline * multiplier));
    }
}

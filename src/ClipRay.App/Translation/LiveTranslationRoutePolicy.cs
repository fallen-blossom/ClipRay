namespace ClipRay.App.Translation;

public static class LiveTranslationRoutePolicy
{
    public static TranslationRouteKind Resolve(
        TranslationRouteKind? requestedRoute,
        string configuredProvider)
    {
        if (requestedRoute is TranslationRouteKind.Machine)
            return TranslationRouteKind.Machine;
        if (requestedRoute is TranslationRouteKind.DeepSeekFlash or TranslationRouteKind.DeepSeekPro)
            return TranslationRouteKind.DeepSeekFlash;

        return configuredProvider switch
        {
            "Machine" => TranslationRouteKind.Machine,
            _ => TranslationRouteKind.DeepSeekFlash,
        };
    }
}

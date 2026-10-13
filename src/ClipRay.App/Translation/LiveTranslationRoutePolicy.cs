namespace ClipRay.App.Translation;

public static class LiveTranslationRoutePolicy
{
    public static bool IsSupported(TranslationRouteKind route) =>
        route is TranslationRouteKind.Machine or TranslationRouteKind.DeepSeekFlash or TranslationRouteKind.DeepSeekPro;

    public static TranslationRouteKind Resolve(
        TranslationRouteKind? requestedRoute,
        string configuredProvider)
    {
        if (!TryResolve(requestedRoute, configuredProvider, out var route))
            throw new NotSupportedException("The explicitly selected route does not support live translation.");
        return route;
    }

    public static bool TryResolve(TranslationRouteKind? requestedRoute, string configuredProvider, out TranslationRouteKind route)
    {
        route = TranslationRouteKind.FollowSettings;
        if (requestedRoute is { } explicitRoute)
        {
            if (!IsSupported(explicitRoute)) return false;
            route = explicitRoute == TranslationRouteKind.Machine ? TranslationRouteKind.Machine : TranslationRouteKind.DeepSeekFlash;
            return true;
        }
        route = configuredProvider switch
        {
            "Machine" => TranslationRouteKind.Machine,
            _ => TranslationRouteKind.DeepSeekFlash,
        };
        return true;
    }
}

using ClipRay.App.Translation;

internal static class LiveTranslationRouteTests
{
    public static void ExplicitSupportedRouteWinsAndMissingOrUnsupportedRouteUsesLiveSettings()
    {
        if (LiveTranslationRoutePolicy.Resolve(TranslationRouteKind.DeepSeekFlash, "Machine")
            != TranslationRouteKind.DeepSeekFlash)
            throw new Exception("The live translation request route was ignored.");

        if (LiveTranslationRoutePolicy.Resolve(null, "Machine") != TranslationRouteKind.Machine
            || LiveTranslationRoutePolicy.Resolve(null, "DeepSeek") != TranslationRouteKind.DeepSeekFlash)
            throw new Exception("Live translation did not follow its independent configured provider.");

        if (LiveTranslationRoutePolicy.Resolve(TranslationRouteKind.LocalLmt, "DeepSeek")
            != TranslationRouteKind.DeepSeekFlash)
            throw new Exception("Live translation accepted a Local LMT route that is not supported for live use.");
    }
}

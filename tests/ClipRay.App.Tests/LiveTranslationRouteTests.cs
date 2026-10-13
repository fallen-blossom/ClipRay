using ClipRay.App.Translation;

internal static class LiveTranslationRouteTests
{
    public static void ExplicitSupportedRouteWinsAndOnlyMissingRouteUsesLiveSettings()
    {
        if (LiveTranslationRoutePolicy.Resolve(TranslationRouteKind.DeepSeekFlash, "Machine")
            != TranslationRouteKind.DeepSeekFlash)
            throw new Exception("The live translation request route was ignored.");

        if (LiveTranslationRoutePolicy.Resolve(null, "Machine") != TranslationRouteKind.Machine
            || LiveTranslationRoutePolicy.Resolve(null, "DeepSeek") != TranslationRouteKind.DeepSeekFlash)
            throw new Exception("Live translation did not follow its independent configured provider.");

        if (LiveTranslationRoutePolicy.Resolve(TranslationRouteKind.Machine, "DeepSeek") != TranslationRouteKind.Machine
            || LiveTranslationRoutePolicy.Resolve(TranslationRouteKind.DeepSeekPro, "Machine") != TranslationRouteKind.DeepSeekFlash)
            throw new Exception("A supported explicit route was ignored.");

        // Old fallback silently converted an explicit private Local LMT choice to the cloud default.
        // Only a null request may use the independent live default; unsupported explicit values fail closed.
        foreach (var unsupported in new[] { TranslationRouteKind.LocalLmt, TranslationRouteKind.FollowSettings, (TranslationRouteKind)99 })
        {
            foreach (var configured in new[] { "DeepSeek", "Machine" })
            {
                if (LiveTranslationRoutePolicy.TryResolve(unsupported, configured, out _))
                    throw new Exception("Unsupported explicit live route fell back to a configured route.");
                try { LiveTranslationRoutePolicy.Resolve(unsupported, configured); }
                catch (NotSupportedException) { continue; }
                throw new Exception("Resolve accepted an unsupported explicit live route.");
            }
        }
    }
}

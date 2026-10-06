namespace ClipRay.App.Translation;

public enum TranslationRouteKind
{
    FollowSettings,
    LocalLmt,
    Machine,
    DeepSeekFlash,
    DeepSeekPro,
}

public sealed record TranslationRouteOption(TranslationRouteKind Kind, string DisplayName, string ShortName)
{
    public override string ToString() => DisplayName;
}

public static class TranslationRouteCatalog
{
    public static IReadOnlyList<TranslationRouteOption> Options { get; } =
    [
        new(TranslationRouteKind.LocalLmt, "本地 LMT（隐私优先）", "本地 LMT"),
        new(TranslationRouteKind.Machine, "本地机翻（离线神经翻译）", "本地机翻"),
        new(TranslationRouteKind.DeepSeekFlash, "DeepSeek V4.1 Flash", "DeepSeek V4.1 Flash"),
    ];

    public static TranslationRouteOption Get(TranslationRouteKind kind) =>
        Options.First(option => option.Kind == (kind == TranslationRouteKind.DeepSeekPro ? TranslationRouteKind.DeepSeekFlash : kind));
}

using ClipRay.App.Translation;

namespace ClipRay.App.Settings;

public static class SettingsProviderTestRouter
{
    public static Task<TranslationResult> TestAsync(
        string provider,
        Func<CancellationToken, Task<TranslationResult>> localTest,
        Func<CancellationToken, Task<TranslationResult>> deepSeekTest,
        CancellationToken cancellationToken) => provider switch
    {
        "Local LMT" or "Machine" => localTest(cancellationToken),
        "DeepSeek" => deepSeekTest(cancellationToken),
        _ => Task.FromResult(new TranslationResult(false, string.Empty, "未知翻译 Provider。")),
    };
}

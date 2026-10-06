using ClipRay.App.Translation;

namespace ClipRay.App.Overlay;

public enum TranslationDisplayMode
{
    Original,
    Translated,
}

public enum TranslationSelectionExportMode
{
    Original,
    Translated,
}

public sealed record TranslationDisplayState(
    TranslationDisplayMode Mode,
    IReadOnlyList<InPlaceLayoutItem> Items)
{
    public string ToggleLabel => Mode == TranslationDisplayMode.Translated ? "显示原文" : "显示译文";

    public TranslationSelectionExportMode ExportMode => Mode == TranslationDisplayMode.Translated
        ? TranslationSelectionExportMode.Translated
        : TranslationSelectionExportMode.Original;

    public TranslationDisplayState Toggle() => this with
    {
        Mode = Mode == TranslationDisplayMode.Translated
            ? TranslationDisplayMode.Original
            : TranslationDisplayMode.Translated,
    };
}

namespace ClipRay.App.Translation;

public sealed record WorkbenchLanguageOption(string Code, string Label)
{
    public override string ToString() => Label;
}

public static class WorkbenchLanguageOptions
{
    // Available inputs describe engine routing, not a translation quality certification.
    public static IReadOnlyList<WorkbenchLanguageOption> Languages { get; } =
    [
        new("en", "英语"), new("zh", "简体中文"), new("ja", "日语"), new("ko", "韩语"),
        new("es", "西班牙语"), new("fr", "法语"), new("de", "德语"), new("ru", "俄语"),
        new("pt", "葡萄牙语"), new("it", "意大利语"), new("ar", "阿拉伯语")
    ];
    public static IReadOnlyList<WorkbenchLanguageOption> Sources(TranslationRouteKind route, bool commonMachineModel) =>
        new[] { new WorkbenchLanguageOption("auto", "自动判断（可手动修正）") }
            .Concat(route == TranslationRouteKind.Machine && !commonMachineModel
                ? Languages.Where(language => language.Code is "en" or "zh") : Languages).ToArray();
    public static IReadOnlyList<WorkbenchLanguageOption> Targets(TranslationRouteKind route) =>
        route == TranslationRouteKind.Machine ? Languages.Where(language => language.Code == "zh").ToArray() : Languages;
    public static string Code(string value) => Languages.FirstOrDefault(language =>
        LocalLmtLanguage.Normalize(language.Code).Equals(LocalLmtLanguage.Normalize(value), StringComparison.OrdinalIgnoreCase))?.Code ?? value;
}

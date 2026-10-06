using System.Text.RegularExpressions;

namespace ClipRay.App.Settings;

public static class SettingsLoadWarningText
{
    public static string ForInterface(string warning, string interfaceLanguage)
    {
        if (AppInterfaceLanguage.Normalize(interfaceLanguage) != "en") return warning;
        var text = warning.Replace("设置文件无效，已使用默认设置。",
                "The settings file is invalid; defaults were loaded.", StringComparison.Ordinal)
            .Replace("实时翻译默认快捷键与旧配置冲突，已仅调整新增快捷键；原有设置保留。",
                "Default live translation shortcuts conflicted with an older configuration. Only the new shortcuts were adjusted; existing settings were kept.",
                StringComparison.Ordinal);
        return Regex.Replace(text,
            @"自动清理计划尚未修复（错误 0x([0-9A-Fa-f]+)）；请在设置中重试。",
            "The scheduled cleanup could not be repaired (error 0x$1). Retry in Settings.",
            RegexOptions.CultureInvariant);
    }
}

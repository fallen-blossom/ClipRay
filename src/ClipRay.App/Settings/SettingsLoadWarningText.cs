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
                StringComparison.Ordinal)
            .Replace("原文件已保留，修正后可在设置中保存。", "The original file was kept. Save in Settings after correcting the values.", StringComparison.Ordinal);
        text = Regex.Replace(text, @"无法读取设置文件（错误 0x([0-9A-Fa-f]+)）；已使用默认设置，原文件未更改。",
            "Could not read the settings file (error 0x$1). Defaults were loaded; the original file was not changed.", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"凭据迁移未完成（错误 0x([0-9A-Fa-f]+)）；原选择保留。",
            "Credential migration did not complete (error 0x$1); the original selection was kept.", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"设置迁移尚未保存（错误 0x([0-9A-Fa-f]+)）；原文件保留，请在设置中重试。",
            "Settings migration was not saved (error 0x$1). The original file was kept; retry in Settings.", RegexOptions.CultureInvariant);
        return Regex.Replace(text,
            @"自动清理计划尚未修复（错误 0x([0-9A-Fa-f]+)）；请在设置中重试。",
            "The scheduled cleanup could not be repaired (error 0x$1). Retry in Settings.",
            RegexOptions.CultureInvariant);
    }
}

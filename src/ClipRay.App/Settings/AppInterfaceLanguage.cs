using Forms = System.Windows.Forms;

namespace ClipRay.App.Settings;

public static class AppInterfaceLanguage
{
    public static string Normalize(string? code) => string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh-CN";
    public static string ForWindowsUiCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return "en";
        var tag = cultureName.Replace('_', '-');
        return tag.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-MY", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN" : "en";
    }
    public static bool IsOnlyInterfaceChange(AppSettings? prior, AppSettings updated) => prior is not null
        && (prior.InterfaceLanguage != updated.InterfaceLanguage
            || prior.InterfaceLanguageFollowsWindows != updated.InterfaceLanguageFollowsWindows)
        && prior == updated with
        {
            InterfaceLanguage = prior.InterfaceLanguage,
            InterfaceLanguageFollowsWindows = prior.InterfaceLanguageFollowsWindows,
        };
    public static string Text(string language, string chinese, string english) => Normalize(language) == "en" ? english : chinese;
    public static void UpdateTray(Forms.ToolStripItemCollection items, string language)
    {
        var english = new Dictionary<string, string>
        {
            ["区域截图"] = "Capture region", ["全屏截图（含任务栏）"] = "Capture screen (with taskbar)",
            ["全屏截图（不含任务栏）"] = "Capture screen (without taskbar)", ["截取全部屏幕"] = "Capture all screens",
            ["翻译"] = "Translate", ["实时区域翻译"] = "Live region translation", ["设置"] = "Settings",
            ["停止实时翻译"] = "Stop live translation", ["停止录屏"] = "Stop recording", ["退出 ClipRay"] = "Exit ClipRay",
            ["最近截取"] = "Recent captures", ["恢复长截图"] = "Recover scrolling capture"
        };
        foreach (Forms.ToolStripItem item in items)
        {
            if (item is Forms.ToolStripSeparator) continue;
            item.Tag ??= item.Text;
            if (item.Tag is string original && english.TryGetValue(original, out var translation))
                item.Text = Text(language, original, translation);
        }
    }
}

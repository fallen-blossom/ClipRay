using ClipRay.App.Security;

namespace ClipRay.App.Settings;

internal static class StartupSettingsReconciliation
{
    internal static SettingsLoadResult Apply(SettingsLoadResult loaded, bool startupEnabled,
        Func<string?, bool> migrateCredential, Action<AppSettings> save)
    {
        var result = loaded;
        // Recovery defaults are usable for maintenance, but must not be mistaken
        // for a successful read and automatically overwrite the original file.
        if (!loaded.PreserveOriginalFile)
        {
            try
            {
                if (migrateCredential(result.Settings.CredentialName)) result = result with {
                    Settings = result.Settings with { CredentialName = CredentialVault.FixedDeepSeekSlot }, RequiresSave = true };
            }
            catch (Exception error)
            { result = Warn(result, $"凭据迁移未完成（错误 0x{unchecked((uint)error.HResult):X8}）；原选择保留。"); }
        }
        if (result.Settings.StartWithWindows != startupEnabled) result = result with {
            Settings = result.Settings with { StartWithWindows = startupEnabled }, RequiresSave = true };
        if (result.RequiresSave && !result.PreserveOriginalFile)
        {
            try { save(result.Settings); }
            catch (Exception error)
            { result = Warn(result, $"设置迁移尚未保存（错误 0x{unchecked((uint)error.HResult):X8}）；原文件保留，请在设置中重试。"); }
        }
        return result with { RequiresSave = result.RequiresSave && !result.PreserveOriginalFile };
    }
    private static SettingsLoadResult Warn(SettingsLoadResult result, string warning) =>
        result with { Warning = string.Join(" ", new[] { result.Warning, warning }.Where(s => !string.IsNullOrWhiteSpace(s))) };
}

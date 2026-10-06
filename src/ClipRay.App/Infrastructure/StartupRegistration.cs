using Microsoft.Win32;

namespace ClipRay.App.Infrastructure;

public interface IStartupRegistration
{
    void SetEnabled(bool enabled, string executablePath);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    public void SetEnabled(bool enabled, string executablePath) => StartupRegistration.SetEnabled(enabled, executablePath);
}

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipRay";

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\" --startup";

    public static bool MatchesCommand(string? command, string executablePath) => string.Equals(
        command?.Trim(),
        BuildCommand(executablePath),
        StringComparison.OrdinalIgnoreCase);

    public static bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return MatchesCommand(key?.GetValue(ValueName) as string, executablePath);
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户启动项注册表位置。");
        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        if (IsEnabled(executablePath) != enabled)
        {
            throw new InvalidOperationException("Windows 启动项写入后未能读回预期状态。");
        }
    }
}

public static class StartupStatusFormatter
{
    public static string Format(bool expectedEnabled, bool actualEnabled, string interfaceLanguage = "zh-CN") =>
        interfaceLanguage == "en"
            ? expectedEnabled == actualEnabled
                ? actualEnabled ? "Enabled (this application)" : "Disabled"
                : expectedEnabled ? "Not applied; save settings to retry" : "Unexpected startup entry still exists"
            :
        expectedEnabled == actualEnabled
            ? actualEnabled ? "已启用（当前程序）" : "未启用"
            : expectedEnabled
                ? "未生效；请保存设置重试"
                : "异常：启动项仍然存在";
}

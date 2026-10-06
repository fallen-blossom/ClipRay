using System.IO;
using ClipRay.App.Settings;

namespace ClipRay.App.Infrastructure;

public static class InstallerCleanupConfiguration
{
    public static AppSettings BuildSettings(AppSettings current) => current with
    {
        CleanupScheduleEnabled = true,
        CleanupRetentionDays = 30,
        CleanupScheduleFrequency = CleanupScheduleFrequency.Daily,
        CleanupDailyTime = new TimeOnly(21, 0),
    };

    public static int Execute(ApplicationPathSet paths, string executablePath)
    {
        var settingsStore = new SettingsStore(Path.Combine(paths.DataRoot, "settings.json"), paths.InstallRoot);
        var current = settingsStore.LoadRecoverable().Settings;
        var configured = BuildSettings(current);
        var registration = new WindowsCleanupScheduleRegistration();

        try
        {
            registration.Set(CleanupScheduleOptions.FromSettings(configured), executablePath);
            settingsStore.Save(configured);
            return 0;
        }
        catch
        {
            try
            {
                registration.Set(CleanupScheduleOptions.FromSettings(current), executablePath);
            }
            catch
            {
                // 主操作已经失败；保留失败退出码，由安装器明确提示用户。
            }
            return 3;
        }
    }
}

using System.Diagnostics;
using System.IO;

namespace ClipRay.App.Infrastructure;

public sealed record InstalledUninstallState(bool IsAvailable, string? UninstallerPath);

public static class InstalledUninstallLauncher
{
    public static InstalledUninstallState Resolve(string applicationRoot, string localApplicationDataRoot)
    {
        var paths = ApplicationPaths.Create(applicationRoot, localApplicationDataRoot);
        if (!paths.ProfileId.Equals("release", StringComparison.Ordinal))
            return new InstalledUninstallState(false, null);

        var uninstallerPath = Path.Combine(paths.InstallRoot, "unins000.exe");
        return File.Exists(uninstallerPath)
            ? new InstalledUninstallState(true, uninstallerPath)
            : new InstalledUninstallState(false, null);
    }

    public static void Launch(InstalledUninstallState state)
    {
        if (!state.IsAvailable || string.IsNullOrWhiteSpace(state.UninstallerPath))
            throw new InvalidOperationException("当前 ClipRay 不是可卸载的正式安装版。");
        if (!File.Exists(state.UninstallerPath))
            throw new FileNotFoundException("找不到 ClipRay 卸载程序。", state.UninstallerPath);

        _ = Process.Start(new ProcessStartInfo(state.UninstallerPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(state.UninstallerPath)!,
        }) ?? throw new InvalidOperationException("无法启动 ClipRay 卸载程序。");
    }
}

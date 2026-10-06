using System.IO;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using Microsoft.VisualBasic.FileIO;

namespace ClipRay.App.Infrastructure;

public sealed record UninstallCleanupResult(int RecycledOutputCount, int FailedOutputCount);

public sealed class UninstallCleanupService
{
    private readonly ApplicationPathSet _paths;
    private readonly string _executablePath;

    public UninstallCleanupService(ApplicationPathSet paths, string executablePath)
    {
        _paths = paths;
        _executablePath = executablePath;
    }

    public UninstallCleanupResult Execute(bool recycleOutputs)
    {
        StartupRegistration.SetEnabled(false, _executablePath);
        new WindowsCleanupScheduleRegistration().Set(
            new CleanupScheduleOptions(false, CleanupScheduleFrequency.Weekly, DayOfWeek.Monday, new TimeOnly(3, 0)),
            _executablePath);
        _ = new CredentialVault().DeleteAllClipRayCredentials();

        var recycled = 0;
        var failed = 0;
        if (recycleOutputs)
        {
            var outputsDirectory = ResolveDefaultOutputsDirectory(_paths);
            if (Directory.Exists(outputsDirectory))
            {
                try
                {
                    FileSystem.DeleteDirectory(
                        outputsDirectory,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                    recycled++;
                }
                catch
                {
                    failed++;
                }
            }
        }

        if (Directory.Exists(_paths.DataRoot)) Directory.Delete(_paths.DataRoot, recursive: true);
        return new UninstallCleanupResult(recycled, failed);
    }

    public static string ResolveDefaultOutputsDirectory(ApplicationPathSet paths) =>
        Path.Combine(Path.GetFullPath(paths.InstallRoot), "Outputs");
}

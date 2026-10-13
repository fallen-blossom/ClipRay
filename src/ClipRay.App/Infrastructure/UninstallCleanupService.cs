using System.IO;
using ClipRay.App.Security;
using ClipRay.App.Settings;
using ClipRay.App.Cleanup;

namespace ClipRay.App.Infrastructure;

public sealed record UninstallCleanupResult(int RecycledOutputCount, int FailedOutputCount);

public interface IUninstallCleanupBackend
{
    void RemoveStartup(string executablePath);
    void RemoveSchedule(string executablePath);
    void RemoveCredentials();
    bool DirectoryExists(string path);
    bool IsOrdinaryTree(string path);
    void RecycleOutputs(string path);
    void RemoveData(string path);
}

public sealed class UninstallCleanupService
{
    private readonly ApplicationPathSet _paths;
    private readonly string _executablePath;
    private readonly IUninstallCleanupBackend _backend;

    public UninstallCleanupService(ApplicationPathSet paths, string executablePath, IUninstallCleanupBackend? backend = null)
    {
        _paths = paths;
        _executablePath = executablePath;
        _backend = backend ?? new WindowsUninstallCleanupBackend();
    }

    public UninstallCleanupResult Execute(bool recycleOutputs)
    {
        var outputs = ResolveDefaultOutputsDirectory(_paths);
        if (!_backend.IsOrdinaryTree(_paths.DataRoot) || (recycleOutputs && !_backend.IsOrdinaryTree(outputs)))
            return new(0,1);
        _backend.RemoveStartup(_executablePath);
        _backend.RemoveSchedule(_executablePath);
        _backend.RemoveCredentials();

        var recycled = 0;
        var failed = 0;
        if (recycleOutputs)
        {
            var outputsDirectory = ResolveDefaultOutputsDirectory(_paths);
            if (_backend.DirectoryExists(outputsDirectory))
            {
                try
                {
                    _backend.RecycleOutputs(outputsDirectory);
                    recycled++;
                }
                catch
                {
                    failed++;
                }
            }
        }

        if (failed == 0 && _backend.DirectoryExists(_paths.DataRoot)) _backend.RemoveData(_paths.DataRoot);
        return new UninstallCleanupResult(recycled, failed);
    }

    public static string ResolveDefaultOutputsDirectory(ApplicationPathSet paths) =>
        Path.Combine(Path.GetFullPath(paths.InstallRoot), "Outputs");
}

internal sealed class WindowsUninstallCleanupBackend : IUninstallCleanupBackend
{
    public void RemoveStartup(string executablePath) => StartupRegistration.SetEnabled(false,executablePath);
    public void RemoveSchedule(string executablePath) => new WindowsCleanupScheduleRegistration().Set(
        new(false,CleanupScheduleFrequency.Weekly,DayOfWeek.Monday,new TimeOnly(3,0)),executablePath);
    public void RemoveCredentials() => _ = new CredentialVault().DeleteAllClipRayCredentials();
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool IsOrdinaryTree(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !CompletedOutputStore.IsOrdinaryPath(path)) return false;
        if (!Directory.Exists(path)) return true;
        try
        {
            var pending = new Stack<string>(); pending.Push(path);
            while(pending.TryPop(out var directory))
                foreach(var item in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes=File.GetAttributes(item);
                    if(attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
                    if(attributes.HasFlag(FileAttributes.Directory)) pending.Push(item);
                }
            return true;
        }
        catch { return false; }
    }
    public void RecycleOutputs(string path)
    {
        if(!IsOrdinaryTree(path)) throw new IOException("Uninstall output tree changed.");
        new WindowsCleanupRecycleBackend().RecycleDirectory(path);
    }
    public void RemoveData(string path)
    {
        if(!IsOrdinaryTree(path)) throw new IOException("Uninstall data tree changed.");
        Directory.Delete(path,recursive:true);
    }
}

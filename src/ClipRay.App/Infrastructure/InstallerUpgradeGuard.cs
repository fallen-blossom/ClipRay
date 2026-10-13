namespace ClipRay.App.Infrastructure;

public static class InstallerUpgradeGuard
{
    public const string MutexName = @"Local\ClipRayUpgrade-C5C7914B-7566-4912-A9E0-4D09E724BDA1";

    public static bool IsActive(string mutexName = MutexName)
    {
        try
        {
            if (!Mutex.TryOpenExisting(mutexName, out var mutex)) return false;
            using (mutex) return true;
        }
        catch (UnauthorizedAccessException) { return true; }
    }
}

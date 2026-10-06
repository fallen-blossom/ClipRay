using System.IO;

namespace ClipRay.App.Infrastructure;

public sealed record ApplicationPathSet(
    string InstallRoot,
    string DataRoot,
    string LongCaptureTempRoot,
    string LocalModelRoot,
    string ProfileId,
    string DiagnosticsRoot = "");

public static class ApplicationPaths
{
    public static ApplicationPathSet Create(string applicationRoot, string localApplicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataRoot);
        var normalizedApplicationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationRoot));
        var workspaceRoot = WorkspaceLocator.TryFind(normalizedApplicationRoot);
        var profileId = workspaceRoot is null
            ? "release"
            : "development";
        var installRoot = profileId == "release"
            && Path.GetFileName(normalizedApplicationRoot).Equals("app", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(normalizedApplicationRoot)!
            : normalizedApplicationRoot;
        var dataDirectoryName = profileId == "development" ? "ClipRay-Dev" : "ClipRay";
        var dataRoot = Path.Combine(Path.GetFullPath(localApplicationDataRoot), dataDirectoryName);
        return new ApplicationPathSet(
            installRoot,
            dataRoot,
            Path.Combine(dataRoot, "tmp", "long-capture"),
            Path.Combine(normalizedApplicationRoot, "models", "local-lmt"),
            profileId,
            Path.Combine(dataRoot, "diagnostics"));
    }
}

using System.IO;
using ClipRay.App.Cleanup;
using ClipRay.App.Infrastructure;
using ClipRay.App.Translation;

namespace ClipRay.App.Settings;

// Constructor reads are explicit so offscreen fixtures never inspect daily state,
// installed applications or model inventories by accident.
public sealed class SettingsWindowEnvironment
{
    public required Func<CleanupRunStatus?> LoadCleanupRun { get; init; }
    public required Func<InstalledUninstallState> ResolveUninstall { get; init; }
    public required Func<string?, string> ResolveLocalModelPath { get; init; }
    public required Func<IReadOnlyList<LocalLmtDevelopmentModelOption>> ResolveLocalModels { get; init; }
    public required Func<string, bool> ModelExists { get; init; }
    internal static SettingsWindowEnvironment Default() => new()
    {
        LoadCleanupRun = () => new CleanupRunStatusStore(Path.Combine(ApplicationPaths.Create(AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)).DataRoot, "cleanup-status.json")).Load(),
        ResolveUninstall = () => InstalledUninstallLauncher.Resolve(AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        ResolveLocalModelPath = path => LocalLmtOptions.Discover(path).ModelPath,
        ResolveLocalModels = () => LocalLmtModelCatalog.ResolveProviderModels(AppContext.BaseDirectory,
            WorkspaceLocator.TryFind(AppContext.BaseDirectory)),
        ModelExists = File.Exists,
    };
}

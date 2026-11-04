using System.IO;
using ClipRay.App.Infrastructure;

internal static class LocalPathPolicyTests
{
    public static void NetworkAndDevicePathsFailBeforeDriveOrFileProbe()
    {
        foreach (var path in new[]
        {
            @"\\server\share\model.bin", "//server/share/model.bin", @"\\?\UNC\server\share\model.bin",
            @"\\.\UNC\server\share\model.bin", @"\??\UNC\server\share\model.bin",
            @"\Device\Mup\server\share\model.bin", @"\\?\GLOBALROOT\Device\Mup\server\share\model.bin",
            @"\\?\C:\models\model.bin", @"\\.\C:\models\model.bin",
        })
        {
            var probes = 0;
            ExpectDenied(() => LocalPathPolicy.RequireLocal(path, _ => { probes++; return DriveType.Fixed; }, Missing));
            if (probes != 0) throw new Exception("Network/device path reached drive classification before denial.");
        }
    }

    public static void MappedNetworkAndUnknownDrivesFailBeforeFileProbe()
    {
        foreach (var kind in new[] { DriveType.Network, DriveType.Unknown, DriveType.NoRootDirectory })
        {
            var probes = 0;
            ExpectDenied(() => LocalPathPolicy.RequireLocal(@"Z:\models\model.bin", root =>
            {
                probes++;
                if (root != "Z:\\") throw new Exception("Drive classification received more than the local root.");
                return kind;
            }, Missing));
            if (probes != 1) throw new Exception("Drive classification was omitted or repeated.");
        }
    }

    public static void LocalDrivePathsNormalizeWithoutExistenceProbe()
    {
        foreach (var kind in new[] { DriveType.Fixed, DriveType.Removable, DriveType.CDRom, DriveType.Ram })
        {
            var normalized = LocalPathPolicy.RequireLocal(@"C:\fixture-does-not-exist\one\..\model.bin", _ => kind, Missing);
            if (normalized != @"C:\fixture-does-not-exist\model.bin")
                throw new Exception("A local path was not normalized without querying the file.");
        }
    }

    public static void NonFilesystemUriCannotReachDriveClassification()
    {
        foreach (var path in new[] { "https://fixture.invalid/model.bin", "file://fixture.invalid/share/model.bin" })
        {
            var probes = 0;
            ExpectDenied(() => LocalPathPolicy.RequireLocal(path, _ => { probes++; return DriveType.Fixed; }, Missing));
            if (probes != 0) throw new Exception("A URI reached drive classification.");
        }
    }

    public static void RemoteReparseTargetFailsBeforeTargetMetadata()
    {
        foreach (var target in new[] { @"\\server\share\models", @"\\?\UNC\server\share\models", @"Z:\models" })
        {
            var inspected = new List<string>();
            ExpectDenied(() => LocalPathPolicy.RequireLocal(@"C:\safe\link\model.bin",
                static root => root == "Z:\\" ? DriveType.Network : DriveType.Fixed,
                path =>
                {
                    inspected.Add(path);
                    if (!path.StartsWith(@"C:\safe", StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Remote link target reached a metadata probe.");
                    return path == @"C:\safe\link" ? new(true, true, target) : new(true, false, null);
                }));
            if (!inspected.SequenceEqual(new[] { @"C:\safe", @"C:\safe\link" }))
                throw new Exception("A path beyond the first unsafe link was inspected.");
        }
    }

    public static void LocalJunctionPreservesSuffixAndReturnsResolvedPath()
    {
        var inspected = new List<string>();
        var resolved = LocalPathPolicy.RequireLocal(@"C:\safe\link\part\model.bin", static _ => DriveType.Fixed,
            path =>
            {
                inspected.Add(path);
                return path == @"C:\safe\link" ? new(true, true, @"D:\models") : new(true, false, null);
            });
        if (resolved != @"D:\models\part\model.bin" || !inspected.SequenceEqual(new[]
            { @"C:\safe", @"C:\safe\link", @"D:\models", @"D:\models\part", @"D:\models\part\model.bin" }))
            throw new Exception("A safe local junction did not resolve one hop before probing its suffix.");
    }

    public static void ReparseLoopsAndUnknownTagsFailClosed()
    {
        foreach (var target in new string?[] { @"C:\link", @"C:\link\extra", null })
        {
            var probes = 0;
            ExpectDenied(() => LocalPathPolicy.RequireLocal(@"C:\link\model.bin", static _ => DriveType.Fixed,
                _ => { probes++; return new(true, true, target); }));
            if (probes > 33) throw new Exception("Reparse resolution exceeded its bounded hop count.");
        }
    }

    public static void RelativeReparseTargetUsesTheLinksParent()
    {
        var resolved = LocalPathPolicy.RequireLocal(@"C:\safe\link\model.bin", static _ => DriveType.Fixed,
            path => path == @"C:\safe\link" ? new(true, true, @"..\models") : new(true, false, null));
        if (resolved != @"C:\models\model.bin") throw new Exception("Relative symlink used the process working directory.");
    }

    private static LocalPathComponent Missing(string _) => new(false, false, null);

    private static void ExpectDenied(Action action)
    {
        try { action(); }
        catch (LocalPathAccessDeniedException) { return; }
        throw new Exception("Expected local-path-required before a filesystem probe.");
    }
}

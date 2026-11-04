using System.IO;

namespace ClipRay.App.Infrastructure;

public sealed class LocalPathAccessDeniedException() : IOException("local-path-required");

/// <summary>Reject remote Windows paths before existence checks or process startup.</summary>
public static class LocalPathPolicy
{
    public static string RequireLocal(string path) => RequireLocal(path,
        static root => new DriveInfo(root).DriveType, ReadComponent);

    // Drive classification is metadata about a local drive-letter root. Never
    // pass a UNC root to DriveInfo; reject it lexically before any disk query.
    internal static string RequireLocal(string path, Func<string, DriveType> classifyDrive) =>
        RequireLocal(path, classifyDrive, ReadComponent);

    internal static string RequireLocal(string path, Func<string, DriveType> classifyDrive,
        Func<string, LocalPathComponent> inspectComponent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(classifyDrive);
        ArgumentNullException.ThrowIfNull(inspectComponent);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Resolve(path, 0);

        string Resolve(string candidate, int links)
        {
            if (links > 32) throw new LocalPathAccessDeniedException();
            var fullPath = Normalize(candidate);
            if (!visited.Add(fullPath)) throw new LocalPathAccessDeniedException();
            var root = Path.GetPathRoot(fullPath)!;
            var kind = classifyDrive(root);
            if (kind is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram))
                throw new LocalPathAccessDeniedException();

            var components = fullPath[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var prefix = root;
            for (var index = 0; index < components.Length; index++)
            {
                prefix = Path.Combine(prefix, components[index]);
                // Inspect one prefix at a time. No child or target metadata is
                // touched until every existing parent is known to be local.
                var metadata = inspectComponent(prefix);
                if (!metadata.Exists) return fullPath;
                if (!metadata.IsReparsePoint) continue;
                if (string.IsNullOrWhiteSpace(metadata.LinkTarget)) throw new LocalPathAccessDeniedException();
                var target = metadata.LinkTarget.Replace('/', '\\');
                if (IsDeviceOrNetworkPath(target)) throw new LocalPathAccessDeniedException();
                try { target = Path.GetFullPath(target, Path.GetDirectoryName(prefix)!); }
                catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
                { throw new LocalPathAccessDeniedException(); }
                for (var suffix = index + 1; suffix < components.Length; suffix++)
                    target = Path.Combine(target, components[suffix]);
                return Resolve(target, links + 1);
            }
            return fullPath;
        }
    }

    private static string Normalize(string path)
    {
        var colon = path.IndexOf(':');
        if (colon > 1) throw new LocalPathAccessDeniedException();
        var windowsPath = path.Replace('/', '\\');
        if (IsDeviceOrNetworkPath(windowsPath)) throw new LocalPathAccessDeniedException();
        string fullPath;
        try { fullPath = Path.GetFullPath(windowsPath); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new LocalPathAccessDeniedException(); }
        if (IsDeviceOrNetworkPath(fullPath)) throw new LocalPathAccessDeniedException();
        var root = Path.GetPathRoot(fullPath);
        if (root is not { Length: 3 } || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            throw new LocalPathAccessDeniedException();
        return fullPath;
    }

    public static bool FileExists(string path) => File.Exists(RequireLocal(path));

    private static LocalPathComponent ReadComponent(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return new(false, false, null); }
        if ((attributes & FileAttributes.ReparsePoint) == 0) return new(true, false, null);
        FileSystemInfo item = (attributes & FileAttributes.Directory) != 0
            ? new DirectoryInfo(path) : new FileInfo(path);
        // One hop only: recursive framework resolution could contact a remote
        // target before our own lexical/drive check. Unknown reparse tags fail.
        var target = item.ResolveLinkTarget(returnFinalTarget: false);
        return new(true, true, target?.FullName);
    }

    private static bool IsDeviceOrNetworkPath(string path) => path.StartsWith("\\\\", StringComparison.Ordinal)
        || path.StartsWith("\\??\\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("\\GLOBAL??\\", StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct LocalPathComponent(bool Exists, bool IsReparsePoint, string? LinkTarget);

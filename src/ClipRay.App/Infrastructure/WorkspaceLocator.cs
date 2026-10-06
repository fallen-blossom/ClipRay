using System.IO;

namespace ClipRay.App.Infrastructure;

public static class WorkspaceLocator
{
    public static string? TryFind(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ClipRay.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    public static string Find(string startDirectory)
    {
        return TryFind(startDirectory)
            ?? throw new DirectoryNotFoundException("Could not locate a workspace containing ClipRay.sln.");
    }
}

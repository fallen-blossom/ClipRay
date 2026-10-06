using System.Drawing;
using System.IO;

namespace ClipRay.App.Infrastructure;

public static class ApplicationIconLoader
{
    public static Icon Load(string path) => new(path);

    public static Icon LoadOrDefault(string path)
    {
        try
        {
            return Load(path);
        }
        catch (FileNotFoundException)
        {
            return (Icon)SystemIcons.Application.Clone();
        }
        catch (DirectoryNotFoundException)
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}

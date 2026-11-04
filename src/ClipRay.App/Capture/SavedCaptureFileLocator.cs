using System.IO;
using System.Runtime.InteropServices;

namespace ClipRay.App.Capture;

internal enum SavedCaptureLocationResult { Selected, FileUnavailable, ShellUnavailable }

/// <summary>Selects a saved local file without executing it or composing a shell command.</summary>
internal static class SavedCaptureFileLocator
{
    // Call on a worker. The injectable selector lets tests verify dispatch without opening Explorer.
    internal static SavedCaptureLocationResult TrySelect(string path, Func<string, bool>? select = null)
    {
        if (!SavedMediaFile.IsRegularLocalFile(path)) return SavedCaptureLocationResult.FileUnavailable;
        try
        {
            var fullPath = Path.GetFullPath(path);
            return (select ?? SelectInWindowsShell)(fullPath)
                ? SavedCaptureLocationResult.Selected : SavedCaptureLocationResult.ShellUnavailable;
        }
        catch { return SavedCaptureLocationResult.ShellUnavailable; }
    }

    private static bool SelectInWindowsShell(string fullPath)
    {
        // Task.Run workers are MTA. Balance a successful initialization, including S_FALSE;
        // an already initialized STA can also use the Shell APIs without being reinitialized.
        var initialized = CoInitializeEx(IntPtr.Zero, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) return false;
        IntPtr item = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(fullPath, IntPtr.Zero, out item, 0, out _) < 0 || item == IntPtr.Zero)
                return false;
            // cidl=0 accepts an absolute file PIDL and selects it in its parent folder.
            return SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0) >= 0;
        }
        finally
        {
            if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
            if (initialized >= 0) CoUninitialize();
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);
}

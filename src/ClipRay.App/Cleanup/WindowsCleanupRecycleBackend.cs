using System.IO;
using System.Runtime.InteropServices;

namespace ClipRay.App.Cleanup;

public interface ICleanupRecycleBackend { void RecycleFile(string path); }
internal interface IRecycleOperation : IDisposable
{
    int SetFlags(uint flags);
    int Delete(string path);
    int Perform();
    int AnyAborted(out bool aborted);
}

/// <summary>Windows 8+ recycle-only Shell operation. No permanent-delete or elevation fallback.</summary>
public sealed class WindowsCleanupRecycleBackend : ICleanupRecycleBackend
{
    internal const uint RecycleFlags = 0x00080000 | 0x00100000 | 0x00000004 | 0x00000400;
    private readonly Func<IRecycleOperation> _factory;
    public WindowsCleanupRecycleBackend() : this(() => new ShellRecycleOperation()) { }
    internal WindowsCleanupRecycleBackend(Func<IRecycleOperation> factory) => _factory=factory;
    public void RecycleFile(string path)=>Recycle(path,isDirectory:false);
    internal void RecycleDirectory(string path)=>Recycle(path,isDirectory:true);
    private void Recycle(string path,bool isDirectory)
    {
        if (!Path.IsPathFullyQualified(path) || !(isDirectory?Directory.Exists(path):File.Exists(path)) || !CompletedOutputStore.IsOrdinaryPath(path))
            throw new IOException("unsafe-recycle-path");
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed || !OperatingSystem.IsWindowsVersionAtLeast(6,2))
            throw new IOException("recycle-platform-unavailable");
        using var operation=_factory();
        Check(operation.SetFlags(RecycleFlags));
        Check(operation.Delete(path));
        Check(operation.Perform());
        Check(operation.AnyAborted(out var aborted));
        if(aborted) throw new IOException("recycle-aborted");
    }
    private static void Check(int result){if(result<0)Marshal.ThrowExceptionForHR(result);}
}

internal sealed class ShellRecycleOperation : IRecycleOperation
{
    private readonly IFileOperation _operation=(IFileOperation)Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"),throwOnError:true)!)!;
    private IShellItem? _item;
    public int SetFlags(uint flags)=>_operation.SetOperationFlags(flags);
    public int Delete(string path)
    {
        var iid=typeof(IShellItem).GUID;
        var result=SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out var item);
        if(result<0)return result;
        _item=item;
        return _operation.DeleteItem(item,IntPtr.Zero);
    }
    public int Perform()=>_operation.PerformOperations();
    public int AnyAborted(out bool aborted)=>_operation.GetAnyOperationsAborted(out aborted);
    public void Dispose()
    {
        if(_item is not null)Marshal.FinalReleaseComObject(_item);
        Marshal.FinalReleaseComObject(_operation);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,PreserveSig=true)]
    private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)]string path,IntPtr bindContext,ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)]out IShellItem item);
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }
    [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        // Preserve native SDK declaration order: all 20 slots after IUnknown, including unused methods.
        [PreserveSig]int Advise(IntPtr sink,out uint cookie);
        [PreserveSig]int Unadvise(uint cookie);
        [PreserveSig]int SetOperationFlags(uint flags);
        [PreserveSig]int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)]string message);
        [PreserveSig]int SetProgressDialog(IntPtr dialog);
        [PreserveSig]int SetProperties(IntPtr properties);
        [PreserveSig]int SetOwnerWindow(IntPtr window);
        [PreserveSig]int ApplyPropertiesToItem([MarshalAs(UnmanagedType.Interface)]IShellItem item);
        [PreserveSig]int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig]int RenameItem([MarshalAs(UnmanagedType.Interface)]IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,IntPtr sink);
        [PreserveSig]int RenameItems(IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig]int MoveItem([MarshalAs(UnmanagedType.Interface)]IShellItem item,[MarshalAs(UnmanagedType.Interface)]IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name,IntPtr sink);
        [PreserveSig]int MoveItems(IntPtr items,[MarshalAs(UnmanagedType.Interface)]IShellItem destination);
        [PreserveSig]int CopyItem([MarshalAs(UnmanagedType.Interface)]IShellItem item,[MarshalAs(UnmanagedType.Interface)]IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string? name,IntPtr sink);
        [PreserveSig]int CopyItems(IntPtr items,[MarshalAs(UnmanagedType.Interface)]IShellItem destination);
        [PreserveSig]int DeleteItem([MarshalAs(UnmanagedType.Interface)]IShellItem item,IntPtr sink);
        [PreserveSig]int DeleteItems(IntPtr items);
        [PreserveSig]int NewItem([MarshalAs(UnmanagedType.Interface)]IShellItem destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string? template,IntPtr sink);
        [PreserveSig]int PerformOperations();
        [PreserveSig]int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)]out bool aborted);
    }
}

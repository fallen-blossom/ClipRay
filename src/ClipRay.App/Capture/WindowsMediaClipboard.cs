using System.Runtime.InteropServices;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using Windows.ApplicationModel.DataTransfer;

namespace ClipRay.App.Capture;

/// <summary>Publish the existing OLE formats with explicit Windows history/privacy options.</summary>
internal static class WindowsMediaClipboard
{
    private static readonly Guid ProviderId = new("3D25F6D6-4B2A-433C-9184-7C33AD35D001");

    internal static ClipboardContentOptions CreateOptions() => new()
    {
        IsAllowedInHistory = true,
        IsRoamable = false,
    };

    internal static DataPackage CreatePackage(System.Windows.DataObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var package = new DataPackage();
        var nativePackage = WinRT.MarshalInspectable<DataPackage>.FromManaged(package);
        IntPtr provider = IntPtr.Zero;
        IntPtr nativeData = IntPtr.Zero;
        try
        {
            var iid = ProviderId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(nativePackage, in iid, out provider));
            nativeData = Marshal.GetComInterfaceForObject(data, typeof(IDataObject));
            // IDataObjectProvider derives from IUnknown: GetDataObject is slot 3,
            // SetDataObject is slot 4. The package retains its own COM reference.
            var set = Marshal.GetDelegateForFunctionPointer<SetDataObject>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(provider), 4 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(set(provider, nativeData));
            return package;
        }
        finally
        {
            if (nativeData != IntPtr.Zero) Marshal.Release(nativeData);
            if (provider != IntPtr.Zero) Marshal.Release(provider);
            WinRT.MarshalInspectable<DataPackage>.DisposeAbi(nativePackage);
            GC.KeepAlive(data);
        }
    }

    internal static void Publish(System.Windows.DataObject data) => Publish(data,
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions,
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush);

    internal static void Publish(System.Windows.DataObject data,
        Func<DataPackage, ClipboardContentOptions, bool> setContent, Action flush)
    {
        var package = CreatePackage(data);
        if (!setContent(package, CreateOptions()))
            throw new ExternalException("The clipboard is in use.", unchecked((int)0x800401D0));
        // Render all formats so the result survives closing the capture window
        // or exiting ClipRay; do not replace it with a second clipboard write.
        try { flush(); }
        catch (Exception error)
        {
            // Content is already set. Do not let a persistence failure enter the
            // publisher's busy retry and replace a subsequent copy a second time.
            throw new ClipboardPersistenceException(error);
        }
        GC.KeepAlive(data);
        GC.KeepAlive(package);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDataObject(IntPtr provider, IntPtr data);
}

internal sealed class ClipboardPersistenceException(Exception inner)
    : Exception("Clipboard content was set, but persistence could not be confirmed.", inner);

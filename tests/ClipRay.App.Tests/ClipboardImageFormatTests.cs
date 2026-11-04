using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Settings;

internal static class ClipboardImageFormatTests
{
    public static void Run()
    {
        // Exercise the real OLE data object without opening or changing any clipboard.
        // QQ can consume PNG/file drops; native image consumers also need a packed DIB.
        var pixels = new byte[] { 30, 20, 10, 255, 60, 50, 40, 255,
                                  90, 80, 70, 255, 120, 110, 100, 255 };
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        image.Freeze();
        var data = MediaClipboardDataFactory.CreateImage(image, Path.Combine(Path.GetTempPath(), "fixture.png"));
        var native = (System.Runtime.InteropServices.ComTypes.IDataObject)data;
        var request = new FORMATETC { cfFormat = 8, dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        Console.WriteLine($"Native CF_DIB query: 0x{native.QueryGetData(ref request):X8}; explicit formats: {string.Join(",", data.GetFormats(false))}");
        native.GetData(ref request, out var medium);
        try
        {
            Check(medium.tymed == TYMED.TYMED_HGLOBAL, "CF_DIB must be a native memory block");
            var pointer = GlobalLock(medium.unionmember);
            Check(pointer != IntPtr.Zero, "DIB block could not be locked");
            try
            {
                Check(Marshal.ReadInt32(pointer) == 40, "Expected BITMAPINFOHEADER, without a BMP file header or .NET serialization");
                Check(Marshal.ReadInt32(pointer, 4) == 2 && Marshal.ReadInt32(pointer, 8) == 2, "DIB dimensions changed");
                Check(Marshal.ReadInt16(pointer, 14) == 24 && Marshal.ReadInt32(pointer, 16) == 0, "Expected uncompressed 24-bit DIB");
                var actual = new byte[16];
                Marshal.Copy(pointer + 40, actual, 0, actual.Length);
                Check(actual.SequenceEqual(new byte[] { 90, 80, 70, 120, 110, 100, 0, 0, 30, 20, 10, 60, 50, 40, 0, 0 }),
                    "DIB row order, pixel channels or aligned padding changed");
            }
            finally { GlobalUnlock(medium.unionmember); }
        }
        finally { ReleaseStgMedium(ref medium); }
        Check(data.GetImage()!.PixelWidth == 2 && data.GetFileDropList().Count == 1, "Existing image/file payload lost");
        CheckNativeBitmap(data, pixels);
        CheckBackgroundPreparedBitmap(image, pixels);
        CheckWindowsPackage(data);
        CheckWindowsPackage(MediaClipboardDataFactory.CreateFile(Path.Combine(Path.GetTempPath(), "fixture.mp4")));
        CheckPublishFailurePhases(data);
        Check(new AppSettings().CopyToClipboard && AppSettings.CreateDefault(Path.GetTempPath()).CopyToClipboard,
            "Automatic copying must default to enabled");
        Check(!(new AppSettings { CopyToClipboard = false }).WithMissingDefaults(Path.GetTempPath()).CopyToClipboard,
            "An explicit disabled choice must survive default reconciliation");
        var transparent = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 99, 88, 77, 0 }, 4);
        var matte = MediaClipboardDataFactory.EncodeDib(transparent);
        Check(matte.Length == 44 && matte.AsSpan(40, 4).SequenceEqual(new byte[] { 255, 255, 255, 0 }),
            "Transparent DIB pixels must have a white matte and aligned zero padding");
        var halfAlpha = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 100, 200, 128 }, 4);
        Check(MediaClipboardDataFactory.EncodeDib(halfAlpha).AsSpan(40, 3).SequenceEqual(new byte[] { 127, 177, 227 }),
            "Partial transparency changed the matte colors");
        var settingsRoot = Path.Combine(Path.GetTempPath(), "clipray-copy-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settingsRoot);
        try
        {
            var store = new SettingsStore(Path.Combine(settingsRoot, "settings.json"), settingsRoot);
            Check(store.Load().CopyToClipboard, "New installation must copy by default");
            store.Save(store.Load() with { CopyToClipboard = false });
            Check(!new SettingsStore(Path.Combine(settingsRoot, "settings.json"), settingsRoot).Load().CopyToClipboard,
                "Disabled copy must persist through reopening");
            store.Save(store.Load() with { CopyToClipboard = true });
            Check(store.Load().CopyToClipboard, "Copy can be re-enabled");
        }
        finally { Directory.Delete(settingsRoot, recursive: true); }
        Console.WriteLine("PASS: native CF_BITMAP/CF_DIB including background decoding, Windows package format preservation/history/privacy options and default/disabled settings (no system clipboard).");
    }

    private static void CheckBackgroundPreparedBitmap(BitmapSource image, byte[] pixels)
    {
        var path = Path.Combine(Path.GetTempPath(), "clipray-background-bitmap-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            File.WriteAllBytes(path, encoded.ToArray());
            var prepared = Task.Run(() => SavedMediaClipboard.TryPreparePng(path, out var result) ? result : null).GetAwaiter().GetResult();
            Check(prepared?.Image?.IsFrozen == true, "Background preparation must return a frozen image.");
            CheckNativeBitmap(prepared!.CreateData(), pixels);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void CheckNativeBitmap(System.Windows.DataObject data, byte[] pixels)
    {
        var native = (System.Runtime.InteropServices.ComTypes.IDataObject)data;
        var request = new FORMATETC { cfFormat = 2, dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1, tymed = TYMED.TYMED_GDI };
        native.GetData(ref request, out var medium);
        try
        {
            Check(medium.tymed == TYMED.TYMED_GDI && medium.unionmember != IntPtr.Zero,
                "CF_BITMAP must render a real HBITMAP, including after background decoding.");
            Check(GetObject(medium.unionmember, Marshal.SizeOf<NativeBitmap>(), out var bitmap) != 0
                && bitmap.Width == 2 && bitmap.Height == 2, "Native clipboard bitmap dimensions changed.");
            var header = new BitmapInfoHeader { Size = 40, Width = 2, Height = -2, Planes = 1, BitCount = 32 };
            var rendered = new byte[pixels.Length];
            var dc = CreateCompatibleDC(IntPtr.Zero);
            Check(dc != IntPtr.Zero, "Could not allocate a bitmap readback DC.");
            try
            {
                Check(GetDIBits(dc, medium.unionmember, 0, 2, rendered, ref header, 0) == 2,
                    "Native clipboard bitmap could not be decoded.");
                for (var offset = 0; offset < pixels.Length; offset += 4)
                    Check(rendered.AsSpan(offset, 3).SequenceEqual(pixels.AsSpan(offset, 3)),
                        "Native clipboard bitmap changed image colors or row order.");
            }
            finally { DeleteDC(dc); }
        }
        finally { ReleaseStgMedium(ref medium); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }

    private static void CheckWindowsPackage(System.Windows.DataObject data)
    {
        var options = WindowsMediaClipboard.CreateOptions();
        Check(options.IsAllowedInHistory && !options.IsRoamable, "History must be explicitly allowed and cloud sync disabled.");
        var package = WindowsMediaClipboard.CreatePackage(data);
        Check(package.GetView().Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap) == (data.GetImage() is not null),
            "The Windows package changed the image/file-only representation.");
        var pointer = WinRT.MarshalInspectable<Windows.ApplicationModel.DataTransfer.DataPackage>.FromManaged(package);
        var iid = new Guid("3D25F6D6-4B2A-433C-9184-7C33AD35D001");
        IntPtr provider = IntPtr.Zero, returned = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(pointer, in iid, out provider));
            var get = Marshal.GetDelegateForFunctionPointer<GetDataObject>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(provider), 3 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(get(provider, out returned));
            var native = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(returned);
            if (data.GetImage() is not null)
            {
                var png = data.GetData(MediaClipboardDataFactory.PngFormat) as MemoryStream ?? throw new Exception("PNG stream missing");
                Check(ReadGlobal(native, MediaClipboardDataFactory.PngFormat).SequenceEqual(png.ToArray()), "Windows package changed the PNG bytes/alpha.");
                var dib = data.GetData(System.Windows.DataFormats.Dib, false) as MemoryStream ?? throw new Exception("DIB stream missing");
                Check(ReadGlobal(native, System.Windows.DataFormats.Dib).SequenceEqual(dib.ToArray()), "Windows package changed native DIB bytes.");
            }
            Check(ReadGlobal(native, MediaClipboardDataFactory.HistoryFormat).SequenceEqual(BitConverter.GetBytes(1)), "History DWORD changed.");
            Check(ReadGlobal(native, MediaClipboardDataFactory.CloudFormat).SequenceEqual(BitConverter.GetBytes(0)), "Cloud DWORD changed.");
            Check(ReadGlobal(native, "Preferred DropEffect").SequenceEqual(BitConverter.GetBytes(1)), "File copy drop effect changed.");
            Check(ReadGlobal(native, System.Windows.DataFormats.FileDrop).SequenceEqual(ReadGlobal((System.Runtime.InteropServices.ComTypes.IDataObject)data, System.Windows.DataFormats.FileDrop)), "Windows package lost the file drop.");
        }
        finally
        {
            if (returned != IntPtr.Zero) Marshal.Release(returned);
            if (provider != IntPtr.Zero) Marshal.Release(provider);
            WinRT.MarshalInspectable<Windows.ApplicationModel.DataTransfer.DataPackage>.DisposeAbi(pointer);
            GC.KeepAlive(package);
        }
    }

    private static void CheckPublishFailurePhases(System.Windows.DataObject data)
    {
        var writes = 0; var flushes = 0;
        try
        {
            WindowsMediaClipboard.Publish(data, (_, options) => { writes++; Check(options.IsAllowedInHistory && !options.IsRoamable, "Wrong publication options"); return false; }, () => flushes++);
            throw new Exception("Busy rejection was silently accepted");
        }
        catch (ExternalException) { Check(writes == 1 && flushes == 0, "Busy rejection flushed someone else's clipboard"); }
        writes = 0;
        try
        {
            WindowsMediaClipboard.Publish(data, (_, _) => { writes++; return true; }, () => throw new ExternalException("synthetic flush failure"));
            throw new Exception("Persistence failure was silently accepted");
        }
        catch (ClipboardPersistenceException error)
        {
            Check(error.InnerException is ExternalException && writes == 1, "Persistence failure must retain its cause without a second write");
            Check(!typeof(ExternalException).IsAssignableFrom(error.GetType()), "A persistence failure must not enter the busy retry");
        }
    }

    private static byte[] ReadGlobal(System.Runtime.InteropServices.ComTypes.IDataObject data, string format)
    {
        var request = new FORMATETC { cfFormat = (short)System.Windows.DataFormats.GetDataFormat(format).Id,
            dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        data.GetData(ref request, out var medium);
        try
        {
            Check(medium.tymed == TYMED.TYMED_HGLOBAL, "Expected raw native bytes for " + format);
            var pointer = GlobalLock(medium.unionmember);
            Check(pointer != IntPtr.Zero, "Could not lock " + format);
            try { var bytes = new byte[checked((int)GlobalSize(medium.unionmember))]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
            finally { GlobalUnlock(medium.unionmember); }
        }
        finally { ReleaseStgMedium(ref medium); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDataObject(IntPtr provider, out IntPtr data);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr bitmap, int count, out NativeBitmap value);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
        [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
}

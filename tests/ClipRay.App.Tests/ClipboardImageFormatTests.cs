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
        Console.WriteLine("PASS: native CF_DIB bytes, bitmap/file compatibility and default/disabled settings (no system clipboard).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}

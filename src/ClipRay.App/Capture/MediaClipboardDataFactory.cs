using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClipRay.App.Capture;

/// <summary>Builds the same local media payload for a first copy and a later copy.</summary>
public static class MediaClipboardDataFactory
{
    public const string PngFormat = "PNG";
    public const string HistoryFormat = "CanIncludeInClipboardHistory";
    public const string CloudFormat = "CanUploadToCloudClipboard";

    public static System.Windows.DataObject CreateImage(BitmapSource image, string path, byte[]? encodedPng = null, byte[]? encodedDib = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var data = new System.Windows.DataObject();
        if (encodedPng is null)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var encoded = new MemoryStream();
            encoder.Save(encoded);
            encodedPng = encoded.ToArray();
        }
        data.SetData(PngFormat, new MemoryStream(encodedPng, writable: false));
        // A BitmapSource advertises CF_BITMAP, but WPF does not render CF_DIB from
        // it. Supply a packed DIB explicitly for native editors and image history.
        data.SetData(System.Windows.DataFormats.Dib,
            new MemoryStream(encodedDib ?? EncodeDib(image), writable: false), autoConvert: false);
        data.SetImage(image);
        AddFile(data, path);
        return data;
    }

    internal static byte[] EncodeDib(BitmapSource image)
    {
        // PNG retains alpha. The broadly supported 24-bit DIB uses a white matte
        // for transparent pixels, rather than leaking their hidden RGB values.
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var inputStride = checked(width * 4);
        var stride = checked((checked(width * 3) + 3) & ~3);
        var pixelBytes = checked(stride * height);
        var dib = new byte[checked(40 + pixelBytes)];
        using (var header = new BinaryWriter(new MemoryStream(dib, writable: true)))
        {
            header.Write(40);
            header.Write(width);
            header.Write(height); // bottom-up DIB
            header.Write((short)1);
            header.Write((short)24);
            header.Write(0); // BI_RGB
            header.Write(pixelBytes);
            header.Write(0); header.Write(0); header.Write(0); header.Write(0);
        }
        var normalized = image.Format == System.Windows.Media.PixelFormats.Bgra32 ? image
            : new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var row = new byte[inputStride];
        for (var y = 0; y < height; y++)
        {
            normalized.CopyPixels(new Int32Rect(0, y, width, 1), row, inputStride, 0);
            var offset = 40 + (height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var input = x * 4;
                var alpha = row[input + 3];
                for (var channel = 0; channel < 3; channel++)
                    dib[offset + x * 3 + channel] = (byte)((row[input + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
            }
        }
        return dib;
    }

    public static System.Windows.DataObject CreateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var data = new System.Windows.DataObject();
        AddFile(data, path);
        return data;
    }

    private static void AddFile(System.Windows.DataObject data, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        data.SetFileDropList(new StringCollection { Path.GetFullPath(path) });
        // A DWORD must be raw bytes, rather than a .NET serialized integer.
        data.SetData("Preferred DropEffect", Dword(1));
        data.SetData(HistoryFormat, Dword(1));
        data.SetData(CloudFormat, Dword(0));
    }

    private static MemoryStream Dword(int value) => new(BitConverter.GetBytes(value), writable: false);
}

public static class MediaClipboardPublisher
{
    public static void Publish(System.Windows.DataObject data)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var image = data.GetData(System.Windows.DataFormats.Bitmap) as BitmapSource;
        var pngBytes = (data.GetData(MediaClipboardDataFactory.PngFormat) as MemoryStream)?.Length;
        var dibBytes = (data.GetData(System.Windows.DataFormats.Dib, autoConvert: false) as MemoryStream)?.Length;
        MediaDeliveryTrace.Record("clipboard_started", operationId, new { Width = image?.PixelWidth, Height = image?.PixelHeight,
            PngBytes = pngBytes, DibBytes = dibBytes, FileCount = data.GetFileDropList()?.Count ?? 0, HistoryRequested = true, CloudUploadRequested = false });
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // Flush OLE data so the media survives the originating window/process.
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                MediaDeliveryTrace.Record("clipboard_completed", operationId, new { Attempts = attempt + 1, ElapsedMs = timer.Elapsed.TotalMilliseconds });
                return;
            }
            catch (ExternalException) when (attempt < 4)
            {
                Thread.Sleep(40 * (attempt + 1));
            }
            catch (Exception exception)
            {
                MediaDeliveryTrace.Record("clipboard_failed", operationId, new { Attempts = attempt + 1, Type = exception.GetType().Name, exception.HResult,
                    ElapsedMs = timer.Elapsed.TotalMilliseconds });
                throw;
            }
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClipRay.App.Capture;

/// <summary>A file-backed copy prepared off the UI thread; its image is frozen.</summary>
public sealed class PreparedMediaCopy
{
    private readonly byte[]? _png;
    private readonly byte[]? _dib;
    private readonly SavedMediaFileStamp _stamp;

    internal PreparedMediaCopy(string path, RecentCaptureKind kind, SavedMediaFileStamp stamp,
        BitmapSource? image = null, byte[]? png = null, byte[]? dib = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        Kind = kind;
        _stamp = stamp;
        Image = image;
        _png = png;
        _dib = dib;
    }

    public string Path { get; }
    public RecentCaptureKind Kind { get; }
    public BitmapSource? Image { get; }

    internal bool MatchesCurrentFile() => SavedMediaFile.TryGetStamp(Path, out var current) && current == _stamp;
    internal System.Windows.DataObject CreateData() => Image is not null
        ? MediaClipboardDataFactory.CreateImage(Image, Path, _png, _dib)
        : MediaClipboardDataFactory.CreateFile(Path);
}

public static class SavedMediaClipboard
{
    public static bool TryCopyPng(string path, Action<System.Windows.DataObject>? publish = null) =>
        TryPreparePng(path, out var prepared) && TryPublish(prepared!, publish);

    public static async Task<bool> TryCopyPngAsync(string path, Func<bool> canPublish,
        Action<System.Windows.DataObject>? publish = null)
    {
        ArgumentNullException.ThrowIfNull(canPublish);
        var prepared = await Task.Run(() => TryPreparePng(path, out var copy) ? copy : null);
        return prepared is not null && canPublish() && TryPublish(prepared, publish);
    }

    public static bool TryPreparePng(string path, out PreparedMediaCopy? prepared,
        RecentCaptureKind kind = RecentCaptureKind.Screenshot)
    {
        prepared = null;
        if (kind == RecentCaptureKind.Recording || !SavedMediaFile.TryGetStamp(path, out var before)) return false;
        if (!SavedMediaFile.TryReadPng(path, out var source, out var png)) return false;
        if (!SavedMediaFile.TryGetStamp(path, out var after) || before != after) return false;
        try
        {
            var dib = MediaClipboardDataFactory.EncodeDib(source!);
            if (!SavedMediaFile.TryGetStamp(path, out var current) || current != after) return false;
            prepared = new PreparedMediaCopy(path, kind, after, source, png, dib);
            return true;
        }
        catch { return false; }
    }

    public static bool TryPublish(PreparedMediaCopy prepared, Action<System.Windows.DataObject>? publish = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        try
        {
            // Recheck the local file/version without reading or decoding media again.
            if (!prepared.MatchesCurrentFile()) return false;
            var data = prepared.CreateData();
            if (!prepared.MatchesCurrentFile()) return false;
            (publish ?? MediaClipboardPublisher.Publish)(data);
            return true;
        }
        catch { return false; }
    }
}

internal readonly record struct SavedMediaFileStamp(long Length, DateTime LastWriteTimeUtc);

internal static class SavedMediaFile
{
    internal static bool TryGetStamp(string path, out SavedMediaFileStamp stamp)
    {
        stamp = default;
        if (!IsRegularLocalFile(path)) return false;
        try
        {
            var info = new FileInfo(path);
            stamp = new(info.Length, info.LastWriteTimeUtc);
            return true;
        }
        catch { return false; }
    }

    internal static bool TryResolveDirectory(string path, out string fullPath)
    {
        fullPath = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
                || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                return false;
            var resolved = Path.GetFullPath(path);
            var driveRoot = Path.GetPathRoot(resolved);
            if (driveRoot is null || new DriveInfo(driveRoot).DriveType == DriveType.Network) return false;
            var directory = new DirectoryInfo(resolved);
            if (!directory.Exists) return false;
            for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
                if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            fullPath = Path.TrimEndingDirectorySeparator(resolved);
            return true;
        }
        catch { return false; }
    }

    internal static bool IsRegularLocalFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            var file = new FileInfo(Path.GetFullPath(path));
            return file.Exists && file.Length > 0
                && (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0
                && TryResolveDirectory(file.DirectoryName!, out _);
        }
        catch { return false; }
    }

    internal static bool TryReadPng(string path, out BitmapSource? source, out byte[]? png)
    {
        source = null;
        png = null;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !IsRegularLocalFile(path)) return false;
        try
        {
            // Deny concurrent writing/deletion while reading; never retain a file lock.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var encoded = new MemoryStream();
            file.CopyTo(encoded);
            var bytes = encoded.ToArray();
            encoded.Position = 0;
            var decoder = new PngBitmapDecoder(encoded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0) return false;
            // Force pixel decoding as well as header parsing, including truncated IDAT data.
            var pixel = new byte[Math.Max(1, (frame.Format.BitsPerPixel + 7) / 8)];
            frame.CopyPixels(new Int32Rect(frame.PixelWidth - 1, frame.PixelHeight - 1, 1, 1), pixel, pixel.Length, 0);
            frame.Freeze();
            if (!IsRegularLocalFile(path)) return false;
            source = frame;
            png = bytes;
            return true;
        }
        catch { return false; }
    }
}

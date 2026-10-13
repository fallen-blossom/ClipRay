using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace ClipRay.App.Scrolling;

public sealed record ScrollCaptureSegment(string FileName, int Width, int Height, int OverlapRows);
public sealed record ScrollCaptureManifest(int SchemaVersion, string SessionId, DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc, string Status, ScrollCaptureStopReason? StopReason, IReadOnlyList<ScrollCaptureSegment> Segments);
public sealed record ScrollCaptureRecoveryCandidate(string Directory, DateTimeOffset? CreatedUtc, int Width,
    int Height, int FrameCount, bool CanRecover, string Status);

/// <summary>Only this local manifest defines the indexed subset. Extra files are retained, never guessed.</summary>
public sealed class ScrollCaptureRecovery
{
    public const int MaximumFrames = 1600;
    public const long MaximumPixels = 64_000_000;
    public const int MaximumHeight = 65_000;
    private readonly string _root;
    public ScrollCaptureRecovery(string root)
    {
        if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Recovery requires an absolute local root.", nameof(root));
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
    }

    public IReadOnlyList<ScrollCaptureRecoveryCandidate> List(IReadOnlySet<string>? active = null)
    {
        if (!Directory.Exists(_root) || !OrdinaryPath(_root)) return [];
        var results = new List<ScrollCaptureRecoveryCandidate>();
        foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.TopDirectoryOnly))
        {
            if (active?.Contains(Path.GetFullPath(directory)) == true)
            {
                results.Add(new(directory, null, 0, 0, 0, false, "active"));
                continue;
            }
            try
            {
                var manifest = ReadValidated(directory);
                var size = CompositeSize(manifest);
                var extras = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Any(path => Path.GetFileName(path) != ScrollCaptureTempStore.RecoveryMarker
                        && !manifest.Segments.Any(s => s.FileName == Path.GetFileName(path)));
                if (manifest.Status == "delivered" && !extras) continue;
                results.Add(new(directory, manifest.CreatedUtc, size.Width, size.Height, manifest.Segments.Count,
                    manifest.Status != "delivered", manifest.Status == "delivered" ? "unsupported" : extras ? "indexed-subset" : "recoverable"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or OverflowException)
            {
                results.Add(new(directory, null, 0, 0, 0, false, error is UnsupportedRecoveryException ? "unsupported" : "invalid"));
            }
        }
        return results.OrderByDescending(c => c.CreatedUtc).ThenBy(c => c.Directory, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public ScrollCaptureManifest ReadValidated(string directory)
    {
        ValidateDirectory(directory);
        var marker = Path.Combine(directory, ScrollCaptureTempStore.RecoveryMarker);
        if (!File.Exists(marker) || !OrdinaryPath(marker)) throw new UnsupportedRecoveryException();
        var info = new FileInfo(marker);
        if (info.Length is <= 0 or > 512_000) throw new InvalidOperationException("Invalid recovery manifest size.");
        using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
        var manifest = JsonSerializer.Deserialize<ScrollCaptureManifest>(stream) ?? throw new InvalidOperationException("Empty recovery manifest.");
        if (manifest.SchemaVersion != 2) throw new UnsupportedRecoveryException();
        if (manifest.SessionId != Path.GetFileName(directory) || !Guid.TryParseExact(manifest.SessionId, "N", out _)
            || manifest.Status is not ("capturing" or "recoverable" or "delivered")
            || manifest.Segments is null || manifest.Segments.Count is < 1 or > MaximumFrames || manifest.Segments.Any(s => s is null)
            || manifest.CreatedUtc == default || manifest.UpdatedUtc < manifest.CreatedUtc
            || (manifest.StopReason is {} reason && !Enum.IsDefined(reason)))
            throw new InvalidOperationException("Invalid recovery manifest fields.");
        var size = CompositeSize(manifest);
        if (size.Height > MaximumHeight || (long)size.Width * size.Height > MaximumPixels)
            throw new InvalidOperationException("Recovery exceeds the image budget.");
        // Validate every name, header and declared geometry before opening any image decoder.
        for (var index = 0; index < manifest.Segments.Count; index++)
        {
            var segment = manifest.Segments[index];
            if (segment.FileName != $"{index:D3}.png") throw new InvalidOperationException("Invalid segment filename.");
            var path = Path.Combine(directory, segment.FileName);
            if (!File.Exists(path) || !OrdinaryPath(path)) throw new InvalidOperationException("Segment is not an ordinary local file.");
            using var png = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ValidatePngHeader(png,segment);
        }
        return manifest;
    }

    public Bitmap CreateComposite(string directory)
    {
        var manifest = ReadValidated(directory);
        var size = CompositeSize(manifest);
        using var first = LoadSegment(directory, manifest.Segments[0]);
        var composite = new Bitmap(size.Width, size.Height, first.PixelFormat);
        try
        {
            FrameStitcher.CopyRows(first, 0, composite, 0, first.Height);
            var destinationY = first.Height;
            foreach (var segment in manifest.Segments.Skip(1))
            {
                using var frame = LoadSegment(directory, segment);
                if (frame.PixelFormat != first.PixelFormat) throw new InvalidOperationException("Segment pixel formats differ.");
                var rows = segment.Height - segment.OverlapRows;
                FrameStitcher.CopyRows(frame, segment.OverlapRows, composite, destinationY, rows);
                destinationY += rows;
            }
            return composite;
        }
        catch { composite.Dispose(); throw; }
    }

    public void MarkDelivered(string directory)
    {
        var manifest = ReadValidated(directory);
        WriteAtomic(directory, manifest with { Status = "delivered", UpdatedUtc = DateTimeOffset.UtcNow });
    }

    private void ValidateDirectory(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("Invalid recovery directory.");
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(full), _root, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(full), "N", out _) || !Directory.Exists(full) || !OrdinaryPath(full))
            throw new InvalidOperationException("Recovery directory escaped its ordinary local root.");
    }
    private static Bitmap LoadSegment(string directory, ScrollCaptureSegment segment)
    {
        var path = Path.Combine(directory, segment.FileName);
        if (!OrdinaryPath(path)) throw new InvalidOperationException("Segment changed before decoding.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // Recheck the header on this locked decode stream; a changed file cannot
        // reach the decoder with dimensions that escaped the validated budget.
        ValidatePngHeader(stream,segment);
        stream.Position=0;
        using var decoded = new Bitmap(stream);
        if (decoded.Width != segment.Width || decoded.Height != segment.Height)
            throw new InvalidOperationException("Decoded segment geometry changed.");
        return decoded.Clone(new Rectangle(0, 0, decoded.Width, decoded.Height), decoded.PixelFormat);
    }
    private static void ValidatePngHeader(FileStream png,ScrollCaptureSegment segment)
    {
        if (png.Length < 24 || png.Length > MaximumPixels*4+1_000_000)
            throw new InvalidOperationException("Segment PNG exceeds its file budget.");
        Span<byte> header=stackalloc byte[24];png.ReadExactly(header);
        if (!header[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
            || BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13 || !header[12..16].SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(header[16..20]) != segment.Width
            || BinaryPrimitives.ReadUInt32BigEndian(header[20..24]) != segment.Height)
            throw new InvalidOperationException("Segment PNG header disagrees with manifest.");
    }
    private static Size CompositeSize(ScrollCaptureManifest manifest)
    {
        var width = manifest.Segments[0].Width;
        var height = 0;
        for (var index=0; index<manifest.Segments.Count; index++)
        {
            var segment = manifest.Segments[index];
            if (segment.Width != width || segment.Width is < 1 or > MaximumHeight || segment.Height is < 1 or > MaximumHeight
                || (long)segment.Width*segment.Height > MaximumPixels
                || segment.OverlapRows < 0 || segment.OverlapRows >= segment.Height
                || (index == 0 ? segment.OverlapRows != 0 : segment.OverlapRows == 0))
                throw new InvalidOperationException("Invalid segment dimensions or overlap.");
            height = checked(height + segment.Height - segment.OverlapRows);
        }
        return new(width, height);
    }
    internal static bool OrdinaryPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }
    internal static void WriteAtomic(string directory, ScrollCaptureManifest manifest)
    {
        if (!OrdinaryPath(directory)) throw new InvalidOperationException("Recovery directory is redirected.");
        var marker = Path.Combine(directory, ScrollCaptureTempStore.RecoveryMarker);
        if (File.Exists(marker) && !OrdinaryPath(marker)) throw new InvalidOperationException("Recovery marker is redirected.");
        var pending = marker + ".tmp-" + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, manifest);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, marker, overwrite: true);
    }
    private sealed class UnsupportedRecoveryException : InvalidOperationException { }
}

internal sealed class ScrollCapturePersistence(string directory, Action<string, ScrollCaptureManifest>? commit = null)
{
    private ScrollCaptureManifest? _manifest;
    private readonly DateTimeOffset _created = DateTimeOffset.UtcNow;
    public void Persist(Bitmap bitmap, int overlap)
    {
        if (!ScrollCaptureRecovery.OrdinaryPath(directory)) throw new InvalidOperationException("Capture directory is redirected.");
        var segments = _manifest?.Segments ?? [];
        var height = segments.Sum(s => s.Height - s.OverlapRows) + bitmap.Height - overlap;
        if (segments.Count >= ScrollCaptureRecovery.MaximumFrames || bitmap.Width is < 1 or > ScrollCaptureRecovery.MaximumHeight
            || bitmap.Height is < 1 or > ScrollCaptureRecovery.MaximumHeight || overlap < 0 || overlap >= bitmap.Height
            || (segments.Count == 0 ? overlap != 0 : overlap == 0 || bitmap.Width != segments[0].Width)
            || height > ScrollCaptureRecovery.MaximumHeight || (long)bitmap.Width * height > ScrollCaptureRecovery.MaximumPixels)
            throw new InvalidOperationException("Capture segment exceeds the recovery geometry budget.");
        var name = $"{segments.Count:D3}.png";
        var path = Path.Combine(directory, name);
        var pending = path + ".tmp-" + Guid.NewGuid().ToString("N");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            bitmap.Save(stream, ImageFormat.Png);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, path, overwrite: false);
        Commit(new(2, Path.GetFileName(directory), _created, DateTimeOffset.UtcNow, "capturing", null,
            segments.Append(new ScrollCaptureSegment(name, bitmap.Width, bitmap.Height, overlap)).ToArray()));
    }
    public void Finish(ScrollCaptureStopReason reason)
    {
        if (_manifest is {} manifest) Commit(manifest with { Status="recoverable", StopReason=reason, UpdatedUtc=DateTimeOffset.UtcNow });
    }
    private void Commit(ScrollCaptureManifest manifest)
    {
        (commit ?? ScrollCaptureRecovery.WriteAtomic)(directory, manifest);
        _manifest = manifest;
    }
}

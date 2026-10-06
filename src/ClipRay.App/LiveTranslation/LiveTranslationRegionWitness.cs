using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

// Exact local pixel witnesses: a changing region cannot commit an older translation.
internal sealed class LiveTranslationRegionWitness
{
    private sealed record Entry(InPlaceLayoutItem Item, Rectangle Bounds, byte[] Hash, LiveTranslationPixelSnapshot Source);
    private readonly Entry[] _entries;
    private LiveTranslationRegionWitness(Entry[] entries) => _entries = entries;
    public static LiveTranslationRegionWitness Empty { get; } = new([]);
    public IReadOnlyList<InPlaceLayoutItem> Items => _entries.Select(entry => entry.Item).ToArray();
    public int Count => _entries.Length;

    public LiveTranslationRegionWitness RetainItems(Func<InPlaceLayoutItem, bool> keep)
    {
        var retained = _entries.Where(entry => keep(entry.Item)).ToArray();
        return retained.Length == _entries.Length ? this : new(retained);
    }

    public static LiveTranslationRegionWitness Create(LiveTranslationPixelSnapshot source,
        IReadOnlyList<InPlaceLayoutItem> items)
    {
        var entries = new List<Entry>();
        foreach (var item in items)
        {
            var region = item.SourceRegion?.Bounds ?? item.SourceBlock.Bounds;
            var left = Math.Min(item.Bounds.Left, region.Left);
            var top = Math.Min(item.Bounds.Top, region.Top);
            var right = Math.Max(item.Bounds.Right, region.Right);
            var bottom = Math.Max(item.Bounds.Bottom, region.Bottom);
            var angle = (item.TextLayout?.RotationDegrees ?? 0) * Math.PI / 180;
            if (Math.Abs(angle) > 0.00001)
            {
                var width = Math.Abs(item.Bounds.Width * Math.Cos(angle)) + Math.Abs(item.Bounds.Height * Math.Sin(angle));
                var height = Math.Abs(item.Bounds.Width * Math.Sin(angle)) + Math.Abs(item.Bounds.Height * Math.Cos(angle));
                var x = item.Bounds.X + item.Bounds.Width / 2;
                var y = item.Bounds.Y + item.Bounds.Height / 2;
                left = Math.Min(left, x - width / 2); right = Math.Max(right, x + width / 2);
                top = Math.Min(top, y - height / 2); bottom = Math.Max(bottom, y + height / 2);
            }
            if (!double.IsFinite(left + top + right + bottom)) continue;
            var bounds = Rectangle.FromLTRB(
                (int)Math.Clamp(Math.Floor(left) - 1, 0, source.Width),
                (int)Math.Clamp(Math.Floor(top) - 1, 0, source.Height),
                (int)Math.Clamp(Math.Ceiling(right) + 1, 0, source.Width),
                (int)Math.Clamp(Math.Ceiling(bottom) + 1, 0, source.Height));
            if (bounds.Width > 0 && bounds.Height > 0)
                entries.Add(new(item, bounds, source.Hash(bounds), source));
        }
        return new(entries.ToArray());
    }

    public LiveTranslationRegionWitness RetainUnchanged(LiveTranslationPixelSnapshot current)
    {
        var retained = new List<Entry>();
        var changed = false;
        foreach (var entry in _entries)
        {
            if (entry.Bounds.Right > current.Width || entry.Bounds.Bottom > current.Height) { changed = true; continue; }
            var hash = current.Hash(entry.Bounds);
            if (hash.AsSpan().SequenceEqual(entry.Hash)) { retained.Add(entry); continue; }
            changed = true;
            if (entry.Source.TryBackgroundShift(current, entry.Bounds, out var shift))
            {
                var item = entry.Item with {
                    BackgroundColor = Shift(entry.Item.BackgroundColor, shift),
                    MaskedPatchPng = ShiftPatch(entry.Item.MaskedPatchPng, entry.Item.BackgroundColor, shift),
                    BackgroundPatchPng = ShiftPatch(entry.Item.BackgroundPatchPng, entry.Item.BackgroundColor, shift)
                };
                retained.Add(new(item, entry.Bounds, hash, current));
            }
        }
        return changed ? new(retained.ToArray()) : this;
    }

    public LiveTranslationRegionWitness AdvanceIncidentalChange(LiveTranslationPixelSnapshot current)
    {
        var advanced = new List<Entry>(_entries.Length);
        var changed = false;
        foreach (var entry in _entries)
        {
            if (entry.Bounds.Right > current.Width || entry.Bounds.Bottom > current.Height)
            {
                changed = true;
                continue;
            }
            var hash = current.Hash(entry.Bounds);
            if (hash.AsSpan().SequenceEqual(entry.Hash))
            {
                advanced.Add(entry);
                continue;
            }
            changed = true;
            // A caret-shaped change is not proof that a glyph stayed the same.
            // Advance only when exact OCR word evidence shows the changed pixels
            // are outside the text that this translation replaces.
            var textBounds = LiveTranslationTextEvidence.BoundsFor(entry.Item, current.Width, current.Height);
            if (textBounds.Count == 0 || textBounds.Any(bounds =>
                    entry.Source.CountChangedPixels(current, bounds) != 0))
                continue;
            advanced.Add(new(entry.Item, entry.Bounds, hash, current));
        }
        return changed ? new(advanced.ToArray()) : this;
    }

    public LiveTranslationRegionWitness RetainForViewportCommit(LiveTranslationPixelSnapshot current,
        Func<InPlaceLayoutItem, bool>? sameSourceText = null,
        IReadOnlyList<VisualTextRegion>? sourceRegions = null)
    {
        var retained = new List<Entry>();
        var changed = false;
        InPlaceLayoutPlan? refreshedPlan = null;
        bool matches(VisualTextRegion region, InPlaceLayoutItem item) => item.SourceRegion is { } source
            && (ReferenceEquals(source, region) || source.Id == region.Id
                && source.SourceText == region.SourceText && source.Bounds == region.Bounds);
        InPlaceLayoutPlan? refresh()
        {
            if (sourceRegions is null || sourceRegions.Count == 0) return null;
            if (refreshedPlan is not null) return refreshedPlan;
            using var bitmap = current.ToBitmap();
            var results = sourceRegions.Select(region =>
            {
                var translated = _entries.Select(static entry => entry.Item)
                    .FirstOrDefault(item => matches(region, item));
                return translated is null
                    ? new TranslationResult(false, region.SourceText, "source-preserved")
                    : new TranslationResult(true, translated.Text, "machine-completed");
            }).ToArray();
            refreshedPlan = InPlaceLayoutService.BuildRegionPlan(bitmap, sourceRegions, results);
            return refreshedPlan;
        }
        foreach (var entry in _entries)
        {
            if (entry.Bounds.Right > current.Width || entry.Bounds.Bottom > current.Height) { changed = true; continue; }
            var hash = current.Hash(entry.Bounds);
            if (hash.AsSpan().SequenceEqual(entry.Hash)) { retained.Add(entry); continue; }
            changed = true;
            if (sameSourceText is not null && !sameSourceText(entry.Item))
                continue;
            if (entry.Source.TryBackgroundShift(current, entry.Bounds, out var shift))
            {
                var item = entry.Item with {
                    BackgroundColor = Shift(entry.Item.BackgroundColor, shift),
                    MaskedPatchPng = ShiftPatch(entry.Item.MaskedPatchPng, entry.Item.BackgroundColor, shift),
                    BackgroundPatchPng = ShiftPatch(entry.Item.BackgroundPatchPng, entry.Item.BackgroundColor, shift)
                };
                retained.Add(new(item, entry.Bounds, hash, current));
                continue;
            }
            if (sameSourceText?.Invoke(entry.Item) == true)
            {
                // Matching OCR permits reuse of the translation and typography,
                // but an animated backing needs a patch repaired from this frame.
                // A failed repair leaves the source visible until the next retry.
                if (entry.Item.MaskedPatchPng is not { Length: > 0 })
                {
                    retained.Add(new(entry.Item, entry.Bounds, hash, current));
                    continue;
                }
                var repaired = refresh()?.Items.FirstOrDefault(item =>
                    item.SourceRegion is { } region && matches(region, entry.Item));
                if (repaired?.MaskedPatchPng is { Length: > 0 })
                {
                    var item = entry.Item with
                    {
                        BackgroundColor = repaired.BackgroundColor,
                        ForegroundColor = repaired.ForegroundColor,
                        BackgroundMode = repaired.BackgroundMode,
                        SourceStyle = repaired.SourceStyle,
                        TextLayout = repaired.TextLayout,
                        MaskedPatchPng = repaired.MaskedPatchPng,
                    };
                    retained.Add(new(item, entry.Bounds, hash, current));
                }
                continue;
            }
            // Pixel count cannot distinguish a one-letter edit from antialiasing
            // or a caret. Without exact equality or a proven backing shift, the
            // candidate belongs to an older source frame and cannot be published.
        }
        return changed ? new(retained.ToArray()) : this;
    }

    private static Color Shift(Color color, Color shift) => Color.FromArgb(color.A,
        Math.Clamp(color.R + shift.R - 128, 0, 255), Math.Clamp(color.G + shift.G - 128, 0, 255),
        Math.Clamp(color.B + shift.B - 128, 0, 255));

    private static byte[]? ShiftPatch(byte[]? png, Color backing, Color shift)
    {
        if (png is not { Length: > 0 }) return png;
        using var stream = new MemoryStream(png);
        using var decoded = new Bitmap(stream);
        using var image = new Bitmap(decoded);
        // Only backing pixels move with hover. Glyph/edge colours and alpha stay intact.
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var color = image.GetPixel(x, y);
            if (Math.Abs(color.R - backing.R) <= 24 && Math.Abs(color.G - backing.G) <= 24
                && Math.Abs(color.B - backing.B) <= 24)
                image.SetPixel(x, y, Shift(color, shift));
        }
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    public LiveTranslationRegionWitness AddUncovered(LiveTranslationRegionWitness candidate)
    {
        // Retained source pixels are still exact: preserve their first text and typography.
        // Do not let a newly regrouped OCR paragraph overwrite or overlap that stable region.
        var entries = _entries.ToList();
        foreach (var entry in candidate._entries)
            // Check retained overlays only. OCR margins of adjacent paragraphs
            // accepted together may overlap without making either paragraph stale.
            if (!_entries.Any(existing => existing.Bounds.IntersectsWith(entry.Bounds)
                && !LiveTranslationViewportCommitPolicy.IsOnlyAdjacentParagraphMarginOverlap(
                    existing.Item, entry.Item)))
                entries.Add(entry);
        return new(entries.ToArray());
    }
}

internal static class LiveTranslationTextEvidence
{
    public static IReadOnlyList<Rectangle> BoundsFor(
        VisualTextRegion region, int width, int height)
    {
        var words = region.Lines.SelectMany(static line => line.Words)
            .Concat(region.OriginalOcrWords).ToArray();
        IEnumerable<ClipRay.Core.Geometry.PixelRect> areas;
        if (words.Length > 0)
            areas = words.Select(static word => word.Bounds);
        else if (region.Lines.Count > 0)
            areas = region.Lines.Select(static line => line.Bounds);
        else
            areas = [region.Bounds];
        return areas.Select(area => ToRectangle(area, width, height))
            .Where(static bounds => !bounds.IsEmpty).Distinct().ToArray();
    }

    public static IReadOnlyList<Rectangle> BoundsFor(
        InPlaceLayoutItem item, int width, int height)
    {
        if (item.SourceRegion is not null)
            return BoundsFor(item.SourceRegion, width, height);
        IEnumerable<ClipRay.Core.Geometry.PixelRect> areas = item.SourceBlock.Words.Count > 0
            ? item.SourceBlock.Words.Select(static word => word.Bounds)
            : new[] { item.SourceBlock.Bounds };
        return areas.Select(area => ToRectangle(area, width, height))
            .Where(static bounds => !bounds.IsEmpty).Distinct().ToArray();
    }

    private static Rectangle ToRectangle(ClipRay.Core.Geometry.PixelRect area, int width, int height)
    {
        // Include antialiasing at the OCR edge. Missing geometry is not proof
        // that a changed pixel is outside the source text.
        var left = (int)Math.Clamp(Math.Floor(area.Left) - 1, 0, width);
        var top = (int)Math.Clamp(Math.Floor(area.Top) - 1, 0, height);
        var right = (int)Math.Clamp(Math.Ceiling(area.Right) + 1, left, width);
        var bottom = (int)Math.Clamp(Math.Ceiling(area.Bottom) + 1, top, height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}

internal sealed class LiveTranslationPixelSnapshot
{
    private readonly byte[] _pixels;
    public int Width { get; }
    public int Height { get; }
    public LiveTranslationPixelSnapshot(Bitmap source)
    {
        Width = source.Width; Height = source.Height;
        _pixels = new byte[checked(Width * Height * 4)];
        using var normalized = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized)) graphics.DrawImageUnscaled(source, 0, 0);
        var data = normalized.LockBits(new(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), _pixels, y * Width * 4, Width * 4);
        }
        finally { normalized.UnlockBits(data); }
    }
    public Bitmap ToBitmap()
    {
        var image = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = image.LockBits(new(0, 0, Width, Height), ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(_pixels, y * Width * 4, IntPtr.Add(data.Scan0, y * data.Stride), Width * 4);
        }
        finally { image.UnlockBits(data); }
        return image;
    }
    private LiveTranslationPixelSnapshot(int width,int height,byte[] pixels)
    { Width=width; Height=height; _pixels=pixels; }
    public LiveTranslationPixelSnapshot UpdateRegions(LiveTranslationPixelSnapshot source,IReadOnlyList<Rectangle> windows)
    {
        if(Width!=source.Width || Height!=source.Height) return source;
        var pixels=(byte[])_pixels.Clone();
        foreach(var window in windows)
            for(var y=window.Top;y<window.Bottom;y++)
                Array.Copy(source._pixels,(y*Width+window.Left)*4,pixels,(y*Width+window.Left)*4,window.Width*4);
        return new(Width,Height,pixels);
    }
    public byte[] Hash(Rectangle bounds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(Width)); hash.AppendData(BitConverter.GetBytes(Height));
        for (var y = bounds.Top; y < bounds.Bottom; y++)
            hash.AppendData(_pixels, (y * Width + bounds.Left) * 4, bounds.Width * 4);
        return hash.GetHashAndReset();
    }
    public byte[] FullHash() => Hash(new(0, 0, Width, Height));
    public int CountChangedPixels(LiveTranslationPixelSnapshot current, Rectangle bounds)
    {
        if (Width != current.Width || Height != current.Height) return bounds.Width * bounds.Height;
        var changed = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var offset = (y * Width + x) * 4;
            if (!_pixels.AsSpan(offset, 4).SequenceEqual(current._pixels.AsSpan(offset, 4))) changed++;
        }
        return changed;
    }
    // Conservative hover gate: a small, dominant backing shift may move antialias
    // shades toward the unchanged foreground. A new/removed stroke cannot fit that
    // range. This is not a frame similarity percentage and does not drop small edits.
    public bool TryBackgroundShift(LiveTranslationPixelSnapshot current, Rectangle bounds, out Color shift)
    {
        shift = default;
        if (Width != current.Width || Height != current.Height) return false;
        var histograms = new int[3][] { new int[511], new int[511], new int[511] };
        var borderHistograms = new int[3][] { new int[511], new int[511], new int[511] };
        var borderCount = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var offset = (y * Width + x) * 4;
            var border = x == bounds.Left || x == bounds.Right - 1 || y == bounds.Top || y == bounds.Bottom - 1;
            if (border) borderCount++;
            for (var channel = 0; channel < 3; channel++)
            {
                histograms[channel][current._pixels[offset + channel] - _pixels[offset + channel] + 255]++;
                if (border) borderHistograms[channel][current._pixels[offset + channel] - _pixels[offset + channel] + 255]++;
            }
            if (_pixels[offset + 3] != current._pixels[offset + 3]) return false;
        }
        var delta = new int[3];
        var half = bounds.Width * bounds.Height / 2;
        for (var channel = 0; channel < 3; channel++)
        {
            var sum = 0;
            for (var i = 0; i < 511; i++)
                if ((sum += histograms[channel][i]) > half) { delta[channel] = i - 255; break; }
        }
        if (delta.All(value => value == 0) || delta.Any(value => Math.Abs(value) > 24)) return false;
        // A coloured interior/animated image is not backing merely because it is
        // dominant. The boundary must independently agree with the backing shift.
        for (var channel = 0; channel < 3; channel++)
        {
            var agreeing = 0;
            for (var i = Math.Max(0, delta[channel] + 253); i <= Math.Min(510, delta[channel] + 257); i++)
                agreeing += borderHistograms[channel][i];
            if (agreeing * 2 <= borderCount) return false;
        }
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var offset = (y * Width + x) * 4;
            for (var channel = 0; channel < 3; channel++)
            {
                var difference = current._pixels[offset + channel] - _pixels[offset + channel];
                if (difference < Math.Min(0, delta[channel]) - 2 || difference > Math.Max(0, delta[channel]) + 2)
                    return false;
            }
        }
        shift = Color.FromArgb(delta[2] + 128, delta[1] + 128, delta[0] + 128);
        return true;
    }

    public IReadOnlyList<Rectangle> ChangedTiles(LiveTranslationPixelSnapshot current, bool ignoreBackgroundShifts = false)
    {
        if(Width!=current.Width || Height!=current.Height)
            return [new Rectangle(0,0,current.Width,current.Height)];
        var changed=new List<Rectangle>();
        const int tileSize=32;
        for(var y=0;y<Height;y+=tileSize)
        for(var x=0;x<Width;x+=tileSize)
        {
            var bounds=new Rectangle(x,y,Math.Min(tileSize,Width-x),Math.Min(tileSize,Height-y));
            for(var row=y;row<bounds.Bottom;row++)
            {
                var offset=(row*Width+x)*4;
                if(!_pixels.AsSpan(offset,bounds.Width*4).SequenceEqual(current._pixels.AsSpan(offset,bounds.Width*4)))
                {
                    if (!ignoreBackgroundShifts || !TryBackgroundShift(current, bounds, out _)) changed.Add(bounds);
                    break;
                }
            }
        }
        return changed;
    }

    public LiveTranslationPixelChangeEvidence MeasureChanges(
        LiveTranslationPixelSnapshot current,
        IReadOnlyList<Rectangle> dirtyTiles)
    {
        if (Width != current.Width || Height != current.Height)
            return new((long)current.Width * current.Height, [new Rectangle(0, 0, current.Width, current.Height)]);
        long changedPixels = 0;
        var rowLeft = Enumerable.Repeat(Width, Height).ToArray();
        var rowRight = new int[Height];
        foreach (var tile in dirtyTiles)
        {
            for (var y = tile.Top; y < tile.Bottom; y++)
            {
                for (var x = tile.Left; x < tile.Right; x++)
                {
                    var offset = (y * Width + x) * 4;
                    if (_pixels.AsSpan(offset, 4).SequenceEqual(current._pixels.AsSpan(offset, 4))) continue;
                    changedPixels++;
                    rowLeft[y] = Math.Min(rowLeft[y], x);
                    rowRight[y] = Math.Max(rowRight[y], x + 1);
                }
            }
        }
        var bands = new List<Rectangle>();
        for (var y = 0; y < Height; y++)
        {
            if (rowRight[y] <= rowLeft[y]) continue;
            var row = Rectangle.FromLTRB(rowLeft[y], y, rowRight[y], y + 1);
            if (bands.Count > 0 && bands[^1].Bottom == row.Top)
            {
                bands[^1] = Rectangle.Union(bands[^1], row);
                continue;
            }
            bands.Add(row);
        }
        return new(changedPixels, bands);
    }
}

internal sealed record LiveTranslationPixelChangeEvidence(
    long ChangedPixels,
    IReadOnlyList<Rectangle> VerticalBands);

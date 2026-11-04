using System.Drawing;
using System.Text.RegularExpressions;
using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

public sealed record LiveTranslationReadingItem(VisualTextRegion Region, string Text);

// Reading text has its own source witness: no renderable layout is required.
// Exact local pixels deliberately favor expiring a changing item over showing
// an old translation after a scroll, edit, or animated source replacement.
internal sealed class LiveTranslationReadingWitness
{
    private sealed record Entry(LiveTranslationReadingItem Item, Rectangle Bounds, byte[] Hash);
    private readonly Entry[] _entries;
    private LiveTranslationReadingWitness(Entry[] entries) => _entries=entries;
    public static LiveTranslationReadingWitness Empty { get; }=new([]);
    public IReadOnlyList<LiveTranslationReadingItem> Items => _entries.Select(entry=>entry.Item).ToArray();
    public int Count => _entries.Length;

    public static LiveTranslationReadingWitness Create(LiveTranslationPixelSnapshot source,
        IReadOnlyList<LiveTranslationReadingItem> items)
    {
        var entries=new List<Entry>();
        foreach(var item in items)
        {
            var b=item.Region.Bounds;
            if(b.IsEmpty || !double.IsFinite(b.Left+b.Top+b.Right+b.Bottom)
                || b.Left<0 || b.Top<0 || b.Right>source.Width || b.Bottom>source.Height) continue;
            var bounds=Rectangle.FromLTRB(Math.Max(0,(int)Math.Floor(b.Left)-1),
                Math.Max(0,(int)Math.Floor(b.Top)-1),Math.Min(source.Width,(int)Math.Ceiling(b.Right)+1),
                Math.Min(source.Height,(int)Math.Ceiling(b.Bottom)+1));
            entries.Add(new(item,bounds,source.Hash(bounds)));
        }
        return new(entries.ToArray());
    }

    public LiveTranslationReadingWitness RetainUnchanged(LiveTranslationPixelSnapshot current)
    {
        var retained=_entries.Where(entry=>entry.Bounds.Right<=current.Width && entry.Bounds.Bottom<=current.Height
            && current.Hash(entry.Bounds).AsSpan().SequenceEqual(entry.Hash)).ToArray();
        return retained.Length==_entries.Length ? this : new(retained);
    }

    public LiveTranslationReadingWitness WithoutDisplayed(IReadOnlyList<InPlaceLayoutItem> displayed)
    {
        var retained=_entries.Where(entry=>!displayed.Any(item=>Displays(item,entry.Item))).ToArray();
        return retained.Length==_entries.Length ? this : new(retained);
    }

    internal static bool Displays(InPlaceLayoutItem layout,LiveTranslationReadingItem reading)
    {
        if(layout.SourceRegion is not {} region) return false;
        if(region.Id==reading.Region.Id) return true;
        var a=region.Bounds;
        var b=reading.Region.Bounds;
        // Stable anchors can retain the prior OCR id. Only matching source text
        // at overlapping geometry can suppress this version's reading entry.
        return Normalize(region.SourceText)==Normalize(reading.Region.SourceText)
            && Math.Min(a.Right,b.Right)>Math.Max(a.Left,b.Left)
            && Math.Min(a.Bottom,b.Bottom)>Math.Max(a.Top,b.Top);
    }

    internal static IReadOnlyList<LiveTranslationReadingItem> Candidates(ApplicationTranslationOutcome outcome) =>
        outcome.Regions.Count!=outcome.Results.Count ? [] : outcome.Regions.Zip(outcome.Results)
            .Where(pair=>pair.Second.SemanticOutcome==TranslationSemanticOutcome.Translated
                && !string.IsNullOrWhiteSpace(pair.Second.Text)
                && !string.Equals(pair.First.SourceText.Trim(),pair.Second.Text.Trim(),StringComparison.Ordinal))
            .Select(pair=>new LiveTranslationReadingItem(pair.First,pair.Second.Text.Trim())).ToArray();
    private static string Normalize(string text)=>Regex.Replace(text,@"\s+"," ").Trim();
}

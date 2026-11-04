using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

namespace ClipRay.App.LiveTranslation;

// OCR is the authority for moving pixels that overlap source text. Region IDs
// are positional and can change when the OCR grouping changes between frames.
internal sealed class LiveTranslationSemanticEvidence
{
    private readonly VisualTextRegion[] _regions;
    internal IReadOnlyList<VisualTextRegion> CurrentRegions { get; }

    private LiveTranslationSemanticEvidence(VisualTextRegion[] regions)
    {
        _regions = regions;
        CurrentRegions = Array.AsReadOnly(regions);
    }

    public static LiveTranslationSemanticEvidence? FromRecognition(
        OcrRecognitionResult result, Bitmap bitmap)
    {
        if (!result.IsSuccess) return null;
        return new(VisualTextRegionBuilder.Build(result.Blocks, bitmap).ToArray());
    }

    public bool SameSource(IReadOnlyList<VisualTextRegion> expected)
    {
        if (expected.Count != _regions.Length) return false;
        var used = new bool[_regions.Length];
        foreach (var region in expected)
        {
            var index = -1;
            for (var candidate = 0; candidate < _regions.Length; candidate++)
                if (!used[candidate] && Equivalent(region, _regions[candidate]))
                {
                    index = candidate;
                    break;
                }
            if (index < 0) return false;
            used[index] = true;
        }
        return true;
    }

    public bool SameSourceStableGeometry(IReadOnlyList<VisualTextRegion> expected,
        LiveTranslationPixelSnapshot? source = null, LiveTranslationPixelSnapshot? current = null)
    {
        if (expected.Count != _regions.Length) return false;
        var used = new bool[_regions.Length];
        foreach (var region in expected)
        {
            var index = -1;
            for (var candidate = 0; candidate < _regions.Length; candidate++)
                if (!used[candidate] && (EquivalentStable(region, _regions[candidate])
                    || HasUnchangedRegionPixels(region, _regions[candidate], source, current)))
                {
                    index = candidate;
                    break;
                }
            if (index < 0) return false;
            used[index] = true;
        }
        return true;
    }

    public bool SameSourceStableGeometry(LiveTranslationSemanticEvidence expected,
        LiveTranslationPixelSnapshot? source = null, LiveTranslationPixelSnapshot? current = null) =>
        SameSourceStableGeometry(expected._regions, source, current);

    public bool ConfirmsViewportReplacement(LiveTranslationSemanticEvidence expected)
    {
        // Extra/missing candidates and a changed word are local observations.
        // Require recognizable source paragraphs to have actually moved. Stable
        // fixed headers do not veto a scrolling body, but one video subtitle does
        // not get to cancel all other regions either.
        var moved = expected._regions.Where(region => region.Role is VisualTextRole.Body or VisualTextRole.Heading)
            .Count(region => !_regions.Any(candidate => EquivalentStable(region, candidate))
                && _regions.Any(candidate => TextEquals(region.SourceText, candidate.SourceText)
                    && !SamePlace(region.Bounds, candidate.Bounds)));
        return moved >= 2;
    }

    public bool Matches(InPlaceLayoutItem item)
    {
        var source = item.SourceRegion;
        if (source is not null) return _regions.Any(candidate => Equivalent(source, candidate));
        return _regions.Any(candidate => TextEquals(item.SourceBlock.Text, candidate.SourceText)
            && SamePlace(item.SourceBlock.Bounds, candidate.Bounds));
    }

    public bool MatchesStableGeometry(InPlaceLayoutItem item,
        LiveTranslationPixelSnapshot? sourcePixels = null, LiveTranslationPixelSnapshot? currentPixels = null)
    {
        var source = item.SourceRegion;
        if (source is not null) return _regions.Any(candidate => EquivalentStable(source, candidate)
            || HasUnchangedRegionPixels(source, candidate, sourcePixels, currentPixels));
        return _regions.Any(candidate => TextEquals(item.SourceBlock.Text, candidate.SourceText)
            && StableBounds(item.SourceBlock.Bounds, candidate.Bounds));
    }

    private static bool EquivalentStable(VisualTextRegion expected, VisualTextRegion current) =>
        TextEquals(expected.SourceText, current.SourceText)
        && StableBounds(expected.Bounds, current.Bounds)
        && expected.Lines.Count == current.Lines.Count
        && expected.Lines.Zip(current.Lines).All(pair =>
            TextEquals(pair.First.Text, pair.Second.Text)
            && StableBounds(pair.First.Bounds, pair.Second.Bounds));

    private static bool HasUnchangedRegionPixels(VisualTextRegion expected, VisualTextRegion observed,
        LiveTranslationPixelSnapshot? source, LiveTranslationPixelSnapshot? current)
    {
        if (source is null || current is null || source.Width != current.Width
            || source.Height != current.Height || !Equivalent(expected, observed)) return false;
        var bounds = Rectangle.FromLTRB(
            (int)Math.Floor(Math.Min(expected.Bounds.Left, observed.Bounds.Left)),
            (int)Math.Floor(Math.Min(expected.Bounds.Top, observed.Bounds.Top)),
            (int)Math.Ceiling(Math.Max(expected.Bounds.Right, observed.Bounds.Right)),
            (int)Math.Ceiling(Math.Max(expected.Bounds.Bottom, observed.Bounds.Bottom)));
        // Detector boxes may jitter when video changes elsewhere in the image.
        // A larger geometry tolerance is safe only when the full union is exact.
        return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0
            && bounds.Right <= source.Width && bounds.Bottom <= source.Height
            && source.CountChangedPixels(current, bounds) == 0;
    }

    private static bool StableBounds(PixelRect expected, PixelRect current) =>
        Math.Abs(expected.Left - current.Left) <= 1
        && Math.Abs(expected.Top - current.Top) <= 1
        && Math.Abs(expected.Right - current.Right) <= 1
        && Math.Abs(expected.Bottom - current.Bottom) <= 1;

    private static bool Equivalent(VisualTextRegion expected, VisualTextRegion current) =>
        TextEquals(expected.SourceText, current.SourceText)
        && SamePlace(expected.Bounds, current.Bounds);

    private static bool TextEquals(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.Ordinal);

    private static string Normalize(string text) =>
        Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();

    private static bool SamePlace(PixelRect expected, PixelRect current)
    {
        if (expected.IsEmpty || current.IsEmpty) return false;
        var overlapWidth = Math.Max(0, Math.Min(expected.Right, current.Right)
            - Math.Max(expected.Left, current.Left));
        var overlapHeight = Math.Max(0, Math.Min(expected.Bottom, current.Bottom)
            - Math.Max(expected.Top, current.Top));
        var smallerArea = Math.Min(expected.Width * expected.Height,
            current.Width * current.Height);
        if (smallerArea <= 0 || overlapWidth * overlapHeight / smallerArea < .6)
            return false;
        var centerDistanceX = Math.Abs(expected.Left + expected.Width / 2
            - current.Left - current.Width / 2);
        var centerDistanceY = Math.Abs(expected.Top + expected.Height / 2
            - current.Top - current.Height / 2);
        return centerDistanceX <= Math.Max(4, expected.Width * .2)
            && centerDistanceY <= Math.Max(4, expected.Height * .25);
    }
}

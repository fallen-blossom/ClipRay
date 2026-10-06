using System.Drawing;
using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

internal enum LiveTranslationViewportCommitState
{
    Ready,
    Stale,
}

internal sealed record LiveTranslationViewportCommit(
    LiveTranslationViewportCommitState State,
    LiveTranslationRegionWitness Witness,
    LiveTranslationViewportResultState ResultState,
    LiveTranslationViewportMetrics Metrics,
    IReadOnlyList<LiveTranslationLayoutQuarantine> Quarantined);

public sealed record LiveTranslationLayoutQuarantine(
    string? RegionId, string Reason, double X, double Y,
    double Width, double Height, double? FontSizePixels);

internal static class LiveTranslationViewportCommitPolicy
{
    internal static bool IsFullViewport(IReadOnlyList<Rectangle> windows, int width, int height) =>
        windows.Count == 1 && windows[0] == new Rectangle(0, 0, width, height);

    internal static LiveTranslationViewportCommit Commit(
        LiveTranslationPixelSnapshot source,
        LiveTranslationPixelSnapshot current,
        IReadOnlyList<VisualTextRegion> sourceRegions,
        LiveTranslationRegionWitness candidate,
        LiveTranslationFrame frame,
        Func<InPlaceLayoutItem, bool>? sameSourceText = null)
    {
        var changes = LiveTranslationChangePlanner.Analyze(source, current, sourceRegions);
        var expected = frame.ExpectedLayoutCount > 0 ? frame.ExpectedLayoutCount : candidate.Count;
        if (changes.IsViewportReplacement)
            return new(LiveTranslationViewportCommitState.Stale, LiveTranslationRegionWitness.Empty,
                LiveTranslationViewportResultState.Stale,
                new(expected, candidate.Count, 0, frame.RejectedLayoutCount), []);
        var witness = candidate.RetainForViewportCommit(current, sameSourceText, sourceRegions);
        var items = witness.Items;
        var rejected = new Dictionary<InPlaceLayoutItem, string>(ReferenceEqualityComparer.Instance);
        foreach (var item in items)
        {
            if (item.TextLayout is { ShouldRender: false }
                || item.TextLayout is { FontSizePixels: < TranslationTextLayoutPolicy.MinimumReadableFontSizePixels })
                rejected[item] = "unreadable-text";
            else if (item.Bounds.IsEmpty || !double.IsFinite(item.Bounds.Left + item.Bounds.Top
                    + item.Bounds.Right + item.Bounds.Bottom)
                || item.Bounds.Left < 0 || item.Bounds.Top < 0
                || item.Bounds.Right > current.Width || item.Bounds.Bottom > current.Height)
                rejected[item] = "out-of-bounds";
        }
        for (var first = 0; first < items.Count; first++)
        for (var second = first + 1; second < items.Count; second++)
            if (HasMaterialOverlap(items[first].Bounds, items[second].Bounds)
                && !IsOnlyAdjacentParagraphMarginOverlap(items[first], items[second]))
            {
                rejected.TryAdd(items[first], "material-overlap");
                rejected.TryAdd(items[second], "material-overlap");
            }
        var quarantined = items.Where(rejected.ContainsKey).Select(item =>
            new LiveTranslationLayoutQuarantine(item.SourceRegion?.Id,
                rejected[item], item.Bounds.X, item.Bounds.Y,
                item.Bounds.Width, item.Bounds.Height, item.TextLayout?.FontSizePixels)).ToArray();
        witness = witness.RetainItems(item => !rejected.ContainsKey(item));
        var measuredFrame = frame with { RejectedLayoutCount = frame.RejectedLayoutCount + quarantined.Length };
        var metrics = Measure(source, measuredFrame, candidate.Count, witness.Items);
        var resultState = LiveTranslationViewportResultPolicy.Classify(frame, metrics);
        return new(LiveTranslationViewportCommitState.Ready, witness, resultState,
            metrics, quarantined);
    }

    internal static LiveTranslationViewportMetrics Measure(
        LiveTranslationPixelSnapshot viewport,
        LiveTranslationFrame frame,
        int candidateLayouts,
        IReadOnlyList<InPlaceLayoutItem> retained)
    {
        var expected = frame.ExpectedLayoutCount > 0 ? frame.ExpectedLayoutCount : candidateLayouts;
        var unreadable = retained.Count(item => item.TextLayout is { ShouldRender: false }
            || item.TextLayout is { FontSizePixels: < TranslationTextLayoutPolicy.MinimumReadableFontSizePixels });
        var outOfBounds = retained.Count(item => item.Bounds.IsEmpty
            || item.Bounds.Left < 0 || item.Bounds.Top < 0
            || item.Bounds.Right > viewport.Width || item.Bounds.Bottom > viewport.Height);
        var overlaps = 0;
        for (var first = 0; first < retained.Count; first++)
        for (var second = first + 1; second < retained.Count; second++)
            if (HasMaterialOverlap(retained[first].Bounds, retained[second].Bounds)
                && !IsOnlyAdjacentParagraphMarginOverlap(retained[first], retained[second])) overlaps++;
        var retainedPrimary = retained.Where(item => item.SourceRegion?.Role
            is VisualTextRole.Body or VisualTextRole.Heading).ToArray();
        return new(expected, candidateLayouts, retained.Count, frame.RejectedLayoutCount,
            unreadable, overlaps, outOfBounds,
            frame.ExpectedPrimaryLayoutCount, retainedPrimary.Length,
            frame.ExpectedPrimaryLayoutArea,
            retainedPrimary.Sum(item => item.Bounds.Width * item.Bounds.Height));
    }

    private static bool HasMaterialOverlap(ClipRay.Core.Geometry.PixelRect first,
        ClipRay.Core.Geometry.PixelRect second)
    {
        var width = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var height = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        var intersection = width * height;
        var smaller = Math.Min(first.Width * first.Height, second.Width * second.Height);
        return smaller > 0 && intersection / smaller >= .15;
    }

    internal static bool IsOnlyAdjacentParagraphMarginOverlap(
        InPlaceLayoutItem first,
        InPlaceLayoutItem second)
    {
        var firstRegion = first.SourceRegion;
        var secondRegion = second.SourceRegion;
        if (firstRegion is null || secondRegion is null)
            return false;

        return !firstRegion.Lines.Any(firstLine => secondRegion.Lines.Any(secondLine =>
            Math.Abs((firstLine.Bounds.Top + firstLine.Bounds.Bottom
                - secondLine.Bounds.Top - secondLine.Bounds.Bottom) / 2)
            < Math.Min(firstLine.Bounds.Height, secondLine.Bounds.Height) * .5));
    }
}

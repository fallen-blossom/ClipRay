using System.Drawing;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

namespace ClipRay.App.LiveTranslation;

public sealed record LiveTranslationRegionWork(Rectangle Bounds, string SourceVersion);

// There is exactly one active region and no queued bitmap/task. Every selection
// uses the newest capture. Unselected pixels retain their old processed version,
// so they remain discoverable instead of being silently marked as completed.
internal sealed class LiveTranslationRegionScheduler
{
    internal const int PriorityBypassLimit = 2;
    private readonly Dictionary<Rectangle, long> _waitingSince = [];
    private long _sequence;

    internal Rectangle Select(IReadOnlyList<Rectangle> windows, IReadOnlyList<VisualTextRegion> regions)
    {
        if (windows.Count == 0) throw new ArgumentException("At least one dirty window is required.", nameof(windows));
        var pending = windows.Distinct().ToArray();
        foreach (var obsolete in _waitingSince.Keys.Where(window => !pending.Contains(window)).ToArray())
            _waitingSince.Remove(obsolete);
        foreach (var window in pending)
            _waitingSince.TryAdd(window, _sequence);
        // Body/heading gets the first opportunity, not a permanent veto. Age is
        // measured in completed selections, including for a newly discovered
        // window that has no OCR role yet. Once overdue, oldest work wins before
        // role priority; serving it starts a fresh waiting interval.
        var overdue = pending.Where(window => _sequence - _waitingSince[window]
            >= PriorityBypassLimit).ToArray();
        var selected = (overdue.Length > 0 ? overdue : pending)
            .OrderBy(window => overdue.Length > 0 ? _waitingSince[window] : 0)
            .ThenBy(window => regions.Any(region =>
                region.Role is VisualTextRole.Body or VisualTextRole.Heading
                && Intersects(window, region.Bounds)) ? 0 : 1)
            .ThenBy(window => _waitingSince[window])
            .ThenBy(window => window.Top).ThenBy(window => window.Left).First();
        _waitingSince[selected] = ++_sequence;
        return selected;
    }

    internal static LiveTranslationFrame Merge(LiveTranslationFrame crop, LiveTranslationFrame previous,
        IReadOnlyList<InPlaceLayoutItem> retained, Rectangle window)
    {
        var failedRecognition = crop.SourceRegions.Count == 0 && crop.LayoutItems.Count == 0
            && crop.ErrorCategory != "none" && crop.ErrorCategory != "Scope:partial-or-failed";
        bool Outside(VisualTextRegion region) => !Intersects(window, region.Bounds);
        var oldRegions = previous.SourceRegions.Where(region => Outside(region) || failedRecognition);
        var sourceRegions = oldRegions.Concat(crop.SourceRegions).ToArray();
        var outsideItems = retained.Where(item => !Intersects(window,
            item.SourceRegion?.Bounds ?? item.SourceBlock.Bounds)).ToArray();
        var items = outsideItems.Concat(crop.LayoutItems).ToArray();
        // Production frames carry exact expected-region identities, including
        // failures. The fallback supports older callers without inventing success.
        var previousExpected = previous.ExpectedLayoutRegions
            ?? previous.SourceRegions.Take(Math.Max(previous.ExpectedLayoutCount, previous.LayoutItems.Count)).ToArray();
        var cropExpected = crop.ExpectedLayoutRegions
            ?? crop.SourceRegions.Take(Math.Max(crop.ExpectedLayoutCount, crop.LayoutItems.Count)).ToArray();
        var expectedRegions = previousExpected.Where(region => Outside(region) || failedRecognition)
            .Concat(cropExpected).ToArray();
        var expectedCount = expectedRegions.Length;
        var expectedPrimary = expectedRegions.Where(region => region.Role
            is VisualTextRole.Body or VisualTextRole.Heading).ToArray();
        var missing = items.Length < expectedCount || failedRecognition;
        return crop with
        {
            SourceRegions = sourceRegions,
            LayoutItems = items,
            ExpectedLayoutRegions = expectedRegions,
            ExpectedLayoutCount = Math.Max(expectedCount, items.Length),
            ExpectedPrimaryLayoutCount = expectedPrimary.Length,
            ExpectedPrimaryLayoutArea = expectedPrimary.Sum(region => region.Bounds.Width * region.Bounds.Height),
            RejectedLayoutCount = Math.Max(crop.RejectedLayoutCount, expectedCount - items.Length),
            ResultState = missing ? LiveTranslationViewportResultState.AcceptablePartial : crop.ResultState
                == LiveTranslationViewportResultState.Failed && !failedRecognition
                    ? LiveTranslationViewportResultState.Ready : crop.ResultState,
            Status = missing ? "translation-partial" : "completed",
            ErrorCategory = missing ? crop.ErrorCategory == "none" ? "region-pending" : crop.ErrorCategory
                : crop.ErrorCategory == "Scope:partial-or-failed" ? "none" : crop.ErrorCategory,
        };
    }

    internal static bool TouchesInteriorCropEdge(LiveTranslationFrame frame, Rectangle window, int width, int height) =>
        frame.SourceRegions.SelectMany(static region => region.Lines).Any(line =>
            window.Left > 0 && line.Bounds.Left <= window.Left + 1
            || window.Top > 0 && line.Bounds.Top <= window.Top + 1
            || window.Right < width && line.Bounds.Right >= window.Right - 1
            || window.Bottom < height && line.Bounds.Bottom >= window.Bottom - 1);

    private static bool Intersects(Rectangle window, PixelRect bounds) =>
        window.Left < bounds.Right && window.Right > bounds.Left
        && window.Top < bounds.Bottom && window.Bottom > bounds.Top;
}

using System.Drawing;
using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

internal sealed record LiveTranslationChangePlan(
    IReadOnlyList<Rectangle> Windows,
    bool IsViewportReplacement,
    bool IsIncidentalChange = false);

public static class LiveTranslationChangePlanner
{
    public static IReadOnlyList<Rectangle> Plan(Bitmap before, Bitmap current,
        IReadOnlyList<VisualTextRegion> paragraphs) => Plan(new LiveTranslationPixelSnapshot(before),new LiveTranslationPixelSnapshot(current),paragraphs);

    internal static IReadOnlyList<Rectangle> Plan(LiveTranslationPixelSnapshot before,
        LiveTranslationPixelSnapshot current, IReadOnlyList<VisualTextRegion> paragraphs) =>
        Analyze(before, current, paragraphs).Windows;

    internal static LiveTranslationChangePlan Analyze(LiveTranslationPixelSnapshot before,
        LiveTranslationPixelSnapshot current, IReadOnlyList<VisualTextRegion> paragraphs)
    {
        var image=new Rectangle(0,0,current.Width,current.Height);
        var paragraphBounds = paragraphs.Select(paragraph => Rectangle.FromLTRB(
            (int)Math.Floor(paragraph.Bounds.Left),(int)Math.Floor(paragraph.Bounds.Top),
            (int)Math.Ceiling(paragraph.Bounds.Right),(int)Math.Ceiling(paragraph.Bounds.Bottom)))
            .Where(bounds => bounds.Width > 0 && bounds.Height > 0).ToArray();
        var dirtyTiles = before.ChangedTiles(current, ignoreBackgroundShifts: true).ToArray();
        var evidence = before.MeasureChanges(current, dirtyTiles);
        if (IsCaretBlink(evidence, paragraphBounds, image))
            return new([], false, true);
        if (IsViewportReplacement(evidence, image))
            return new([image], true);
        var windows=new List<Rectangle>();
        foreach(var dirty in dirtyTiles)
        {
            var window=dirty;
            foreach(var bounds in paragraphBounds)
            {
                if(bounds.IntersectsWith(dirty)) window=Rectangle.Union(window,bounds);
            }
            var core = window;
            window.Inflate(24,24);
            window=Rectangle.Intersect(window,image);
            // Empty context may overlap a neighbor; trim it rather than pulling
            // that unchanged paragraph and its neighbors into this OCR crop.
            foreach (var neighbor in paragraphBounds.Where(bounds => !bounds.IntersectsWith(core)))
            {
                if (!window.IntersectsWith(neighbor)) continue;
                if (core.Bottom <= neighbor.Top)
                    window = Rectangle.FromLTRB(window.Left,window.Top,window.Right,Math.Min(window.Bottom,neighbor.Top));
                else if (core.Top >= neighbor.Bottom)
                    window = Rectangle.FromLTRB(window.Left,Math.Max(window.Top,neighbor.Bottom),window.Right,window.Bottom);
                else if (core.Right <= neighbor.Left)
                    window = Rectangle.FromLTRB(window.Left,window.Top,Math.Min(window.Right,neighbor.Left),window.Bottom);
                else if (core.Left >= neighbor.Right)
                    window = Rectangle.FromLTRB(Math.Max(window.Left,neighbor.Right),window.Top,window.Right,window.Bottom);
            }
            if(!window.IsEmpty) windows.Add(window);
        }
        // Padding and unions can reach another paragraph. Expand to complete
        // source geometry until both paragraph containment and merging settle.
        bool expanded;
        do
        {
            expanded = false;
            for(var i=0;i<windows.Count;i++)
            {
                var window=windows[i];
                foreach(var bounds in paragraphBounds)
                {
                    if(window.IntersectsWith(bounds) && !window.Contains(bounds))
                        window=Rectangle.Intersect(Rectangle.Union(window,bounds),image);
                }
                if(window!=windows[i]) { windows[i]=window; expanded=true; }
                for(var j=i+1;j<windows.Count;j++)
                    if(window.IntersectsWith(windows[j]) && MayMerge(window,windows[j]))
                    {
                        windows[i]=Rectangle.Union(window,windows[j]);
                        windows.RemoveAt(j); expanded=true; break;
                    }
            }
        } while(expanded);
        return new(windows, false);

        static bool IsViewportReplacement(
            LiveTranslationPixelChangeEvidence evidence,
            Rectangle image)
        {
            if (evidence.ChangedPixels == 0 || image.Width <= 0 || image.Height <= 0) return false;

            // A viewport replacement invalidates the old OCR geometry. Measure the
            // changed evidence itself instead of dividing by all known regions: fixed
            // headers and sidebars must not dilute a scrolling body, while two distant
            // caret/badge edits must not become a full-frame translation merely because
            // their bounding box is tall.
            // A fixed absolute floor rejects caret/counter noise without making a
            // short paragraph harder to detect on a larger or high-DPI canvas.
            if (evidence.ChangedPixels < 128) return false;
            if (evidence.VerticalBands.Count < 2) return false;
            var changedSpan = evidence.VerticalBands[^1].Bottom - evidence.VerticalBands[0].Top;
            if (changedSpan * 10 < image.Height) return false;

            // Scrolling text produces separated old/new glyph bands. A video or
            // animated card normally changes one continuous band, even when a
            // subtitle happens to sit inside it. Reject one dominant band plus
            // incidental small counters or caret changes.
            var changedRows = evidence.VerticalBands.Sum(static band => band.Height);
            return evidence.VerticalBands.Max(static band => band.Height) * 4 <= changedRows * 3;
        }

        static bool IsCaretBlink(
            LiveTranslationPixelChangeEvidence evidence,
            IReadOnlyList<Rectangle> paragraphs,
            Rectangle image)
        {
            if (evidence.ChangedPixels == 0 || evidence.ChangedPixels > 128
                || evidence.VerticalBands.Count != 1 || paragraphs.Count == 0) return false;
            var band = evidence.VerticalBands[0];
            var verticalCaret = band.Width <= 3 && band.Height is >= 6 and <= 96;
            var horizontalCaret = band.Height <= 3 && band.Width is >= 6 and <= 32;
            if (!verticalCaret && !horizontalCaret) return false;
            if (!image.Contains(band)) return false;
            return paragraphs.Any(paragraph => paragraph.IntersectsWith(band));
        }

        bool MayMerge(Rectangle first, Rectangle second)
        {
            if (first.Contains(second) || second.Contains(first)) return true;
            // Merge repeated tiles of one paragraph, or media without known
            // text. Overlapping empty margins alone do not join body paragraphs.
            if (paragraphBounds.Any(bounds => first.Contains(bounds) && second.Contains(bounds))) return true;
            return !paragraphBounds.Any(bounds => first.IntersectsWith(bounds) || second.IntersectsWith(bounds));
        }
    }
}

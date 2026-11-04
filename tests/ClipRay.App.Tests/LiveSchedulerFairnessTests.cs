using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveSchedulerFairnessTests
{
    public static void AlwaysDirtyBodyCannotStarveButtonOrUnknown()
    {
        var body = new Rectangle(0, 0, 100, 40);
        var button = new Rectangle(0, 60, 100, 30);
        var unknown = new Rectangle(150, 60, 100, 30);
        var regions = new[] { Region("body", body, VisualTextRole.Body), Region("button", button, VisualTextRole.Control) };
        var scheduler = new LiveTranslationRegionScheduler();
        var selections = Enumerable.Range(0, 16).Select(_ => scheduler.Select([body, button, unknown], regions)).ToArray();
        if (selections[0] != body)
            throw new Exception("Primary content lost its initial priority.");
        foreach (var window in new[] { button, unknown })
        {
            var first = Array.IndexOf(selections, window);
            if (first < 0 || first > LiveTranslationRegionScheduler.PriorityBypassLimit + 2)
                throw new Exception("Continuous Body work starved a button or unknown dirty window.");
            var last = first;
            for (var index = first + 1; index < selections.Length; index++)
                if (selections[index] == window)
                {
                    if (index - last > LiveTranslationRegionScheduler.PriorityBypassLimit + 3)
                        throw new Exception("A repeatedly dirty secondary window lost its aging priority.");
                    last = index;
                }
            if (selections.Length - 1 - last > LiveTranslationRegionScheduler.PriorityBypassLimit + 3)
                throw new Exception("A secondary window stopped receiving service.");
        }
    }

    public static void NewUnknownWindowHasBoundedWaitUnderBodyChurn()
    {
        var scheduler = new LiveTranslationRegionScheduler();
        var unknown = new Rectangle(200, 200, 40, 30);
        var servedAt = -1;
        for (var turn = 0; turn < 12; turn++)
        {
            // The animated Body crop changes shape on every observation. Its
            // fresh identity cannot repeatedly displace an older unknown area.
            var body = new Rectangle(0, 0, 100 + turn, 40);
            var regions = new[] { Region("body", body, VisualTextRole.Body) };
            if (scheduler.Select(turn < 4 ? [body] : [body, unknown], regions) == unknown)
            { servedAt = turn; break; }
        }
        if (servedAt < 4 || servedAt > 4 + LiveTranslationRegionScheduler.PriorityBypassLimit)
            throw new Exception("A newly found unknown region starved behind changing Body crop identities.");
    }

    public static void AgingDoesNotMarkUnselectedSourcePixelsProcessed()
    {
        using var beforeBitmap = new Bitmap(300, 150);
        using var afterBitmap = new Bitmap(beforeBitmap);
        afterBitmap.SetPixel(20, 20, Color.White);
        afterBitmap.SetPixel(220, 110, Color.Blue);
        var before = new LiveTranslationPixelSnapshot(beforeBitmap);
        var after = new LiveTranslationPixelSnapshot(afterBitmap);
        var body = new Rectangle(0, 0, 100, 60);
        var unknown = new Rectangle(200, 90, 80, 50);
        var scheduler = new LiveTranslationRegionScheduler();
        var selected = scheduler.Select([body, unknown], [Region("body", body, VisualTextRole.Body)]);
        var processed = before.UpdateRegions(after, [selected]);
        if (selected != body || processed.CountChangedPixels(after, body) != 0
            || processed.CountChangedPixels(after, unknown) == 0)
            throw new Exception("A priority selection advanced the source version of an unprocessed sibling.");
    }

    private static VisualTextRegion Region(string id, Rectangle bounds, VisualTextRole role)
    {
        var area = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var block = new OcrTextBlock(id, area, [new(id, area)]);
        return new(id, id, [block], area, role, VisualStructureConfidence.High);
    }
}

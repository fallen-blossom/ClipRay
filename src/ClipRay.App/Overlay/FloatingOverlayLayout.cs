using ClipRay.Core.Geometry;

namespace ClipRay.App.Overlay;

public static class FloatingOverlayLayout
{
    public static PixelRect PlaceAdjacent(
        PixelRect toolbar,
        double panelWidth,
        double panelHeight,
        double viewportWidth,
        double viewportHeight,
        double gap,
        IReadOnlyList<PixelRect>? forbiddenRegions = null)
    {
        if (panelWidth <= 0 || panelHeight <= 0) return new PixelRect(0, 0, 0, 0);

        var width = Math.Min(panelWidth, Math.Max(0, viewportWidth));
        var height = Math.Min(panelHeight, Math.Max(0, viewportHeight));
        var left = Math.Clamp(
            toolbar.Left + (toolbar.Width - width) / 2,
            0,
            Math.Max(0, viewportWidth - width));
        var candidates = new[]
        {
            new PixelRect(left, toolbar.Bottom + gap, width, height),
            new PixelRect(left, toolbar.Top - gap - height, width, height),
            new PixelRect(toolbar.Right + gap, toolbar.Top + (toolbar.Height - height) / 2, width, height),
            new PixelRect(toolbar.Left - gap - width, toolbar.Top + (toolbar.Height - height) / 2, width, height),
        }.Select(candidate => ClampToViewport(candidate, viewportWidth, viewportHeight)).ToArray();

        foreach (var candidate in candidates)
        {
            if (forbiddenRegions is null || forbiddenRegions.All(region => !Intersects(candidate, region)))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static PixelRect ClampToViewport(PixelRect rect, double viewportWidth, double viewportHeight) => new(
        Math.Clamp(rect.Left, 0, Math.Max(0, viewportWidth - rect.Width)),
        Math.Clamp(rect.Top, 0, Math.Max(0, viewportHeight - rect.Height)),
        rect.Width,
        rect.Height);

    private static bool Intersects(PixelRect first, PixelRect second) =>
        first.Left < second.Right && second.Left < first.Right && first.Top < second.Bottom && second.Top < first.Bottom;
}

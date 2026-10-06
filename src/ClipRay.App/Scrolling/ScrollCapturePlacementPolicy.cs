using System.Drawing;

namespace ClipRay.App.Scrolling;

public readonly record struct ScrollCapturePlacement(Rectangle Bounds);

public static class ScrollCapturePlacementPolicy
{
    public static ScrollCapturePlacement? Place(Rectangle region, Size controlSize, IReadOnlyList<Rectangle> workAreas)
    {
        var area = workAreas.FirstOrDefault(work => work.IntersectsWith(region));
        if (area.IsEmpty) return null;
        var candidates = new[]
        {
            new Rectangle(region.Right + 12, region.Top, controlSize.Width, controlSize.Height),
            new Rectangle(region.Left - 12 - controlSize.Width, region.Top, controlSize.Width, controlSize.Height),
            new Rectangle(region.Left, region.Bottom + 12, controlSize.Width, controlSize.Height),
            new Rectangle(region.Left, region.Top - 12 - controlSize.Height, controlSize.Width, controlSize.Height),
        };
        foreach (var candidate in candidates)
        {
            var clamped = new Rectangle(Math.Clamp(candidate.X, area.Left, area.Right - candidate.Width), Math.Clamp(candidate.Y, area.Top, area.Bottom - candidate.Height), candidate.Width, candidate.Height);
            if (area.Contains(clamped) && !clamped.IntersectsWith(region)) return new ScrollCapturePlacement(clamped);
        }
        return null;
    }

    public static ScrollCapturePlacement? PlaceToolbar(Rectangle region, Size controlSize, IReadOnlyList<Rectangle> workAreas)
    {
        var area = workAreas.FirstOrDefault(work => work.IntersectsWith(region));
        if (area.IsEmpty) return null;
        var candidates = new[]
        {
            new Rectangle(region.Left, region.Bottom + 8, controlSize.Width, controlSize.Height),
            new Rectangle(region.Left, region.Top - 8 - controlSize.Height, controlSize.Width, controlSize.Height),
            new Rectangle(region.Right + 8, region.Bottom - controlSize.Height, controlSize.Width, controlSize.Height),
            new Rectangle(region.Left - 8 - controlSize.Width, region.Bottom - controlSize.Height, controlSize.Width, controlSize.Height),
        };
        foreach (var candidate in candidates)
        {
            var clamped = new Rectangle(Math.Clamp(candidate.X, area.Left, area.Right - candidate.Width), Math.Clamp(candidate.Y, area.Top, area.Bottom - candidate.Height), candidate.Width, candidate.Height);
            if (area.Contains(clamped) && !clamped.IntersectsWith(region)) return new ScrollCapturePlacement(clamped);
        }
        return null;
    }
}

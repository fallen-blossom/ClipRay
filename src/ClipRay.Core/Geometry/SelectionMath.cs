namespace ClipRay.Core.Geometry;

public static class SelectionMath
{
    public static PixelRect FromPoints(PixelPoint start, PixelPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        return new PixelRect(left, top, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    public static PixelRect Move(PixelRect rect, double deltaX, double deltaY, PixelRect bounds)
    {
        var x = Math.Clamp(rect.X + deltaX, bounds.Left, bounds.Right - rect.Width);
        var y = Math.Clamp(rect.Y + deltaY, bounds.Top, bounds.Bottom - rect.Height);
        return rect with { X = x, Y = y };
    }

    public static PixelRect Resize(
        PixelRect rect,
        ResizeHandle handle,
        PixelPoint pointer,
        PixelRect bounds,
        double minimumSize)
    {
        var left = rect.Left;
        var top = rect.Top;
        var right = rect.Right;
        var bottom = rect.Bottom;

        if (handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft)
        {
            left = Math.Clamp(pointer.X, bounds.Left, right - minimumSize);
        }

        if (handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight)
        {
            right = Math.Clamp(pointer.X, left + minimumSize, bounds.Right);
        }

        if (handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight)
        {
            top = Math.Clamp(pointer.Y, bounds.Top, bottom - minimumSize);
        }

        if (handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight)
        {
            bottom = Math.Clamp(pointer.Y, top + minimumSize, bounds.Bottom);
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static PixelPoint PlaceToolbar(
        PixelRect selection,
        double toolbarWidth,
        double toolbarHeight,
        PixelRect bounds,
        double gap,
        IReadOnlyList<PixelRect>? obstacles = null)
    {
        var x = selection.Left + (selection.Width - toolbarWidth) / 2;
        x = Math.Clamp(x, bounds.Left + gap, bounds.Right - toolbarWidth - gap);

        var below = selection.Bottom + gap;
        var above = selection.Top - gap - toolbarHeight;
        var candidates = new[]
        {
            new PixelPoint(x, below),
            new PixelPoint(x, above),
            new PixelPoint(selection.Right + gap, selection.Bottom - toolbarHeight),
            new PixelPoint(selection.Left - gap - toolbarWidth, selection.Bottom - toolbarHeight),
        };
        var first = candidates[0];
        foreach (var candidate in candidates)
        {
            var clamped = ClampToolbar(candidate, toolbarWidth, toolbarHeight, bounds, gap);
            first = clamped;
            if (!Intersects(clamped, toolbarWidth, toolbarHeight, selection)
                && !(obstacles ?? []).Any(obstacle => Intersects(clamped, toolbarWidth, toolbarHeight, obstacle)))
            {
                return clamped;
            }
        }

        return ClampToolbar(first, toolbarWidth, toolbarHeight, bounds, gap);
    }

    private static PixelPoint ClampToolbar(
        PixelPoint point,
        double toolbarWidth,
        double toolbarHeight,
        PixelRect bounds,
        double gap) => new(
            Math.Clamp(point.X, bounds.Left + gap, bounds.Right - toolbarWidth - gap),
            Math.Clamp(point.Y, bounds.Top + gap, bounds.Bottom - toolbarHeight - gap));

    private static bool Intersects(PixelPoint point, double width, double height, PixelRect other) =>
        point.X < other.Right && point.X + width > other.Left
        && point.Y < other.Bottom && point.Y + height > other.Top;
}

using ClipRay.Core.Geometry;

namespace ClipRay.App.LiveTranslation;

public sealed class LiveTranslationSelectionCoordinator(IReadOnlyList<PixelRect> monitors)
{
    private readonly IReadOnlyList<PixelRect> _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));

    public bool IsClosed { get; private set; }

    public bool TryComplete(
        PixelPoint pointerDown,
        PixelPoint pointerUp,
        out PixelRect region,
        out string error)
    {
        region = default;
        error = string.Empty;
        var downMonitor = FindMonitor(pointerDown);
        var upMonitor = FindMonitor(pointerUp);
        if (downMonitor is null || upMonitor is null || downMonitor.Value != upMonitor.Value)
        {
            error = "请在同一显示器内重新框选";
            return false;
        }

        var left = Math.Min(pointerDown.X, pointerUp.X);
        var top = Math.Min(pointerDown.Y, pointerUp.Y);
        var right = Math.Max(pointerDown.X, pointerUp.X);
        var bottom = Math.Max(pointerDown.Y, pointerUp.Y);
        if (right - left < 8 || bottom - top < 8)
        {
            error = "框选区域太小，请重新框选";
            return false;
        }

        region = new PixelRect(left, top, right - left, bottom - top);
        IsClosed = true;
        return true;
    }

    private PixelRect? FindMonitor(PixelPoint point)
    {
        foreach (var monitor in _monitors)
        {
            if (point.X >= monitor.X && point.X < monitor.X + monitor.Width
                && point.Y >= monitor.Y && point.Y < monitor.Y + monitor.Height)
                return monitor;
        }
        return null;
    }
}

public static class LiveTranslationCaptureVisibilityPolicy
{
    public const bool SelectedRegionBorderVisible = true;

    public static bool MustHideDuringCapture(bool displayAffinityApplied) => !displayAffinityApplied;
    public static bool CanStart(bool excludeFromCaptureRequested, bool displayAffinityApplied) =>
        !excludeFromCaptureRequested || displayAffinityApplied;
}

public static class LiveTranslationStopPlacement
{
    private const int Gap = 10;

    public static bool TryPlaceOutside(
        System.Drawing.Rectangle selected,
        System.Drawing.Rectangle workArea,
        System.Drawing.Size controlSize,
        out System.Drawing.Rectangle bounds)
    {
        var clampedY = Math.Clamp(
            selected.Top,
            workArea.Top,
            Math.Max(workArea.Top, workArea.Bottom - controlSize.Height));
        var clampedX = Math.Clamp(
            selected.Left,
            workArea.Left,
            Math.Max(workArea.Left, workArea.Right - controlSize.Width));
        var candidates = new[]
        {
            new System.Drawing.Rectangle(selected.Right + Gap, clampedY, controlSize.Width, controlSize.Height),
            new System.Drawing.Rectangle(selected.Left - Gap - controlSize.Width, clampedY, controlSize.Width, controlSize.Height),
            new System.Drawing.Rectangle(clampedX, selected.Top - Gap - controlSize.Height, controlSize.Width, controlSize.Height),
            new System.Drawing.Rectangle(clampedX, selected.Bottom + Gap, controlSize.Width, controlSize.Height),
        };
        foreach (var candidate in candidates)
        {
            if (workArea.Contains(candidate) && !candidate.IntersectsWith(selected))
            {
                bounds = candidate;
                return true;
            }
        }

        bounds = default;
        return false;
    }
}

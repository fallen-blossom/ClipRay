using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Overlay;

public sealed class UiAutomationSnapCandidateSource : IUiElementCandidateSource
{
    public IReadOnlyList<PixelRect> GetHierarchy(Point point, IntPtr rootWindow)
    {
        if (rootWindow == IntPtr.Zero) return [];
        try
        {
            var current = AutomationElement.FromHandle(rootWindow);
            var hierarchy = new List<PixelRect>();
            if (TryGetVisibleBounds(current, out var rootBounds)) hierarchy.Add(rootBounds);

            while (FindContainingChild(current, point) is { } child)
            {
                current = child;
                if (TryGetVisibleBounds(current, out var bounds)) hierarchy.Add(bounds);
            }

            hierarchy.Reverse();
            return hierarchy;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
            or InvalidOperationException
            or COMException)
        {
            return [];
        }
    }

    private static AutomationElement? FindContainingChild(AutomationElement parent, Point point)
    {
        var walker = TreeWalker.ControlViewWalker;
        for (var child = walker.GetFirstChild(parent); child is not null; child = walker.GetNextSibling(child))
        {
            if (TryGetVisibleBounds(child, out var bounds)
                && point.X >= bounds.Left && point.X <= bounds.Right
                && point.Y >= bounds.Top && point.Y <= bounds.Bottom)
            {
                return child;
            }
        }

        return null;
    }

    private static bool TryGetVisibleBounds(AutomationElement element, out PixelRect bounds)
    {
        bounds = default;
        if (element.Current.IsOffscreen) return false;
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty
            || !double.IsFinite(rect.X)
            || !double.IsFinite(rect.Y)
            || !double.IsFinite(rect.Width)
            || !double.IsFinite(rect.Height))
        {
            return false;
        }

        bounds = new PixelRect(rect.X, rect.Y, rect.Width, rect.Height);
        return !bounds.IsEmpty;
    }
}

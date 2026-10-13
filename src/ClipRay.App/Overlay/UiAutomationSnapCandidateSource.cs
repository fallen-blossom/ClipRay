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
            var walker=TreeWalker.ControlViewWalker;
            return CollectHierarchy(current,point,walker.GetFirstChild,walker.GetNextSibling,
                element=>TryGetVisibleBounds(element,out var bounds)?bounds:null);
        }
        catch (Exception exception) when (exception is ElementNotAvailableException
            or InvalidOperationException
            or COMException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<PixelRect> CollectHierarchy<T>(T root,Point point,Func<T,T?> firstChild,Func<T,T?> nextSibling,
        Func<T,PixelRect?> visibleBounds,int nodeBudget=128,int depthBudget=32) where T:class
    {
        var hierarchy=new List<PixelRect>();var current=root;var remaining=nodeBudget;
        if(visibleBounds(root) is { } rootBounds)hierarchy.Add(rootBounds);
        for(var depth=0;depth<depthBudget&&remaining>0;depth++)
        {
            T? containing=null;
            for(var child=firstChild(current);child is not null&&remaining-->0;child=remaining>0?nextSibling(child):null)
            {
                if(visibleBounds(child) is { } bounds&&point.X>=bounds.Left&&point.X<=bounds.Right&&point.Y>=bounds.Top&&point.Y<=bounds.Bottom)
                {containing=child;hierarchy.Add(bounds);break;}
            }
            if(containing is null)break;
            current=containing;
        }
        hierarchy.Reverse();return hierarchy;
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

using System.Windows.Input;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Overlay;

public enum OverlayKeyboardAction
{
    None,
    Confirm,
    Escape,
}

public static class OverlayKeyboardPolicy
{
    public static OverlayKeyboardAction Resolve(Key key, bool longCaptureActive) => key switch
    {
        Key.Enter when !longCaptureActive => OverlayKeyboardAction.Confirm,
        Key.Escape => OverlayKeyboardAction.Escape,
        _ => OverlayKeyboardAction.None,
    };
}

public static class OverlayMovePolicy
{
    public static bool ShouldBeginLockedMove(
        bool moveToolActive,
        PixelRect selection,
        PixelPoint point,
        double borderTolerance)
    {
        if (!moveToolActive || selection.IsEmpty) return false;
        var expanded = new PixelRect(
            selection.X - borderTolerance,
            selection.Y - borderTolerance,
            selection.Width + borderTolerance * 2,
            selection.Height + borderTolerance * 2);
        return point.X >= expanded.Left && point.X < expanded.Right
            && point.Y >= expanded.Top && point.Y < expanded.Bottom;
    }
}

public enum OverlaySelectionGesture
{
    None,
    Draw,
    Move,
    Resize,
    LockProposal,
}

public static class OverlaySelectionGesturePolicy
{
    public static OverlaySelectionGesture Resolve(
        bool hasSelection,
        bool moveToolActive,
        bool onBorder,
        bool onResizeHandle,
        bool isSnapProposal,
        double dragDistance)
    {
        if (onResizeHandle) return OverlaySelectionGesture.Resize;
        if (isSnapProposal) return OverlaySelectionGesture.LockProposal;
        if (moveToolActive && onBorder) return OverlaySelectionGesture.Move;
        if (!hasSelection || dragDistance >= 8) return OverlaySelectionGesture.Draw;
        return OverlaySelectionGesture.None;
    }
}

public static class OverlaySnapLockPolicy
{
    public readonly record struct LockResult(PixelRect Selection, bool IsProposal, bool GestureLocked);

    public static LockResult Lock(PixelRect snapped) => new(snapped, IsProposal: false, GestureLocked: true);

    public static PixelRect Resolve(bool latched, PixelRect snapped, PixelPoint point) =>
        latched ? snapped : SelectionMath.FromPoints(new PixelPoint(snapped.X, snapped.Y), point);
}

public enum OverlaySnapPressOutcome
{
    Pending,
    LockProposal,
    FreeDraw,
}

public sealed class OverlaySnapInteraction
{
    public bool IsTracking { get; private set; } = true;

    public long RequestVersion { get; private set; }

    public long BeginRefinement() => ++RequestVersion;

    public bool CanAcceptRefinement(long requestVersion) =>
        IsTracking && requestVersion == RequestVersion;

    public void StopTracking()
    {
        if (!IsTracking) return;
        IsTracking = false;
        RequestVersion++;
    }

    public static OverlaySnapPressOutcome ResolveProposalPress(
        PixelPoint start,
        PixelPoint current,
        bool released,
        double threshold)
    {
        if (WindowSnapService.HasMaterialDrag(start, current, threshold))
        {
            return OverlaySnapPressOutcome.FreeDraw;
        }

        return released ? OverlaySnapPressOutcome.LockProposal : OverlaySnapPressOutcome.Pending;
    }
}

public static class SnapProposalUpdatePolicy
{
    public static bool ShouldReplaceWithImmediate(
        PixelRect currentSelection,
        bool isCurrentProposal,
        PixelPoint pointer,
        PixelRect? immediateCandidate)
    {
        if (immediateCandidate is null) return false;
        if (!isCurrentProposal || currentSelection.IsEmpty) return true;

        // UIA 细化结果通常是窗口内部的小控件。鼠标仍在该控件内时保留它，
        // 避免每次 MouseMove 都先回退到整窗、随后又跳回控件而产生抖动。
        return pointer.X < currentSelection.Left
            || pointer.X > currentSelection.Right
            || pointer.Y < currentSelection.Top
            || pointer.Y > currentSelection.Bottom;
    }
}

public static class SelectionSizePolicy
{
    public static PixelRect ResizeAroundCenter(PixelRect selection, double width, double height, PixelRect bounds)
    {
        width = Math.Clamp(width, 1, bounds.Width);
        height = Math.Clamp(height, 1, bounds.Height);
        var centerX = selection.X + selection.Width / 2;
        var centerY = selection.Y + selection.Height / 2;
        var x = Math.Clamp(centerX - width / 2, bounds.Left, bounds.Right - width);
        var y = Math.Clamp(centerY - height / 2, bounds.Top, bounds.Bottom - height);
        return new PixelRect(x, y, width, height);
    }
}

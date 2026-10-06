using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Overlay;

public interface IWindowCandidateSource
{
    IEnumerable<WindowSnapCandidate> Enumerate();
}

public interface IUiElementCandidateSource
{
    IReadOnlyList<PixelRect> GetHierarchy(Point point, IntPtr rootWindow);
}

public enum SnapDetectionMode
{
    ElementFirst,
    WindowFirst,
}

public static class SnapDetectionModePolicy
{
    public static SnapDetectionMode Resolve(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Control) != 0
            ? SnapDetectionMode.ElementFirst
            : SnapDetectionMode.WindowFirst;
}

public readonly record struct WindowSnapCandidate(
    IntPtr Handle,
    PixelRect Bounds,
    bool IsClipRayOwned,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsToolWindow,
    string WindowClass);

public sealed class WindowSnapService
{
    private readonly PixelRect _frozenMonitorBounds;
    private readonly IReadOnlyList<WindowSnapCandidate> _windowCandidates;
    private readonly IUiElementCandidateSource _uiElementSource;

    public WindowSnapService(
        PixelRect frozenMonitorBounds,
        IWindowCandidateSource? candidateSource = null,
        IUiElementCandidateSource? uiElementSource = null)
    {
        _frozenMonitorBounds = frozenMonitorBounds;
        _windowCandidates = (candidateSource ?? new NativeWindowCandidateSource()).Enumerate().ToArray();
        _uiElementSource = uiElementSource ?? new UiAutomationSnapCandidateSource();
    }

    public PixelRect? TryGetCandidate(Point point, IntPtr overlayHwnd)
        => TryGetWindowCandidate(point, overlayHwnd)?.Bounds
            ?? (Contains(_frozenMonitorBounds, point) ? _frozenMonitorBounds : null);

    public IReadOnlyList<PixelRect> GetCandidates(Point point, IntPtr overlayHwnd, SnapDetectionMode mode)
    {
        var window = TryGetWindowCandidate(point, overlayHwnd);
        var elements = _uiElementSource.GetHierarchy(point, window?.Handle ?? IntPtr.Zero)
            .Where(candidate => IsFinite(candidate)
                && !candidate.IsEmpty
                && Contains(candidate, point))
            .Select(candidate => Intersect(candidate, _frozenMonitorBounds))
            .Where(candidate => !candidate.IsEmpty && Contains(candidate, point));
        var windows = window is { } windowValue ? new[] { windowValue.Bounds } : [];
        var ordered = mode == SnapDetectionMode.ElementFirst
            ? elements.Concat(windows)
            : windows.Concat(elements);
        var candidates = ordered.Distinct().ToArray();
        return candidates.Length > 0 || !Contains(_frozenMonitorBounds, point)
            ? candidates
            : [_frozenMonitorBounds];
    }

    private WindowCandidateMatch? TryGetWindowCandidate(Point point, IntPtr overlayHwnd)
    {
        foreach (var candidate in _windowCandidates)
        {
            if (!IsEligible(candidate, overlayHwnd))
            {
                continue;
            }

            var bounded = Intersect(candidate.Bounds, _frozenMonitorBounds);
            if (!bounded.IsEmpty && Contains(bounded, point))
            {
                return new WindowCandidateMatch(candidate.Handle, bounded);
            }
        }
        return null;
    }

    private readonly record struct WindowCandidateMatch(IntPtr Handle, PixelRect Bounds);

    public static bool HasMaterialDrag(PixelPoint start, PixelPoint current, double threshold) =>
        Math.Max(Math.Abs(current.X - start.X), Math.Abs(current.Y - start.Y)) >= threshold;

    private static bool IsEligible(WindowSnapCandidate candidate, IntPtr overlayHwnd) =>
        candidate.Handle != overlayHwnd
        && candidate.IsVisible
        && !candidate.IsMinimized
        && !candidate.IsCloaked
        && (!candidate.IsToolWindow || IsTaskbar(candidate.WindowClass))
        && candidate.WindowClass is not "Progman" and not "WorkerW";

    private static bool IsTaskbar(string windowClass) =>
        windowClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    private static bool Contains(PixelRect rect, Point point) =>
        point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

    private static bool IsFinite(PixelRect rect) =>
        double.IsFinite(rect.X)
        && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width)
        && double.IsFinite(rect.Height);

    private static PixelRect Intersect(PixelRect first, PixelRect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private sealed class NativeWindowCandidateSource : IWindowCandidateSource
    {
        private const int DwmaExtendedFrameBounds = 9;
        private const int DwmaCloaked = 14;
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x00000080L;

        public IEnumerable<WindowSnapCandidate> Enumerate()
        {
            var candidates = new List<WindowSnapCandidate>();
            EnumWindows((handle, _) =>
            {
                if (!TryGetExtendedFrameBounds(handle, out var bounds))
                {
                    return true;
                }

                GetWindowThreadProcessId(handle, out var processId);
                DwmGetWindowAttribute(handle, DwmaCloaked, out int cloaked, sizeof(int));
                var windowClass = GetClassName(handle);
                var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
                candidates.Add(new WindowSnapCandidate(
                    handle,
                    bounds,
                    processId == Environment.ProcessId,
                    IsWindowVisible(handle),
                    IsIconic(handle),
                    cloaked != 0,
                    (exStyle & WsExToolWindow) != 0,
                    windowClass));
                return true;
            }, IntPtr.Zero);
            return candidates;
        }

        private static bool TryGetExtendedFrameBounds(IntPtr handle, out PixelRect bounds)
        {
            bounds = default;
            if (DwmGetWindowAttribute(handle, DwmaExtendedFrameBounds, out NativeRect rect, Marshal.SizeOf<NativeRect>()) != 0)
            {
                return false;
            }

            bounds = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return !bounds.IsEmpty;
        }

        private static string GetClassName(IntPtr handle)
        {
            var value = new StringBuilder(256);
            GetClassName(handle, value, value.Capacity);
            return value.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out NativeRect value, int valueSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);
    }
}

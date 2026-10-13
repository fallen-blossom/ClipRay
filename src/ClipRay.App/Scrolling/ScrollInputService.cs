using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipRay.App.Scrolling;

public interface IScrollInputService
{
    Task SendWheelAsync(Point physicalPixelTarget, int delta, CancellationToken cancellationToken);
}

public interface IScrollSettler
{
    Task WaitForSettleAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class DelayScrollSettler : IScrollSettler
{
    public Task WaitForSettleAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

/// <summary>只发送有界 wheel 输入；调用者负责提供已冻结区域的物理像素中心。</summary>
public sealed class ScrollInputService : IScrollInputService
{
    private const uint WmMouseWheel = 0x020A;
    private const int WheelDelta = -120;
    private readonly IScrollWindowNative _native;
    private readonly IntPtr _targetWindow;
    private ScrollWindowSnapshot? _targetSnapshot;
    private ScrollWindowSnapshot? _rootSnapshot;
    private readonly bool _bindingFailed;

    public ScrollInputService(IntPtr targetWindow = default, IScrollWindowNative? native = null)
    {
        _native = native ?? new WindowsScrollNative();
        _targetWindow = targetWindow;
        if (targetWindow != IntPtr.Zero)
            try { Bind(targetWindow); } catch (InvalidOperationException) { _bindingFailed=true; }
    }

    public Task SendWheelAsync(Point physicalPixelTarget, int delta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delta == 0) return Task.CompletedTask;
        if (_bindingFailed) throw new InvalidOperationException("The original scroll source is unavailable.");
        if (physicalPixelTarget.X is < short.MinValue or > short.MaxValue || physicalPixelTarget.Y is < short.MinValue or > short.MaxValue)
            throw new InvalidOperationException("Wheel coordinates exceed the signed Windows message range.");
        var atPoint = _native.WindowAt(physicalPixelTarget);
        if (_targetSnapshot is null) Bind(_targetWindow != IntPtr.Zero ? _targetWindow : atPoint);
        var expected = _targetSnapshot!;
        var root = _rootSnapshot!;
        var current = _native.Observe(expected.Handle);
        var currentRoot = _native.Observe(root.Handle);
        var receiver = _native.Observe(atPoint);
        if (current != expected || currentRoot != root || !root.Bounds.Contains(physicalPixelTarget)
            || !expected.Visible || expected.Minimized || !root.Visible || root.Minimized
            || receiver is null || !receiver.Visible || receiver.Minimized || receiver.ProcessId != root.ProcessId || !receiver.Bounds.Contains(physicalPixelTarget)
            || !(atPoint == root.Handle || _native.IsChild(root.Handle, atPoint)))
            throw new InvalidOperationException("The capture source moved, changed, or is covered; automatic scrolling was paused.");
        var wheelData = unchecked((ushort)delta);
        var wParam = new IntPtr((long)wheelData << 16);
        var lParam = new IntPtr((long)(ushort)physicalPixelTarget.X | ((long)(ushort)physicalPixelTarget.Y << 16));
        cancellationToken.ThrowIfCancellationRequested();
        if (!_native.Post(atPoint, WmMouseWheel, wParam, lParam))
            throw new InvalidOperationException("The automatic scroll request was rejected by Windows.");
        return Task.CompletedTask;
    }

    public static int DefaultWheelDelta => WheelDelta;

    public static IntPtr ResolveTarget(Point physicalPixelTarget) => WindowFromPoint(physicalPixelTarget);

    private void Bind(IntPtr handle)
    {
        var target = _native.Observe(handle);
        var root = _native.Observe(_native.Root(handle));
        if (target is null || root is null || !target.Visible || target.Minimized || !root.Visible || root.Minimized)
            throw new InvalidOperationException("No valid source window exists at the capture region center.");
        _targetSnapshot = target;
        _rootSnapshot = root;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private sealed class WindowsScrollNative : IScrollWindowNative
    {
        public IntPtr WindowAt(Point point) => WindowFromPoint(point);
        public IntPtr Root(IntPtr handle) => GetAncestor(handle, 2);
        public bool IsChild(IntPtr parent, IntPtr child) => NativeIsChild(parent, child);
        public bool Post(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam) => PostMessage(handle,message,wParam,lParam);
        public ScrollWindowSnapshot? Observe(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !IsWindow(handle) || !GetWindowRect(handle,out var rect)) return null;
            var thread = GetWindowThreadProcessId(handle,out var process);
            var className = new StringBuilder(256);
            GetClassName(handle,className,className.Capacity);
            return new(handle,process,thread,Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom),
                IsWindowVisible(handle),IsIconic(handle),className.ToString());
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle,out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle,uint flag);
    [DllImport("user32.dll",EntryPoint="IsChild")] private static extern bool NativeIsChild(IntPtr parent,IntPtr child);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr handle,StringBuilder name,int maximum);
}

public sealed record ScrollWindowSnapshot(IntPtr Handle,uint ProcessId,uint ThreadId,Rectangle Bounds,bool Visible,bool Minimized,string WindowClass);
public interface IScrollWindowNative
{
    ScrollWindowSnapshot? Observe(IntPtr handle);
    IntPtr WindowAt(Point point);
    IntPtr Root(IntPtr handle);
    bool IsChild(IntPtr parent,IntPtr child);
    bool Post(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
}

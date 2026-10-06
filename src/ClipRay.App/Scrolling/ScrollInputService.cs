using System.Drawing;
using System.Runtime.InteropServices;

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
    private readonly IntPtr _targetWindow;

    public ScrollInputService(IntPtr targetWindow = default) => _targetWindow = targetWindow;

    public Task SendWheelAsync(Point physicalPixelTarget, int delta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delta == 0) return Task.CompletedTask;
        var target = _targetWindow != IntPtr.Zero ? _targetWindow : WindowFromPoint(physicalPixelTarget);
        if (target == IntPtr.Zero)
        {
            throw new InvalidOperationException("No foreground target exists at the capture region center.");
        }
        var wheelData = unchecked((ushort)delta);
        var wParam = new IntPtr((long)wheelData << 16);
        var lParam = new IntPtr((long)(ushort)physicalPixelTarget.X | ((long)(ushort)physicalPixelTarget.Y << 16));
        if (!PostMessage(target, WmMouseWheel, wParam, lParam))
            throw new InvalidOperationException("The automatic scroll request was rejected by Windows.");
        return Task.CompletedTask;
    }

    public static int DefaultWheelDelta => WheelDelta;

    public static IntPtr ResolveTarget(Point physicalPixelTarget) => WindowFromPoint(physicalPixelTarget);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

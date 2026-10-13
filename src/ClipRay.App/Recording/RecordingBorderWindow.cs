using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClipRay.App.Recording;

public sealed class RecordingBorderWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly Rectangle _region;

    public RecordingBorderWindow(Rectangle region)
    {
        _region = region;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x4D, 0x4F)),
            BorderThickness = new Thickness(2),
            Background = System.Windows.Media.Brushes.Transparent,
        };
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Width = 1;
        Height = 1;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        ApplyExtendedStyles(handle);
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        var dwmResult = DwmIsCompositionEnabled(out var dwmCompositionEnabled);
        AffinityApplied = SetWindowDisplayAffinity(handle, RecordingBorderPolicy.DisplayAffinity);
        var affinityErrorCode = AffinityApplied ? 0 : Marshal.GetLastWin32Error();
        var visibleBounds = RecordingBorderPlacement.VisibleBounds(_region);
        PlacementApplied = SetWindowPos(
            handle,
            HwndTopmost,
            visibleBounds.X,
            visibleBounds.Y,
            visibleBounds.Width,
            visibleBounds.Height,
            0x0010 | 0x0020);
        var placementErrorCode = PlacementApplied ? 0 : Marshal.GetLastWin32Error();
        Diagnostics = new RecordingBorderDiagnostics(
            AffinityApplied,
            affinityErrorCode,
            PlacementApplied,
            placementErrorCode,
            dwmResult >= 0 && dwmCompositionEnabled,
            (extendedStyle & WsExLayered) != 0,
            extendedStyle);
    }

    public bool AffinityApplied { get; private set; }

    public bool PlacementApplied { get; private set; }

    public bool IsCaptureSafe => AffinityApplied && PlacementApplied;

    public RecordingBorderDiagnostics Diagnostics { get; private set; } = RecordingBorderDiagnostics.NotStarted;

    private static void ApplyExtendedStyles(IntPtr handle)
    {
        var current = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(current | (long)RecordingBorderPolicy.ExtendedWindowStyle));
    }

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));
    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr handle, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
}

public static class RecordingBorderPlacement
{
    public static Rectangle VisibleBounds(Rectangle recordingRegion)
    {
        if (recordingRegion.Width <= 0 || recordingRegion.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recordingRegion));
        }

        // 边框绘制在窗口内侧；区域贴住屏幕边缘时不会被显示器边界裁掉。
        return recordingRegion;
    }
}

public sealed record RecordingBorderDiagnostics(
    bool AffinityApplied,
    int AffinityErrorCode,
    bool PlacementApplied,
    int PlacementErrorCode,
    bool DwmCompositionEnabled,
    bool IsLayeredWindow,
    long ExtendedWindowStyle,
    string? ProbeError = null)
{
    public static RecordingBorderDiagnostics NotStarted { get; } = new(false, -1, false, -1, false, false, 0);
}

public static class RecordingBorderProbeStore
{
    public static string Write(string dataRoot, RecordingBorderDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Directory.CreateDirectory(dataRoot);
        var path = Path.Combine(dataRoot, "recording-border-probe.json");
        var json = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true });
        if (!new ClipRay.App.Diagnostics.LocalDiagnosticSession(dataRoot).Json(path,diagnostics,append:false))
            throw new IOException("Diagnostic budget.");
        return path;
    }
}

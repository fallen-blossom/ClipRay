using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ClipRay.App.Recording;

public sealed record VideoRecordingSample(
    byte[] BgraPixels,
    int Width,
    int Height,
    long Timestamp100Nanoseconds,
    long Duration100Nanoseconds);

public interface IGraphicsCaptureSource : IAsyncDisposable
{
    Task StartAsync(Func<VideoRecordingSample, ValueTask> onSample, CancellationToken cancellationToken);

    Task StopAsync();
}

public sealed class GraphicsCaptureSource : IGraphicsCaptureSource
{
    private readonly Rectangle _sourceBounds;
    private readonly Rectangle _crop;
    private readonly RecordingTimeline _timeline;
    private readonly Func<GraphicsCaptureItem> _createItem;
    private readonly bool _includeCursor;
    private readonly bool _enforceSourceSize;
    private readonly Action<Exception>? _onFault;
    private Func<VideoRecordingSample, ValueTask>? _onSample;
    private ID3D11Device? _nativeDevice;
    private ID3D11DeviceContext? _context;
    private ID3D11Texture2D? _staging;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private RecordingVideoPump? _videoPump;
    private readonly RecordingFramePacer _pacer = new(RecordingFrameRatePolicy.FramesPerSecond, Stopwatch.Frequency);
    private int _frameBusy;
    private int _started;
    private int _stopped;

    public GraphicsCaptureSource(
        RecordingMonitorMapping mapping,
        RecordingTimeline timeline,
        bool normalizeForEncoder = true)
        : this(
            mapping.MonitorBounds,
            normalizeForEncoder
                ? RecordingGeometry.NormalizeForEncoder(mapping.CropInMonitor)
                : mapping.CropInMonitor,
            timeline,
            () => GraphicsCaptureInterop.CreateForMonitor(MonitorFromRectangle(mapping.MonitorBounds)),
            RecordingCapturePolicy.IncludeCursor,
            enforceSourceSize: false,
            onFault: null)
    {
    }

    private GraphicsCaptureSource(
        Rectangle sourceBounds,
        Rectangle crop,
        RecordingTimeline timeline,
        Func<GraphicsCaptureItem> createItem,
        bool includeCursor,
        bool enforceSourceSize,
        Action<Exception>? onFault)
    {
        _sourceBounds = sourceBounds;
        _crop = crop;
        if (_crop.Width <= 0 || _crop.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(crop));
        }
        _timeline = timeline;
        _createItem = createItem;
        _includeCursor = includeCursor;
        _enforceSourceSize = enforceSourceSize;
        _onFault = onFault;
    }

    public static GraphicsCaptureSource ForWindow(
        IntPtr windowHandle,
        Rectangle windowBounds,
        Rectangle screenRegion,
        RecordingTimeline timeline,
        Action<Exception>? onFault = null)
    {
        if (windowHandle == IntPtr.Zero) throw new ArgumentOutOfRangeException(nameof(windowHandle));
        var crop = WindowCaptureGeometry.MapScreenRegion(windowBounds, windowBounds.Size, screenRegion);
        return new GraphicsCaptureSource(
            windowBounds,
            crop,
            timeline,
            () => GraphicsCaptureInterop.CreateForWindow(windowHandle),
            includeCursor: false,
            enforceSourceSize: true,
            onFault);
    }

    public Task StartAsync(Func<VideoRecordingSample, ValueTask> onSample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onSample);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Graphics capture already started.");
        if (!GraphicsCaptureSession.IsSupported()) throw new PlatformNotSupportedException("Windows.Graphics.Capture is unavailable.");
        _onSample = onSample;
        CreateDevices();
        _item = _createItem();
        _item.Closed += OnItemClosed;
        if (_item.Size.Width != _sourceBounds.Width || _item.Size.Height != _sourceBounds.Height)
        {
            throw new InvalidOperationException($"WGC source size {_item.Size.Width}x{_item.Size.Height} does not match physical bounds {_sourceBounds.Width}x{_sourceBounds.Height}.");
        }
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _item.Size);
        _framePool.FrameArrived += OnFrameArrived;
        _captureSession = _framePool.CreateCaptureSession(_item);
        _captureSession.IsCursorCaptureEnabled = _includeCursor;
        _videoPump = new RecordingVideoPump(onSample);
        _captureSession.StartCapture();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        if (_framePool is not null) _framePool.FrameArrived -= OnFrameArrived;
        // The callback checks stopped again after acquiring busy, so no new GPU
        // access can begin after this wait and no devices close during a readback.
        while (Volatile.Read(ref _frameBusy) != 0) await Task.Delay(1).ConfigureAwait(false);
        _captureSession?.Dispose();
        _captureSession = null;
        _framePool?.Dispose();
        _framePool = null;
        if (_item is not null) _item.Closed -= OnItemClosed;
        if (_videoPump is not null) await _videoPump.CompleteAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _item = null;
            try { _staging?.Dispose(); }
            finally
            {
                _staging = null;
                try
                {
                    if (_winrtDevice is not null && Marshal.IsComObject(_winrtDevice))
                        Marshal.FinalReleaseComObject(_winrtDevice);
                }
                finally
                {
                    _winrtDevice = null;
                    try { _context?.Dispose(); }
                    finally
                    {
                        _context = null;
                        _nativeDevice?.Dispose();
                        _nativeDevice = null;
                    }
                }
            }
        }
    }

    private void CreateDevices()
    {
        _nativeDevice = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        _context = _nativeDevice.ImmediateContext;
        using var dxgiDevice = _nativeDevice.QueryInterface<IDXGIDevice>();
        var result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var winrtPointer);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtPointer);
        }
        finally
        {
            Marshal.Release(winrtPointer);
        }
        _staging = _nativeDevice.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)_sourceBounds.Width,
            (uint)_sourceBounds.Height,
            1,
            1,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read,
            1,
            0,
            ResourceOptionFlags.None));
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (Volatile.Read(ref _stopped) != 0 || Interlocked.Exchange(ref _frameBusy, 1) != 0) return;
        try
        {
            if (Volatile.Read(ref _stopped) != 0) return;
            using var frame = sender.TryGetNextFrame();
            var qpc = Stopwatch.GetTimestamp();
            if (frame is null || !_pacer.ShouldCapture(qpc)) return;
            if (_enforceSourceSize &&
                (frame.ContentSize.Width != _sourceBounds.Width || frame.ContentSize.Height != _sourceBounds.Height))
            {
                ReportFault(new InvalidOperationException("window-capture-geometry-changed"));
                return;
            }
            var access = WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccess>(frame.Surface);
            var texturePointer = access.GetInterface(typeof(ID3D11Texture2D).GUID);
            using var texture = new ID3D11Texture2D(texturePointer);
            _context!.CopyResource(_staging!, texture);
            _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
            byte[] pixels;
            try
            {
                var destinationStride = _crop.Width * 4;
                pixels = new byte[destinationStride * _crop.Height];
                for (var row = 0; row < _crop.Height; row++)
                {
                    var source = IntPtr.Add(mapped.DataPointer, checked((_crop.Y + row) * (int)mapped.RowPitch + _crop.X * 4));
                    Marshal.Copy(source, pixels, row * destinationStride, destinationStride);
                }
            }
            finally
            {
                _context.Unmap(_staging!, 0);
            }

            var timestamp = _timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, qpc);
            var sample = new VideoRecordingSample(pixels, _crop.Width, _crop.Height, timestamp, RecordingFrameRatePolicy.SampleDuration100Nanoseconds);
            _videoPump!.TryPost(sample);
        }
        catch (Exception exception) when (_onFault is not null)
        {
            ReportFault(new InvalidOperationException("window-capture-frame-failed", exception));
        }
        finally
        {
            Volatile.Write(ref _frameBusy, 0);
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) =>
        ReportFault(new InvalidOperationException("window-capture-source-closed"));

    private void ReportFault(Exception exception)
    {
        try { _onFault?.Invoke(exception); }
        catch { /* A fault observer cannot escape the WGC callback. */ }
    }

    private static IntPtr MonitorFromRectangle(Rectangle rectangle)
    {
        var nativeRectangle = new NativeRectangle(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        var monitor = MonitorFromRect(ref nativeRectangle, 2);
        return monitor != IntPtr.Zero ? monitor : throw new InvalidOperationException("Unable to resolve the recording monitor.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRectangle(int Left, int Top, int Right, int Bottom);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRectangle rectangle, uint flags);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface(in Guid iid);
}

internal static class GraphicsCaptureInterop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForMonitor(IntPtr monitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = interop.CreateForMonitor(monitor, GraphicsCaptureItemGuid);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public static GraphicsCaptureItem CreateForWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = interop.CreateForWindow(window, GraphicsCaptureItemGuid);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
    }
}

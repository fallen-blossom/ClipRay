using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
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
    long Duration100Nanoseconds) : IDisposable
{
    private Action? _release;
    private int _disposed;
    public ID3D11Texture2D? GpuTexture { get; init; }
    internal VideoRecordingSample Own(Action release) { _release = release; return this; }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Interlocked.Exchange(ref _release, null)?.Invoke(); }
        finally { GpuTexture?.Dispose(); }
    }
}

public interface IGraphicsCaptureSource : IAsyncDisposable
{
    void RequestStop() { }
    Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
    private readonly RecordingFramePacer _pacer;
    private readonly long _sampleDuration;
    private readonly Func<ID3D11Device?>? _sharedDevice;
    private bool _gpuFrames;
    private readonly object _lifetime = new();
    private Task? _prepareTask, _stopTask, _disposeTask;
    private readonly List<Exception> _releaseErrors = [];
    private readonly RecordingFaultSignal _faults = new();
    private RecordingPixelBuffers? _buffers;
    private int _frameBusy;
    private int _started;
    private int _stopped;
    public CaptureBorderAccessResult? BorderAccess { get; private set; }
    public bool SystemBorderRequired { get; private set; } = true;
    private long _arrivals, _busySkips, _pacingSkips, _captured, _readbackTicks, _readbackMaxTicks, _copyTicks;
    public RecordingCaptureStatistics Statistics => new(Interlocked.Read(ref _arrivals), Interlocked.Read(ref _busySkips),
        Interlocked.Read(ref _pacingSkips), Interlocked.Read(ref _captured),
        Interlocked.Read(ref _readbackTicks) * 1000d / Stopwatch.Frequency / Math.Max(1, Interlocked.Read(ref _captured)),
        Interlocked.Read(ref _readbackMaxTicks) * 1000d / Stopwatch.Frequency,
        Interlocked.Read(ref _copyTicks) * 1000d / Stopwatch.Frequency / Math.Max(1, Interlocked.Read(ref _captured)));

    public GraphicsCaptureSource(
        RecordingMonitorMapping mapping,
        RecordingTimeline timeline,
        bool normalizeForEncoder = true, Action<Exception>? onFault = null, bool reuseBuffers = false,
        RecordingVideoOptions? options = null, Func<ID3D11Device?>? sharedDevice = null)
        : this(
            mapping.MonitorBounds,
            normalizeForEncoder
                ? RecordingGeometry.NormalizeForEncoder(mapping.CropInMonitor)
                : mapping.CropInMonitor,
            timeline,
            () => GraphicsCaptureInterop.CreateForMonitor(MonitorFromRectangle(mapping.MonitorBounds)),
            options?.IncludeCursor ?? RecordingCapturePolicy.IncludeCursor,
            enforceSourceSize: true,
            onFault, options)
    {
        _sharedDevice = sharedDevice;
        if (reuseBuffers) _buffers = new RecordingPixelBuffers(checked(_crop.Width * _crop.Height * 4));
    }

    private GraphicsCaptureSource(
        Rectangle sourceBounds,
        Rectangle crop,
        RecordingTimeline timeline,
        Func<GraphicsCaptureItem> createItem,
        bool includeCursor,
        bool enforceSourceSize,
        Action<Exception>? onFault, RecordingVideoOptions? options = null)
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
        options ??= new();
        options.Validate();
        _pacer = new(options.FramesPerSecond, Stopwatch.Frequency);
        _sampleDuration = options.SampleDuration100Nanoseconds;
        _enforceSourceSize = enforceSourceSize;
        _onFault = onFault;
        _faults.Raised += fault => ReportFault(fault.Error);
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

    public Task PrepareAsync(CancellationToken cancellationToken)
    {
        lock (_lifetime) return _prepareTask ??= PrepareCoreAsync(cancellationToken);
    }

    private async Task PrepareCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _stopped) != 0) throw new OperationCanceledException();
        if (!GraphicsCaptureSession.IsSupported()) throw new PlatformNotSupportedException("Windows.Graphics.Capture is unavailable.");
        BorderAccess = await CaptureBorderAccess.GetAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_lifetime)
        {
        cancellationToken.ThrowIfCancellationRequested();
        if (_stopped != 0) throw new OperationCanceledException();
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
        if (BorderAccess?.Allowed == true)
        {
            try { _captureSession.IsBorderRequired = false; SystemBorderRequired = _captureSession.IsBorderRequired; }
            catch (Exception error) { BorderAccess = new("ApplyFailed", false, error.HResult); }
        }
        }
    }

    public async Task StartAsync(Func<VideoRecordingSample, ValueTask> onSample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onSample);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Graphics capture already started.");
        await PrepareAsync(cancellationToken).ConfigureAwait(false);
        lock (_lifetime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stopped != 0) throw new OperationCanceledException();
            _onSample = onSample;
            _videoPump = new RecordingVideoPump(onSample, _faults);
            _captureSession!.StartCapture();
        }
    }

    public void RequestStop() { Interlocked.Exchange(ref _stopped,1); _videoPump?.RequestStop(); }
    public Task StopAsync()
    {
        RequestStop();
        lock (_lifetime) return _stopTask ??= Task.Run(async () =>
        {
            var errors = _releaseErrors;
            if (_framePool is not null) _framePool.FrameArrived -= OnFrameArrived;
            while (Volatile.Read(ref _frameBusy) != 0) await Task.Delay(1).ConfigureAwait(false);
            void Release(Action action) { try { action(); } catch(Exception error) { errors.Add(error); } }
            Release(() => _captureSession?.Dispose()); _captureSession = null;
            Release(() => _framePool?.Dispose()); _framePool = null;
            if (_item is not null) _item.Closed -= OnItemClosed;
            try { if (_videoPump is not null) await _videoPump.CompleteAsync().ConfigureAwait(false); }
            catch(Exception error) { _faults.TrySet("video-drain",error); }
            if (errors.Count > 0) throw new AggregateException(errors);
        });
    }
    public ValueTask DisposeAsync() { lock (_lifetime) return new(_disposeTask ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        try { await StopAsync().ConfigureAwait(false); } catch { }
        var errors = _releaseErrors;
        void Release(Action action) { try { action(); } catch(Exception error) { errors.Add(error); } }
        _item = null;
        Release(() => _staging?.Dispose()); _staging = null;
        Release(() => { if (_winrtDevice is not null && Marshal.IsComObject(_winrtDevice)) Marshal.FinalReleaseComObject(_winrtDevice); }); _winrtDevice = null;
        Release(() => _context?.Dispose()); _context = null;
        Release(() => _nativeDevice?.Dispose()); _nativeDevice = null;
        if (errors.Count > 0) throw new AggregateException(errors);
    }

    private void CreateDevices()
    {
        var shared = _sharedDevice?.Invoke();
        _gpuFrames = shared is not null;
        _nativeDevice = shared?.QueryInterface<ID3D11Device>()
            ?? D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
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
        if (_gpuFrames) return;
        _staging = _nativeDevice.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm,
            (uint)_crop.Width,
            (uint)_crop.Height,
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
        if (Volatile.Read(ref _stopped) != 0) return;
        Interlocked.Increment(ref _arrivals);
        if (Interlocked.Exchange(ref _frameBusy, 1) != 0) { Interlocked.Increment(ref _busySkips); return; }
        byte[]? leased = null;
        try
        {
            if (Volatile.Read(ref _stopped) != 0) return;
            using var frame = sender.TryGetNextFrame();
            var qpc = Stopwatch.GetTimestamp();
            if (frame is null) return;
            if (!_pacer.ShouldCapture(qpc)) { Interlocked.Increment(ref _pacingSkips); return; }
            if (_enforceSourceSize &&
                (frame.ContentSize.Width != _sourceBounds.Width || frame.ContentSize.Height != _sourceBounds.Height))
            {
                ReportFault(new InvalidOperationException("graphics-capture-geometry-changed"));
                return;
            }
            var access = WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccess>(frame.Surface);
            var texturePointer = access.GetInterface(typeof(ID3D11Texture2D).GUID);
            using var texture = new ID3D11Texture2D(texturePointer);
            if (_gpuFrames)
            {
                var cropped = _nativeDevice!.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                    (uint)_crop.Width, (uint)_crop.Height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource,
                    ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
                VideoRecordingSample? gpuSample = null;
                var transferred = false;
                try
                {
                    _context!.CopySubresourceRegion(cropped, 0, 0, 0, 0, texture, 0,
                        new Box(_crop.Left, _crop.Top, 0, _crop.Right, _crop.Bottom, 1));
                    var gpuTimestamp = _timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, qpc);
                    gpuSample = new([], _crop.Width, _crop.Height, gpuTimestamp, _sampleDuration) { GpuTexture = cropped };
                    if (!_videoPump!.TryPost(gpuSample) && Volatile.Read(ref _stopped) == 0)
                        ReportFault(new InvalidOperationException("capture-video-admission-closed"));
                    transferred = true;
                    gpuSample = null;
                    Interlocked.Increment(ref _captured);
                }
                finally
                {
                    if (!transferred)
                    {
                        if (gpuSample is not null) gpuSample.Dispose();
                        else cropped.Dispose();
                    }
                }
                return;
            }
            // Read back only the selected pixels, rather than the full monitor for every frame.
            var readbackStart = Stopwatch.GetTimestamp();
            _context!.CopySubresourceRegion(_staging!, 0, 0, 0, 0, texture, 0,
                new Box(_crop.Left, _crop.Top, 0, _crop.Right, _crop.Bottom, 1));
            _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
            var readbackTicks = Stopwatch.GetTimestamp() - readbackStart;
            Interlocked.Add(ref _readbackTicks, readbackTicks);
            if (readbackTicks > _readbackMaxTicks) Interlocked.Exchange(ref _readbackMaxTicks, readbackTicks);
            var copyStart = Stopwatch.GetTimestamp();
            byte[] pixels;
            try
            {
                var destinationStride = _crop.Width * 4;
                pixels = _buffers is null ? new byte[checked(destinationStride * _crop.Height)] : _buffers.Rent()!;
                if (pixels is null) return;
                if (_buffers is not null) leased = pixels;
                for (var row = 0; row < _crop.Height; row++)
                {
                    var source = IntPtr.Add(mapped.DataPointer, checked(row * (int)mapped.RowPitch));
                    Marshal.Copy(source, pixels, row * destinationStride, destinationStride);
                }
            }
            finally
            {
                _context.Unmap(_staging!, 0);
            }
            Interlocked.Add(ref _copyTicks, Stopwatch.GetTimestamp() - copyStart);
            Interlocked.Increment(ref _captured);

            var timestamp = _timeline.GetTimestamp100Nanoseconds(RecordingStream.Video, qpc);
            var sample = new VideoRecordingSample(pixels, _crop.Width, _crop.Height, timestamp, _sampleDuration);
            if (leased is not null) { var owned = leased; sample.Own(() => _buffers!.Return(owned)); leased = null; }
            if (!_videoPump!.TryPost(sample) && Volatile.Read(ref _stopped) == 0)
                ReportFault(new InvalidOperationException("capture-video-admission-closed"));
        }
        catch (Exception exception)
        {
            ReportFault(new InvalidOperationException("graphics-capture-frame-failed", exception));
        }
        finally
        {
            if (leased is not null) _buffers!.Return(leased);
            Volatile.Write(ref _frameBusy, 0);
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    { if (Volatile.Read(ref _stopped) == 0) ReportFault(new InvalidOperationException("graphics-capture-source-closed")); }

    private void ReportFault(Exception exception)
    {
        RequestStop();
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

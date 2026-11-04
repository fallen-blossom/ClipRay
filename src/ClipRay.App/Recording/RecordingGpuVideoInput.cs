using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

namespace ClipRay.App.Recording;

/// <summary>Owns one shared D3D11 device and converts BGRA frames to encoder-ready NV12 surfaces.</summary>
internal sealed class RecordingGpuVideoInput : IDisposable
{
    private readonly int _width, _height;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11VideoDevice? _videoDevice;
    private ID3D11VideoContext1? _videoContext;
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _processor;
    private IMFDXGIDeviceManager? _manager;
    private BindFlags _encoderBindFlags;
    private uint _frameIndex;

    public ID3D11Device Device => _device ?? throw new ObjectDisposedException(nameof(RecordingGpuVideoInput));
    public IMFDXGIDeviceManager Manager => _manager ?? throw new ObjectDisposedException(nameof(RecordingGpuVideoInput));

    private RecordingGpuVideoInput(int width, int height) { _width = width; _height = height; }

    public static RecordingGpuVideoInput Create(int width, int height, int framesPerSecond)
    {
        var input = new RecordingGpuVideoInput(width, height);
        try
        {
            input.Initialize(framesPerSecond);
            return input;
        }
        catch { input.Dispose(); throw; }
    }

    private void Initialize(int framesPerSecond)
    {
        _device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport);
        _context = _device.ImmediateContext;
        using (var multithread = _context.QueryInterface<ID3D11Multithread>())
        {
            multithread.SetMultithreadProtected(true);
            if (!multithread.GetMultithreadProtected()) throw new NotSupportedException("D3D11 multithread protection is unavailable.");
        }
        _manager = MediaFactory.MFCreateDXGIDeviceManager();
        _manager.ResetDevice(_device).CheckError();
        _videoDevice = _device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = _context.QueryInterface<ID3D11VideoContext1>();
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational((uint)framesPerSecond, 1),
            InputWidth = (uint)_width,
            InputHeight = (uint)_height,
            OutputFrameRate = new Rational((uint)framesPerSecond, 1),
            OutputWidth = (uint)_width,
            OutputHeight = (uint)_height,
            Usage = VideoUsage.OptimalSpeed
        });
        if ((_enumerator.CheckVideoProcessorFormat(Format.B8G8R8A8_UNorm) & VideoProcessorFormatSupport.Input) == 0
            || (_enumerator.CheckVideoProcessorFormat(Format.NV12) & VideoProcessorFormatSupport.Output) == 0)
            throw new NotSupportedException("The D3D11 video processor cannot convert BGRA to NV12.");
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);
        _videoContext.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);
        _videoContext.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
        var bounds = new RawRect(0, 0, _width, _height);
        _videoContext.VideoProcessorSetStreamSourceRect(_processor, 0, true, bounds);
        _videoContext.VideoProcessorSetStreamDestRect(_processor, 0, true, bounds);
        _videoContext.VideoProcessorSetOutputTargetRect(_processor, true, bounds);
        _videoContext.VideoProcessorSetStreamColorSpace1(_processor, 0, ColorSpaceType.RgbFullG22NoneP709);
        _videoContext.VideoProcessorSetOutputColorSpace1(_processor, ColorSpaceType.YcbcrStudioG22LeftP709);
    }

    // Driver-specific encoder texture requirements must be known before capture starts.
    public void SetEncoderBindFlags(BindFlags flags)
    {
        _encoderBindFlags = flags;
        using var source = Device.CreateTexture2D(Describe(Format.B8G8R8A8_UNorm, BindFlags.RenderTarget | BindFlags.ShaderResource));
        using var converted = Convert(source);
        using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, converted, 0, false);
        buffer.CurrentLength = buffer.MaxLength;
    }

    public IMFSample CreateSample(VideoRecordingSample sample)
    {
        // Legacy byte samples remain supported even when GPU input was explicitly enabled.
        using var uploaded = sample.GpuTexture is null ? Upload(sample.BgraPixels) : null;
        var source = sample.GpuTexture ?? uploaded!;
        using var converted = Convert(source);
        var mediaSample = MediaFactory.MFCreateSample();
        try
        {
            // The media buffer AddRefs its texture, and the sample AddRefs the buffer. Every
            // frame owns a distinct NV12 texture; no pool reuses a surface still held by the MFT.
            using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, converted, 0, false);
            buffer.CurrentLength = buffer.MaxLength;
            mediaSample.AddBuffer(buffer);
            mediaSample.SampleTime = sample.Timestamp100Nanoseconds;
            mediaSample.SampleDuration = sample.Duration100Nanoseconds;
            return mediaSample;
        }
        catch { mediaSample.Dispose(); throw; }
    }

    private ID3D11Texture2D Upload(byte[] pixels)
    {
        if (pixels.Length != checked(_width * _height * 4)) throw new InvalidOperationException("Video sample dimensions do not match the GPU encoder.");
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return Device.CreateTexture2D(Describe(Format.B8G8R8A8_UNorm, BindFlags.RenderTarget | BindFlags.ShaderResource),
                new SubresourceData(pinned.AddrOfPinnedObject(), (uint)(_width * 4)));
        }
        finally { pinned.Free(); }
    }

    private ID3D11Texture2D Convert(ID3D11Texture2D source)
    {
        var description = source.Description;
        if (description.Width != _width || description.Height != _height || description.Format != Format.B8G8R8A8_UNorm
            || description.SampleDescription.Count != 1 || description.ArraySize != 1 || description.MipLevels != 1)
            throw new InvalidOperationException("GPU recording input must be a single BGRA texture matching the selected frame size.");
        var output = Device.CreateTexture2D(Describe(Format.NV12, BindFlags.RenderTarget | _encoderBindFlags));
        try
        {
            using var inputView = _videoDevice!.CreateVideoProcessorInputView(source, _enumerator!, new VideoProcessorInputViewDescription
            {
                FourCC = 0,
                ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 }
            });
            using var outputView = _videoDevice.CreateVideoProcessorOutputView(output, _enumerator!, new VideoProcessorOutputViewDescription
            {
                ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 }
            });
            var frameIndex = _frameIndex++;
            _videoContext!.VideoProcessorBlt(_processor!, outputView, frameIndex,
                [new VideoProcessorStream { Enable = true, OutputIndex = 0, InputFrameOrField = frameIndex, InputSurface = inputView }]).CheckError();
            // Submit the conversion before the asynchronous encoder consumes this texture.
            // Flush does not wait for GPU completion or copy pixels to the CPU.
            _context!.Flush();
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    private Texture2DDescription Describe(Format format, BindFlags flags) => new(format, (uint)_width, (uint)_height,
        1, 1, flags, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);

    public void Dispose()
    {
        // The backend releases the Sink Writer before this owner. Source owns its own device
        // reference, and capture has already drained before recorder retirement.
        _manager?.Dispose(); _manager = null;
        _processor?.Dispose(); _processor = null;
        _enumerator?.Dispose(); _enumerator = null;
        _videoContext?.Dispose(); _videoContext = null;
        _videoDevice?.Dispose(); _videoDevice = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
    }
}

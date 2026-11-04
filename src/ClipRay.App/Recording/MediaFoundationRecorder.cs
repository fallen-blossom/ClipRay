using System.Runtime.InteropServices;
using System.IO;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace ClipRay.App.Recording;

public interface IMediaFoundationRecorder : IAsyncDisposable
{
    RecordingStopReport? Report => null;
    RecordingEncoderDiagnostics? EncoderDiagnostics => null;
    ID3D11Device? SharedDevice => null;
    bool AcceptsGpuFrames => false;
    Task StartAsync(CancellationToken cancellationToken);

    ValueTask WriteVideoAsync(VideoRecordingSample sample);

    ValueTask WriteAudioAsync(AudioRecordingSample sample);

    Task StopAsync();
}

internal sealed class NativeRecordingEncoderBackend : IRecordingEncoderBackend
{
    private static readonly Guid H264Format = new("34363248-0000-0010-8000-00AA00389B71");
    private readonly RecordingOutputPaths _paths;
    private readonly int _width;
    private readonly int _height;
    private readonly AudioCaptureFormat? _audioFormat;
    private readonly RecordingVideoOptions _videoOptions;
    private readonly bool _preferGpuInput;
    private IMFSinkWriter? _writer;
    private RecordingGpuVideoInput? _gpuInput;
    private int _videoStream;
    private int _audioStream;
    private int _started;
    public bool BeganWriting { get; private set; }
    private bool _mediaFoundationStarted;
    public RecordingEncoderDiagnostics? EncoderDiagnostics { get; private set; }
    public ID3D11Device? SharedDevice => BeganWriting ? _gpuInput?.Device : null;
    public bool AcceptsGpuFrames => BeganWriting && _gpuInput is not null;

    public NativeRecordingEncoderBackend(
        RecordingOutputPaths paths,
        int width,
        int height,
        AudioCaptureFormat? audioFormat,
        RecordingVideoOptions? videoOptions = null,
        bool preferGpuInput = false)
    {
        if (width < 2 || (width & 1) != 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2 || (height & 1) != 0) throw new ArgumentOutOfRangeException(nameof(height));
        _paths = paths with
        {
            PartialPath = ClipRay.App.Infrastructure.LocalPathPolicy.RequireLocal(paths.PartialPath),
            FinalPath = ClipRay.App.Infrastructure.LocalPathPolicy.RequireLocal(paths.FinalPath),
        };
        _width = width;
        _height = height;
        _audioFormat = audioFormat;
        _videoOptions = videoOptions ?? new RecordingVideoOptions();
        _videoOptions.Validate();
        _preferGpuInput = preferGpuInput;
    }

    public void Start(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Media Foundation recorder already started.");
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.PartialPath)!);
        using (new FileStream(_paths.PartialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        RecordingStartupGuard.Execute(RecordingStartupStage.MediaFoundation, () => MediaFactory.MFStartup());
        _mediaFoundationStarted = true;
        string? gpuFallbackReason = null;
        if (_preferGpuInput)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _gpuInput = RecordingGpuVideoInput.Create(_width, _height, _videoOptions.FramesPerSecond);
                CreateWriter();
                ConfigureVideo(_writer!);
                EncoderDiagnostics = InspectEncoder();
                var encoder = EncoderDiagnostics.Transforms.SingleOrDefault(item => item.Category == TransformCategoryGuids.VideoEncoder);
                if (encoder is not { IsHardwareAccelerated: true, IsD3D11Aware: true })
                    throw new NotSupportedException("The selected H.264 encoder does not expose D3D11 hardware input.");
                _gpuInput.SetEncoderBindFlags(RecordingEncoderConfiguration.GetEncoderBindFlags(_writer!, _videoStream));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                gpuFallbackReason = $"{error.GetType().Name}:0x{unchecked((uint)error.HResult):X8}:{error.Message}";
                // No sample has been submitted and BeginWriting has not run. Release all
                // references before reopening the same reserved partial file with RGB32 input.
                ReleaseWriter();
                EncoderDiagnostics = null;
            }
        }
        if (_writer is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateWriter();
            RecordingStartupGuard.Execute(RecordingStartupStage.VideoEncoder, () => ConfigureVideo(_writer!));
            EncoderDiagnostics = InspectEncoder();
        }
        EncoderDiagnostics = EncoderDiagnostics! with
        {
            GpuInputRequested = _preferGpuInput,
            GpuInputEnabled = _gpuInput is not null,
            GpuFallbackReason = gpuFallbackReason
        };
        var activeWriter = _writer ?? throw new InvalidOperationException("Video encoder setup did not produce a Sink Writer.");
        if (_audioFormat is not null) RecordingStartupGuard.Execute(RecordingStartupStage.AudioEncoder, () => ConfigureAudio(activeWriter));
        RecordingStartupGuard.Execute(RecordingStartupStage.SinkWriterStart, activeWriter.BeginWriting);
        BeganWriting = true;
    }
    public void FinalizeEncoding() => _writer!.Finalize();
    private void CreateWriter()
    {
        using var attributes = CreateSinkWriterAttributes();
        if (_gpuInput is not null) attributes.Set(SinkWriterAttributeKeys.D3DManager, _gpuInput.Manager).CheckError();
        _writer = RecordingStartupGuard.Execute(RecordingStartupStage.Mp4SinkWriter,
            () => MediaFactory.MFCreateSinkWriterFromURL(_paths.PartialPath, null, attributes));
    }
    private RecordingEncoderDiagnostics InspectEncoder() => RecordingEncoderConfiguration.Configure(_writer!, _videoStream, _videoOptions,
        RecordingBitratePolicy.ForFrameSize(_width, _height, _videoOptions.FramesPerSecond, _videoOptions.Quality),
        _gpuInput is not null ? "NV12/D3D11" : "RGB32");
    public void ReleaseWriter()
    {
        _writer?.Dispose(); _writer = null;
        _gpuInput?.Dispose(); _gpuInput = null;
    }
    public void Shutdown()
    {
        if (_writer is not null) throw new InvalidOperationException("Writer still owns Media Foundation.");
        ShutdownMediaFoundation();
    }
    public void WriteVideo(VideoRecordingSample sample)
    {
        if (sample.Width != _width || sample.Height != _height)
            throw new InvalidOperationException("Video sample dimensions do not match the encoder.");
        if (_gpuInput is not null)
        {
            var writer = _writer ?? throw new InvalidOperationException("Recorder is not running.");
            using var nativeSample = _gpuInput.CreateSample(sample);
            writer.WriteSample(_videoStream, nativeSample);
            return;
        }
        if (sample.GpuTexture is not null || sample.BgraPixels.Length != checked(_width * _height * 4))
            throw new InvalidOperationException("The CPU encoder requires a complete BGRA byte sample.");
        Write(sample.BgraPixels,sample.Timestamp100Nanoseconds,sample.Duration100Nanoseconds,_videoStream);
    }
    public void WriteAudio(AudioRecordingSample sample)
    {
        if (_audioFormat is null || sample.SampleRate != _audioFormat.SampleRate || sample.Channels != _audioFormat.Channels
            || sample.BitsPerSample != _audioFormat.BitsPerSample || sample.PcmBytes.Length % _audioFormat.BlockAlign != 0)
            throw new InvalidOperationException("Audio sample format does not match the encoder.");
        Write(sample.PcmBytes,sample.Timestamp100Nanoseconds,sample.Duration100Nanoseconds,_audioStream);
    }
    private void Write(byte[] bytes,long timestamp,long duration,int stream)
    {
        var writer = _writer ?? throw new InvalidOperationException("Recorder is not running.");
        using var sample = CreateSample(bytes,timestamp,duration);
        writer.WriteSample(stream,sample);
    }

    /// <summary>Permit hardware MFT selection; the actual selected transform is reported separately.</summary>
    private static readonly Guid EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");

    /// <summary>Keep native backpressure; application queues and held samples are bounded separately.</summary>
    public static IMFAttributes CreateSinkWriterAttributes()
    {
        var attributes = MediaFactory.MFCreateAttributes(2);
        try
        {
            attributes.Set(EnableHardwareTransforms, 1u).CheckError();
            attributes.Set(SinkWriterAttributeKeys.LowLatency, 1u).CheckError();
            return attributes;
        }
        catch { attributes.Dispose(); throw; }
    }

    private void ConfigureVideo(IMFSinkWriter writer)
    {
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        output.Set(MediaTypeAttributeKeys.Subtype, H264Format);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, RecordingBitratePolicy.ForFrameSize(_width, _height, _videoOptions.FramesPerSecond, _videoOptions.Quality));
        output.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        output.Set(MediaTypeAttributeKeys.FrameSize, PackRatio((uint)_width, (uint)_height));
        output.Set(MediaTypeAttributeKeys.FrameRate, PackRatio((uint)_videoOptions.FramesPerSecond, 1));
        output.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackRatio(1, 1));
        if (_gpuInput is not null) SetNv12ColorSpace(output);
        _videoStream = writer.AddStream(output);

        using var input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        input.Set(MediaTypeAttributeKeys.Subtype, _gpuInput is not null ? VideoFormatGuids.NV12 : VideoFormatGuids.Rgb32);
        input.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        input.Set(MediaTypeAttributeKeys.FrameSize, PackRatio((uint)_width, (uint)_height));
        input.Set(MediaTypeAttributeKeys.FrameRate, PackRatio((uint)_videoOptions.FramesPerSecond, 1));
        input.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackRatio(1, 1));
        input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(_gpuInput is not null ? _width : _width * 4));
        if (_gpuInput is not null)
        {
            // Match the processor's RGB-full BT.709 -> YCbCr-studio BT.709 conversion.
            SetNv12ColorSpace(input);
        }
        writer.SetInputMediaType(_videoStream, input, null);
    }

    private static void SetNv12ColorSpace(IMFAttributes type)
    {
        type.Set(MediaTypeAttributeKeys.VideoPrimaries, 2u).CheckError();
        type.Set(MediaTypeAttributeKeys.TransferFunction, 5u).CheckError();
        type.Set(MediaTypeAttributeKeys.YuvMatrix, 1u).CheckError();
        type.Set(MediaTypeAttributeKeys.VideoNominalRange, 2u).CheckError();
    }

    private void ConfigureAudio(IMFSinkWriter writer)
    {
        if (_audioFormat is null) throw new InvalidOperationException("Audio stream not requested.");
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
        output.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)_audioFormat.Channels);
        output.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)_audioFormat.SampleRate);
        output.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        output.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24_000u);
        output.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 1u);
        output.Set(MediaTypeAttributeKeys.AacPayloadType, 0u);
        output.Set(MediaTypeAttributeKeys.AacAudioProfileLevelIndication, 0x29u);
        _audioStream = writer.AddStream(output);

        using var input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        input.Set(MediaTypeAttributeKeys.Subtype, _audioFormat.IsIeeeFloat ? AudioFormatGuids.Float : AudioFormatGuids.Pcm);
        input.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)_audioFormat.Channels);
        input.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)_audioFormat.SampleRate);
        input.Set(MediaTypeAttributeKeys.AudioBitsPerSample, (uint)_audioFormat.BitsPerSample);
        input.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)_audioFormat.BlockAlign);
        input.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(_audioFormat.SampleRate * _audioFormat.BlockAlign));
        input.Set(MediaTypeAttributeKeys.AllSamplesIndependent, 1u);
        writer.SetInputMediaType(_audioStream, input, null);
    }

    private static IMFSample CreateSample(byte[] bytes, long timestamp, long duration)
    {
        var sample = MediaFactory.MFCreateSample();
        try
        {
            using var buffer = MediaFactory.MFCreateMemoryBuffer(bytes.Length);
            buffer.Lock(out var pointer, out _, out _);
            try { Marshal.Copy(bytes,0,pointer,bytes.Length); buffer.CurrentLength = bytes.Length; }
            finally { buffer.Unlock(); }
            sample.AddBuffer(buffer); sample.SampleTime = timestamp; sample.SampleDuration = duration;
            return sample;
        }
        catch { sample.Dispose(); throw; }
    }

    private static ulong PackRatio(uint numerator, uint denominator) => ((ulong)numerator << 32) | denominator;

    private void ShutdownMediaFoundation()
    {
        if (!_mediaFoundationStarted) return;
        MediaFactory.MFShutdown();
        _mediaFoundationStarted = false;
    }
}

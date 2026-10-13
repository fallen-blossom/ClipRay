using System.Runtime.InteropServices;
using System.IO;
using Vortice.MediaFoundation;

namespace ClipRay.App.Recording;

public interface IMediaFoundationRecorder : IAsyncDisposable
{
    RecordingStopReport? Report => null;
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
    private IMFSinkWriter? _writer;
    private int _videoStream;
    private int _audioStream;
    private int _started;
    public bool BeganWriting { get; private set; }
    private bool _mediaFoundationStarted;

    public NativeRecordingEncoderBackend(
        RecordingOutputPaths paths,
        int width,
        int height,
        AudioCaptureFormat? audioFormat)
    {
        if (width < 2 || (width & 1) != 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2 || (height & 1) != 0) throw new ArgumentOutOfRangeException(nameof(height));
        _paths = paths;
        _width = width;
        _height = height;
        _audioFormat = audioFormat;
    }

    public void Start(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Media Foundation recorder already started.");
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.PartialPath)!);
        using (new FileStream(_paths.PartialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        RecordingStartupGuard.Execute(RecordingStartupStage.MediaFoundation, () => MediaFactory.MFStartup());
        _mediaFoundationStarted = true;
        using var attributes = CreateSinkWriterAttributes();
        _writer = RecordingStartupGuard.Execute(RecordingStartupStage.Mp4SinkWriter,
            () => MediaFactory.MFCreateSinkWriterFromURL(_paths.PartialPath, null, attributes));
        RecordingStartupGuard.Execute(RecordingStartupStage.VideoEncoder, () => ConfigureVideo(_writer));
        if (_audioFormat is not null) RecordingStartupGuard.Execute(RecordingStartupStage.AudioEncoder, () => ConfigureAudio(_writer));
        RecordingStartupGuard.Execute(RecordingStartupStage.SinkWriterStart, _writer.BeginWriting);
        BeganWriting = true;
    }
    public void FinalizeEncoding() => _writer!.Finalize();
    public void ReleaseWriter() { _writer?.Dispose(); _writer = null; }
    public void Shutdown()
    {
        if (_writer is not null) throw new InvalidOperationException("Writer still owns Media Foundation.");
        ShutdownMediaFoundation();
    }
    public void WriteVideo(VideoRecordingSample sample)
    {
        if (sample.Width != _width || sample.Height != _height || sample.BgraPixels.Length != checked(_width * _height * 4))
            throw new InvalidOperationException("Video sample dimensions do not match the encoder.");
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

    /// <summary>
    /// 允许 Sink Writer 选用 Media Foundation 硬件 MFT；2560x1600 八秒录制实测
    /// CPU 由 31s 降到 20s（软件编码路径）。
    /// </summary>
    private static readonly Guid EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");

    /// <summary>Keep native backpressure; application queues and held samples are bounded separately.</summary>
    public static IMFAttributes CreateSinkWriterAttributes()
    {
        var attributes = MediaFactory.MFCreateAttributes(1);
        try { attributes.Set(EnableHardwareTransforms, 1u); return attributes; }
        catch { attributes.Dispose(); throw; }
    }

    private void ConfigureVideo(IMFSinkWriter writer)
    {
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        output.Set(MediaTypeAttributeKeys.Subtype, H264Format);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, RecordingBitratePolicy.ForFrameSize(_width, _height, RecordingFrameRatePolicy.FramesPerSecond));
        output.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        output.Set(MediaTypeAttributeKeys.FrameSize, PackRatio((uint)_width, (uint)_height));
        output.Set(MediaTypeAttributeKeys.FrameRate, PackRatio((uint)RecordingFrameRatePolicy.FramesPerSecond, 1));
        output.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackRatio(1, 1));
        _videoStream = writer.AddStream(output);

        using var input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
        input.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        input.Set(MediaTypeAttributeKeys.FrameSize, PackRatio((uint)_width, (uint)_height));
        input.Set(MediaTypeAttributeKeys.FrameRate, PackRatio((uint)RecordingFrameRatePolicy.FramesPerSecond, 1));
        input.Set(MediaTypeAttributeKeys.PixelAspectRatio, PackRatio(1, 1));
        input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)(_width * 4));
        writer.SetInputMediaType(_videoStream, input, null);
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

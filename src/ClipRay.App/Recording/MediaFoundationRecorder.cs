using System.Runtime.InteropServices;
using System.IO;
using Vortice.MediaFoundation;

namespace ClipRay.App.Recording;

public interface IMediaFoundationRecorder : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    ValueTask WriteVideoAsync(VideoRecordingSample sample);

    ValueTask WriteAudioAsync(AudioRecordingSample sample);

    Task StopAsync();
}

public sealed class MediaFoundationRecorder : IMediaFoundationRecorder
{
    private static readonly Guid H264Format = new("34363248-0000-0010-8000-00AA00389B71");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RecordingOutputPaths _paths;
    private readonly int _width;
    private readonly int _height;
    private readonly AudioCaptureFormat _audioFormat;
    private IMFSinkWriter? _writer;
    private int _videoStream;
    private int _audioStream;
    private int _started;
    private int _stopped;
    private bool _mediaFoundationStarted;

    public MediaFoundationRecorder(
        RecordingOutputPaths paths,
        int width,
        int height,
        AudioCaptureFormat audioFormat)
    {
        if (width < 2 || (width & 1) != 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2 || (height & 1) != 0) throw new ArgumentOutOfRangeException(nameof(height));
        _paths = paths;
        _width = width;
        _height = height;
        _audioFormat = audioFormat;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Media Foundation recorder already started.");
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.PartialPath)!);
        using (new FileStream(_paths.PartialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            RecordingStartupGuard.Execute(RecordingStartupStage.MediaFoundation, () => MediaFactory.MFStartup());
            _mediaFoundationStarted = true;
            using var attributes = CreateSinkWriterAttributes();
            _writer = RecordingStartupGuard.Execute(
                RecordingStartupStage.Mp4SinkWriter,
                () => MediaFactory.MFCreateSinkWriterFromURL(_paths.PartialPath, null, attributes));
            RecordingStartupGuard.Execute(RecordingStartupStage.VideoEncoder, () => ConfigureVideo(_writer));
            RecordingStartupGuard.Execute(RecordingStartupStage.AudioEncoder, () => ConfigureAudio(_writer));
            RecordingStartupGuard.Execute(RecordingStartupStage.SinkWriterStart, _writer.BeginWriting);
            return Task.CompletedTask;
        }
        catch
        {
            try { _writer?.Dispose(); }
            catch { /* 清理失败不能覆盖初始化阶段的 HRESULT。 */ }
            _writer = null;
            try { ShutdownMediaFoundation(); }
            catch { /* 同上。 */ }
            throw;
        }
    }

    public ValueTask WriteVideoAsync(VideoRecordingSample sample) => WriteAsync(sample, true);

    public ValueTask WriteAudioAsync(AudioRecordingSample sample) => WriteAsync(sample, false);

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer is null) return;
            _writer.Finalize();
            _writer.Dispose();
            _writer = null;
            ShutdownMediaFoundation();
            new RecordingFileMover().Finalize(_paths);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _writer?.Dispose();
            _writer = null;
            ShutdownMediaFoundation();
            _gate.Dispose();
        }
    }

    private async ValueTask WriteAsync(object recordingSample, bool video)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var writer = _writer ?? throw new InvalidOperationException("Recorder is not running.");
            byte[] bytes;
            long timestamp;
            long duration;
            if (video)
            {
                var sample = (VideoRecordingSample)recordingSample;
                if (sample.Width != _width || sample.Height != _height || sample.BgraPixels.Length != _width * _height * 4)
                {
                    throw new InvalidOperationException("Video sample dimensions do not match the encoder.");
                }
                bytes = sample.BgraPixels;
                timestamp = sample.Timestamp100Nanoseconds;
                duration = sample.Duration100Nanoseconds;
            }
            else
            {
                var sample = (AudioRecordingSample)recordingSample;
                if (sample.SampleRate != _audioFormat.SampleRate || sample.Channels != _audioFormat.Channels || sample.BitsPerSample != _audioFormat.BitsPerSample)
                {
                    throw new InvalidOperationException("Audio sample format changed during recording.");
                }
                bytes = sample.PcmBytes;
                timestamp = sample.Timestamp100Nanoseconds;
                duration = sample.Duration100Nanoseconds;
            }

            using var mediaSample = CreateSample(bytes, timestamp, duration);
            writer.WriteSample(video ? _videoStream : _audioStream, mediaSample);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 允许 Sink Writer 选用 Media Foundation 硬件 MFT；2560x1600 八秒录制实测
    /// CPU 由 31s 降到 20s（软件编码路径）。
    /// </summary>
    private static readonly Guid EnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");

    /// <summary>
    /// 关掉 Sink Writer 节流。默认节流会在写入约 135 帧后把每次 WriteSample 拖成约 1 秒，
    /// 捕获被饿死、成片退化成十几 fps 的重复帧，停止排空也要等好几秒。
    /// 采集侧最多按 60fps 实时投喂，编码吸收一帧只要个位数毫秒，因此不需要它的背压。
    /// </summary>
    private static readonly Guid DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

    /// <summary>Creates the sink writer attributes used by every recording.</summary>
    public static IMFAttributes CreateSinkWriterAttributes()
    {
        var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(EnableHardwareTransforms, 1u);
        attributes.Set(DisableThrottling, 1u);
        return attributes;
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
        using var buffer = MediaFactory.MFCreateMemoryBuffer(bytes.Length);
        buffer.Lock(out var pointer, out _, out _);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            buffer.CurrentLength = bytes.Length;
        }
        finally
        {
            buffer.Unlock();
        }
        sample.AddBuffer(buffer);
        sample.SampleTime = timestamp;
        sample.SampleDuration = duration;
        return sample;
    }

    private static ulong PackRatio(uint numerator, uint denominator) => ((ulong)numerator << 32) | denominator;

    private void ShutdownMediaFoundation()
    {
        if (!_mediaFoundationStarted) return;
        MediaFactory.MFShutdown();
        _mediaFoundationStarted = false;
    }
}

using System.Diagnostics;
using NAudio.Wave;

namespace ClipRay.App.Recording;

public interface ISystemAudioCaptureFactory
{
    ISystemAudioCapture CreateDefaultRenderLoopback();
}

public interface ISystemAudioCapture : IAsyncDisposable
{
    AudioCaptureFormat Format { get; }

    Task StartAsync(Func<AudioRecordingSample, ValueTask> onSample, CancellationToken cancellationToken);

    Task StopAsync();
}

public sealed record AudioCaptureFormat(int SampleRate, int Channels, int BitsPerSample, bool IsIeeeFloat)
{
    public int BlockAlign => checked(Channels * BitsPerSample / 8);
}

public sealed record AudioRecordingSample(
    byte[] PcmBytes,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long Timestamp100Nanoseconds,
    long Duration100Nanoseconds);

public sealed class WasapiSystemAudioCaptureFactory : ISystemAudioCaptureFactory
{
    private readonly RecordingTimeline _timeline;

    public WasapiSystemAudioCaptureFactory(RecordingTimeline timeline)
    {
        _timeline = timeline;
    }

    public ISystemAudioCapture CreateDefaultRenderLoopback() => new WasapiSystemAudioCapture(_timeline);
}

public sealed class WasapiSystemAudioCapture : ISystemAudioCapture
{
    private readonly object _sync = new();
    private readonly RecordingTimeline _timeline;
    private readonly WasapiLoopbackCapture _capture;
    private Func<AudioRecordingSample, ValueTask>? _onSample;
    private Task _pendingWrites = Task.CompletedTask;
    private int _started;
    private int _stopped;

    public WasapiSystemAudioCapture(RecordingTimeline timeline)
    {
        _timeline = timeline;
        // 无参数构造固定为默认播放端点的 loopback；该 seam 不暴露任何麦克风端点入口。
        _capture = new WasapiLoopbackCapture();
        var waveFormat = _capture.WaveFormat;
        Format = new AudioCaptureFormat(
            waveFormat.SampleRate,
            waveFormat.Channels,
            waveFormat.BitsPerSample,
            waveFormat.Encoding == WaveFormatEncoding.IeeeFloat);
    }

    public AudioCaptureFormat Format { get; }

    public Task StartAsync(Func<AudioRecordingSample, ValueTask> onSample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onSample);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("System audio capture already started.");
        _onSample = onSample;
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        if (Volatile.Read(ref _started) != 0) _capture.StopRecording();
        Task pending;
        lock (_sync) pending = _pendingWrites;
        await pending.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _onSample is null || Volatile.Read(ref _stopped) != 0) return;
        var bytes = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
        var timestamp = _timeline.GetTimestamp100Nanoseconds(RecordingStream.Audio, Stopwatch.GetTimestamp());
        var sampleFrames = e.BytesRecorded / Format.BlockAlign;
        var duration = checked((long)((decimal)sampleFrames * 10_000_000m / Format.SampleRate));
        var sample = new AudioRecordingSample(
            bytes,
            Format.SampleRate,
            Format.Channels,
            Format.BitsPerSample,
            timestamp,
            duration);
        lock (_sync)
        {
            _pendingWrites = _pendingWrites.ContinueWith(
                async antecedent =>
                {
                    await antecedent.ConfigureAwait(false);
                    await _onSample(sample).ConfigureAwait(false);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }
    }
}

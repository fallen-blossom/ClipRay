using System.Diagnostics;
using NAudio.Wave;

namespace ClipRay.App.Recording;

public interface ISystemAudioCaptureFactory
{
    ISystemAudioCapture CreateDefaultRenderLoopback();
}

public interface ISystemAudioCapture : IAsyncDisposable
{
    void RequestStop() { }

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
    private readonly RecordingFaultSignal _faults;
    private RecordingAudioPump? _pump;
    private Task? _stopTask, _disposeTask;
    private int _started, _stopped;
    public WasapiSystemAudioCapture(RecordingTimeline timeline, RecordingFaultSignal? faults = null)
    {
        _timeline = timeline; _faults = faults ?? new();
        _capture = new WasapiLoopbackCapture();
        try
        {
            var format = _capture.WaveFormat;
            Format = new(format.SampleRate, format.Channels, format.BitsPerSample, format.Encoding == WaveFormatEncoding.IeeeFloat);
        }
        catch { _capture.Dispose(); throw; }
    }
    public AudioCaptureFormat Format { get; }
    public Task StartAsync(Func<AudioRecordingSample, ValueTask> onSample, CancellationToken token)
    {
        lock (_sync)
        {
            token.ThrowIfCancellationRequested();
            if (_started != 0 || _stopped != 0) throw new InvalidOperationException("Audio cannot restart.");
            _started = 1;
            _pump = new RecordingAudioPump(Format, onSample, _faults);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            return Task.CompletedTask;
        }
    }
    public void RequestStop() { Interlocked.Exchange(ref _stopped,1); _pump?.RequestStop(); }
    public Task StopAsync()
    {
        RequestStop();
        lock (_sync) return _stopTask ??= Task.Run(async () =>
        {
            Exception? failure = null;
            try { if (_started != 0) _capture.StopRecording(); } catch(Exception error) { failure = error; }
            if (_pump is not null) await _pump.CompleteAsync().ConfigureAwait(false);
            if (failure is not null) throw failure;
        });
    }
    public ValueTask DisposeAsync() { lock (_sync) return new(_disposeTask ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        try { await StopAsync().ConfigureAwait(false); } catch(Exception error) { _faults.TrySet("audio-stop",error); }
        _capture.DataAvailable -= OnDataAvailable; _capture.RecordingStopped -= OnRecordingStopped; _capture.Dispose();
    }
    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (args.BytesRecorded <= 0 || Volatile.Read(ref _stopped) != 0) return;
        try
        {
            var timestamp = _timeline.GetTimestamp100Nanoseconds(RecordingStream.Audio, Stopwatch.GetTimestamp());
            _pump?.TryPost(args.Buffer.AsSpan(0,args.BytesRecorded),timestamp);
        }
        catch(Exception error) { RequestStop(); _faults.TrySet("audio-capture",error); }
    }
    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null || Volatile.Read(ref _stopped) == 0)
        { RequestStop(); _faults.TrySet("audio-device",args.Exception ?? new InvalidOperationException("audio-device-stopped")); }
    }
}

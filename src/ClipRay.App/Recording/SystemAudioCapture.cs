using System.Diagnostics;
using NAudio.CoreAudioApi;
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
    private readonly MMDevice _device;
    private readonly WasapiLoopbackCapture _capture;
    private readonly WasapiOut _silence;
    private readonly RecordingFaultSignal _faults;
    private RecordingAudioPump? _pump;
    private Task? _stopTask, _disposeTask;
    private int _started, _stopped;
    public WasapiSystemAudioCapture(RecordingTimeline timeline, RecordingFaultSignal? faults = null)
    {
        _timeline = timeline; _faults = faults ?? new();
        using var devices = new MMDeviceEnumerator();
        _device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        WasapiLoopbackCapture? capture = null;
        WasapiOut? silence = null;
        try
        {
            capture = new WasapiLoopbackCapture(_device);
            var format = capture.WaveFormat;
            // WASAPI loopback emits no packets when the render endpoint is idle. An
            // empty audio stream makes the MP4 writer stall video interleaving.
            // NAudio's documented workaround keeps the SAME endpoint active with
            // digital silence; the existing capture clock and sample path stay intact.
            silence = new WasapiOut(_device, AudioClientShareMode.Shared, true, 100);
            silence.Init(new SilenceProvider(format));
            Format = new(format.SampleRate, format.Channels, format.BitsPerSample, format.Encoding == WaveFormatEncoding.IeeeFloat);
            _capture = capture;
            _silence = silence;
        }
        catch
        {
            try { silence?.Dispose(); }
            finally { try { capture?.Dispose(); } finally { _device.Dispose(); } }
            throw;
        }
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
            _silence.PlaybackStopped += OnSilenceStopped;
            _capture.StartRecording();
            _silence.Play();
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
            try { if (_started != 0) _silence.Stop(); } catch(Exception error) { failure = error; }
            try { if (_started != 0) _capture.StopRecording(); } catch(Exception error) { failure ??= error; }
            if (_pump is not null) await _pump.CompleteAsync().ConfigureAwait(false);
            if (failure is not null) throw failure;
        });
    }
    public ValueTask DisposeAsync() { lock (_sync) return new(_disposeTask ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        try { await StopAsync().ConfigureAwait(false); } catch(Exception error) { _faults.TrySet("audio-stop",error); }
        _capture.DataAvailable -= OnDataAvailable; _capture.RecordingStopped -= OnRecordingStopped;
        _silence.PlaybackStopped -= OnSilenceStopped;
        try { _silence.Dispose(); }
        finally { try { _capture.Dispose(); } finally { _device.Dispose(); } }
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
    private void OnSilenceStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null || Volatile.Read(ref _stopped) == 0)
        { RequestStop(); _faults.TrySet("audio-keepalive",args.Exception ?? new InvalidOperationException("audio-keepalive-stopped")); }
    }
}

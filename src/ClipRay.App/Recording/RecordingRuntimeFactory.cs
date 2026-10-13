namespace ClipRay.App.Recording;

/// <summary>Ownership begins before the first device constructor, including partial construction.</summary>
internal sealed class RecordingRuntimeFactory : IRecordingRuntime
{
    private readonly object _sync = new();
    private readonly Func<ISystemAudioCapture?> _createAudio;
    private readonly Func<AudioCaptureFormat?,IMediaFoundationRecorder> _createEncoder;
    private readonly Func<IGraphicsCaptureSource> _createVideo;
    private ISystemAudioCapture? _audio;
    private IMediaFoundationRecorder? _encoder;
    private IGraphicsCaptureSource? _video;
    private RegionRecordingRuntime? _inner;
    private Task? _start, _stop;
    private int _stopping;
    public RecordingFaultSignal Faults { get; }
    public RecordingStopReport? Report { get; private set; }
    internal RecordingRuntimeFactory(Func<ISystemAudioCapture?> audio,Func<AudioCaptureFormat?,IMediaFoundationRecorder> encoder,
        Func<IGraphicsCaptureSource> video,RecordingFaultSignal faults)
    { _createAudio=audio;_createEncoder=encoder;_createVideo=video;Faults=faults; }
    public Task StartAsync(CancellationToken token)
    {
        lock (_sync)
        {
            if (_start is not null || _stop is not null) throw new InvalidOperationException("Factory cannot restart.");
            return _start = Task.Run(async () =>
            {
                CheckStart(token);
                _audio = RecordingStartupGuard.Execute(RecordingStartupStage.AudioDevice,_createAudio);
                CheckStart(token); _encoder = _createEncoder(_audio?.Format);
                CheckStart(token); _video = _createVideo();
                CheckStart(token); _inner = new(_video,_audio,_encoder,Faults);
                await _inner.StartAsync(token).ConfigureAwait(false);
            });
        }
    }
    private void CheckStart(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (Volatile.Read(ref _stopping)!=0) throw new OperationCanceledException(); }
    public void RequestStop()
    { Interlocked.Exchange(ref _stopping,1); _inner?.RequestStop(); _video?.RequestStop(); _audio?.RequestStop(); }
    public Task StopAsync()
    {
        RequestStop();
        lock (_sync) return _stop ??= Task.Run(async () =>
        {
            try { if (_start is not null) await _start.ConfigureAwait(false); } catch { }
            if (_inner is not null) { await _inner.DisposeAsync().ConfigureAwait(false); Report=_inner.Report; return; }
            var errors = new List<string>();
            if (_audio is not null) await RecordingRelease.Attempt(errors,"audio-construction-release",_audio.DisposeAsync).ConfigureAwait(false);
            if (_video is not null) await RecordingRelease.Attempt(errors,"video-construction-release",_video.DisposeAsync).ConfigureAwait(false);
            if (_encoder is not null) await RecordingRelease.Attempt(errors,"encoder-construction-release",_encoder.DisposeAsync).ConfigureAwait(false);
            var report = _encoder?.Report ?? RecordingStopReport.Empty;
            Report = report with { CleanupErrors=report.CleanupErrors.Concat(errors).ToArray() };
        });
    }
    public ValueTask DisposeAsync() => new(StopAsync());
}

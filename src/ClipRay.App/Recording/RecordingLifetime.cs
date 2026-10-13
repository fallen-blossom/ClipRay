namespace ClipRay.App.Recording;

public sealed record RecordingFault(string Stage, Exception Error);

/// <summary>The first fault is authoritative; observers cannot break native callbacks.</summary>
public sealed class RecordingFaultSignal
{
    private RecordingFault? _first;
    public RecordingFault? First => Volatile.Read(ref _first);
    public event Action<RecordingFault>? Raised;
    public bool TrySet(string stage, Exception error)
    {
        var fault = new RecordingFault(stage, error);
        if (Interlocked.CompareExchange(ref _first, fault, null) is not null) return false;
        foreach (var handler in Raised?.GetInvocationList() ?? [])
            try { ((Action<RecordingFault>)handler)(fault); } catch { }
        return true;
    }
}

public sealed record RecordingStopReport(
    RecordingOutputPaths? Paths, bool Encoded, bool Saved, RecordingFault? FirstFault,
    IReadOnlyList<string> CleanupErrors, bool PhysicalCompleted = true)
{
    public bool ResourcesReleased => PhysicalCompleted && CleanupErrors.Count == 0;
    public static RecordingStopReport Empty { get; } = new(null, false, false, null, []);
}

internal static class RecordingRelease
{
    internal static async Task Attempt(List<string> errors, string stage, Func<ValueTask> release)
    {
        try { await release().ConfigureAwait(false); }
        catch (Exception error) { errors.Add($"{stage}:0x{unchecked((uint)error.HResult):X8}"); }
    }
}

public sealed class RecordingSession : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IRecordingRuntime _runtime;
    private readonly CancellationTokenSource _stop = new();
    private Task? _startTask;
    private Task? _stopTask;
    public RecordingSession(IRecordingRuntime runtime)
    {
        _runtime = runtime;
        Faults = runtime.Faults ?? new RecordingFaultSignal();
        Faults.Raised += OnFault;
    }
    public RecordingFaultSignal Faults { get; }
    public RecordingStopReport Report { get; private set; } = RecordingStopReport.Empty;
    public RecordingSessionState State { get; private set; } = RecordingSessionState.Created;
    private void OnFault(RecordingFault _) => RequestStop();
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_startTask is not null || _stopTask is not null) throw new InvalidOperationException("Recording cannot restart.");
            State = RecordingSessionState.Starting;
            return _startTask = Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
                try
                {
                    await _runtime.StartAsync(linked.Token).ConfigureAwait(false);
                    lock (_sync) { if (!_stop.IsCancellationRequested) State = RecordingSessionState.Recording; }
                }
                catch { lock (_sync) State = RecordingSessionState.Failed; throw; }
            });
        }
    }
    public void RequestStop()
    {
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        _runtime.RequestStop();
    }
    public Task StopAsync()
    {
        lock (_sync)
        {
            if (_stopTask is not null) return _stopTask;
            RequestStop();
            State = RecordingSessionState.Stopping;
            return _stopTask = Task.Run(StopCoreAsync);
        }
    }
    private async Task StopCoreAsync()
    {
        var errors = new List<string>();
        try { if (_startTask is not null) await _startTask.ConfigureAwait(false); } catch { }
        try { await _runtime.StopAsync().ConfigureAwait(false); }
        catch (Exception error) { Faults.TrySet("stop", error); }
        await RecordingRelease.Attempt(errors, "runtime-release", _runtime.DisposeAsync).ConfigureAwait(false);
        var report = _runtime.Report ?? RecordingStopReport.Empty;
        Report = report with { FirstFault = Faults.First ?? report.FirstFault, CleanupErrors = report.CleanupErrors.Concat(errors).ToArray() };
        State = Report.FirstFault is null ? RecordingSessionState.Stopped : RecordingSessionState.Failed;
        Faults.Raised -= OnFault;
        _stop.Dispose();
    }
    public ValueTask DisposeAsync() => new(StopAsync());
}

public sealed class RegionRecordingRuntime : IRecordingRuntime
{
    private readonly object _sync = new();
    private readonly IGraphicsCaptureSource _video;
    private readonly ISystemAudioCapture? _audio;
    private readonly IMediaFoundationRecorder _recorder;
    private Task? _startTask, _stopTask, _disposeTask;
    private int _stopping;
    public RegionRecordingRuntime(IGraphicsCaptureSource video, ISystemAudioCapture? audio,
        IMediaFoundationRecorder recorder, RecordingFaultSignal? faults = null)
    {
        _video = video; _audio = audio; _recorder = recorder;
        Faults = faults ?? new RecordingFaultSignal();
        Faults.Raised += OnFault;
    }
    public RecordingFaultSignal Faults { get; }
    public RecordingStopReport? Report { get; private set; }
    private void OnFault(RecordingFault _) => RequestStop();
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_startTask is not null || _stopTask is not null) throw new InvalidOperationException("Recording runtime cannot restart.");
            return _startTask = Task.Run(async () =>
            {
                CheckStart(cancellationToken);
                await _recorder.StartAsync(cancellationToken).ConfigureAwait(false);
                CheckStart(cancellationToken);
                // Audio availability is resolved before any desktop frame is admitted.
                if (_audio is not null) await RecordingStartupGuard.ExecuteAsync(RecordingStartupStage.SystemAudio,
                    () => _audio.StartAsync(_recorder.WriteAudioAsync, cancellationToken)).ConfigureAwait(false);
                CheckStart(cancellationToken);
                await RecordingStartupGuard.ExecuteAsync(RecordingStartupStage.GraphicsCapture,
                    () => _video.StartAsync(_recorder.WriteVideoAsync, cancellationToken)).ConfigureAwait(false);
                CheckStart(cancellationToken);
            });
        }
    }
    private void CheckStart(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _stopping) != 0) throw new OperationCanceledException("Recording is stopping.");
    }
    public void RequestStop()
    {
        Interlocked.Exchange(ref _stopping, 1);
        // Both admissions close before waiting for either physical source.
        try { _video.RequestStop(); } catch (Exception error) { Faults.TrySet("video-stop", error); }
        try { _audio?.RequestStop(); } catch (Exception error) { Faults.TrySet("audio-stop", error); }
    }
    public Task StopAsync()
    {
        lock (_sync)
        {
            RequestStop();
            return _stopTask ??= Task.Run(async () =>
            {
                try { if (_startTask is not null) await _startTask.ConfigureAwait(false); } catch { }
                async Task Stop(string stage, Func<Task> action)
                { try { await action().ConfigureAwait(false); } catch (Exception error) { Faults.TrySet(stage,error); } }
                await Task.WhenAll(Stop("video-drain", _video.StopAsync),
                    Stop("audio-drain", () => _audio?.StopAsync() ?? Task.CompletedTask)).ConfigureAwait(false);
                await Stop("encoder-finalize", _recorder.StopAsync).ConfigureAwait(false);
                Report = (_recorder.Report ?? RecordingStopReport.Empty) with { FirstFault = Faults.First };
            });
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync) return new(_disposeTask ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        await StopAsync().ConfigureAwait(false);
        var errors = new List<string>();
        if (_audio is not null) await RecordingRelease.Attempt(errors, "audio-release", _audio.DisposeAsync).ConfigureAwait(false);
        await RecordingRelease.Attempt(errors, "video-release", _video.DisposeAsync).ConfigureAwait(false);
        await RecordingRelease.Attempt(errors, "encoder-release", _recorder.DisposeAsync).ConfigureAwait(false);
        var report = _recorder.Report ?? Report ?? RecordingStopReport.Empty;
        Report = report with { FirstFault = Faults.First ?? report.FirstFault, CleanupErrors = report.CleanupErrors.Concat(errors).ToArray() };
        Faults.Raised -= OnFault;
    }
}

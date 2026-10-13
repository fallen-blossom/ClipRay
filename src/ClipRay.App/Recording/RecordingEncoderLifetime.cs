using ClipRay.App.Infrastructure;
using Vortice.MediaFoundation;

namespace ClipRay.App.Recording;

/// <summary>Narrow native seam. Only the production implementation calls Media Foundation.</summary>
public interface IRecordingEncoderBackend
{
    bool BeganWriting { get; }
    void Start(CancellationToken token);
    void WriteVideo(VideoRecordingSample sample);
    void WriteAudio(AudioRecordingSample sample);
    void FinalizeEncoding();
    void ReleaseWriter();
    void Shutdown();
}

public sealed class MediaFoundationRecorder : IMediaFoundationRecorder
{
    private readonly object _sync = new();
    private readonly AsyncResourceLifetime _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1,1);
    private readonly RecordingOutputPaths _paths;
    private readonly IRecordingEncoderBackend _backend;
    private readonly RecordingFaultSignal _faults;
    private Task? _startTask, _stopTask;
    public MediaFoundationRecorder(RecordingOutputPaths paths, int width, int height, AudioCaptureFormat? audioFormat)
        : this(paths, new NativeRecordingEncoderBackend(paths,width,height,audioFormat), new RecordingFaultSignal()) { }
    public MediaFoundationRecorder(RecordingOutputPaths paths, IRecordingEncoderBackend backend, RecordingFaultSignal faults)
    { _paths = paths; _backend = backend; _faults = faults; }
    public RecordingStopReport? Report { get; private set; }
    public Task StartAsync(CancellationToken token)
    {
        lock (_sync)
        {
            if (_startTask is not null) throw new InvalidOperationException("Encoder cannot restart.");
            var lease = _lifetime.Enter();
            return _startTask = Task.Run(async () =>
            {
                using (lease)
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try { token.ThrowIfCancellationRequested(); _backend.Start(token); token.ThrowIfCancellationRequested(); }
                    finally { _gate.Release(); }
                }
            });
        }
    }
    private ValueTask WriteAsync(Action write, string stage)
    {
        var lease = _lifetime.Enter();
        return new(Task.Run(async () =>
        {
            using (lease)
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try { write(); }
                catch(Exception error) { _faults.TrySet(stage,error); throw; }
                finally { _gate.Release(); }
            }
        }));
    }
    public ValueTask WriteVideoAsync(VideoRecordingSample sample) => WriteAsync(() => _backend.WriteVideo(sample),"video-encode");
    public ValueTask WriteAudioAsync(AudioRecordingSample sample) => WriteAsync(() => _backend.WriteAudio(sample),"audio-encode");
    public Task StopAsync()
    {
        lock (_sync) return _stopTask ??= _lifetime.RetireAsync(StopCoreAsync);
    }
    private Task StopCoreAsync()
    {
        var errors = new List<string>(); var encoded = false; var saved = false;
        try
        {
            if (_backend.BeganWriting) { _backend.FinalizeEncoding(); encoded = true; }
        }
        catch(Exception error) { _faults.TrySet("encoder-finalize",error); }
        void Release(string name, Action release)
        { try { release(); } catch(Exception error) { errors.Add($"{name}:0x{unchecked((uint)error.HResult):X8}"); } }
        Release("writer-release", _backend.ReleaseWriter);
        // The backend refuses MFShutdown while an unreleased writer still owns it.
        Release("mf-release", _backend.Shutdown);
        if (encoded)
        {
            try { new RecordingFileMover().Finalize(_paths); saved = true; }
            catch(Exception error) { _faults.TrySet("file-commit",error); }
        }
        _gate.Dispose();
        Report = new(_paths, encoded, saved, _faults.First, errors.ToArray());
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() => new(StopAsync());
    public static IMFAttributes CreateSinkWriterAttributes() => NativeRecordingEncoderBackend.CreateSinkWriterAttributes();
}

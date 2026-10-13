using System.Threading.Channels;

namespace ClipRay.App.Recording;

/// <summary>Single encoder consumer; at most two newest frames wait in memory.</summary>
public sealed class RecordingVideoPump
{
    private readonly Channel<VideoRecordingSample> _queue = Channel.CreateBounded<VideoRecordingSample>(
        new BoundedChannelOptions(2) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false }, sample => sample.Dispose());
    private readonly Task _consumer;
    private readonly RecordingFaultSignal? _faults;
    private int _closed;

    public RecordingVideoPump(Func<VideoRecordingSample, ValueTask> write) : this(write, null) { }
    public RecordingVideoPump(Func<VideoRecordingSample, ValueTask> write, RecordingFaultSignal? faults)
    {
        ArgumentNullException.ThrowIfNull(write);
        _faults = faults;
        _consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var sample in _queue.Reader.ReadAllAsync())
                    try { await write(sample).ConfigureAwait(false); } finally { sample.Dispose(); }
            }
            catch (Exception error)
            {
                RequestStop();
                _faults?.TrySet("video-write", error);
                throw;
            }
            finally { while (_queue.Reader.TryRead(out var sample)) sample.Dispose(); }
        });
        _ = _consumer.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public int PendingCount => _queue.Reader.Count;
    public bool TryPost(VideoRecordingSample sample)
    {
        if (_queue.Writer.TryWrite(sample)) return true;
        sample.Dispose();
        if (Volatile.Read(ref _closed) == 0) _faults?.TrySet("video-admission", new InvalidOperationException("video-queue-closed"));
        return false;
    }
    public void RequestStop() { Interlocked.Exchange(ref _closed, 1); _queue.Writer.TryComplete(); }
    public Task CompleteAsync() { RequestStop(); return _consumer; }
}

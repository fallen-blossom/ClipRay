using System.Threading.Channels;

namespace ClipRay.App.Recording;

/// <summary>Single encoder consumer; at most two newest frames wait in memory.</summary>
public sealed class RecordingVideoPump
{
    private readonly Channel<VideoRecordingSample> _queue = Channel.CreateBounded<VideoRecordingSample>(
        new BoundedChannelOptions(2) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false });
    private readonly Task _consumer;

    public RecordingVideoPump(Func<VideoRecordingSample, ValueTask> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        _consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var sample in _queue.Reader.ReadAllAsync())
                    await write(sample).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                _queue.Writer.TryComplete(error);
                throw;
            }
        });
    }

    public int PendingCount => _queue.Reader.Count;
    public bool TryPost(VideoRecordingSample sample) => _queue.Writer.TryWrite(sample);
    public async Task CompleteAsync()
    {
        _queue.Writer.TryComplete();
        await _consumer.ConfigureAwait(false);
    }
}

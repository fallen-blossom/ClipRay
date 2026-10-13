namespace ClipRay.App.Infrastructure;

// A cancellation request closes admission. Physical resources remain owned until
// every admitted operation has actually returned, even when its caller timed out.
internal sealed class AsyncResourceLifetime
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _closed;
    private Task? _retirement;
    internal CancellationToken Token => _stop.Token;
    internal IDisposable Enter()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _active++;
            return new Lease(this);
        }
    }
    internal Task RetireAsync(Func<Task> release)
    {
        lock (_gate)
        {
            if (_retirement is not null) return _retirement;
            _closed = true;
            if (_active == 0) _drained.TrySetResult();
            var cancellationSent = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = Task.Run(async () =>
            {
                var cancellationFailure = await cancellationSent.Task.ConfigureAwait(false);
                await RetireCoreAsync(release).ConfigureAwait(false);
                if (cancellationFailure is not null) throw new AggregateException(cancellationFailure);
            });
            // Publish the one retirement task before cancellation callbacks, but
            // do not allow an idle resource to dispose its CTS before Cancel.
            try { _stop.Cancel(); cancellationSent.TrySetResult(null); }
            catch (Exception error) { cancellationSent.TrySetResult(error); }
            return _retirement;
        }
    }
    private async Task RetireCoreAsync(Func<Task> release)
    {
        await _drained.Task.ConfigureAwait(false);
        try { await release().ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
    private void Leave()
    {
        lock (_gate) if (--_active == 0 && _closed) _drained.TrySetResult();
    }
    private sealed class Lease(AsyncResourceLifetime owner) : IDisposable
    {
        private AsyncResourceLifetime? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Leave();
    }
}

internal sealed record ResourceDrainResult(int Pending, IReadOnlyList<string> Failures)
{
    internal bool Completed => Pending == 0 && Failures.Count == 0;
}

internal sealed class ResourceRetirementRegistry
{
    private readonly object _gate = new();
    private readonly List<(string Name, Task Work)> _work = [];
    internal void Track(string name, Task work)
    {
        lock (_gate) _work.Add((name, work));
        _ = work.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    internal async Task<ResourceDrainResult> DrainAsync(TimeSpan budget)
    {
        (string Name, Task Work)[] work;
        lock (_gate) work = _work.ToArray();
        try { await Task.WhenAll(work.Select(w => w.Work)).WaitAsync(budget).ConfigureAwait(false); }
        catch { /* Individual outcomes below retain failures and physical pending owners. */ }
        return new(work.Count(w => !w.Work.IsCompleted), work.Where(w => w.Work.IsFaulted || w.Work.IsCanceled)
            .Select(w => w.Name + ":" + (w.Work.IsCanceled ? "retirement-cancelled"
                : w.Work.Exception!.GetBaseException().GetType().Name)).ToArray());
    }
}

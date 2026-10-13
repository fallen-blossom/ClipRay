using ClipRay.Core.Geometry;

namespace ClipRay.App.Overlay;

// Logical deadlines never release the physical provider-call slot.
public sealed class UiQueryScheduler
{
    internal static UiQueryScheduler Shared { get; } = new();
    private readonly object _gate=new();
    private Work? _pending;
    private Work? _active;
    private Task? _worker;
    private bool _closed;
    internal bool IsRetired { get { lock(_gate) return _closed; } }
    private sealed class Work(Func<IReadOnlyList<PixelRect>> query,CancellationToken token)
    {
        internal readonly Func<IReadOnlyList<PixelRect>> Query=query;
        internal readonly CancellationToken Token=token;
        internal readonly TaskCompletionSource<IReadOnlyList<PixelRect>> Result=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile bool Abandoned;
    }
    internal async Task<IReadOnlyList<PixelRect>> QueryAsync(Func<IReadOnlyList<PixelRect>> query,TimeSpan budget,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var work=new Work(query,token);
        lock(_gate)
        {
            if(_closed) return [];
            if(_pending is { } prior) {prior.Abandoned=true;prior.Result.TrySetResult([]);}
            _pending=work;
            _worker??=Task.Run(RunAsync);
        }
        try { return await work.Result.Task.WaitAsync(budget,token).ConfigureAwait(false); }
        catch(TimeoutException) { Abandon(work);return []; }
        catch(OperationCanceledException) { Abandon(work);throw; }
    }
    private void Abandon(Work work)
    {
        lock(_gate) {work.Abandoned=true;if(ReferenceEquals(_pending,work))_pending=null;work.Result.TrySetResult([]);}
    }
    private Task RunAsync()
    {
        while(true)
        {
            Work? work;
            lock(_gate)
            {
                work=_pending;_pending=null;
                if(work is null||_closed) {_worker=null;return Task.CompletedTask;}
                _active=work;
            }
            IReadOnlyList<PixelRect> result=[];
            try { if(!work.Abandoned&&!work.Token.IsCancellationRequested)result=work.Query(); }
            catch { /* Existing instant window proposal remains usable. */ }
            lock(_gate)
            {
                _active=null;
                work.Result.TrySetResult(_closed||work.Abandoned||work.Token.IsCancellationRequested?[]:result);
            }
        }
    }
    internal Task RetireAsync()
    {
        lock(_gate)
        {
            _closed=true;
            if(_pending is { } pending){pending.Abandoned=true;pending.Result.TrySetResult([]);_pending=null;}
            // Revoke delivery immediately; the original worker still owns the physical call.
            if(_active is { } active){active.Abandoned=true;active.Result.TrySetResult([]);}
            return _worker??Task.CompletedTask;
        }
    }
}

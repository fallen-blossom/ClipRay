using ClipRay.Core.Geometry;
using ClipRay.App.Settings;
using ClipRay.App.Translation;

namespace ClipRay.App.LiveTranslation;

internal sealed class LiveTranslationStartupCoordinator
{
    private LiveTranslationOperation? _current;
    private long _version;
    internal LiveTranslationOperation? Current => _current;
    internal bool IsCurrent(LiveTranslationOperation operation) => ReferenceEquals(_current, operation) && !operation.Token.IsCancellationRequested;
    internal async Task RunAsync(PixelRect? region, TranslationRouteKind route, AppSettings settings,
        string target, Func<LiveTranslationOperation, Task<PixelRect?>> select,
        Func<LiveTranslationOperation, Task> warmup,
        Func<LiveTranslationOperation, PixelRect, Task> start)
    {
        if (_current is not null) return;
        var operation = new LiveTranslationOperation(++_version, route, settings, target, IsCurrent);
        _current = operation;
        try
        {
            // Start required warmup during selection, with the same owner.
            var warming = operation.Observe(warmup(operation));
            var selected = region ?? await operation.AwaitAsync(select(operation));
            operation.CheckCurrent();
            if (selected is null) return;
            await operation.AwaitAsync(warming, TimeSpan.FromSeconds(12));
            operation.CheckCurrent();
            await operation.AwaitAsync(start(operation, selected.Value));
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_current, operation)) _current = null;
            operation.Finish();
        }
    }
    internal void Stop()
    {
        var previous = _current;
        _current = null; // Revoke eligibility before any callbacks or late finally.
        if (previous is not null) LiveTranslationWorkerRegistry.Register(previous.Completion);
        previous?.Stop();
    }
}

internal sealed class LiveTranslationOperation(long version, TranslationRouteKind route, AppSettings settings,
    string target, Func<LiveTranslationOperation, bool> isCurrent)
{
    private readonly CancellationTokenSource _source = new();
    private readonly List<Task> _owned = [];
    private Action? _closeSelection;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Completion => _finished.Task;
    internal long Version { get; } = version;
    internal TranslationRouteKind Route { get; } = route;
    internal AppSettings Settings { get; } = settings;
    internal string Target { get; } = target;
    internal CancellationToken Token => _source.Token;
    internal void OwnSelection(Action close) { _closeSelection = close; CheckCurrent(); }
    internal void CheckCurrent() { Token.ThrowIfCancellationRequested(); if (!isCurrent(this)) throw new OperationCanceledException(Token); }
    internal Task<T> Observe<T>(Task<T> task) { _owned.Add(task); return task; }
    internal Task Observe(Task task) { _owned.Add(task); return task; }
    internal async Task<T> AwaitAsync<T>(Task<T> task, TimeSpan? budget = null)
    { _ = Observe(task); var result = await (budget is { } limit ? task.WaitAsync(limit, Token) : task.WaitAsync(Token)); CheckCurrent(); return result; }
    internal async Task AwaitAsync(Task task, TimeSpan? budget = null)
    { _ = Observe(task); await (budget is { } limit ? task.WaitAsync(limit, Token) : task.WaitAsync(Token)); CheckCurrent(); }
    internal void Stop() { _source.Cancel(); _closeSelection?.Invoke(); _closeSelection = null; }
    internal void Finish()
    {
        Stop();
        var completion = Task.WhenAll(_owned);
        var cleanup = completion.ContinueWith(task => { _ = task.Exception; _source.Dispose(); _finished.TrySetResult(); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (!cleanup.IsCompleted) LiveTranslationWorkerRegistry.Register(Completion);
    }
}

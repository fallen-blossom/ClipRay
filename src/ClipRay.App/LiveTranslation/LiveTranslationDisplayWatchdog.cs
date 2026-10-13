namespace ClipRay.App.LiveTranslation;

// Display validity has its own clock; native completion never owns this deadline.
internal sealed class LiveTranslationDisplayWatchdog : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly Action<LiveTranslationPixelSnapshot?> _expire;
    private readonly ITimer _timer;
    private LiveTranslationPixelSnapshot? _latest;
    private long _capturedAt;
    private long? _unknownAt;
    private bool _disposed;
    private bool _captureExpired;
    private bool _semanticExpired;
    internal LiveTranslationDisplayWatchdog(TimeProvider clock, Action<LiveTranslationPixelSnapshot?> expire)
    {
        _clock = clock; _expire = expire; _capturedAt = clock.GetTimestamp();
        _timer = clock.CreateTimer(_ => Tick(), null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
    }
    internal void Captured(LiveTranslationPixelSnapshot snapshot)
    {
        lock (_gate) { _latest = snapshot; _capturedAt = _clock.GetTimestamp(); _captureExpired = false; }
    }
    internal void Unknown() { lock (_gate) _unknownAt ??= _clock.GetTimestamp(); }
    internal void Confirmed() { lock (_gate) { _unknownAt = null; _semanticExpired = false; } }
    private void Tick()
    {
        lock (_gate)
        {
            LiveTranslationPixelSnapshot? latest;
            if (_disposed) return;
            var now = _clock.GetTimestamp();
            if (!_captureExpired && _clock.GetElapsedTime(_capturedAt, now) >= TimeSpan.FromSeconds(2))
            { _captureExpired = true; latest = null; }
            else if (!_semanticExpired && _unknownAt is { } unknown
                && _clock.GetElapsedTime(unknown, now) >= TimeSpan.FromSeconds(2))
            { _semanticExpired = true; latest = _captureExpired ? null : _latest; }
            else return;
            _expire(latest);
        }
    }
    public void Dispose() { lock (_gate) _disposed = true; _timer.Dispose(); }
}

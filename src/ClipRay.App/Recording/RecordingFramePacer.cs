namespace ClipRay.App.Recording;

public sealed class RecordingFramePacer
{
    private readonly long _interval;
    private long? _next;

    public RecordingFramePacer(int framesPerSecond, long clockFrequency)
    {
        if (framesPerSecond <= 0 || clockFrequency < framesPerSecond) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        _interval = clockFrequency / framesPerSecond;
    }

    public bool ShouldCapture(long timestamp)
    {
        if (_next is long next && timestamp < next) return false;
        _next = _next is long deadline ? deadline + _interval : timestamp + _interval;
        // Keep cadence under refresh jitter, but do not burst after a long stall.
        if (_next <= timestamp) _next = timestamp + _interval;
        return true;
    }
}

namespace ClipRay.App.Recording;

public sealed class RecordingFramePacer
{
    private readonly long _interval;
    private readonly long _tolerance;
    private long? _next;

    public RecordingFramePacer(int framesPerSecond, long clockFrequency)
    {
        if (framesPerSecond <= 0 || clockFrequency < framesPerSecond) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        _interval = clockFrequency / framesPerSecond;
        _tolerance = Math.Max(1, _interval / 10);
    }

    public bool ShouldCapture(long timestamp)
    {
        // Present callbacks jitter around a refresh boundary. Rejecting a frame a fraction
        // early turns an otherwise 60Hz source into alternating 30/60fps capture.
        if (_next is long next && timestamp < next - _tolerance) return false;
        _next = _next is long deadline ? deadline + _interval : timestamp + _interval;
        // Keep cadence under refresh jitter, but do not burst after a long stall.
        if (_next <= timestamp) _next = timestamp + _interval;
        return true;
    }
}

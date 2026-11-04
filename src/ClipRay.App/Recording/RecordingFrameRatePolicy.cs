namespace ClipRay.App.Recording;

/// <summary>Default timing for callers without explicit per-recording options.</summary>
public static class RecordingFrameRatePolicy
{
    public const int FramesPerSecond = 60;

    public const long SampleDuration100Nanoseconds = 10_000_000L / FramesPerSecond;
}

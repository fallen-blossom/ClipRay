namespace ClipRay.App.Recording;

public sealed record RecordingCaptureStatistics(long Arrivals, long BusySkips, long PacingSkips, long Captured,
    double ReadbackMeanMilliseconds, double ReadbackMaxMilliseconds, double CopyMeanMilliseconds);

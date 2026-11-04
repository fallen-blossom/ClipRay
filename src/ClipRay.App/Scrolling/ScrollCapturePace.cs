namespace ClipRay.App.Scrolling;

/// <summary>Learn the source's wheel distance only from confirmed overlaps.</summary>
public sealed class ScrollCapturePace(int viewportHeight)
{
    private int _notches = 1;
    private double _maximumPixelsPerNotch;

    public int WheelDelta => ScrollInputService.DefaultWheelDelta * _notches;

    public void ObserveAccepted(int displacement, int sentDelta, double confidence)
    {
        if (displacement <= 0 || sentDelta == 0 || confidence < 0.9) { Reset(); return; }
        var sentNotches = Math.Abs(sentDelta / (double)ScrollInputService.DefaultWheelDelta);
        _maximumPixelsPerNotch = Math.Max(_maximumPixelsPerNotch, displacement / sentNotches);
        // Leave about half of the viewport as overlap. Keep even tall
        // captures well inside FrameStitcher's 1024-pixel search distance.
        var target = Math.Min(512, viewportHeight / 2d);
        var wanted = (int)Math.Clamp(Math.Floor(target / _maximumPixelsPerNotch), 1, 4);
        _notches = Math.Min(wanted, _notches + 1);
    }

    public void Reset() { _notches = 1; _maximumPixelsPerNotch = 0; }
}

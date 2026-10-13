namespace ClipRay.App.Recording;

public static class RecordingBitratePolicy
{
    private const double BitsPerPixelPerFrame = 0.055;
    private const uint Step = 500_000;
    private const uint Minimum = 2_500_000;
    private const uint Maximum = 8_000_000;

    public static uint ForFrameSize(int width, int height, int framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);

        var requested = (double)width * height * framesPerSecond * BitsPerPixelPerFrame;
        // Clamp in floating point before conversion; round upward to 0.5 Mbps.
        var rounded = (uint)(Math.Ceiling(Math.Clamp(requested, Minimum, Maximum) / Step) * Step);
        return Math.Clamp(rounded, Minimum, Maximum);
    }
}

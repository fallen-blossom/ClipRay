namespace ClipRay.App.Recording;

public static class RecordingBitratePolicy
{
    private const double BitsPerPixelPerFrame = 0.20;
    private const uint Step = 500_000;
    private const uint Minimum = 4_000_000;
    private const uint Maximum = 20_000_000;

    public static uint ForFrameSize(int width, int height, int framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);

        var requested = (double)width * height * framesPerSecond * BitsPerPixelPerFrame;
        var rounded = (uint)Math.Ceiling(requested / Step) * Step;
        return Math.Clamp(rounded, Minimum, Maximum);
    }
}

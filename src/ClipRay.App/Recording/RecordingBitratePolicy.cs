namespace ClipRay.App.Recording;

public static class RecordingBitratePolicy
{
    private const uint Step = 500_000;

    public static uint ForFrameSize(int width, int height, int framesPerSecond, RecordingQuality quality = RecordingQuality.Balanced)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerSecond);

        var (bitsPerPixel, minimum, maximum) = quality switch
        {
            RecordingQuality.Compact => (0.035, 1_000_000u, 5_000_000u),
            RecordingQuality.Balanced => (0.055, 2_500_000u, 8_000_000u),
            RecordingQuality.High => (0.20, 4_000_000u, 20_000_000u),
            _ => throw new ArgumentOutOfRangeException(nameof(quality)),
        };
        var requested = (double)width * height * framesPerSecond * bitsPerPixel;
        // Clamp in floating point before conversion; round upward to 0.5 Mbps.
        var rounded = (uint)(Math.Ceiling(Math.Clamp(requested, minimum, maximum) / Step) * Step);
        return Math.Clamp(rounded, minimum, maximum);
    }
}

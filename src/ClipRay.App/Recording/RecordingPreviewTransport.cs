namespace ClipRay.App.Recording;

/// <summary>Maps the preview seek bar between playback position and a draggable fraction.</summary>
public static class RecordingPreviewTransport
{
    public static TimeSpan PositionFromFraction(double fraction, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || double.IsNaN(fraction)) return TimeSpan.Zero;
        return TimeSpan.FromTicks((long)(duration.Ticks * Math.Clamp(fraction, 0d, 1d)));
    }

    public static double FractionFromPosition(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return 0d;
        if (position <= TimeSpan.Zero) return 0d;
        if (position >= duration) return 1d;
        return position.Ticks / (double)duration.Ticks;
    }

    public static string FormatPosition(TimeSpan position, TimeSpan duration) =>
        $"{Format(position)} / {Format(duration)}";

    private static string Format(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        var totalSeconds = (long)value.TotalSeconds;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
    }
}

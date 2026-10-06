using MediaColor = System.Windows.Media.Color;

namespace ClipRay.App.Feedback;

public readonly record struct FeedbackPulse(bool Visible, TimeSpan Duration);

public sealed record CaptureFeedbackPattern(MediaColor Color, IReadOnlyList<FeedbackPulse> Pulses)
{
    public static CaptureFeedbackPattern Success { get; } = new(
        MediaColor.FromRgb(0x1A, 0xA8, 0xFF),
        Array.AsReadOnly([new FeedbackPulse(true, TimeSpan.FromMilliseconds(150))]));

    public static CaptureFeedbackPattern Failure { get; } = new(
        MediaColor.FromRgb(0xFF, 0x4D, 0x4F),
        Array.AsReadOnly(
        [
            new FeedbackPulse(true, TimeSpan.FromMilliseconds(120)),
            new FeedbackPulse(false, TimeSpan.FromMilliseconds(90)),
            new FeedbackPulse(true, TimeSpan.FromMilliseconds(120)),
        ]));
}

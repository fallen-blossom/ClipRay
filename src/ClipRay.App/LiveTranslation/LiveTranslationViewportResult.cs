using System.Threading;

namespace ClipRay.App.LiveTranslation;

public enum LiveTranslationViewportResultState
{
    Pending,
    Ready,
    AcceptablePartial,
    Failed,
    Stale,
}

public sealed record LiveTranslationWorkIdentity(
    long CaptureId,
    long PipelineId,
    long ViewportGeneration);

public sealed record LiveTranslationViewportMetrics(
    int ExpectedLayouts,
    int CandidateLayouts,
    int RetainedLayouts,
    int RejectedLayouts,
    int UnreadableLayouts = 0,
    int OverlappingLayouts = 0,
    int OutOfBoundsLayouts = 0,
    int ExpectedPrimaryLayouts = 0,
    int RetainedPrimaryLayouts = 0,
    double ExpectedPrimaryArea = 0,
    double RetainedPrimaryArea = 0)
{
    public double Coverage => ExpectedLayouts <= 0 ? (RetainedLayouts == 0 ? 1 : 0)
        : Math.Clamp((double)RetainedLayouts / ExpectedLayouts, 0, 1);
    public double RejectionRate => ExpectedLayouts <= 0 ? 0
        : Math.Clamp((double)RejectedLayouts / ExpectedLayouts, 0, 1);
    public bool HasUnsafeLayout => UnreadableLayouts > 0 || OverlappingLayouts > 0 || OutOfBoundsLayouts > 0;
    public bool HasMissingPrimaryContent => ExpectedPrimaryLayouts > 0
            && (RetainedPrimaryLayouts <= 0
                || (double)RetainedPrimaryLayouts / ExpectedPrimaryLayouts < .5)
        || ExpectedPrimaryArea > 0 && RetainedPrimaryArea / ExpectedPrimaryArea < .4;
}

internal static class LiveTranslationViewportResultPolicy
{
    internal static LiveTranslationViewportResultState Classify(
        LiveTranslationFrame frame,
        int retainedLayouts) => Classify(frame, new LiveTranslationViewportMetrics(
            frame.ExpectedLayoutCount > 0 ? frame.ExpectedLayoutCount : frame.LayoutItems.Count,
            frame.LayoutItems.Count,
            retainedLayouts,
            frame.RejectedLayoutCount));

    internal static LiveTranslationViewportResultState Classify(
        LiveTranslationFrame frame,
        LiveTranslationViewportMetrics metrics)
    {
        if (frame.ResultState is LiveTranslationViewportResultState.Failed
            or LiveTranslationViewportResultState.Stale)
            return frame.ResultState;
        if (metrics.HasUnsafeLayout)
            return LiveTranslationViewportResultState.Failed;
        if (metrics.ExpectedLayouts == 0)
            return frame.ErrorCategory == "none"
                ? LiveTranslationViewportResultState.Ready
                : LiveTranslationViewportResultState.Failed;
        if (metrics.RetainedLayouts <= 0) return LiveTranslationViewportResultState.Failed;
        if (metrics.Coverage >= 1 && frame.ErrorCategory == "none")
            return LiveTranslationViewportResultState.Ready;
        // A missing translation remains readable in its original language.
        // Publish safe successful siblings even when they are a minority of the page.
        return LiveTranslationViewportResultState.AcceptablePartial;
    }

    internal static bool CanPresent(LiveTranslationViewportResultState state) =>
        state is LiveTranslationViewportResultState.Ready
            or LiveTranslationViewportResultState.AcceptablePartial;
}

internal static class LiveTranslationWorkContext
{
    private static readonly AsyncLocal<LiveTranslationWorkIdentity?> CurrentIdentity = new();
    internal static LiveTranslationWorkIdentity? Current => CurrentIdentity.Value;

    internal static IDisposable Push(LiveTranslationWorkIdentity identity)
    {
        var prior = CurrentIdentity.Value;
        CurrentIdentity.Value = identity;
        return new Scope(() => CurrentIdentity.Value = prior);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

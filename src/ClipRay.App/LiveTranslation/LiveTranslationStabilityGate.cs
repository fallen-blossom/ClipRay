using System.Drawing;

namespace ClipRay.App.LiveTranslation;

// Scrolling may use a bounded deadline so the newest viewport is eventually processed.
// Local animation has no deadline: only windows that actually settle can schedule work.
internal sealed class LiveTranslationStabilityGate
{
    private long? _motionStartedAt;
    private const long MaximumWaitMilliseconds = 750;

    public IReadOnlyList<Rectangle> Select(IReadOnlyList<Rectangle> windows,
        Func<Rectangle, bool> isUnchanged, long nowMilliseconds,
        bool allowUnsettledAfterDeadline = true)
    {
        if (windows.Count == 0)
        {
            _motionStartedAt = null;
            return [];
        }
        if (!allowUnsettledAfterDeadline)
        {
            _motionStartedAt = null;
            return windows.Where(isUnchanged).ToArray();
        }
        if (windows.All(isUnchanged))
        {
            _motionStartedAt = null;
            return windows;
        }
        _motionStartedAt ??= nowMilliseconds;
        if (nowMilliseconds - _motionStartedAt < MaximumWaitMilliseconds) return [];
        // The deadline belongs to the motion episode, not a crop rectangle. A
        // slowly moving page must eventually process the newest full snapshot.
        _motionStartedAt = null;
        return windows;
    }
}

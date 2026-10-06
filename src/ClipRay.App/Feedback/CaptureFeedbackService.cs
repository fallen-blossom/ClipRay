using System.Drawing;
using System.Windows.Threading;

namespace ClipRay.App.Feedback;

public sealed class CaptureFeedbackService : IDisposable
{
    private readonly HashSet<PulseSequence> _activeSequences = [];

    public void ShowSuccess(IEnumerable<Rectangle> bounds) => Show(CaptureFeedbackPattern.Success, bounds);

    public void ShowFailure(Rectangle bounds) => Show(CaptureFeedbackPattern.Failure, [bounds]);

    public void Dispose()
    {
        foreach (var sequence in _activeSequences.ToArray())
        {
            sequence.Dispose();
        }

        _activeSequences.Clear();
    }

    private void Show(CaptureFeedbackPattern pattern, IEnumerable<Rectangle> bounds)
    {
        PulseSequence? sequence = null;
        try
        {
            var physicalBounds = bounds.Where(static value => value.Width > 0 && value.Height > 0).ToArray();
            if (physicalBounds.Length == 0)
            {
                return;
            }

            sequence = new PulseSequence(pattern, physicalBounds);
            sequence.Completed += HandleSequenceCompleted;
            _activeSequences.Add(sequence);
            sequence.Start();
        }
        catch
        {
            if (sequence is not null)
            {
                sequence.Completed -= HandleSequenceCompleted;
                _activeSequences.Remove(sequence);
                sequence.Dispose();
            }

            // 反馈失败不能覆盖截图结果，也不能留下窗口。
        }
    }

    private void HandleSequenceCompleted(object? sender, EventArgs e)
    {
        if (sender is not PulseSequence sequence)
        {
            return;
        }

        sequence.Completed -= HandleSequenceCompleted;
        _activeSequences.Remove(sequence);
    }

    private sealed class PulseSequence : IDisposable
    {
        private readonly CaptureFeedbackPattern _pattern;
        private readonly List<ScreenEdgeFeedbackWindow> _windows = [];
        private readonly DispatcherTimer _timer;
        private int _pulseIndex;
        private bool _disposed;

        public PulseSequence(CaptureFeedbackPattern pattern, IEnumerable<Rectangle> bounds)
        {
            _pattern = pattern;
            _timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = pattern.Pulses[0].Duration,
            };
            _timer.Tick += OnTick;

            try
            {
                foreach (var bound in bounds)
                {
                    _windows.Add(new ScreenEdgeFeedbackWindow(bound, pattern.Color));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public event EventHandler? Completed;

        public void Start()
        {
            Apply(_pattern.Pulses[0]);
            _timer.Start();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            foreach (var window in _windows)
            {
                try
                {
                    window.Close();
                }
                catch
                {
                    // 单个 native handle 已失效时仍要继续关闭其他屏幕窗口。
                }
            }

            _windows.Clear();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                _timer.Stop();
                _pulseIndex++;
                if (_pulseIndex >= _pattern.Pulses.Count)
                {
                    Complete();
                    return;
                }

                var pulse = _pattern.Pulses[_pulseIndex];
                Apply(pulse);
                _timer.Interval = pulse.Duration;
                _timer.Start();
            }
            catch
            {
                Complete();
            }
        }

        private void Complete()
        {
            Dispose();
            Completed?.Invoke(this, EventArgs.Empty);
        }

        private void Apply(FeedbackPulse pulse)
        {
            foreach (var window in _windows)
            {
                if (pulse.Visible)
                {
                    window.Show();
                }
                else
                {
                    window.Hide();
                }
            }
        }
    }
}

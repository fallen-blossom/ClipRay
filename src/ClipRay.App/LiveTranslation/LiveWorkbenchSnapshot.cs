using System.Diagnostics;
using System.Drawing;

namespace ClipRay.App.LiveTranslation;

/// <summary>Owns only the latest raw capture, never pixels from the translated overlay.</summary>
public sealed class LiveWorkbenchSnapshot : IDisposable
{
    private readonly object _sync = new();
    private Bitmap? _pixels;
    private long _capturedAt;
    private bool _closed;

    public void Record(Bitmap source)
    {
        lock (_sync)
        {
            if (_closed) return;
            var next = (Bitmap)source.Clone();
            _pixels?.Dispose(); _pixels = next; _capturedAt = Stopwatch.GetTimestamp();
        }
    }
    public Bitmap? CopyFresh(TimeSpan maximumAge)
    {
        lock (_sync)
            return _closed || _pixels is null || Stopwatch.GetElapsedTime(_capturedAt) > maximumAge
                ? null : (Bitmap)_pixels.Clone();
    }
    public void Clear() { lock (_sync) { _pixels?.Dispose(); _pixels = null; } }
    public void Dispose() { lock (_sync) { _closed = true; Clear(); } }
}

using System.Drawing;

namespace ClipRay.App.Capture;

public sealed class CapturedFrame(
    Bitmap bitmap,
    Rectangle screenBounds,
    bool hasSystemCaptureFeedback = false) : IDisposable
{
    public Bitmap Bitmap { get; } = bitmap;

    public Rectangle ScreenBounds { get; } = screenBounds;

    public bool HasSystemCaptureFeedback { get; } = hasSystemCaptureFeedback;

    public void Dispose() => Bitmap.Dispose();
}

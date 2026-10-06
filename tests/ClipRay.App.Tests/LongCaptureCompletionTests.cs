using System.Drawing;
using System.IO;
using ClipRay.App.Scrolling;

internal static class LongCaptureCompletionTests
{
    public static void CompletionIsDistinctFromCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-long-complete-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var cancellation = new CancellationTokenSource();
            using var session = new ScrollCaptureSession(new Rectangle(0, 0, 16, 16), ScrollCaptureMode.Manual,
                new FixtureCapture(), new Input(), new Settler(), root);
            var running = session.RunAsync(cancellation.Token);
            session.RequestCompletion();
            cancellation.Cancel();
            using var result = running.GetAwaiter().GetResult();
            if (result.StopReason != ScrollCaptureStopReason.CompletedByUser)
                throw new Exception("Completing a long capture was reported as cancellation.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class FixtureCapture : IScrollRegionCapture
    {
        public Bitmap Capture(Rectangle region) => new(region.Width, region.Height);
    }

    private sealed class Input : IScrollInputService
    {
        public Task SendWheelAsync(Point target, int delta, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class Settler : IScrollSettler
    {
        public Task WaitForSettleAsync(TimeSpan delay, CancellationToken token) => Task.Delay(TimeSpan.FromSeconds(5), token);
    }
}

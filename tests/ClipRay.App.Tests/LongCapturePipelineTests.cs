using System.Drawing;
using System.IO;
using System.Text.Json;
using ClipRay.App.Scrolling;

internal static class LongCapturePipelineTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "clipray-long-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            BoundedPreviewRetainsExactFullOutput(root);
            NoMovementStopsWithinPollBudget(root);
            SizeLimitKeepsTheAcceptedPartial(root);
            CompletionWithoutExternalCancellationWorks(root);
            MidRunEventsAndFailureStageSurvive(root);
            Console.WriteLine("PASS 5 long-capture pipeline regressions");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void BoundedPreviewRetainsExactFullOutput(string root)
    {
        var capture = new PatternCapture(480, 240, 120);
        using var session = Create(root, capture, 10);
        var previews = 0;
        session.PreviewChanged += (_, preview) =>
        {
            Require(preview.Bitmap.Width <= 420 && preview.Bitmap.Height <= 900, "Live preview allocated the full growing image.");
            previews++;
        };
        using var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var output = result.CreateComposite();
        Require(result.AcceptedFrameCount == 10 && previews == 10, "Accepted frames or previews were dropped.");
        Require(output.Width == 480 && output.Height == 1320, "Full output was resized or truncated.");
        for (var y = 0; y < output.Height; y++)
            Require(output.GetPixel(101, y).ToArgb() == PatternCapture.Pixel(101, y).ToArgb(), "A stitched row was duplicated, skipped or resampled.");
    }

    private static void NoMovementStopsWithinPollBudget(string root)
    {
        var capture = new PatternCapture(24, 24, 0, maximumCalls: 5);
        using var session = Create(root, capture, 10, maximumPolls: 3);
        using var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Require(capture.Calls == 4 && result.StopReason == ScrollCaptureStopReason.PollLimit, "Automatic capture ignored its first-movement poll budget.");
    }

    private static void SizeLimitKeepsTheAcceptedPartial(string root)
    {
        using var session = Create(root, new PatternCapture(24, 24, 12), 10, maxPixels: 24 * 36);
        using var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var output = result.CreateComposite();
        Require(result.StopReason == ScrollCaptureStopReason.ImageSizeLimit && result.AcceptedFrameCount == 2 && output.Height == 36,
            "Size limit failed to return the valid partial image.");
    }

    private static void CompletionWithoutExternalCancellationWorks(string root)
    {
        using var session = Create(root, new PatternCapture(24, 24, 12), 10);
        session.PreviewChanged += (_, _) => session.RequestCompletion();
        using var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Require(result.AcceptedFrameCount == 1 && result.StopReason == ScrollCaptureStopReason.CompletedByUser, "Completion required a second cancellation signal.");
    }

    private static void MidRunEventsAndFailureStageSurvive(string root)
    {
        var diagnostics = new LongCaptureRunDiagnostics("failure", "test-version", root);
        using var session = new ScrollCaptureSession(new Rectangle(0, 0, 24, 24), ScrollCaptureMode.Automatic,
            new PatternCapture(24, 24, 0, maximumCalls: 1), new Input(), new Settler(), root,
            new ScrollCaptureOptions(MaximumPolls: 3, SettleDelay: TimeSpan.Zero), diagnostics: diagnostics);
        session.PreviewChanged += (_, _) =>
        {
            var events = File.ReadAllLines(Path.Combine(root, "long-capture-failure.events.jsonl"));
            Require(events.Any(line => line.Contains("segment_saved")) && !events.Any(line => line.Contains("capture_stopped")),
                "No durable evidence was written before the capture finished.");
        };
        using var result = session.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Require(result.StopReason == ScrollCaptureStopReason.CaptureFailure, "A capture exception was incorrectly blamed on wheel input.");
        var rows = File.ReadAllLines(Path.Combine(root, "long-capture-failure.events.jsonl"));
        for (var index = 0; index < rows.Length; index++)
        {
            using var json = JsonDocument.Parse(rows[index]);
            Require(json.RootElement.GetProperty("Sequence").GetInt64() == index + 1, "Missing or reordered trace event.");
        }
        Require(rows.Any(line => line.Contains("\"FailedStage\":\"capture\"")) && diagnostics.Snapshot().FailureType == nameof(InvalidOperationException),
            "Failure stage was lost.");
        Require(!File.ReadAllText(Path.Combine(root, "long-capture-failure.events.jsonl")).Contains(root, StringComparison.OrdinalIgnoreCase), "Trace disclosed an absolute path.");
    }

    private static ScrollCaptureSession Create(string root, PatternCapture capture, int maximumFrames, int maximumPolls = 20, long maxPixels = 64_000_000) =>
        new(new Rectangle(0, 0, capture.Width, capture.Height), ScrollCaptureMode.Automatic, capture, new Input(), new Settler(), root,
            new ScrollCaptureOptions(MaximumFrames: maximumFrames, SettleDelay: TimeSpan.Zero, MaximumPolls: maximumPolls, MaximumCompositePixels: maxPixels),
            new FrameStitcher(maximumOverlapRows: capture.Height - 1, minimumOverlapRows: Math.Max(1, capture.Height / 4)));

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class PatternCapture(int width, int height, int step, int maximumCalls = int.MaxValue) : IScrollRegionCapture
    {
        public int Width => width;
        public int Height => height;
        public int Calls { get; private set; }
        public static Color Pixel(int x, int y) => Color.FromArgb(255, (y * 37 + x * 11) & 255, (y * 13 + x * 17) & 255, (y * 71 + x * 29) & 255);
        public Bitmap Capture(Rectangle region)
        {
            if (++Calls > maximumCalls) throw new InvalidOperationException("Synthetic capture failure");
            var bitmap = new Bitmap(width, height);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, Pixel(x, y + (Calls - 1) * step));
            return bitmap;
        }
    }
    private sealed class Input : IScrollInputService { public Task SendWheelAsync(Point point, int delta, CancellationToken token) => Task.CompletedTask; }
    private sealed class Settler : IScrollSettler { public Task WaitForSettleAsync(TimeSpan delay, CancellationToken token) => Task.CompletedTask; }
}

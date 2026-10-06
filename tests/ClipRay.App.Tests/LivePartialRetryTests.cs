using System.Diagnostics;
using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LivePartialRetryTests
{
    public static void StaticCloudPartialRequestsHaveAFiveMinuteUpperBound()
    {
        var clock = new ManualClock();
        var calls = 0;
        var polls = 0;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 40, 40),
            _ => Picture(false), (_, _) =>
            {
                calls++;
                return Task.FromResult(PartialFrame());
            }, TranslationRouteKind.DeepSeekFlash,
            delay: (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (duration > TimeSpan.FromMilliseconds(500))
                    throw new Exception("Partial retry stopped sampling the screen at a bounded cadence.");
                polls++;
                clock.Advance(duration);
                if (clock.GetTimestamp() >= 89_000) session!.Stop();
                return Task.CompletedTask;
            }, validateLayoutRegions: true, timeProvider: clock);
        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (calls != 4 || polls < 100)
            throw new Exception($"Static failed cloud requests were not bounded: calls={calls}, polls={polls}.");
    }

    public static void ChangedTextInterruptsCloudBackoffWithinThreeSeconds()
    {
        var clock = new ManualClock();
        var changed = false;
        var calls = 0;
        var elapsed = Stopwatch.StartNew();
        var firstPublishedAt = TimeSpan.Zero;
        var secondPublishedAt = TimeSpan.Zero;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 40, 40),
            _ => Picture(changed), (_, _) =>
            {
                var call = ++calls;
                return Task.FromResult(call == 1 ? PartialFrame() : ReadyFrame());
            }, TranslationRouteKind.DeepSeekFlash,
            delay: (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(duration);
                return Task.CompletedTask;
            }, validateLayoutRegions: true, timeProvider: clock);
        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            session.FrameReady += (_, args) =>
            {
                if (!changed)
                {
                    firstPublishedAt = elapsed.Elapsed;
                    changed = true;
                }
                else if (args.Frame.ResultState == LiveTranslationViewportResultState.Ready)
                {
                    secondPublishedAt = elapsed.Elapsed;
                    session.Stop();
                }
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (calls != 2 || secondPublishedAt == TimeSpan.Zero
            || secondPublishedAt - firstPublishedAt > TimeSpan.FromSeconds(3))
            throw new Exception($"Changed text waited for the cloud backoff: calls={calls}, elapsed={secondPublishedAt - firstPublishedAt}.");
    }

    public static void OneHundredTemporaryPartialsRemainRetryableAndSuccessResetsDelay()
    {
        var clock = new ManualClock();
        var retry = new LiveTranslationPartialRetryPolicy(clock);
        for (var index = 0; index < 100; index++)
        {
            var delay = retry.RecordPartial(TranslationRouteKind.DeepSeekFlash);
            if (delay < TimeSpan.FromSeconds(6) || delay > TimeSpan.FromSeconds(60))
                throw new Exception("The cloud retry interval escaped its finite bounds.");
            clock.Advance(delay);
            if (!retry.IsDue)
                throw new Exception($"Temporary partial #{index + 1} permanently stopped retrying.");
        }
        retry.Reset();
        if (retry.IsPending || retry.RecordPartial(TranslationRouteKind.DeepSeekFlash)
                != TimeSpan.FromSeconds(6))
            throw new Exception("A successful result did not reset the cloud retry series.");
    }

    private static Bitmap Picture(bool changed)
    {
        var bitmap = new Bitmap(40, 40);
        bitmap.SetPixel(10, 10, changed ? Color.Red : Color.White);
        return bitmap;
    }

    private static LiveTranslationFrame PartialFrame()
    {
        var (region, item) = Content("old", "已译", 4);
        return new LiveTranslationFrame("DeepSeekFlash", "partial", [item], "fixture-transient")
        {
            SourceRegions = [region],
            ExpectedLayoutCount = 2,
        };
    }

    private static LiveTranslationFrame ReadyFrame()
    {
        var (region, item) = Content("new", "新译", 4);
        var (otherRegion, otherItem) = Content("other", "第二译", 24);
        return new LiveTranslationFrame("DeepSeekFlash", "completed", [item, otherItem])
        {
            SourceRegions = [region, otherRegion],
            ExpectedLayoutCount = 2,
        };
    }

    private static (VisualTextRegion Region, InPlaceLayoutItem Item) Content(
        string source, string translation, int y)
    {
        var bounds = new PixelRect(4, y, 20, 8);
        var block = new OcrTextBlock(source, bounds,
            [new OcrWord(source, bounds)]);
        var region = new VisualTextRegion(source, source, [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var item = new InPlaceLayoutItem(block, translation, bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White,
            SourceRegion: region);
        return (region, item);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _milliseconds);
        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _milliseconds, (long)Math.Ceiling(duration.TotalMilliseconds));
    }
}

using System.Drawing;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveDisplayRecoveryTests
{
    public static void StaticApplyFalseRetriesWithoutOcr() => Recover(Failure.ApplyFalse);
    public static void StaticObserverExceptionRetriesWithoutOcr() => Recover(Failure.ObserverException);
    public static void StaticMissingApplyAckRetriesWithoutOcr() => Recover(Failure.MissingApplyAck);
    public static void AppliedFrameRenderFailureRetriesWithoutOcr() => Recover(Failure.RenderFailure);
    public static void AppliedFrameMissingRenderAckRetriesWithoutOcr() => Recover(Failure.MissingRenderAck);
    public static void SuccessfulRenderAckRetiresRetriesAndLateFailure() => Recover(Failure.None);

    private enum Failure { None, ApplyFalse, ObserverException, MissingApplyAck, RenderFailure, MissingRenderAck }

    private static void Recover(Failure failure)
    {
        foreach (var validated in new[] { false, true })
        {
            var clock = new Clock();
            var calls = 0; var semanticCalls = 0; var attempts = 0; var polls = 0; var afterAckPolls = 0;
            var acknowledged = false;
            string? problem = null;
            LiveTranslationFrameReadyEventArgs? first = null;
            long firstAttemptAt = 0; long recoveredAt = 0;
            LiveTranslationSession? session = null;
            session = new(new(0, 0, 100, 60), _ => Picture(false), (_, _) =>
            {
                calls++;
                return Task.FromResult(Frame(false));
            }, TranslationRouteKind.Machine,
                delay: (duration, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    clock.Advance(duration);
                    if (++polls > 30 || acknowledged && ++afterAckPolls >= 8) session!.Stop();
                    return Task.CompletedTask;
                }, validateLayoutRegions: validated, timeProvider: clock,
                verifySource: (_, _) =>
                {
                    semanticCalls++;
                    return Task.FromResult(new OcrRecognitionResult(true, [], "completed"));
                });
            using (session)
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                session.FrameReady += (_, args) =>
                {
                    attempts++;
                    if (!session.CanPresent(args)) problem ??= "A recovery candidate bypassed CanPresent.";
                    if (attempts == 1)
                    {
                        first = args;
                        firstAttemptAt = clock.GetTimestamp();
                        if (session.AppliedDisplayGeneration != 0)
                            problem ??= "Publication was prematurely marked as applied.";
                        if (failure is Failure.RenderFailure or Failure.MissingRenderAck)
                            session.MarkDisplayApplied(args.Generation);
                        if (failure is Failure.ApplyFalse or Failure.RenderFailure)
                            session.ReportDisplayNotApplied(args);
                        if (failure == Failure.ObserverException)
                            throw new InvalidOperationException("fixture render observer failed");
                        if (failure != Failure.None) return;
                    }
                    else
                    {
                        recoveredAt = clock.GetTimestamp();
                        if (!ReferenceEquals(first!.Frame, args.Frame))
                            problem ??= "Recovery rebuilt the cached translation/layout.";
                        var appliedBefore = failure is Failure.RenderFailure or Failure.MissingRenderAck;
                        if (appliedBefore ? args.Generation <= first.Generation : args.Generation != first.Generation)
                            problem ??= "Recovery did not preserve the successful Apply watermark.";
                    }
                    session.MarkDisplayApplied(args.Generation);
                    session.MarkDisplayPresented(args.Generation);
                    // A delayed negative ACK after successful presentation cannot
                    // resurrect this frame or generate a new display attempt.
                    session.ReportDisplayNotApplied(args);
                    acknowledged = true;
                };
                session.StartAsync(timeout.Token).GetAwaiter().GetResult();
                if (problem is not null) throw new Exception(problem);
                var expectedAttempts = failure == Failure.None ? 1 : 2;
                if (!acknowledged || attempts != expectedAttempts || calls != 1 || semanticCalls != 0)
                    throw new Exception($"Display recovery failed: mode={failure}, validated={validated}, attempts={attempts}, pipelines={calls}, semantic={semanticCalls}.");
                if (failure is Failure.MissingApplyAck or Failure.MissingRenderAck
                    && recoveredAt - firstAttemptAt < 500)
                    throw new Exception("Missing-ACK recovery spun before the acknowledgement timeout.");
                if (session.LastErrorCategory != "none")
                    throw new Exception("A display failure was incorrectly reported as an OCR/translation failure.");
            }
        }
    }

    public static void ChangedSourceCannotRetryOldCandidate()
    {
        var phase = 0; var calls = 0; var attempts = 0; var staleRetry = false;
        LiveTranslationFrameReadyEventArgs? old = null;
        LiveTranslationSession? session = null;
        session = new(new(0, 0, 100, 60), _ => Picture(phase != 0), (_, _) =>
        {
            calls++;
            return Task.FromResult(Frame(phase != 0));
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
        {
            session.FrameReady += (_, args) =>
            {
                attempts++;
                if (old is null)
                {
                    old = args;
                    session.ReportDisplayNotApplied(args);
                    phase = 1;
                    return;
                }
                staleRetry |= args.Frame.LayoutItems.Single().Text != "新译文";
                session.ReportDisplayNotApplied(old);
                session.MarkDisplayApplied(args.Generation);
                session.MarkDisplayPresented(args.Generation);
                session.Stop();
            };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
            if (calls != 2 || attempts != 2 || staleRetry || old is null || session.CanPresent(old))
                throw new Exception("An invalidated source retried its old cached display or survived Stop.");
        }
    }

    private static Bitmap Picture(bool changed)
    {
        var bitmap = new Bitmap(100, 60);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        graphics.FillRectangle(changed ? Brushes.Blue : Brushes.White, 14, 15, 12, 10);
        return bitmap;
    }

    private static LiveTranslationFrame Frame(bool changed)
    {
        var bounds = new PixelRect(8, 8, 80, 28);
        var source = changed ? "second source" : "first source";
        var block = new OcrTextBlock(source, bounds, [new(source, new(14, 15, 12, 10))]);
        var region = new VisualTextRegion("fixture", source, [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        var item = new InPlaceLayoutItem(block, changed ? "新译文" : "旧译文", bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
        return new("Machine", "completed", [item])
        {
            SourceRegions = [region], ExpectedLayoutRegions = [region], ExpectedLayoutCount = 1,
            ExpectedPrimaryLayoutCount = 1, ExpectedPrimaryLayoutArea = bounds.Width * bounds.Height,
        };
    }

    private sealed class Clock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _milliseconds);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _milliseconds,
            (long)Math.Ceiling(duration.TotalMilliseconds));
    }
}

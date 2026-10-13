using System.Drawing;
using System.IO;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class LiveRegionValidationTests
{
    public static void ManyTransientCaptureFailuresStillRecover()
    {
        var attempts = 0;
        var recoveries = 0;
        var published = 0;
        using var session = new LiveTranslationSession(new Rectangle(0, 0, 40, 40),
            _ => Interlocked.Increment(ref attempts) <= 100
                ? throw new IOException("fixture transient capture failure")
                : Picture(0, 1),
            (_, _) => Task.FromResult(new LiveTranslationFrame("Machine", "completed", [Item("recovered", 4)])
                { ExpectedLayoutCount = 1 }),
            TranslationRouteKind.Machine,
            delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            validateLayoutRegions: true);
        session.CaptureDecision += (_, decision) =>
        {
            if (decision.Kind == "session_recovering") recoveries++;
        };
        session.FrameReady += (_, _) => { published++; session.Stop(); };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (attempts <= 100 || recoveries != 100 || published != 1
            || session.State != LiveTranslationSessionState.Stopped)
            throw new Exception($"Live capture stopped instead of recovering: attempts={attempts}, recoveries={recoveries}, published={published}, state={session.State}.");
    }

    public static void StaticPartialViewportRetriesMissingTranslation()
    {
        var calls = 0;
        var states = new List<LiveTranslationViewportResultState>();
        using var session = Create(_ => Picture(0, 1), (_, _) =>
        {
            var first = Interlocked.Increment(ref calls) == 1;
            return Task.FromResult(new LiveTranslationFrame("Machine", first ? "partial" : "completed",
                first ? [Item("first", 4)] : [Item("first", 4), Item("recovered", 24)], first ? "fixture-transient" : "none")
                { ExpectedLayoutCount = 2 });
        });
        session.FrameReady += (_, result) =>
        {
            states.Add(result.Frame.ResultState);
            if (result.Frame.ResultState == LiveTranslationViewportResultState.Ready) session.Stop();
        };
        using var timeout = new CancellationTokenSource(2000);
        session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (calls != 2 || !states.SequenceEqual(new[] { LiveTranslationViewportResultState.AcceptablePartial, LiveTranslationViewportResultState.Ready }))
            throw new Exception($"Static partial viewport never recovered: calls {calls}, states {string.Join(',', states)}.");
    }

    public static void MotionDeadlineUsesNewestCapturedFrame()
    {
        var moving = false; var captureNumber = 0; var calls = 0;
        var translatedCapture = -1; var newestCapture = -1;
        LiveTranslationSession? session = null;
        session = Create(_ =>
        {
            if (!moving) return Picture(0, 1);
            var number = Interlocked.Increment(ref captureNumber);
            var bitmap = new Bitmap(40, 40);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            graphics.FillRectangle(Brushes.White, 0, 4, 28 + number % 6, 8);
            graphics.FillRectangle(Brushes.White, 0, 24, 28 + number % 6, 8);
            bitmap.SetPixel(0, 0, Color.FromArgb(number, 0, 0));
            return bitmap;
        }, (bitmap, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                translatedCapture = bitmap.GetPixel(0, 0).R;
                newestCapture = Volatile.Read(ref captureNumber);
                session!.Stop();
            }
            return Task.FromResult(new LiveTranslationFrame("Machine", "completed", [Item("fixture", 4)]));
        });
        using (session)
        using (var timeout = new CancellationTokenSource(4000))
        {
            session.FrameReady += (_, _) => moving = true;
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        if (calls != 2 || newestCapture < 2 || translatedCapture != newestCapture)
            throw new Exception($"Motion deadline translated old capture {translatedCapture}; newest was {newestCapture}, calls {calls}.");
    }

    public static void NewViewportCancelsOldPipelineAndKeepsItsIdentity()
    {
        var phase = 0;
        var calls = 0;
        var cancelled = false;
        var diagnostics = new List<LiveTranslationDiagnostic>();
        LiveTranslationFrameReadyEventArgs? published = null;
        LiveTranslationSession? session = null;
        session = new LiveTranslationSession(new Rectangle(0, 0, 600, 900), _ =>
        {
            var image = new Bitmap(600, 900);
            using var graphics = Graphics.FromImage(image);
            using var brush = new SolidBrush(Color.White);
            foreach (var y in phase == 0 ? new[] { 60, 240, 420, 600, 780 } : new[] { 20, 200, 380, 560, 740 })
                graphics.FillRectangle(brush, 40, y, 500, 42);
            return image;
        }, async (_, token) =>
        {
            if (++calls == 1)
            {
                phase = 1;
                try { await Task.Delay(3000, token); }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    await Task.Delay(250);
                    throw;
                }
            }
            var item = Item("new viewport", 200);
            return new LiveTranslationFrame("Machine", "completed", [item]) { ExpectedLayoutCount = 1 };
        }, TranslationRouteKind.Machine, validateLayoutRegions: true);
        using (session)
        using (var timeout = new CancellationTokenSource(4000))
        {
            session.Diagnostic += (_, diagnostic) => diagnostics.Add(diagnostic);
            session.FrameReady += (_, args) => { published = args; session.Stop(); };
            session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        }
        var started = diagnostics.Where(item => item.Kind == "pipeline_started").ToArray();
        if (!cancelled || calls != 2 || started.Length != 2 || published?.WorkIdentity is null
            || published.WorkIdentity.PipelineId != started[1].Identity.PipelineId
            || published.WorkIdentity.ViewportGeneration != started[1].Identity.ViewportGeneration
            || diagnostics.All(item => item.ResultState != LiveTranslationViewportResultState.Stale))
            throw new Exception("A stale pipeline was relabelled or allowed to publish over the newest viewport.");
    }

    public static void UncooperativeStaleWorkerStopsWithinBound()
    {
        var phase = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new LiveTranslationSession(new Rectangle(0, 0, 40, 40), _ =>
        {
            var image = new Bitmap(40, 40);
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.Black);
            if (phase != 0)
            {
                graphics.FillRectangle(Brushes.White, 0, 4, 40, 5);
                graphics.FillRectangle(Brushes.White, 0, 24, 40, 5);
            }
            phase = 1;
            return image;
        }, async (bitmap, ignoredToken) =>
        {
            entered.TrySetResult();
            await release.Task;
            _ = bitmap.GetPixel(4, 4);
            return new("Machine", "completed", [Item("late", 4)]);
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
            staleWorkerCancelTimeout: TimeSpan.FromMilliseconds(120));
        var running = session.StartAsync(default);
        try
        {
        entered.Task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        running.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        using var blocked = Create(_ => Picture(0, 1), (_, _) =>
            Task.FromResult(new LiveTranslationFrame("Machine", "completed", [Item("new", 4)])));
        try
        {
            blocked.StartAsync(default).GetAwaiter().GetResult();
            throw new Exception("A new session overlapped an unresolved stale worker.");
        }
        catch (LiveTranslationPipelineException exception)
            when (exception.Category == "stale-worker-still-running") { }
        if (session.State != LiveTranslationSessionState.StoppedAfterConsecutiveFailures
            || session.LastErrorCategory != "stale-worker-cancel-timeout")
            throw new Exception("An uncooperative stale worker blocked the session or allowed overlapping work.");
        }
        finally
        {
            session.Stop(); release.TrySetResult();
            running.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            if (!SpinWait.SpinUntil(() => !LiveTranslationWorkerRegistry.HasUnresolvedWorker, 1000))
                throw new Exception("A completed stale worker remained registered.");
        }
    }

    public static void ChangedGeometryDoesNotReuseStaleTranslation()
    {
        var phase = 0;
        var captures = 0;
        var calls = 0;
        var observed = new List<string>();
        using var session = Create(_ => Picture(phase, ++captures), async (_, token) =>
        {
            var call = ++calls;
            await Task.Delay(180, token);
            // Model an OCR regrouping/typography change while the body pixels stay identical.
            return new("Machine", "completed", [Item(call == 1 ? "first translation" : "changed translation", 4)
                with { Bounds = new PixelRect(4, 4, call == 1 ? 12 : 14, 8) }]);
        });
        session.FrameReady += (_, args) =>
        {
            observed.AddRange(args.Frame.LayoutItems.Select(item => item.Text));
            if (phase == 0) phase = 1;
            else session.Stop();
        };
        using var timeout = new CancellationTokenSource(2500);
        session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (calls != 2 || observed.Count != 2 || observed[0] != "first translation"
            || observed.Skip(1).Any(text => text != "changed translation"))
            throw new Exception("Changed geometry reused a stale translation or failed to publish the current result.");
    }
    public static void MediaAndScrolling()
    {
        var phase = 0;
        var captures = 0;
        var calls = 0;
        var cleared = false;
        var firstApplied = false;
        using var session = Create(_ => Picture(phase, ++captures), async (bitmap, token) =>
        {
            var call = ++calls;
            if (call == 2 && !cleared) throw new Exception("Changed text remained displayed when its replacement started.");
            var text = bitmap.GetPixel(4, 4).R == 255 && bitmap.GetPixel(4, 4).G == 0 ? "new" : "old";
            await Task.Delay(600, token);
            return new("Machine", "completed", [Item(text, 4), Item("changing media", 24)]);
        });
        session.FrameInvalidated += (_, _) => { if (firstApplied) cleared = true; };
        session.FrameReady += (_, args) =>
        {
            if (args.Frame.LayoutItems.Any(item => item.Text == "changing media"))
                throw new Exception("A changed media region was published from an old image.");
            if (args.Frame.LayoutItems.Any(item => item.Text == "old"))
            {
                if (cleared) throw new Exception("Old text reappeared after invalidation.");
                firstApplied = true;
                phase = 1;
            }
            if (args.Frame.LayoutItems.Any(item => item.Text == "new")) session.Stop();
        };
        using var timeout = new CancellationTokenSource(3500);
        session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (!firstApplied || !cleared || calls is < 2 or > 3
            || session.State != LiveTranslationSessionState.Stopped)
            throw new Exception($"Media starved static text, or changed text was not invalidated and replaced. first={firstApplied}, cleared={cleared}, calls={calls}, state={session.State}");
    }

    public static void StopWaitsForLateWorker()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = 0;
        using var session = Create(_ => Picture(0, 1), async (bitmap, ignoredToken) =>
        {
            entered.SetResult();
            await finish.Task;
            _ = bitmap.GetPixel(4, 4); // The pending worker still owns this source.
            return new("Machine", "completed", [Item("late", 4)]);
        });
        session.FrameReady += (_, _) => published++;
        var running = session.StartAsync(default);
        var prematurelyEnded = false;
        try
        {
        entered.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        session.Stop();
        Thread.Sleep(180);
        prematurelyEnded = running.IsCompleted;
        }
        finally
        {
            session.Stop(); finish.TrySetResult();
            running.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            if (!SpinWait.SpinUntil(() => !LiveTranslationWorkerRegistry.HasUnresolvedWorker, 1000))
                throw new Exception("A completed stopped worker remained registered.");
        }
        if (prematurelyEnded || published != 0) throw new Exception("Stop abandoned a live source or published a late result.");
    }

    public static void StopBoundsUncooperativeWorkerWithoutDisposingItsSource(bool captureFailure = false)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = 0;
        var captures = 0;
        using var session = new LiveTranslationSession(new Rectangle(0, 0, 40, 40),
            _ => captureFailure && ++captures > 1 ? throw new IOException("fixture capture failure") : Picture(0, 1), async (bitmap, ignoredToken) =>
        {
            entered.TrySetResult();
            await release.Task;
            try { _ = bitmap.GetPixel(4, 4); sourceRead.TrySetResult(); }
            catch (Exception error) { sourceRead.TrySetException(error); throw; }
            return new("Machine", "completed", [Item("late", 4)]);
        }, TranslationRouteKind.Machine, validateLayoutRegions: true,
            staleWorkerCancelTimeout: TimeSpan.FromMilliseconds(120));
        session.FrameReady += (_, _) => published++;
        var running = session.StartAsync(default);
        try
        {
            entered.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            if (!captureFailure) session.Stop();
            running.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            if (!LiveTranslationWorkerRegistry.HasUnresolvedWorker || published != 0)
                throw new Exception("Stop lost track of an active worker or published its late result.");
        }
        finally
        {
            release.TrySetResult();
            running.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        sourceRead.Task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        if (!SpinWait.SpinUntil(() => !LiveTranslationWorkerRegistry.HasUnresolvedWorker, 1000))
            throw new Exception("Stopped worker registry did not drain.");
        var expectedState = captureFailure ? LiveTranslationSessionState.StoppedAfterConsecutiveFailures : LiveTranslationSessionState.Stopped;
        if (published != 0 || session.State != expectedState)
            throw new Exception("Stopped session published a late result or did not finish.");
    }

    public static void CaptureFailureDoesNotOverlapWorkers()
    {
        var captures = 0;
        var calls = 0;
        var active = 0;
        var maximum = 0;
        var cancelled = false;
        using var session = Create(_ => ++captures == 2 ? throw new IOException("probe failed") : Picture(0, 1),
            async (bitmap, token) =>
            {
                maximum = Math.Max(maximum, Interlocked.Increment(ref active));
                try
                {
                    if (++calls == 1)
                    {
                        try { await Task.Delay(3000, token); }
                        catch (OperationCanceledException) { _ = bitmap.GetPixel(4, 4); cancelled = true; throw; }
                    }
                    return new("Machine", "completed", [Item("recovered", 4)]);
                }
                finally { Interlocked.Decrement(ref active); }
            });
        session.FrameReady += (_, _) => session.Stop();
        using var timeout = new CancellationTokenSource(3500);
        session.StartAsync(timeout.Token).GetAwaiter().GetResult();
        if (!cancelled || calls != 2 || maximum != 1 || active != 0)
            throw new Exception("A failed capture did not recover serially after draining its worker.");
    }

    private static LiveTranslationSession Create(Func<Rectangle, Bitmap> capture,
        Func<Bitmap, CancellationToken, Task<LiveTranslationFrame>> translate)
    {
        return new(new(0, 0, 40, 40), capture, translate, TranslationRouteKind.Machine,
            validateLayoutRegions: true);
    }
    private static Bitmap Picture(int phase, int media)
    {
        var bitmap = new Bitmap(40, 40);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        using var body = new SolidBrush(phase == 0 ? Color.White : Color.Red);
        using var animation = new SolidBrush(Color.FromArgb(media % 255, 80, 120));
        graphics.FillRectangle(body, 4, 4, 12, 8);
        graphics.FillRectangle(animation, 4, 24, 12, 8);
        return bitmap;
    }
    private static InPlaceLayoutItem Item(string text, int y)
    {
        var bounds = new PixelRect(4, y, 12, 8);
        var block = new OcrTextBlock("source", bounds, []);
        var region = new VisualTextRegion($"r{y}", block.Text, [block], bounds,
            VisualTextRole.Body, VisualStructureConfidence.High);
        return new(block, text, bounds,
            InPlaceBackgroundMode.FlatSampled, Color.Black, Color.White, SourceRegion: region);
    }
}

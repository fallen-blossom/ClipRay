using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Reflection;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Infrastructure;
using ClipRay.App.Translation;
using static BackgroundTask3Tests;
using static BackgroundTask3LifetimeTests;

internal static class BackgroundTask3ReviewTests
{
    internal static void FailedChildBlocksFallback() => FailedChildBlocksFallbackAsync().GetAwaiter().GetResult();
    private static async Task FailedChildBlocksFallbackAsync()
    {
        var releases = 0; var calls = 0;
        var child = new TranslationChildLifetime(() => throw new IOException("fixture stop failed"),
            () => Task.CompletedTask, Task.CompletedTask, () => releases++);
        var factory = new FailedRetirementFactory(async () =>
        { calls++; await child.RetireAsync(); throw new Exception("Failed retirement unexpectedly succeeded."); });
        var provider = new LocalTranslationProvider(new("never-opened-model", "never-opened-runtime"), factory);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try { await ((Task<ILocalLmtSession>)Invoke(provider, "GetOrStartSessionAsync", CancellationToken.None)!).WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TranslationChildRetirementException error) { Check(ReferenceEquals(error.Owner, child), "Exact failed child lost."); }
            }
            Check(calls == 1 && releases == 1, "A failed retirement opened fallback/new-session admission.");
        }
        finally
        {
            try { await provider.RetireAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TranslationChildRetirementException) { }
        }
        Check(provider.RetireAsync().IsFaulted, "Provider drain lost startup retirement failure.");
    }

    internal static void ChildRetirement(bool local, bool stopFailure = false) => ChildRetirementAsync(local, stopFailure).GetAwaiter().GetResult();
    private static async Task ChildRetirementAsync(bool local, bool stopFailure)
    {
        var app = FakeApp(); var requested = NewSignal(); var exit = NewSignal(); var transport = NewSignal();
        var stops = 0; var released = 0; var shutdown = 0;
        var child = new TranslationChildLifetime(() =>
        {
            Interlocked.Increment(ref stops); requested.TrySetResult();
            if (stopFailure) throw new IOException("fixture stop failed");
        }, () => exit.Task, transport.Task, () =>
        { Check(exit.Task.IsCompleted && transport.Task.IsCompleted, "Handle released before exit/transport."); released++; });
        if (local)
        {
            var provider = new LocalTranslationProvider(new("never-opened-model", "never-opened-runtime"));
            Set(provider, "_session", new LocalLmtProcessSession(child));
            Set(app, "_localTranslationProvider", provider);
        }
        else
        {
            var process = new BundledMachineTranslationProcess("never-opened-runtime", "never-opened-worker", "never-opened-model", keepAlive: true);
            Set(process, "_child", child);
            Set(app, "_machineTranslationProvider", new LocalMachineTranslationProvider(process));
        }
        ResourceDrainResult? result = null;
        try
        {
            Check(!await app.RequestManagedShutdownAsync(finalShutdown: () => shutdown++, drainBudget: TimeSpan.FromMilliseconds(100)),
                "Stop request was counted as child exit.");
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(shutdown == 0 && released == 0 && stops == 1, "Child handle released before physical exit.");
            if (stopFailure) Check(child.StopFailure is IOException, "Stop failure was discarded.");
            exit.TrySetResult();
            Check(!(await app.DrainTranslationAsync(TimeSpan.FromMilliseconds(100))).Completed && released == 0,
                "Exit was counted as transport drain.");
        }
        finally
        {
            exit.TrySetResult(); transport.TrySetResult();
            result = await app.DrainTranslationAsync(TimeSpan.FromSeconds(3));
        }
        Check(result.Pending == 0 && stops == 1 && released == 1, "Late child retirement did not release once.");
        if (stopFailure)
            Check(!result.Completed && result.Failures.Count == 1 && shutdown == 0, "Failed stop became successful retirement.");
        else
            Check(result.Completed && await app.RequestManagedShutdownAsync(finalShutdown: () => shutdown++) && shutdown == 1,
                "Actual exit and transport completion did not permit final shutdown.");
    }

    internal static void QueuedDisplay(bool retain, bool publishedBeforeInvalidation = false) =>
        QueuedDisplayAsync(retain, publishedBeforeInvalidation).GetAwaiter().GetResult();
    private static async Task QueuedDisplayAsync(bool retain, bool publishedBeforeInvalidation)
    {
        var app = FakeApp(); var phase = 0; var calls = 0; var visible = 0; var cleared = 0;
        var first = NewSignal(); var next = NewSignal(); var release = NewSignal(); var applyAllowed = true; var applyThrows = false;
        var failedApply = 0;
        var fresh = NewSignal(); var queue = new ConcurrentQueue<Action>();
        var frames = new ConcurrentQueue<LiveTranslationFrameReadyEventArgs>();
        app.LiveDisplayDispatcher = queue.Enqueue;
        app.LiveWarmupFactory = _ => Task.CompletedTask;
        using var before = Picture(0); using var after = Picture(0);
        using (var g = Graphics.FromImage(after))
        {
            g.FillRectangle(Brushes.Blue, 60, 4, 50, 8);
            g.FillRectangle(Brushes.Blue, 60, 35, 50, 8);
            g.FillRectangle(Brushes.Blue, 60, 65, 50, 8);
            g.FillRectangle(Brushes.Red, 12, 12, 20, 8);
            if (!retain) g.FillRectangle(Brushes.Red, 12, 52, 20, 8);
        }
        Check(LiveTranslationChangePlanner.Analyze(new(before), new(after), Frame().SourceRegions).IsViewportReplacement,
            "Queued fixture must trigger viewport replacement.");
        LiveTranslationSession? active = null;
        app.LiveSessionFactory = async (owner, _) =>
        {
            using var session = new LiveTranslationSession(new(0, 0, 120, 80), _ => new Bitmap(phase == 0 ? before : after),
                async (_, _) => { if (Interlocked.Increment(ref calls) > 1) { next.TrySetResult(); await release.Task; } return Frame(); },
                TranslationRouteKind.Machine, validateLayoutRegions: true, staleWorkerCancelTimeout: TimeSpan.FromMilliseconds(100));
            active = session; Set(app, "_liveTranslationSession", session);
            session.FrameReady += (_, frame) =>
            {
                frames.Enqueue(frame);
                app.QueueLiveFrame(owner, session, frame, () =>
                {
                    if (applyThrows && frame.RetainedDisplayRevision is null) throw new InvalidOperationException("fixture render failure");
                    if (!applyAllowed && frame.RetainedDisplayRevision is null) return false;
                    visible = frame.Frame.LayoutItems.Count; return true;
                }, failed: _ => failedApply++);
                if (phase == 0) first.TrySetResult();
                else if (calls > 1 && release.Task.IsCompleted) fresh.TrySetResult();
            };
            session.FrameInvalidated += (_, _) => app.QueueLiveInvalidation(owner, session, () => { visible = 0; cleared++; });
            try { await session.StartAsync(owner.Token); }
            finally { Set(app, "_liveTranslationSession", null!); }
        };
        var running = app.RunLiveTranslationEntryAsync(new(0, 0, 120, 80));
        try
        {
            await first.Task.WaitAsync(TimeSpan.FromSeconds(3)); Drain(); Check(visible == 2, "Initial display missing.");
            phase = 1;
            await next.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var delayed = Take();
            if (!publishedBeforeInvalidation)
            {
                foreach (var action in delayed) action();
                Check(visible == (retain ? 1 : 0), "New held worker swallowed queued clear/retained update.");
            }
            release.TrySetResult(); await fresh.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var replacement = Take();
            if (publishedBeforeInvalidation)
            {
                applyThrows = true;
                foreach (var action in replacement) action();
                Check(failedApply > 0 && visible == 2, "Apply exception was not observed without changing the display.");
                applyThrows = false;
                applyAllowed = false;
                foreach (var action in replacement) action();
                Check(visible == 2, "Failed Apply changed the display.");
                foreach (var action in delayed) action();
                Check(visible == (retain ? 1 : 0), $"Publication/failed Apply swallowed a required old invalidation. retain={retain}; visible={visible}; delayed={delayed.Length}; generations={string.Join(';', frames.Select(f => $"{f.Generation}/{f.PresentationViewportGeneration}/{f.RetainedDisplayRevision}/{sessionEligibility(f)}"))}");
                applyAllowed = true;
            }
            foreach (var action in replacement) action();
            Check(visible == 2, "New current frame did not apply.");
            var clearsBeforeReplay = cleared;
            foreach (var staleAction in delayed) staleAction();
            Check(visible == 2 && cleared == clearsBeforeReplay, "Old queued update erased a newer applied display.");
        }
        finally
        {
            Invoke(app, "StopLiveTranslation"); release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(3)); await DrainRegistry();
        }
        void Drain() { while (queue.TryDequeue(out var action)) action(); }
        bool sessionEligibility(LiveTranslationFrameReadyEventArgs frame) => active?.CanPresent(frame) == true;
        Action[] Take() { var actions = new List<Action>(); while (queue.TryDequeue(out var action)) actions.Add(action); return actions.ToArray(); }
    }

    internal static void IdleRelease() => IdleReleaseAsync().GetAwaiter().GetResult();
    private static async Task IdleReleaseAsync()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var fake = new HeldSession(entered, release);
        var local = new LocalTranslationProvider(new("never-opened-model", "never-opened-runtime"));
        Set(local, "_session", fake); Set(local, "_lastUse", DateTimeOffset.UtcNow.AddDays(-1));
        var idle = Task.Run(() => Invoke(local, "StopIdleSession", new object[] { null! }));
        Task retirement = Task.CompletedTask;
        try
        {
            Check(entered.Wait(2000), "Idle callback not entered."); retirement = local.RetireAsync();
            await Task.Delay(100);
            Check(!retirement.IsCompleted && !fake.Completed, "Retirement reported success during physical idle release.");
        }
        finally { release.Set(); await idle.WaitAsync(TimeSpan.FromSeconds(3)); await retirement.WaitAsync(TimeSpan.FromSeconds(3)); }
        Check(fake.Completed && fake.Disposals == 1, "Idle owner not released exactly once.");
    }

    internal static void FailedModelAdmission()
    {
        foreach (var failed in new[] { Task.FromException(new IOException("fixture release failed")), Task.FromCanceled(new CancellationToken(true)) })
        {
            var app = FakeApp(); var created = 0; LocalTranslationProvider? made = null;
            app.LocalProviderFactory = () => { created++; return made = new(new("never-opened-model", "never-opened-runtime")); };
            Set(app, "_localProviderRetirement", failed);
            try
            {
                try { Invoke(app, "GetLocalTranslationProvider"); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
                Check(created == 0, "Failed/cancelled predecessor allowed a new model.");
            }
            finally { made?.RetireAsync().GetAwaiter().GetResult(); _ = failed.Exception; }
        }
        var changingApp = FakeApp(); var oldProvider = new LocalTranslationProvider(new("never-opened-model", "never-opened-runtime"));
        Set(oldProvider, "_session", new FailedReleaseSession());
        Set(changingApp, "_localTranslationProvider", oldProvider);
        var before = Field<ClipRay.App.Settings.AppSettings>(changingApp, "_settings");
        Invoke(changingApp, "ApplySettings", before with { LocalLmtModelPath = "never-opened-replacement" }, false);
        var retiring = Field<Task>(changingApp, "_localProviderRetirement");
        try { retiring.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch (IOException) { }
        var newProviders = 0;
        changingApp.LocalProviderFactory = () => { newProviders++; throw new Exception("Forbidden new provider."); };
        try { Invoke(changingApp, "GetLocalTranslationProvider"); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException { InnerException: IOException }) { }
        Check(newProviders == 0 && ReferenceEquals(Field<LocalTranslationProvider>(changingApp, "_retiringLocalProvider"), oldProvider),
            "ApplySettings lost the failed predecessor or its admission guard.");
        var result = changingApp.DrainTranslationAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Check(result.Pending == 0 && result.Failures.Count == 1, "Failed model was not retained in managed drain.");
    }
    private sealed class HeldSession(ManualResetEventSlim entered, ManualResetEventSlim release) : ILocalLmtSession
    {
        internal bool Completed; internal int Disposals;
        public bool IsAlive => !Completed;
        public Task<string> TranslateAsync(string prompt, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { Disposals++; entered.Set(); release.Wait(); Completed = true; }
    }
    private sealed class FailedRetirementFactory(Func<Task<ILocalLmtSession>> start) : ILocalLmtSessionFactory
    {
        public Task<ILocalLmtSession> StartAsync(LocalLmtSessionStart options, CancellationToken token) => start();
    }
    private sealed class FailedReleaseSession : ILocalLmtSession
    {
        public bool IsAlive => false;
        public Task<string> TranslateAsync(string prompt, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() => throw new IOException("fixture release failed");
    }
}

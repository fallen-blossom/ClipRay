using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.App.Acceptance;

namespace ClipRay.App.LiveTranslation;

public enum LiveTranslationSessionState
{
    Idle,
    Running,
    Stopped,
    StoppedAfterConsecutiveFailures,
}

public sealed record LiveTranslationFrame(
    string Route,
    string Status,
    IReadOnlyList<InPlaceLayoutItem> LayoutItems,
    string ErrorCategory = "none")
{
    public IReadOnlyList<VisualTextRegion> SourceRegions { get; init; } = [];
    public LiveTranslationViewportResultState ResultState { get; init; } = LiveTranslationViewportResultState.Ready;
    public int ExpectedLayoutCount { get; init; }
    public int RejectedLayoutCount { get; init; }
    public int ExpectedPrimaryLayoutCount { get; init; }
    public double ExpectedPrimaryLayoutArea { get; init; }
    public LiveTranslationWorkIdentity? WorkIdentity { get; init; }
    public static LiveTranslationFrame Empty(string route) => new(route, "empty", []);

    public static LiveTranslationFrame FromOutcome(TranslationRouteKind route, ApplicationTranslationOutcome outcome)
    {
        var expected = TranslationAcceptancePolicy.ExpectedLayoutCount(outcome);
        var error = TranslationAcceptancePolicy.ErrorCategory(outcome);
        return new(route.ToString(), outcome.Status, outcome.LayoutItems, error)
        {
            SourceRegions = outcome.Regions,
            ExpectedLayoutCount = expected,
            RejectedLayoutCount = outcome.LayoutRejections?.Count ?? 0,
            ExpectedPrimaryLayoutCount = TranslationAcceptancePolicy.ExpectedPrimaryLayoutCount(outcome),
            ExpectedPrimaryLayoutArea = TranslationAcceptancePolicy.ExpectedPrimaryLayoutArea(outcome),
            ResultState = TranslationAcceptancePolicy.HasDisplayableResult(outcome)
                ? outcome.IsSuccess && outcome.LayoutItems.Count >= expected
                    ? LiveTranslationViewportResultState.Ready
                    : LiveTranslationViewportResultState.AcceptablePartial
                : LiveTranslationViewportResultState.Failed,
        };
    }
}

public sealed class LiveTranslationPipelineException(string category) : Exception("live-translation-pipeline-failed")
{
    public string Category { get; } = category;
}

public sealed class LiveTranslationFrameReadyEventArgs(
    LiveTranslationFrame frame,
    long generation,
    long? presentationViewportGeneration = null,
    long? retainedDisplayRevision = null) : EventArgs
{
    public LiveTranslationFrame Frame { get; } = frame;
    public long Generation { get; } = generation;
    public LiveTranslationWorkIdentity? WorkIdentity => Frame.WorkIdentity;
    public long? PresentationViewportGeneration { get; } = presentationViewportGeneration ?? frame.WorkIdentity?.ViewportGeneration;
    public long? RetainedDisplayRevision { get; } = retainedDisplayRevision;
}

public sealed class LiveTranslationDiagnostic(
    string kind,
    LiveTranslationWorkIdentity identity,
    LiveTranslationViewportResultState resultState,
    LiveTranslationViewportMetrics? metrics = null,
    string errorCategory = "none",
    IReadOnlyList<LiveTranslationLayoutQuarantine>? quarantinedLayouts = null) : EventArgs
{
    public string Kind { get; } = kind;
    public LiveTranslationWorkIdentity Identity { get; } = identity;
    public LiveTranslationViewportResultState ResultState { get; } = resultState;
    public LiveTranslationViewportMetrics? Metrics { get; } = metrics;
    public string ErrorCategory { get; } = errorCategory;
    public IReadOnlyList<LiveTranslationLayoutQuarantine>? QuarantinedLayouts { get; } = quarantinedLayouts;
}

public sealed record LiveTranslationCaptureDecision(
    string Kind, long ObservationId, string? SourceHash,
    int WindowCount = 0, bool ViewportReplacement = false,
    bool IncidentalChange = false, string ErrorCategory = "none");

public sealed class LiveTranslationSession : IDisposable
{
    private readonly Rectangle _region;
    private readonly Func<Rectangle, Bitmap> _capture;
    private readonly Func<Bitmap, CancellationToken, Task<LiveTranslationFrame>> _translate;
    private readonly Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>>? _verifySource;
    private readonly TranslationRouteKind _route;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeProvider _timeProvider;
    private readonly bool _validateLayoutRegions;
    private readonly TimeSpan _staleWorkerCancelTimeout;
    private readonly CancellationTokenSource _stopSource = new();
    private int _started;
    private int _disposed;
    private long _generation;
    private long _captureId;
    private long _observationId;
    private long _pipelineId;
    private long _viewportGeneration;
    private long _displayRevision;
    private long _appliedDisplayGeneration;

    public LiveTranslationSession(
        Rectangle region,
        Func<Rectangle, Bitmap> capture,
        Func<Bitmap, CancellationToken, Task<LiveTranslationFrame>> translate,
        TranslationRouteKind route,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        bool validateLayoutRegions = false,
        TimeSpan? staleWorkerCancelTimeout = null,
        TimeProvider? timeProvider = null,
        Func<Bitmap, CancellationToken, Task<OcrRecognitionResult>>? verifySource = null)
    {
        if (region.Width <= 0 || region.Height <= 0) throw new ArgumentOutOfRangeException(nameof(region));
        _region = region;
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
        _verifySource = verifySource;
        _route = route;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, _timeProvider, token));
        _validateLayoutRegions = validateLayoutRegions;
        _staleWorkerCancelTimeout = staleWorkerCancelTimeout ?? TimeSpan.FromSeconds(2);
        if (_staleWorkerCancelTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleWorkerCancelTimeout));
    }

    public LiveTranslationSessionState State { get; private set; } = LiveTranslationSessionState.Idle;
    public event EventHandler? StateChanged;
    public event EventHandler<LiveTranslationFrameReadyEventArgs>? FrameReady;
    public event EventHandler? FrameInvalidated;
    public event EventHandler<LiveTranslationDiagnostic>? Diagnostic;
    public event EventHandler<LiveTranslationCaptureDecision>? CaptureDecision;
    public long Generation => Volatile.Read(ref _generation);
    public long ViewportGeneration => Volatile.Read(ref _viewportGeneration);
    internal long AppliedDisplayGeneration => Volatile.Read(ref _appliedDisplayGeneration);
    public bool CanPresent(LiveTranslationFrameReadyEventArgs frame) =>
        CanInvalidateDisplay(frame.Generation)
        && (frame.RetainedDisplayRevision is { } revision
            ? revision == Volatile.Read(ref _displayRevision) : frame.Generation == Generation)
        && frame.PresentationViewportGeneration == ViewportGeneration;
    internal bool CanInvalidateDisplay(long generation) =>
        Volatile.Read(ref _disposed) == 0 && !_stopSource.IsCancellationRequested
        && generation > Volatile.Read(ref _appliedDisplayGeneration);
    internal void MarkDisplayApplied(long generation)
    {
        // Called only after the actual UI replacement/clear succeeds. Publishing
        // a candidate, including one whose Apply fails, cannot retire a fallback.
        long previous;
        do { previous = Volatile.Read(ref _appliedDisplayGeneration); if (generation <= previous) return; }
        while (Interlocked.CompareExchange(ref _appliedDisplayGeneration, generation, previous) != previous);
    }
    public string Status { get; private set; } = "等待开始";
    public string LastErrorCategory { get; private set; } = "none";
    public event EventHandler? StatusChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (LiveTranslationWorkerRegistry.HasUnresolvedWorker)
            throw new LiveTranslationPipelineException("stale-worker-still-running");
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("Live translation session can only be started once.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopSource.Token);
        var token = linked.Token;
        if (_validateLayoutRegions)
        {
            await StartRegionValidatedAsync(token).ConfigureAwait(false);
            return;
        }
        byte[]? previousHash = null;
        byte[]? displayedHash = null;
        SetState(LiveTranslationSessionState.Running);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var generation = Interlocked.Increment(ref _generation);
                try
                {
                    using var bitmap = _capture(_region);
                    var hash = HashPixels(bitmap);
                    if (previousHash is null || !hash.AsSpan().SequenceEqual(previousHash))
                    {
                        if (displayedHash is null || !hash.AsSpan().SequenceEqual(displayedHash))
                            FrameInvalidated?.Invoke(this, EventArgs.Empty);
                        ReportStatus(_route == TranslationRouteKind.Machine ? "正在识别并机翻…" : "正在识别并翻译…");
                        var frameCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                        var workerBitmap = new Bitmap(bitmap);
                        var translating = Task.Run(() => _translate(workerBitmap, frameCancellation.Token), frameCancellation.Token);
                        var changed = false;
                        // Capture continues while OCR/translation is pending. Only one pipeline runs.
                        try
                        {
                            while (!translating.IsCompleted && !token.IsCancellationRequested)
                            {
                                using var pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                                var poll = Task.Delay(TimeSpan.FromMilliseconds(150), _timeProvider, pollCancellation.Token);
                                if (await Task.WhenAny(translating, poll).ConfigureAwait(false) == translating)
                                {
                                    pollCancellation.Cancel();
                                    break;
                                }
                                token.ThrowIfCancellationRequested();
                                using var probe = _capture(_region);
                                if (!HashPixels(probe).AsSpan().SequenceEqual(hash))
                                {
                                    changed = true;
                                    Interlocked.Increment(ref _generation);
                                    FrameInvalidated?.Invoke(this, EventArgs.Empty);
                                    frameCancellation.Cancel();
                                    break;
                                }
                            }
                            if (!changed && !token.IsCancellationRequested)
                            {
                                using var current = _capture(_region);
                                changed = !HashPixels(current).AsSpan().SequenceEqual(hash);
                                if (changed)
                                {
                                    Interlocked.Increment(ref _generation);
                                    FrameInvalidated?.Invoke(this, EventArgs.Empty);
                                    frameCancellation.Cancel();
                                }
                            }
                            if (token.IsCancellationRequested) frameCancellation.Cancel();
                            var completed = await (changed || token.IsCancellationRequested
                                ? translating.WaitAsync(_staleWorkerCancelTimeout, _timeProvider)
                                : translating).ConfigureAwait(false);
                            if (changed || token.IsCancellationRequested) { previousHash = null; continue; }
                            Publish(completed);
                        }
                        catch (OperationCanceledException) when (changed && !token.IsCancellationRequested)
                        {
                            previousHash = null;
                            continue;
                        }
                        catch (Exception) when (changed && !token.IsCancellationRequested)
                        {
                            previousHash = null;
                            continue;
                        }
                        finally
                        {
                            // Capture/observer failures must not abandon a worker using this bitmap.
                            if (!translating.IsCompleted)
                            {
                                frameCancellation.Cancel();
                                try { await translating.WaitAsync(_staleWorkerCancelTimeout, _timeProvider).ConfigureAwait(false); }
                                catch { /* The original failure determines session retry status. */ }
                            }
                            if (translating.IsCompleted) { workerBitmap.Dispose(); frameCancellation.Dispose(); }
                            else
                            {
                                var cleanup = translating.ContinueWith(task => {
                                    _ = task.Exception; workerBitmap.Dispose(); frameCancellation.Dispose();
                                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                                LiveTranslationWorkerRegistry.Register(cleanup);
                                if (!token.IsCancellationRequested)
                                    throw new LiveTranslationPipelineException("stale-worker-cancel-timeout");
                            }
                        }

                        void Publish(LiveTranslationFrame frame)
                        {
                            if (!LiveTranslationViewportResultPolicy.CanPresent(frame.ResultState))
                                throw new LiveTranslationPipelineException(
                                    frame.ErrorCategory == "none" ? "viewport-result-failed" : frame.ErrorCategory);
                            displayedHash = hash;
                            previousHash = hash;
                            LastErrorCategory = frame.ErrorCategory;
                            ReportStatus(frame.ErrorCategory == "none" ? "已更新 · 等待画面变化"
                                : "部分翻译完成 · 未成功区域保留原文，稍后重试");
                            if (!token.IsCancellationRequested && generation == Volatile.Read(ref _generation))
                                FrameReady?.Invoke(this, new LiveTranslationFrameReadyEventArgs(frame, generation));
                        }
                    }

                    await _delay(
                        LiveTranslationRefreshPolicy.NextDelay(_route, LastErrorCategory == "none" ? 0 : 1),
                        token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    LastErrorCategory = exception is LiveTranslationPipelineException pipeline
                        ? pipeline.Category : "runtime-failed";
                    previousHash = null;
                    if (LiveTranslationWorkerRegistry.HasUnresolvedWorker)
                    {
                        SetState(LiveTranslationSessionState.StoppedAfterConsecutiveFailures);
                        return;
                    }
                    ReportStatus("暂未更新 · 后台恢复中");
                    await _delay(
                        LiveTranslationRefreshPolicy.NextDelay(_route, 1),
                        token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (State != LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
                SetState(LiveTranslationSessionState.Stopped);
        }
    }

    private async Task StartRegionValidatedAsync(CancellationToken token)
    {
        byte[]? previousHash = null;
        LiveTranslationPixelSnapshot? processedSource = null;
        var processedRegions = new List<VisualTextRegion>();
        var layoutCache = new LiveTranslationLayoutCache();
        var stabilityGate = new LiveTranslationStabilityGate();
        var startedAt = _timeProvider.GetTimestamp();
        long ElapsedMilliseconds() => (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
        var partialRetry = new LiveTranslationPartialRetryPolicy(_timeProvider);
        var displayed = LiveTranslationRegionWitness.Empty;
        LiveTranslationFrame? displayedFrame = null;
        var retryPartialViewport = false;
        var retryChangedText = false;
        long? ambiguousCaretStartedAt = null;
        const long ambiguousCaretMaximumWaitMilliseconds = 750;
        long lastSemanticAttemptAt = -10_000;
        long? semanticUnknownStartedAt = null;
        const long semanticMinimumIntervalMilliseconds = 400;
        const long semanticUnknownMaximumWaitMilliseconds = 2_000;
        const long semanticBackingPublishIntervalMilliseconds = 1_000;
        long lastSemanticBackingPublishedAt = -10_000;
        var pendingSemanticBackingRefresh = false;
        var displayGate = new object();
        Task<OcrRecognitionResult>? semanticOwner = null;
        using var watchdog = new LiveTranslationDisplayWatchdog(_timeProvider, current =>
        {
            lock (displayGate)
            {
                if (token.IsCancellationRequested || displayed.Count == 0) return;
                if (current is null) InvalidateViewport();
                else InvalidateChanged(current);
            }
        });
        Bitmap Capture(out LiveTranslationPixelSnapshot snapshot)
        {
            var captured = _capture(_region);
            snapshot = new LiveTranslationPixelSnapshot(captured);
            watchdog.Captured(snapshot);
            return captured;
        }
        SetState(LiveTranslationSessionState.Running);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var observationId = Interlocked.Increment(ref _observationId);
                try
                {
                    using var captured = Capture(out var source);
                    using var bitmap = new Bitmap(captured);
                    var hash = source.FullHash();
                    var sourceHash = Convert.ToHexString(hash);
                    if (_verifySource is not null && processedSource is not null
                        && previousHash is not null && !hash.AsSpan().SequenceEqual(previousHash))
                    {
                        semanticUnknownStartedAt ??= ElapsedMilliseconds();
                        watchdog.Unknown();
                        var (checkedLatest, evidence) = await ReadSemanticEvidenceAsync(bitmap)
                            .ConfigureAwait(false);
                        if (!checkedLatest)
                        {
                            await _delay(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
                            continue;
                        }
                        if (evidence is null)
                        {
                            // An OCR failure cannot certify a changed source. Keep
                            // only exact pixel witnesses while retrying briefly.
                            InvalidateChanged(source);
                            semanticUnknownStartedAt ??= ElapsedMilliseconds();
                            if (ElapsedMilliseconds() - semanticUnknownStartedAt.Value
                                < semanticUnknownMaximumWaitMilliseconds)
                            {
                                ReportCaptureDecision(new("semantic_unknown", observationId, sourceHash));
                                await _delay(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
                                continue;
                            }
                            InvalidateChanged(source);
                            ReportCaptureDecision(new("semantic_unknown_expired", observationId, sourceHash));
                        }
                        else
                        {
                            semanticUnknownStartedAt = null;
                            watchdog.Confirmed();
                            InvalidateChanged(source, sameSourceText: item =>
                                evidence.MatchesStableGeometry(item, processedSource, source));
                            FlushPendingSemanticBacking(source);
                            if (evidence.SameSourceStableGeometry(processedRegions, processedSource, source))
                            {
                                // Rebase on the animated frame. A changed word is
                                // checked on the next bounded OCR observation.
                                processedSource = source;
                                previousHash = hash;
                                if (retryPartialViewport && partialRetry.IsDue)
                                {
                                    ReportCaptureDecision(new("semantic_partial_retry", observationId, sourceHash));
                                    await ProcessFrameAsync(bitmap, source, hash,
                                        new([new Rectangle(0, 0, bitmap.Width, bitmap.Height)], false))
                                        .ConfigureAwait(false);
                                }
                                else
                                {
                                    ReportCaptureDecision(new("semantic_unchanged", observationId, sourceHash));
                                    await _delay(retryPartialViewport ? partialRetry.NextPollDelay()
                                        : TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
                                }
                                continue;
                            }
                            retryChangedText = true;
                            ReportCaptureDecision(new("semantic_text_changed", observationId, sourceHash));
                        }
                        // OCR confirmed a new source, or remained unknown beyond
                        // the bounded retry. Process its newest frame immediately;
                        // local animation cannot own an infinite stability veto.
                        if (retryPartialViewport)
                        {
                            partialRetry.Reset();
                            retryPartialViewport = false;
                        }
                        var semanticPlan = LiveTranslationChangePlanner.Analyze(
                            processedSource, source, processedRegions);
                        await ProcessFrameAsync(bitmap, source, hash, semanticPlan).ConfigureAwait(false);
                        continue;
                    }
                    if (retryPartialViewport && !partialRetry.IsDue)
                    {
                        var samePixels = previousHash is not null
                            && hash.AsSpan().SequenceEqual(previousHash);
                        if (!samePixels) InvalidateChanged(source);
                        if (samePixels || !await HasStableContentChangeAsync(source).ConfigureAwait(false))
                        {
                            FlushPendingSemanticBacking(source);
                            ReportCaptureDecision(new("partial_retry_deferred", observationId, sourceHash));
                            await _delay(partialRetry.NextPollDelay(), token).ConfigureAwait(false);
                            continue;
                        }
                        // A new text/viewport state takes precedence over the
                        // failed request's timer and starts a fresh retry series.
                        partialRetry.Reset();
                        retryPartialViewport = false;
                    }
                    if (!retryPartialViewport && !retryChangedText
                        && previousHash is not null && hash.AsSpan().SequenceEqual(previousHash))
                    {
                        // A thin transient stroke returned to the old pixels.
                        ambiguousCaretStartedAt = null;
                        FlushPendingSemanticBacking(source);
                        ReportCaptureDecision(new("unchanged_skipped", observationId, sourceHash));
                        await _delay(LiveTranslationRefreshPolicy.NextCaptureDelay(
                            LastErrorCategory == "none" ? 0 : 1), token).ConfigureAwait(false);
                        continue;
                    }

                    var changePlan = processedSource is null
                        ? new LiveTranslationChangePlan([new Rectangle(0, 0, bitmap.Width, bitmap.Height)], false)
                        : LiveTranslationChangePlanner.Analyze(processedSource, source, processedRegions);
                    if ((retryPartialViewport || retryChangedText) && changePlan.Windows.Count == 0)
                        changePlan = new([new Rectangle(0, 0, bitmap.Width, bitmap.Height)], false);
                    if (changePlan.Windows.Count == 0)
                    {
                        ReportCaptureDecision(new(
                            changePlan.IsIncidentalChange ? "incidental_change" : "no_text_window",
                            observationId, sourceHash, 0,
                            changePlan.IsViewportReplacement, changePlan.IsIncidentalChange));
                        stabilityGate.Select([], _ => false, ElapsedMilliseconds());
                        // A uniform hover/theme backing shift needs no OCR, but the
                        // retained translated patch must follow the new backing.
                        if (changePlan.IsIncidentalChange)
                        {
                            var baseline = processedSource;
                            var changedKnownText = baseline is not null
                                && processedRegions.SelectMany(region =>
                                    LiveTranslationTextEvidence.BoundsFor(region, source.Width, source.Height))
                                    .Any(bounds => baseline.CountChangedPixels(source, bounds) != 0);
                            if (changedKnownText)
                            {
                                // A caret-shaped glyph edit must get a fresh OCR pass.
                                // Keep the prior baseline so the next capture sees it.
                                ambiguousCaretStartedAt = null;
                                InvalidateChanged(source);
                                retryChangedText = true;
                                previousHash = null;
                                continue;
                            }
                            // A new narrow glyph outside old OCR word boxes looks
                            // exactly like a caret in one image. Keep the old baseline
                            // until the stroke disappears or outlives a short blink.
                            ambiguousCaretStartedAt ??= ElapsedMilliseconds();
                            if (ElapsedMilliseconds() - ambiguousCaretStartedAt.Value
                                < ambiguousCaretMaximumWaitMilliseconds)
                            {
                                ReportCaptureDecision(new("ambiguous_caret_observing", observationId, sourceHash));
                                await Task.Delay(TimeSpan.FromMilliseconds(100), _timeProvider, token).ConfigureAwait(false);
                                continue;
                            }
                            ambiguousCaretStartedAt = null;
                            InvalidateChanged(source);
                            retryChangedText = true;
                            previousHash = null;
                            ReportCaptureDecision(new("ambiguous_caret_persisted", observationId, sourceHash));
                            continue;
                        }
                        else
                        {
                            ambiguousCaretStartedAt = null;
                            InvalidateChanged(source);
                        }
                        processedSource = source;
                        previousHash = hash;
                        continue;
                    }

                    if (processedSource is not null)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(150), _timeProvider, token).ConfigureAwait(false);
                        using var stabilityProbe = Capture(out var probeSource);
                        bool IsSettled(Rectangle window) =>
                            window.Right <= probeSource.Width && window.Bottom <= probeSource.Height
                            && (source.Hash(window).AsSpan().SequenceEqual(probeSource.Hash(window))
                                || HasSettledChangedTextOutsideMotion(window));
                        bool HasSettledChangedTextOutsideMotion(Rectangle window)
                        {
                            var baseline = processedSource;
                            if (changePlan.IsViewportReplacement || baseline is null
                                || source.Width != probeSource.Width || source.Height != probeSource.Height)
                                return false;
                            var textBounds = processedRegions.SelectMany(region =>
                                    LiveTranslationTextEvidence.BoundsFor(region, source.Width, source.Height))
                                .Where(bounds => bounds.IntersectsWith(window)).ToArray();
                            // A stable changed text area may proceed even if another
                            // known word in the same crop is animated. The later
                            // per-region pixel witness rejects the moving word.
                            return textBounds.Length > 0
                                && textBounds.Any(bounds =>
                                    baseline.CountChangedPixels(source, bounds) != 0
                                    && source.CountChangedPixels(probeSource, bounds) == 0);
                        }
                        var ready = stabilityGate.Select(changePlan.Windows, IsSettled,
                            ElapsedMilliseconds(),
                            // An incomplete viewport must retain a finite retry
                            // even when an unrelated animation never settles.
                            allowUnsettledAfterDeadline: changePlan.IsViewportReplacement
                                || retryPartialViewport && partialRetry.IsDue
                                || _verifySource is not null);
                        if (ready.Count == 0)
                        {
                            ReportCaptureDecision(new("waiting_for_stability", observationId,
                                sourceHash, changePlan.Windows.Count,
                                changePlan.IsViewportReplacement, changePlan.IsIncidentalChange));
                            InvalidateChanged(probeSource);
                            continue;
                        }
                        // Select returns every window both when all are stable and when
                        // the motion deadline expires. In either case process the newest
                        // captured pixels, not the frame from before the settling wait.
                        using var latest = new Bitmap(stabilityProbe);
                        source = probeSource;
                        hash = source.FullHash();
                        changePlan = LiveTranslationChangePlanner.Analyze(
                            processedSource, source, processedRegions);
                        ReportCaptureDecision(new("pipeline_scheduled", observationId,
                            Convert.ToHexString(hash), changePlan.Windows.Count,
                            changePlan.IsViewportReplacement, changePlan.IsIncidentalChange));
                        await ProcessFrameAsync(latest, source, hash, changePlan).ConfigureAwait(false);
                        continue;
                    }

                    ReportCaptureDecision(new("pipeline_scheduled", observationId, sourceHash,
                        changePlan.Windows.Count, changePlan.IsViewportReplacement,
                        changePlan.IsIncidentalChange));
                    await ProcessFrameAsync(bitmap, source, hash, changePlan).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    LastErrorCategory = exception is LiveTranslationPipelineException pipeline
                        ? pipeline.Category : "runtime-failed";
                    ReportCaptureDecision(new("session_recovering", observationId, null,
                        ErrorCategory: LastErrorCategory));
                    if (LastErrorCategory == "stale-worker-cancel-timeout")
                    {
                        ReportStatus("已停止：旧画面处理未能及时取消 · 请重启 ClipRay");
                        SetState(LiveTranslationSessionState.StoppedAfterConsecutiveFailures);
                        return;
                    }
                    previousHash = null;
                    ReportStatus("暂未更新 · 后台恢复中");
                    await _delay(LiveTranslationRefreshPolicy.NextDelay(_route, 1), token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (State != LiveTranslationSessionState.StoppedAfterConsecutiveFailures)
                SetState(LiveTranslationSessionState.Stopped);
        }

        async Task<(bool Checked, LiveTranslationSemanticEvidence? Evidence)> ReadSemanticEvidenceAsync(
            Bitmap bitmap, bool force = false)
        {
            if (_verifySource is null) return (false, null);
            if (semanticOwner is { IsCompleted: false }) return (true, null);
            var now = ElapsedMilliseconds();
            if (!force && now - lastSemanticAttemptAt < semanticMinimumIntervalMilliseconds)
                return (false, null);
            lastSemanticAttemptAt = now;
            var owned = new Bitmap(bitmap);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<OcrRecognitionResult> recognizing;
            try
            {
                recognizing = Task.Run(() => _verifySource(owned, cancellation.Token), cancellation.Token);
                semanticOwner = recognizing;
            }
            catch
            {
                owned.Dispose();
                cancellation.Dispose();
                return (true, null);
            }
            try
            {
                var result = await recognizing.WaitAsync(TimeSpan.FromMilliseconds(300), _timeProvider, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return (true, LiveTranslationSemanticEvidence.FromRecognition(result, owned));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return (true, null);
            }
            finally
            {
                if (recognizing.IsCompleted)
                { owned.Dispose(); cancellation.Dispose(); }
                else
                {
                    cancellation.Cancel();
                    var cleanup = recognizing.ContinueWith(task =>
                    {
                        _ = task.Exception;
                        owned.Dispose();
                        cancellation.Dispose();
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    LiveTranslationWorkerRegistry.Register(cleanup);
                }
            }
        }

        async Task<bool> HasStableContentChangeAsync(LiveTranslationPixelSnapshot source)
        {
            var baseline = processedSource;
            if (baseline is null) return true;
            var change = LiveTranslationChangePlanner.Analyze(baseline, source, processedRegions);
            if (change.IsViewportReplacement) return true;
            if (change.Windows.Count == 0 && !change.IsIncidentalChange) return false;

            await Task.Delay(TimeSpan.FromMilliseconds(150), _timeProvider, token).ConfigureAwait(false);
            using var probe = Capture(out var current);
            if (source.Width != current.Width || source.Height != current.Height) return true;
            var textBounds = processedRegions.SelectMany(region =>
                LiveTranslationTextEvidence.BoundsFor(region, source.Width, source.Height)).ToArray();
            if (textBounds.Any(bounds => baseline.CountChangedPixels(source, bounds) != 0))
                return textBounds.All(bounds => source.CountChangedPixels(current, bounds) == 0);
            return change.Windows.Count > 0 && change.Windows.All(window =>
                window.Right <= current.Width && window.Bottom <= current.Height
                && source.Hash(window).AsSpan().SequenceEqual(current.Hash(window)));
        }

        async Task ProcessFrameAsync(Bitmap bitmap, LiveTranslationPixelSnapshot source,
            byte[] sourceHash, LiveTranslationChangePlan changePlan)
        {
            if (semanticOwner is { IsCompleted: false })
            {
                InvalidateChanged(source);
                ReportStatus("等待上一项识别退出 · 保留已确认区域");
                await _delay(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
                return;
            }
            var firstViewport = processedSource is null;
            if (firstViewport)
                Interlocked.Increment(ref _viewportGeneration);
            else if (changePlan.IsViewportReplacement)
                InvalidateViewport(source);

            // Correctness first: every accepted change rebuilds the latest complete
            // selection. The semantic request cache keeps unchanged text inexpensive.
            IReadOnlyList<Rectangle> windows = [new Rectangle(0, 0, bitmap.Width, bitmap.Height)];
            var identity = new LiveTranslationWorkIdentity(
                Interlocked.Increment(ref _captureId),
                Interlocked.Increment(ref _pipelineId),
                ViewportGeneration);
            // Starting work cannot supersede a queued clear or retained display.
            // Display generation changes only when the display changes or stops.
            ReportStatus("正在识别并机翻…");
            var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var workerBitmap = new Bitmap(bitmap);
            var workerBitmapDisposed = 0;
            void DisposeWorkerBitmap()
            {
                if (Interlocked.Exchange(ref workerBitmapDisposed, 1) == 0) workerBitmap.Dispose();
            }
            var translating = Task.Run(async () =>
            {
                using var scope = LiveTranslationWorkContext.Push(identity);
                return await _translate(workerBitmap, workerCancellation.Token).ConfigureAwait(false);
            }, workerCancellation.Token);
            var detachedWorker = false;
            Diagnostic?.Invoke(this, new("pipeline_started", identity,
                LiveTranslationViewportResultState.Pending));

            var staleViewport = false;
            LiveTranslationSemanticEvidence? workerSourceEvidence = null;
            try
            {
                while (!translating.IsCompleted)
                {
                    using var pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                    if (await Task.WhenAny(translating, Task.Delay(TimeSpan.FromMilliseconds(150), _timeProvider, pollCancellation.Token)).ConfigureAwait(false)
                        == translating)
                    {
                        pollCancellation.Cancel();
                        break;
                    }
                    token.ThrowIfCancellationRequested();
                    using var probe = Capture(out var probeSource);
                    var probePlan = LiveTranslationChangePlanner.Analyze(source, probeSource, processedRegions);
                    var replacesViewport = probePlan.IsViewportReplacement;
                    LiveTranslationSemanticEvidence? probeEvidence = null;
                    if (replacesViewport && _verifySource is not null)
                    {
                        // Separate moving video bands can look like scrolling pixels.
                        // Compare this worker's actual source text and geometry before
                        // canceling; the previously displayed viewport may be unrelated.
                        workerSourceEvidence ??= (await ReadSemanticEvidenceAsync(bitmap, force: true)
                            .ConfigureAwait(false)).Evidence;
                        probeEvidence = (await ReadSemanticEvidenceAsync(probe, force: true)
                            .ConfigureAwait(false)).Evidence;
                        replacesViewport = workerSourceEvidence is null || probeEvidence is null
                            || !probeEvidence.SameSourceStableGeometry(workerSourceEvidence, source, probeSource);
                        ReportCaptureDecision(new(replacesViewport ? "semantic_viewport_changed"
                            : "semantic_motion_during_worker", Interlocked.Read(ref _observationId),
                            Convert.ToHexString(probeSource.FullHash())));
                    }
                    if (!probePlan.IsIncidentalChange)
                    {
                        if (_verifySource is null || replacesViewport)
                            InvalidateChanged(probeSource);
                        else
                        {
                            var (checkedLatest, evidence) = probeEvidence is not null
                                ? (true, probeEvidence)
                                : await ReadSemanticEvidenceAsync(probe).ConfigureAwait(false);
                            if (checkedLatest)
                                InvalidateChanged(probeSource,
                                    sameSourceText: evidence is null
                                        ? static _ => false : item =>
                                            evidence.MatchesStableGeometry(item, source, probeSource));
                        }
                    }
                    if (replacesViewport)
                    {
                        staleViewport = true;
                        InvalidateViewport(probeSource);
                        workerCancellation.Cancel();
                        break;
                    }
                }

                if (staleViewport && !translating.IsCompleted
                    && await Task.WhenAny(translating, Task.Delay(_staleWorkerCancelTimeout, _timeProvider)).ConfigureAwait(false)
                        != translating)
                {
                    DetachWorker("stale-worker-cancel-timeout");
                    throw new LiveTranslationPipelineException("stale-worker-cancel-timeout");
                }

                LiveTranslationFrame completed;
                try
                {
                    completed = await translating.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (staleViewport && !token.IsCancellationRequested)
                {
                    Diagnostic?.Invoke(this, new("pipeline_completed", identity,
                        LiveTranslationViewportResultState.Stale));
                    previousHash = null;
                    return;
                }
                token.ThrowIfCancellationRequested();
                if (staleViewport)
                {
                    Diagnostic?.Invoke(this, new("pipeline_completed", identity,
                        LiveTranslationViewportResultState.Stale));
                    previousHash = null;
                    return;
                }

                var candidateCache = layoutCache.Fork();
                completed = candidateCache.Update(completed, windows) with { WorkIdentity = identity };
                using var currentBitmap = Capture(out var current);
                if (completed.SourceRegions.Count == 0 && completed.LayoutItems.Count == 0
                    && completed.ExpectedLayoutCount == 0 && completed.ErrorCategory == "Scope:partial-or-failed")
                {
                    var stillEmpty = sourceHash.AsSpan().SequenceEqual(current.FullHash());
                    if (!stillEmpty && _verifySource is not null)
                    {
                        var (_, evidence) = await ReadSemanticEvidenceAsync(currentBitmap, force: true)
                            .ConfigureAwait(false);
                        stillEmpty = evidence?.SameSourceStableGeometry([]) == true;
                    }
                    if (!stillEmpty)
                    {
                        previousHash = null;
                        Diagnostic?.Invoke(this, new("pipeline_completed", identity,
                            LiveTranslationViewportResultState.Stale));
                        return;
                    }
                    if (displayed.Count > 0) InvalidateViewport();
                    displayedFrame = null;
                    processedRegions.Clear();
                    processedSource = current;
                    previousHash = current.FullHash();
                    pendingSemanticBackingRefresh = false;
                    retryPartialViewport = false;
                    retryChangedText = false;
                    partialRetry.Reset();
                    LastErrorCategory = "none";
                    ReportCaptureDecision(new("empty_source_waiting", Interlocked.Read(ref _observationId),
                        Convert.ToHexString(previousHash)));
                    Diagnostic?.Invoke(this, new("pipeline_completed", identity,
                        LiveTranslationViewportResultState.Ready, errorCategory: "source-empty"));
                    ReportStatus("等待文字…");
                    return;
                }
                var currentPlan = LiveTranslationChangePlanner.Analyze(source, current, completed.SourceRegions);
                var candidate = LiveTranslationRegionWitness.Create(source, completed.LayoutItems);
                Func<InPlaceLayoutItem, bool>? sameSourceText = null;
                var sourceGeometryVerified = false;
                if (_verifySource is not null
                    && !sourceHash.AsSpan().SequenceEqual(current.FullHash()))
                {
                    var (_, evidence) = await ReadSemanticEvidenceAsync(currentBitmap, force: true)
                        .ConfigureAwait(false);
                    sameSourceText = evidence is null
                        ? static _ => false : item =>
                            evidence.MatchesStableGeometry(item, source, current);
                    sourceGeometryVerified = evidence is not null
                        && evidence.SameSourceStableGeometry(completed.SourceRegions, source, current);
                }
                if (!currentPlan.IsIncidentalChange)
                    InvalidateChanged(current, sameSourceText: sameSourceText);
                var commit = LiveTranslationViewportCommitPolicy.Commit(
                    source, current, completed.SourceRegions, candidate, completed,
                    sameSourceText, sourceGeometryVerified);
                if (commit.State == LiveTranslationViewportCommitState.Stale)
                {
                    InvalidateViewport(current);
                    Diagnostic?.Invoke(this, new("pipeline_completed", identity,
                        LiveTranslationViewportResultState.Stale, commit.Metrics));
                    processedSource = source;
                    previousHash = null;
                    return;
                }

                var resultState = commit.ResultState;
                var resultError = commit.Quarantined.Count > 0 && completed.ErrorCategory == "none"
                    ? "layout-quarantined" : completed.ErrorCategory;
                Diagnostic?.Invoke(this, new("pipeline_completed", identity,
                    resultState, commit.Metrics, resultError, commit.Quarantined));
                if (!LiveTranslationViewportResultPolicy.CanPresent(resultState))
                    throw new LiveTranslationPipelineException(
                        completed.ErrorCategory == "none" ? "viewport-result-failed" : completed.ErrorCategory);

                if (identity.ViewportGeneration != ViewportGeneration) return;
                lock (displayGate)
                {
                    if (identity.ViewportGeneration != ViewportGeneration) return;
                    layoutCache = candidateCache;
                    displayed = commit.Witness;
                    pendingSemanticBackingRefresh = false;
                    displayedFrame = completed with
                    {
                        LayoutItems = displayed.Items,
                        RejectedLayoutCount = commit.Metrics.RejectedLayouts,
                        ErrorCategory = resultError,
                        ResultState = resultState,
                        WorkIdentity = identity,
                    };
                    processedRegions.Clear();
                    processedRegions.AddRange(completed.SourceRegions);
                    processedSource = source;
                    previousHash = sourceHash;
                    retryPartialViewport = resultState == LiveTranslationViewportResultState.AcceptablePartial;
                    retryChangedText = false;
                    if (retryPartialViewport)
                    {
                        var delay = partialRetry.RecordPartial(_route);
                        ReportCaptureDecision(new("partial_retry_scheduled",
                            Interlocked.Read(ref _observationId), Convert.ToHexString(sourceHash),
                            ErrorCategory: $"retry-after-{delay.TotalSeconds:0}-seconds"));
                    }
                    else
                        partialRetry.Reset();
                    LastErrorCategory = resultError;
                    ReportStatus(resultState == LiveTranslationViewportResultState.Ready
                        ? "已更新 · 等待画面变化"
                        : "已更新可用区域 · 未完成区域稍后重试");
                    var presentationGeneration = Interlocked.Increment(ref _generation);
                    if (!token.IsCancellationRequested && identity.ViewportGeneration == ViewportGeneration)
                        FrameReady?.Invoke(this, new(displayedFrame, presentationGeneration));
                }
            }
            finally
            {
                if (!translating.IsCompleted && !detachedWorker)
                {
                    workerCancellation.Cancel();
                    if (await Task.WhenAny(translating, Task.Delay(_staleWorkerCancelTimeout, _timeProvider)).ConfigureAwait(false) == translating)
                    {
                        try { await translating.ConfigureAwait(false); } catch { }
                    }
                    else
                    {
                        var category = token.IsCancellationRequested ? "worker-cancel-timeout" : "stale-worker-cancel-timeout";
                        DetachWorker(category);
                        LastErrorCategory = category;
                        ReportStatus("会话已停止 · 后台任务尚未退出，暂不能重新开始");
                        if (!token.IsCancellationRequested) throw new LiveTranslationPipelineException(category);
                    }
                }
                if (!detachedWorker)
                {
                    DisposeWorkerBitmap();
                    workerCancellation.Dispose();
                }
            }

            void DetachWorker(string category)
            {
                detachedWorker = true;
                var cleanup = translating.ContinueWith(task =>
                {
                    _ = task.Exception;
                    DisposeWorkerBitmap();
                    workerCancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                LiveTranslationWorkerRegistry.Register(cleanup);
                Diagnostic?.Invoke(this, new("pipeline_completed", identity,
                    LiveTranslationViewportResultState.Failed, errorCategory: category));
            }
        }

        void InvalidateViewport(LiveTranslationPixelSnapshot? current = null)
        {
            lock (displayGate)
            {
                Interlocked.Increment(ref _viewportGeneration);
                if (current is not null && displayed.Count > 0)
                {
                    displayed = displayed.RetainUnchanged(current);
                    PublishDisplayed(current);
                    return;
                }
                displayed = LiveTranslationRegionWitness.Empty;
                displayedFrame = null;
                Interlocked.Increment(ref _displayRevision);
                Interlocked.Increment(ref _generation);
                FrameInvalidated?.Invoke(this, EventArgs.Empty);
            }
        }

        void InvalidateChanged(LiveTranslationPixelSnapshot current,
            bool allowIncidentalOutsideText = false,
            Func<InPlaceLayoutItem, bool>? sameSourceText = null)
        {
            lock (displayGate)
            {
                var retained = sameSourceText is not null
                    ? displayed.RetainForViewportCommit(current, sameSourceText, processedRegions)
                    : allowIncidentalOutsideText
                    ? displayed.AdvanceIncidentalChange(current)
                    : displayed.RetainUnchanged(current);
                if (ReferenceEquals(retained, displayed)) return;
                var onlyAdvancedOutsideText = allowIncidentalOutsideText
                    && retained.Count == displayed.Count;
                var samePresentation = retained.Count == displayed.Count
                    && retained.Items.Zip(displayed.Items)
                        .All(pair => ReferenceEquals(pair.First, pair.Second));
                var sameTextBackingOnly = sameSourceText is not null
                    && retained.Count == displayed.Count;
                displayed = retained;
                if (onlyAdvancedOutsideText || samePresentation) return;
                if (sameTextBackingOnly)
                {
                    if (ElapsedMilliseconds() - lastSemanticBackingPublishedAt
                        < semanticBackingPublishIntervalMilliseconds)
                    {
                        pendingSemanticBackingRefresh = true;
                        return;
                    }
                    lastSemanticBackingPublishedAt = ElapsedMilliseconds();
                }
                pendingSemanticBackingRefresh = false;
                PublishDisplayed(current);
            }
        }

        void FlushPendingSemanticBacking(LiveTranslationPixelSnapshot current)
        {
            lock (displayGate)
            {
                if (!pendingSemanticBackingRefresh
                    || ElapsedMilliseconds() - lastSemanticBackingPublishedAt
                        < semanticBackingPublishIntervalMilliseconds)
                    return;
                pendingSemanticBackingRefresh = false;
                lastSemanticBackingPublishedAt = ElapsedMilliseconds();
                PublishDisplayed(current);
            }
        }

        void PublishDisplayed(LiveTranslationPixelSnapshot current)
        {
            lock (displayGate)
            {
                var revision = Interlocked.Increment(ref _displayRevision);
                var generation = Interlocked.Increment(ref _generation);
                if (displayedFrame is null)
                {
                    if (displayed.Count == 0) FrameInvalidated?.Invoke(this, EventArgs.Empty);
                    return;
                }
                var metrics = LiveTranslationViewportCommitPolicy.Measure(
                    current, displayedFrame, displayedFrame.LayoutItems.Count, displayed.Items);
                var state = LiveTranslationViewportResultPolicy.Classify(displayedFrame, metrics);
                Diagnostic?.Invoke(this, new("viewport_invalidated", displayedFrame.WorkIdentity!,
                    state, metrics, displayedFrame.ErrorCategory));
                if (!LiveTranslationViewportResultPolicy.CanPresent(state))
                {
                    displayed = LiveTranslationRegionWitness.Empty;
                    displayedFrame = null;
                    FrameInvalidated?.Invoke(this, EventArgs.Empty);
                    return;
                }
                if (!token.IsCancellationRequested)
                {
                    displayedFrame = displayedFrame with { LayoutItems = displayed.Items, ResultState = state };
                    FrameReady?.Invoke(this, new(displayedFrame, generation, ViewportGeneration, revision));
                }
            }
        }
    }
    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        if (!_stopSource.IsCancellationRequested) _stopSource.Cancel();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        _stopSource.Dispose();
    }

    private void SetState(LiveTranslationSessionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportStatus(string status)
    {
        Status = status;
        try { StatusChanged?.Invoke(this, EventArgs.Empty); }
        catch { /* A status observer must not change translation or retries. */ }
    }

    private void ReportCaptureDecision(LiveTranslationCaptureDecision decision)
    {
        try { CaptureDecision?.Invoke(this, decision); }
        catch { /* A diagnostic observer must not change capture or retries. */ }
    }

    private static byte[] HashPixels(Bitmap source)
    {
        using var normalized = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized)) graphics.DrawImageUnscaled(source, 0, 0);
        var bounds = new Rectangle(0, 0, normalized.Width, normalized.Height);
        var data = normalized.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(BitConverter.GetBytes(normalized.Width));
            hash.AppendData(BitConverter.GetBytes(normalized.Height));
            var row = new byte[normalized.Width * 4];
            for (var y = 0; y < normalized.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                hash.AppendData(row);
            }
            return hash.GetHashAndReset();
        }
        finally
        {
            normalized.UnlockBits(data);
        }
    }
}

internal static class LiveTranslationWorkerRegistry
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Task, byte> Workers = new();
    internal static bool HasUnresolvedWorker => !Workers.IsEmpty;
    internal static async Task DrainAsync()
    {
        while (!Workers.IsEmpty)
        {
            await Task.WhenAll(Workers.Keys).ConfigureAwait(false);
            await Task.Yield();
        }
    }

    internal static void Register(Task worker)
    {
        if (!Workers.TryAdd(worker, 0)) return;
        _ = worker.ContinueWith(static task => { _ = task.Exception; Workers.TryRemove(task, out _); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

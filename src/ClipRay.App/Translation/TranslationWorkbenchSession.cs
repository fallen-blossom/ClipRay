using System.Drawing;
using System.Diagnostics;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public enum WorkbenchRegionState { Pending, Modified, Translating, Translated, Preserved, Failed, LayoutRejected, Cancelled }

public sealed record WorkbenchRegion(Guid Id, VisualTextRegion Original, string Draft,
    string SourceLanguage, string TargetLanguage, TranslationRouteKind Route, long Revision,
    long Attempt, WorkbenchRegionState State, string Translation = "", string Detail = "",
    InPlaceLayoutItem? Layout = null)
{
    public WorkbenchManualBaseline? ManualBaseline { get; init; }
}

public sealed record WorkbenchManualBaseline(WorkbenchRegionState State, string Translation,
    string Detail, InPlaceLayoutItem? Layout);

public sealed record WorkbenchTranslationInput(Guid SessionId, Guid RegionId, long Revision,
    long Attempt, VisualTextRegion Region, string SourceLanguage, string TargetLanguage,
    TranslationRouteKind Route);

public sealed record WorkbenchTranslationOutput(Guid RegionId, TranslationResult Result,
    InPlaceLayoutItem? Layout = null, string? LayoutFailure = null);

public sealed record WorkbenchAttempt(Guid RegionId, long Revision, long Attempt, string SourceText,
    string SourceLanguage, string TargetLanguage, TranslationRouteKind RequestedRoute,
    DateTimeOffset StartedUtc, long ElapsedMilliseconds, string Status, bool Completed,
    bool CurrentAtCompletion, TranslationExecutionEvidence? ExecutionEvidence = null);

/// <summary>Owns a private source copy and rejects stale per-region completions, even
/// when a provider ignores cancellation. Each worker owns a separate pixel copy.</summary>
public sealed partial class TranslationWorkbenchSession : IDisposable
{
    private readonly object _sync = new();
    private readonly Bitmap _source;
    private readonly Dictionary<Guid, WorkbenchRegion> _regions = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _active = [];
    private readonly List<WorkbenchAttempt> _attempts = [];
    private bool _closed;
    private long _nextAttempt;

    public TranslationWorkbenchSession(Bitmap source, PixelRect captureBounds, PixelRect selection,
        IReadOnlyList<VisualTextRegion> regions, TranslationRouteKind route, string targetLanguage)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        if (captureBounds.Width != source.Width || captureBounds.Height != source.Height
            || selection.IsEmpty || selection.Left < captureBounds.Left || selection.Top < captureBounds.Top
            || selection.Right > captureBounds.Right || selection.Bottom > captureBounds.Bottom)
            throw new ArgumentException("Selection must be inside the source capture bounds.");
        _source = (Bitmap)source.Clone();
        CaptureBounds = captureBounds;
        Selection = selection;
        foreach (var region in regions)
        {
            var id = Guid.NewGuid();
            var original = region with { Id = id.ToString("N"), Lines = Array.AsReadOnly(region.Lines
                .Select(line => line with { Words = Array.AsReadOnly(line.Words.ToArray()) }).ToArray()),
                OriginalOcrWords = Array.AsReadOnly(region.OriginalOcrWords.ToArray()) };
            _regions.Add(id, new(id, original, original.SourceText, "auto", targetLanguage,
                route, 0, 0, WorkbenchRegionState.Pending));
        }
    }

    public Guid Id { get; } = Guid.NewGuid();
    public PixelRect CaptureBounds { get; }
    public PixelRect Selection { get; }
    public event EventHandler? Changed;
    public IReadOnlyList<WorkbenchRegion> Regions { get { lock (_sync) return _regions.Values
        .OrderBy(region => region.Original.Bounds.Top).ThenBy(region => region.Original.Bounds.Left).ToArray(); } }
    public IReadOnlyList<WorkbenchAttempt> Attempts { get { lock (_sync) return _attempts.ToArray(); } }

    public Bitmap CopySource()
    {
        lock (_sync) { ThrowIfClosed(); return (Bitmap)_source.Clone(); }
    }

    public void Edit(Guid id, string draft) => Change(id, region => region with { Draft = draft });
    public void RestoreOriginal(Guid id) => Change(id, region => region with { Draft = region.Original.SourceText });
    public void Configure(Guid id, string sourceLanguage, string targetLanguage, TranslationRouteKind route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        Change(id, region => region with { SourceLanguage = sourceLanguage, TargetLanguage = targetLanguage, Route = route });
    }

    private void Change(Guid id, Func<WorkbenchRegion, WorkbenchRegion> change)
    {
        CancellationTokenSource? pending;
        lock (_sync)
        {
            ThrowIfClosed();
            var old = _regions[id];
            var next = change(old);
            ArgumentNullException.ThrowIfNull(next.Draft);
            _regions[id] = next with { Revision = old.Revision + 1, Attempt = ++_nextAttempt,
                State = WorkbenchRegionState.Modified, Translation = "", Detail = "", Layout = null,
                ManualBaseline = null };
            _active.Remove(id, out pending);
        }
        CancelSafely(pending);
        Notify();
    }

    public void Preserve(Guid id) => StopRegion(id, WorkbenchRegionState.Preserved);
    public void Cancel(Guid id) => StopRegion(id, WorkbenchRegionState.Cancelled);
    public long BeginManualTranslation(Guid id, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        CancellationTokenSource? pending;
        long attempt;
        lock (_sync)
        {
            ThrowIfClosed();
            var old = _regions[id];
            var baseline = old.ManualBaseline ?? new WorkbenchManualBaseline(
                old.State == WorkbenchRegionState.Translating ? WorkbenchRegionState.Cancelled : old.State,
                old.State == WorkbenchRegionState.Translating ? "" : old.Translation,
                old.State == WorkbenchRegionState.Translating ? "cancelled" : old.Detail,
                old.State == WorkbenchRegionState.Translating ? null : old.Layout);
            attempt = ++_nextAttempt;
            _regions[id] = old with { Attempt = attempt, State = WorkbenchRegionState.LayoutRejected,
                Translation = text, Detail = "manual-layout-pending", Layout = null,
                ManualBaseline = baseline };
            _active.Remove(id, out pending);
        }
        CancelSafely(pending);
        return attempt;
    }
    public bool CompleteManualTranslation(Guid id, long attempt, InPlaceLayoutItem? layout, string? layoutFailure)
    {
        lock (_sync)
        {
            if (_closed || !_regions.TryGetValue(id, out var region) || region.Attempt != attempt
                || region.ManualBaseline is null) return false;
            var sourceMatches = string.Equals(region.Translation.Trim(), region.Draft.Trim(), StringComparison.Ordinal);
            _regions[id] = region with
            {
                State = layout is not null ? WorkbenchRegionState.Translated
                    : sourceMatches ? WorkbenchRegionState.Preserved : WorkbenchRegionState.LayoutRejected,
                Layout = layout,
                Detail = layout is not null ? "manual-translation" : sourceMatches
                    ? "manual-source-preserved" : layoutFailure ?? "manual-layout-unavailable"
            };
        }
        Notify();
        return true;
    }
    public bool RestoreTranslationBeforeManualEdit(Guid id)
    {
        CancellationTokenSource? pending;
        lock (_sync)
        {
            ThrowIfClosed();
            var region = _regions[id];
            if (region.ManualBaseline is not { } baseline) return false;
            _regions[id] = region with { Attempt = ++_nextAttempt, State = baseline.State,
                Translation = baseline.Translation, Detail = baseline.Detail, Layout = baseline.Layout,
                ManualBaseline = null };
            _active.Remove(id, out pending);
        }
        CancelSafely(pending);
        Notify();
        return true;
    }
    private void StopRegion(Guid id, WorkbenchRegionState state)
    {
        CancellationTokenSource? pending;
        lock (_sync)
        {
            ThrowIfClosed();
            var old = _regions[id];
            _regions[id] = old with { Attempt = ++_nextAttempt, State = state,
                Translation = "", Layout = null, Detail = "", ManualBaseline = null };
            _active.Remove(id, out pending);
        }
        CancelSafely(pending);
        Notify();
    }

    public async Task TranslateAsync(Guid id,
        Func<Bitmap, WorkbenchTranslationInput, CancellationToken, Task<WorkbenchTranslationOutput>> translate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(translate);
        WorkbenchTranslationInput input;
        Bitmap pixels;
        CancellationTokenSource operation;
        CancellationTokenSource? previous;
        lock (_sync)
        {
            ThrowIfClosed();
            var region = _regions[id];
            if (string.IsNullOrWhiteSpace(region.Draft)) throw new InvalidOperationException("Region text is empty.");
            pixels = (Bitmap)_source.Clone();
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _active.Remove(id, out previous);
            _active[id] = operation;
            var attempt = ++_nextAttempt;
            _regions[id] = region with { Attempt = attempt, State = WorkbenchRegionState.Translating,
                Translation = "", Layout = null, Detail = "", ManualBaseline = null };
            input = new(Id, id, region.Revision, attempt, region.Original with { SourceText = region.Draft },
                region.SourceLanguage, region.TargetLanguage, region.Route);
            _attempts.Add(new(id, region.Revision, attempt, region.Draft, region.SourceLanguage,
                region.TargetLanguage, region.Route, DateTimeOffset.UtcNow, 0, "running", false, false));
        }
        var elapsed = Stopwatch.StartNew();
        var completionStatus = "translation-failed";
        TranslationExecutionEvidence? executionEvidence = null;
        CancelSafely(previous);
        Notify();
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            var output = await translate(pixels, input, operation.Token).ConfigureAwait(false);
            if (output.RegionId == id) executionEvidence = output.Result.ExecutionEvidence;
            operation.Token.ThrowIfCancellationRequested();
            completionStatus = output.RegionId != id ? "region-result-mismatch" : output.Result.Status;
            if (output.RegionId != id)
                Apply(input, WorkbenchRegionState.Failed, "", "region-result-mismatch", null);
            else if (!output.Result.IsSuccess || string.IsNullOrWhiteSpace(output.Result.Text))
                Apply(input, WorkbenchRegionState.Failed, "", output.Result.Status, null);
            else if (output.Result.Status == "machine-preserved"
                || string.Equals(output.Result.Text.Trim(), input.Region.SourceText.Trim(), StringComparison.Ordinal))
                Apply(input, WorkbenchRegionState.Preserved, "", output.Result.Status, null);
            else if (output.Layout is null || output.LayoutFailure is not null)
            {
                completionStatus = output.LayoutFailure ?? "layout-unavailable";
                Apply(input, WorkbenchRegionState.LayoutRejected, output.Result.Text,
                    completionStatus, null);
            }
            else if (output.Layout.SourceRegion?.Id != input.Region.Id)
            {
                completionStatus = "layout-region-mismatch";
                Apply(input, WorkbenchRegionState.LayoutRejected, output.Result.Text, "layout-region-mismatch", null);
            }
            else
                Apply(input, WorkbenchRegionState.Translated, output.Result.Text, output.Result.Status, output.Layout);
        }
        catch (OperationCanceledException) { completionStatus = "cancelled"; Apply(input, WorkbenchRegionState.Cancelled, "", "cancelled", null); }
        catch { completionStatus = "translation-failed"; Apply(input, WorkbenchRegionState.Failed, "", completionStatus, null); }
        finally
        {
            lock (_sync)
            {
                if (_active.TryGetValue(id, out var current) && ReferenceEquals(current, operation)) _active.Remove(id);
                if (!_closed)
                {
                    var index = _attempts.FindIndex(attempt => attempt.Attempt == input.Attempt);
                    if (index >= 0) _attempts[index] = _attempts[index] with
                    {
                        Completed = true, ElapsedMilliseconds = elapsed.ElapsedMilliseconds, Status = completionStatus,
                        ExecutionEvidence = executionEvidence,
                        CurrentAtCompletion = _regions.TryGetValue(id, out var region)
                            && region.Attempt == input.Attempt && region.Revision == input.Revision
                    };
                }
            }
            pixels.Dispose();
            operation.Dispose();
            Notify();
        }
    }

    private void Apply(WorkbenchTranslationInput input, WorkbenchRegionState state, string text,
        string detail, InPlaceLayoutItem? layout)
    {
        lock (_sync)
        {
            if (_closed || !_regions.TryGetValue(input.RegionId, out var region)
                || region.Revision != input.Revision || region.Attempt != input.Attempt) return;
            _regions[input.RegionId] = region with { State = state, Translation = text, Detail = detail, Layout = layout };
        }
        Notify();
    }

    private void Notify()
    {
        // UI observers must not interrupt worker cleanup or other observers.
        var observers = Changed;
        if (observers is null) return;
        foreach (EventHandler observer in observers.GetInvocationList())
            try { observer(this, EventArgs.Empty); } catch { }
    }
    private void ThrowIfClosed() { ObjectDisposedException.ThrowIf(_closed, this); }
    private static void CancelSafely(CancellationTokenSource? operation)
    {
        // A worker can finish between invalidation and cancellation. Identity is authoritative.
        try { operation?.Cancel(); } catch (ObjectDisposedException) { } catch (AggregateException) { }
    }

    public void Dispose()
    {
        CancellationTokenSource[] pending;
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
            pending = _active.Values.ToArray();
            _active.Clear();
            _regions.Clear();
            _attempts.Clear();
            _groupUndo.Clear();
            _groupRedo.Clear();
            Changed = null;
            _source.Dispose();
        }
        foreach (var operation in pending) CancelSafely(operation);
    }
}

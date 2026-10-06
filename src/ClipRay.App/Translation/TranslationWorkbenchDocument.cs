using System.Drawing;
using ClipRay.App.Ocr;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public enum WorkbenchRecognitionState { Ready, Recognizing, Recognized, Empty, Failed, Cancelled, Closed }

/// <summary>Keeps the source available even when recognition fails or returns no text.</summary>
public sealed class TranslationWorkbenchDocument : IDisposable
{
    private readonly object _sync = new();
    private readonly Bitmap _source;
    private readonly TranslationApplicationOrchestrator _orchestrator;
    private readonly Func<IReadOnlyList<OcrTextBlock>, IReadOnlyList<OcrTextBlock>> _scope;
    private CancellationTokenSource? _recognition;
    private long _attempt;
    private TranslationWorkbenchSession? _session;
    private WorkbenchRecognitionState _state;
    private string? _windowsOcrLanguage;
    private string _recognitionDetail = "";

    public TranslationWorkbenchDocument(Bitmap source, PixelRect captureBounds, PixelRect selection,
        TranslationRouteKind route, string targetLanguage, TranslationApplicationOrchestrator orchestrator,
        Func<IReadOnlyList<OcrTextBlock>, IReadOnlyList<OcrTextBlock>> scope, bool commonMachineModel = false)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(scope);
        // Reuse the session's coordinate validation before taking ownership.
        using var validation = new TranslationWorkbenchSession(source, captureBounds, selection, [], route, targetLanguage);
        _source = validation.CopySource();
        CaptureBounds = captureBounds; Selection = selection; Route = route; TargetLanguage = targetLanguage;
        _orchestrator = orchestrator; _scope = scope;
        CommonMachineModel = commonMachineModel;
    }

    public PixelRect CaptureBounds { get; }
    public PixelRect Selection { get; }
    public TranslationRouteKind Route { get; }
    public string TargetLanguage { get; }
    public bool CommonMachineModel { get; }
    public string? WindowsOcrLanguage { get { lock (_sync) return _windowsOcrLanguage; } }
    public string RecognitionDetail { get { lock (_sync) return _recognitionDetail; } }
    public void ConfigureOcrLanguage(string? languageTag)
    {
        lock (_sync)
        {
            ThrowIfClosed();
            if (_session is not null || _state == WorkbenchRecognitionState.Recognizing)
                throw new InvalidOperationException("OCR language is fixed during and after recognition.");
            if (languageTag is not null) ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
            _windowsOcrLanguage = languageTag;
        }
        Notify();
    }
    public WorkbenchRecognitionState State { get { lock (_sync) return _state; } }
    public TranslationWorkbenchSession? Session { get { lock (_sync) return _session; } }
    public event EventHandler? Changed;
    public Bitmap CopySource() { lock (_sync) { ThrowIfClosed(); return (Bitmap)_source.Clone(); } }

    public async Task RecognizeAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource operation;
        CancellationTokenSource? previous;
        Bitmap pixels;
        long attempt;
        string? windowsLanguage;
        lock (_sync)
        {
            ThrowIfClosed();
            if (_session is not null) throw new InvalidOperationException("An existing session must not be replaced by OCR.");
            pixels = (Bitmap)_source.Clone();
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            previous = _recognition; _recognition = operation;
            attempt = ++_attempt; _state = WorkbenchRecognitionState.Recognizing;
            _recognitionDetail = "";
            windowsLanguage = _windowsOcrLanguage;
        }
        CancelSafely(previous); Notify();
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            var result = await _orchestrator.RecognizeWorkbenchAsync(pixels, _scope, operation.Token, windowsLanguage).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_state == WorkbenchRecognitionState.Closed || attempt != _attempt) return;
                _recognitionDetail = result.Status;
                if (result.IsSuccess)
                {
                    _session = new TranslationWorkbenchSession(_source, CaptureBounds, Selection, result.Regions, Route, TargetLanguage);
                    _state = WorkbenchRecognitionState.Recognized;
                }
                else _state = result.Status == "ocr-empty" ? WorkbenchRecognitionState.Empty : WorkbenchRecognitionState.Failed;
            }
            Notify();
        }
        catch (OperationCanceledException) { SetFailure(attempt, WorkbenchRecognitionState.Cancelled); }
        catch { SetFailure(attempt, WorkbenchRecognitionState.Failed); }
        finally
        {
            lock (_sync) if (ReferenceEquals(_recognition, operation)) _recognition = null;
            pixels.Dispose(); operation.Dispose();
        }
    }

    public Task TranslateAsync(Guid regionId, CancellationToken cancellationToken = default)
    {
        TranslationWorkbenchSession session;
        lock (_sync) { ThrowIfClosed(); session = _session ?? throw new InvalidOperationException("Recognition is required."); }
        var siblings = session.Regions.Select(region => region.Original).ToArray();
        return session.TranslateAsync(regionId, (pixels, input, token) =>
            _orchestrator.TranslateWorkbenchRegionAsync(pixels, input, siblings, token), cancellationToken);
    }

    public bool EditTranslation(Guid regionId, string correctedText)
    {
        TranslationWorkbenchSession session;
        lock (_sync) { ThrowIfClosed(); session = _session ?? throw new InvalidOperationException("Recognition is required."); }
        var region = session.Regions.First(item => item.Id == regionId);
        var attempt = session.BeginManualTranslation(regionId, correctedText);
        InPlaceLayoutItem? layout = null;
        string? failure = null;
        try
        {
            using var pixels = session.CopySource();
            var source = region.Original with { SourceText = region.Draft };
            var plan = InPlaceLayoutService.BuildRegionPlan(pixels, [source],
                [new TranslationResult(true, correctedText, "manual-translation")]);
            layout = plan.Items.SingleOrDefault();
            failure = plan.Rejections.FirstOrDefault()?.Reason;
        }
        catch (Exception) { failure = "manual-layout-failed"; }
        return session.CompleteManualTranslation(regionId, attempt, layout, failure);
    }

    public void CancelRecognition()
    {
        CancellationTokenSource? pending;
        lock (_sync)
        {
            ThrowIfClosed();
            if (_state != WorkbenchRecognitionState.Recognizing) return;
            ++_attempt; pending = _recognition; _recognition = null; _state = WorkbenchRecognitionState.Cancelled;
        }
        CancelSafely(pending); Notify();
    }

    private void SetFailure(long attempt, WorkbenchRecognitionState state)
    {
        lock (_sync) { if (_state == WorkbenchRecognitionState.Closed || attempt != _attempt) return; _state = state; }
        Notify();
    }
    private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(_state == WorkbenchRecognitionState.Closed, this);
    private void Notify()
    {
        var observers = Changed;
        if (observers is null) return;
        foreach (EventHandler observer in observers.GetInvocationList())
            try { observer(this, EventArgs.Empty); } catch { }
    }
    private static void CancelSafely(CancellationTokenSource? operation)
    { try { operation?.Cancel(); } catch (ObjectDisposedException) { } catch (AggregateException) { } }
    public void Dispose()
    {
        CancellationTokenSource? pending;
        lock (_sync)
        {
            if (_state == WorkbenchRecognitionState.Closed) return;
            _state = WorkbenchRecognitionState.Closed; ++_attempt;
            pending = _recognition; _recognition = null;
            _session?.Dispose(); _session = null; _source.Dispose(); Changed = null;
        }
        CancelSafely(pending);
    }
}

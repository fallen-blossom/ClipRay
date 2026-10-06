using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ClipRay.App.Scrolling;

public enum ScrollCaptureMode { Manual, Automatic }

public enum ScrollCaptureState { Idle, Capturing, Paused, Finishing, Failed }

public sealed record LongCaptureRequest(Rectangle PhysicalPixelRegion, ScrollCaptureMode Mode);

public enum ScrollCaptureStopReason { EndOfScroll, LowConfidence, CaptureFailure, InputFailure, Cancelled, CompletedByUser, FrameLimit }

public sealed record ScrollCaptureProgress(int AcceptedFrameCount, ScrollCaptureStopReason? StopReason = null, string? Status = null);

/// <summary>Bitmap 只在同步事件回调期间有效；订阅方需要保留时应立即复制或转换。</summary>
public sealed class ScrollCapturePreviewEventArgs(Bitmap bitmap) : EventArgs
{
    public Bitmap Bitmap { get; } = bitmap;
}

public sealed record ScrollCaptureOptions(
    int MaximumFrames = 160,
    TimeSpan? SettleDelay = null,
    int MaximumPolls = 720,
    int ConsecutiveRepeatedFramesToStop = 8)
{
    public TimeSpan EffectiveSettleDelay => SettleDelay ?? TimeSpan.FromMilliseconds(250);
}

public interface IScrollRegionCapture
{
    Bitmap Capture(Rectangle physicalPixelRegion);
}

public sealed class ScrollCaptureResult : IDisposable
{
    private readonly FrameStitcher _stitcher;
    private readonly LongCaptureRunDiagnostics? _diagnostics;
    private int _disposed;
    private bool _preserveForRecovery;
    internal ScrollCaptureResult(
        IReadOnlyList<string> segments,
        string tempSessionDirectory,
        ScrollCaptureStopReason stopReason,
        FrameStitcher stitcher,
        LongCaptureRunDiagnostics? diagnostics)
    {
        SegmentPaths = segments;
        TempSessionDirectory = tempSessionDirectory;
        StopReason = stopReason;
        _stitcher = stitcher;
        _diagnostics = diagnostics;
    }

    public IReadOnlyList<string> SegmentPaths { get; }
    public int AcceptedFrameCount => SegmentPaths.Count;
    public string TempSessionDirectory { get; }
    public ScrollCaptureStopReason StopReason { get; }

    public Bitmap CreateComposite()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (SegmentPaths.Count == 0) throw new InvalidOperationException("No valid capture segment is available.");
        using var initial = new Bitmap(SegmentPaths[0]);
        var composite = new Bitmap(initial);
        try
        {
            var previousPath = SegmentPaths[0];
            foreach (var path in SegmentPaths.Skip(1))
            {
                using var previous = new Bitmap(previousPath);
                using var next = new Bitmap(path);
                var analysis = _stitcher.Analyze(previous, next);
                using var stitched = _stitcher.Stitch(composite, next, analysis);
                composite.Dispose();
                composite = new Bitmap(stitched);
                previousPath = path;
            }
            _diagnostics?.RecordComposite(composite.Size);
            return composite;
        }
        catch
        {
            composite.Dispose();
            throw;
        }
    }

    /// <summary>仅在最终 PNG 已经成功保存后允许删除本次会话的精确临时目录。</summary>
    public void CleanupAfterSuccessfulOutput()
    {
        Interlocked.Exchange(ref _disposed, 1);
        if (Directory.Exists(TempSessionDirectory)) Directory.Delete(TempSessionDirectory, recursive: true);
    }

    /// <summary>失败、取消或调用方选择 partial 时保留目录以便恢复。</summary>
    public void PreserveForRecovery()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _preserveForRecovery = true;
        File.WriteAllText(Path.Combine(TempSessionDirectory, ScrollCaptureTempStore.RecoveryMarker),
            System.Text.Json.JsonSerializer.Serialize(new { SchemaVersion = 1, AcceptedFrameCount, StopReason }));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (!_preserveForRecovery && Directory.Exists(TempSessionDirectory)) Directory.Delete(TempSessionDirectory, recursive: true);
    }
}

public sealed class ScrollCaptureSession : IDisposable
{
    private readonly Rectangle _physicalPixelRegion;
    private readonly ScrollCaptureMode _mode;
    private readonly IScrollRegionCapture _capture;
    private readonly IScrollInputService _input;
    private readonly IScrollSettler _settler;
    private readonly FrameStitcher _stitcher;
    private readonly ScrollCaptureOptions _options;
    private readonly string _tempSessionDirectory;
    private readonly LongCaptureRunDiagnostics? _diagnostics;
    private int _automaticScrollingEnabled;
    private int _completionRequested;
    private int _state = (int)ScrollCaptureState.Idle;

    public event EventHandler<ScrollCaptureProgress>? ProgressChanged;

    public event EventHandler<ScrollCapturePreviewEventArgs>? PreviewChanged;

    public event EventHandler<ScrollCaptureState>? StateChanged;

    public bool AutomaticScrollingEnabled => Volatile.Read(ref _automaticScrollingEnabled) != 0;

    public ScrollCaptureState State => (ScrollCaptureState)Volatile.Read(ref _state);

    public void RequestCompletion() => Volatile.Write(ref _completionRequested, 1);

    public void SetAutomaticScrolling(bool enabled)
    {
        Volatile.Write(ref _automaticScrollingEnabled, enabled ? 1 : 0);
        if (State != ScrollCaptureState.Idle)
            SetState(enabled ? ScrollCaptureState.Capturing : ScrollCaptureState.Paused);
    }

    public ScrollCaptureSession(
        Rectangle physicalPixelRegion,
        ScrollCaptureMode mode,
        IScrollRegionCapture capture,
        IScrollInputService input,
        IScrollSettler settler,
        string temporaryRoot,
        ScrollCaptureOptions? options = null,
        FrameStitcher? stitcher = null,
        LongCaptureRunDiagnostics? diagnostics = null)
    {
        if (physicalPixelRegion.Width <= 0 || physicalPixelRegion.Height <= 0) throw new ArgumentOutOfRangeException(nameof(physicalPixelRegion));
        _physicalPixelRegion = physicalPixelRegion;
        _mode = mode;
        _automaticScrollingEnabled = mode == ScrollCaptureMode.Automatic ? 1 : 0;
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _settler = settler ?? throw new ArgumentNullException(nameof(settler));
        _options = options ?? new ScrollCaptureOptions();
        if (_options.MaximumFrames < 1 || _options.MaximumPolls < 1 || _options.ConsecutiveRepeatedFramesToStop < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        _stitcher = stitcher ?? new FrameStitcher();
        _diagnostics = diagnostics;
        _tempSessionDirectory = Path.Combine(temporaryRoot ?? throw new ArgumentNullException(nameof(temporaryRoot)), Guid.NewGuid().ToString("N"));
    }

    public async Task<ScrollCaptureResult> RunAsync(CancellationToken cancellationToken)
    {
        var segments = new List<string>(_options.MaximumFrames);
        Bitmap? previous = null;
        Bitmap? composite = null;
        var reason = ScrollCaptureStopReason.FrameLimit;
        var consecutiveUnusableFrames = 0;
        var hasObservedMovement = false;
        var announcedNoMovement = false;
        try
        {
            SetState(ScrollCaptureState.Capturing);
            Directory.CreateDirectory(_tempSessionDirectory);
            previous = _capture.Capture(_physicalPixelRegion);
            Persist(previous, segments);
            composite = ClonePreservingPixels(previous);
            PublishPreview(composite);
            ProgressChanged?.Invoke(this, new ScrollCaptureProgress(segments.Count));
            for (var poll = 0; poll < _options.MaximumPolls || (AutomaticScrollingEnabled && !hasObservedMovement); poll++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segments.Count >= _options.MaximumFrames) break;
                if (AutomaticScrollingEnabled)
                {
                    await _input.SendWheelAsync(Center(_physicalPixelRegion), ScrollInputService.DefaultWheelDelta, cancellationToken);
                }
                await _settler.WaitForSettleAsync(_options.EffectiveSettleDelay, cancellationToken);
                using var current = _capture.Capture(_physicalPixelRegion);
                var analysis = await Task.Run(() => _stitcher.Analyze(previous, current, cancellationToken), cancellationToken);
                if (analysis.Disposition == FrameStitchDisposition.Append)
                {
                    hasObservedMovement = true;
                    announcedNoMovement = false;
                    consecutiveUnusableFrames = 0;
                    Persist(current, segments);
                    using var stitched = _stitcher.Stitch(composite, current, analysis);
                    composite.Dispose();
                    composite = ClonePreservingPixels(stitched);
                    PublishPreview(composite);
                    ProgressChanged?.Invoke(this, new ScrollCaptureProgress(segments.Count));
                    previous.Dispose();
                    previous = ClonePreservingPixels(current);
                    continue;
                }
                if (!AutomaticScrollingEnabled)
                {
                    if (!announcedNoMovement)
                    {
                        ProgressChanged?.Invoke(this, new ScrollCaptureProgress(
                            segments.Count,
                            Status: "当前画面暂时无法可靠拼接；仍在等待手动滚动或完成。"));
                        announcedNoMovement = true;
                    }
                    continue;
                }
                if (!hasObservedMovement)
                {
                    if (!announcedNoMovement)
                    {
                        ProgressChanged?.Invoke(this, new ScrollCaptureProgress(
                            segments.Count,
                            Status: "尚未检测到可拼接滚动；自动滚动仍在继续。"));
                        announcedNoMovement = true;
                    }
                    continue;
                }
                consecutiveUnusableFrames++;
                if (consecutiveUnusableFrames < _options.ConsecutiveRepeatedFramesToStop)
                {
                    ProgressChanged?.Invoke(this, new ScrollCaptureProgress(
                        segments.Count,
                        Status: "暂时无法确认新内容；自动滚动仍在继续。"));
                    continue;
                }
                reason = analysis.Disposition == FrameStitchDisposition.Repeated ? ScrollCaptureStopReason.EndOfScroll : ScrollCaptureStopReason.LowConfidence;
                break;
            }
        }
        catch (OperationCanceledException)
        {
            reason = Volatile.Read(ref _completionRequested) != 0
                ? ScrollCaptureStopReason.CompletedByUser
                : ScrollCaptureStopReason.Cancelled;
        }
        catch (InvalidOperationException) when (AutomaticScrollingEnabled)
        {
            reason = ScrollCaptureStopReason.InputFailure;
        }
        catch
        {
            reason = ScrollCaptureStopReason.CaptureFailure;
        }
        finally
        {
            previous?.Dispose();
            composite?.Dispose();
        }
        SetState(reason is ScrollCaptureStopReason.InputFailure or ScrollCaptureStopReason.CaptureFailure or ScrollCaptureStopReason.LowConfidence
            ? ScrollCaptureState.Failed
            : ScrollCaptureState.Finishing);
        ProgressChanged?.Invoke(this, new ScrollCaptureProgress(segments.Count, reason));
        _diagnostics?.RecordStop(reason);
        return new ScrollCaptureResult(segments, _tempSessionDirectory, reason, _stitcher, _diagnostics);
    }

    public void Dispose() { }

    private void Persist(Bitmap bitmap, ICollection<string> segments)
    {
        var path = Path.Combine(_tempSessionDirectory, $"{segments.Count:D3}.png");
        bitmap.Save(path, ImageFormat.Png);
        segments.Add(path);
        _diagnostics?.RecordSegment(bitmap.Size);
    }

    private void PublishPreview(Bitmap composite)
    {
        _diagnostics?.RecordPreview(composite.Size);
        var handler = PreviewChanged;
        if (handler is null) return;
        using var snapshot = ClonePreservingPixels(composite);
        handler(this, new ScrollCapturePreviewEventArgs(snapshot));
    }

    private static Bitmap ClonePreservingPixels(Bitmap bitmap)
    {
        var clone = new Bitmap(bitmap.Width, bitmap.Height, bitmap.PixelFormat);
        using var graphics = Graphics.FromImage(clone);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImageUnscaled(bitmap, 0, 0);
        return clone;
    }

    private static Point Center(Rectangle rectangle) => new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);

    private void SetState(ScrollCaptureState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) == (int)state) return;
        StateChanged?.Invoke(this, state);
    }
}

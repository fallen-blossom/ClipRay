using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ClipRay.App.Scrolling;

public enum ScrollCaptureMode { Manual, Automatic }

public enum ScrollCaptureState { Idle, Capturing, Paused, Finishing, Failed }

public sealed record LongCaptureRequest(Rectangle PhysicalPixelRegion, ScrollCaptureMode Mode);

public enum ScrollCaptureStopReason { EndOfScroll, LowConfidence, CaptureFailure, InputFailure, Cancelled, CompletedByUser, FrameLimit, PollLimit, ImageSizeLimit }

public sealed record ScrollCaptureProgress(int AcceptedFrameCount, ScrollCaptureStopReason? StopReason = null, string? Status = null, bool? AutomaticScrolling = null);

/// <summary>Bitmap 只在同步事件回调期间有效；订阅方需要保留时应立即复制或转换。</summary>
public sealed class ScrollCapturePreviewEventArgs(Bitmap bitmap, Size? compositeSize = null) : EventArgs
{
    public Bitmap Bitmap { get; } = bitmap;
    public Size CompositeSize { get; } = compositeSize ?? bitmap.Size;
}

public sealed record ScrollCaptureOptions(
    int MaximumFrames = 160,
    TimeSpan? SettleDelay = null,
    int MaximumPolls = 720,
    int ConsecutiveRepeatedFramesToStop = 8,
    long MaximumCompositePixels = 64_000_000,
    int MaximumCompositeHeight = 65_000,
    int InitialNoProgressPolls = 8)
{
    public TimeSpan EffectiveSettleDelay => SettleDelay ?? TimeSpan.FromMilliseconds(250);
}

public interface IScrollRegionCapture
{
    Bitmap Capture(Rectangle physicalPixelRegion);
}

public sealed class ScrollCaptureResult : IDisposable
{
    private readonly LongCaptureRunDiagnostics? _diagnostics;
    private int _disposed;
    internal ScrollCaptureResult(
        IReadOnlyList<string> segments,
        string tempSessionDirectory,
        ScrollCaptureStopReason stopReason,
        IReadOnlyList<int> overlapRows,
        LongCaptureRunDiagnostics? diagnostics)
    {
        SegmentPaths = segments;
        TempSessionDirectory = tempSessionDirectory;
        StopReason = stopReason;
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
        _diagnostics?.RecordEvent("compose_started", new { Frames=AcceptedFrameCount });
        var composite = new ScrollCaptureRecovery(Path.GetDirectoryName(TempSessionDirectory)!).CreateComposite(TempSessionDirectory);
        _diagnostics?.RecordComposite(composite.Size);
        return composite;
    }

    /// <summary>仅在最终 PNG 已经成功保存后允许删除本次会话的精确临时目录。</summary>
    public void CleanupAfterSuccessfulOutput()
    {
        var recovery = new ScrollCaptureRecovery(Path.GetDirectoryName(TempSessionDirectory)!);
        var manifest = recovery.ReadValidated(TempSessionDirectory);
        recovery.MarkDelivered(TempSessionDirectory);
        Interlocked.Exchange(ref _disposed, 1);
        var allowed = manifest.Segments.Select(s => s.FileName).Append(ScrollCaptureTempStore.RecoveryMarker).ToHashSet(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(TempSessionDirectory).ToArray();
        if (Directory.EnumerateDirectories(TempSessionDirectory).Any() || files.Any(p => !allowed.Contains(Path.GetFileName(p)) || !ScrollCaptureRecovery.OrdinaryPath(p))) return;
        foreach (var file in files) File.Delete(file);
        Directory.Delete(TempSessionDirectory, recursive: false);
    }

    /// <summary>失败、取消或调用方选择 partial 时保留目录以便恢复。</summary>
    public void PreserveForRecovery()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var recovery = new ScrollCaptureRecovery(Path.GetDirectoryName(TempSessionDirectory)!);
        var manifest = recovery.ReadValidated(TempSessionDirectory);
        ScrollCaptureRecovery.WriteAtomic(TempSessionDirectory, manifest with { Status="recoverable", StopReason=StopReason, UpdatedUtc=DateTimeOffset.UtcNow });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Uncommitted capture pieces survive close/cancel/crash; only an explicit
        // successful output commit can make its exact indexed files disposable.
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
    private int _automaticVersion;
    private int _completionRequested;
    private int _state = (int)ScrollCaptureState.Idle;

    public event EventHandler<ScrollCaptureProgress>? ProgressChanged;

    public event EventHandler<ScrollCapturePreviewEventArgs>? PreviewChanged;

    public event EventHandler<ScrollCaptureState>? StateChanged;

    public bool AutomaticScrollingEnabled => Volatile.Read(ref _automaticScrollingEnabled) != 0;

    public ScrollCaptureState State => (ScrollCaptureState)Volatile.Read(ref _state);

    public void RequestCompletion()
    {
        Volatile.Write(ref _completionRequested, 1);
        _diagnostics?.RecordEvent("completion_requested");
    }

    public void SetAutomaticScrolling(bool enabled)
    {
        Volatile.Write(ref _automaticScrollingEnabled, enabled ? 1 : 0);
        Interlocked.Increment(ref _automaticVersion);
        if (State is ScrollCaptureState.Capturing or ScrollCaptureState.Paused)
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
        LongCaptureRunDiagnostics? diagnostics = null,
        Action<string, ScrollCaptureManifest>? manifestCommit = null)
    {
        if (physicalPixelRegion.Width <= 0 || physicalPixelRegion.Height <= 0) throw new ArgumentOutOfRangeException(nameof(physicalPixelRegion));
        _physicalPixelRegion = physicalPixelRegion;
        _mode = mode;
        _automaticScrollingEnabled = mode == ScrollCaptureMode.Automatic ? 1 : 0;
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _settler = settler ?? throw new ArgumentNullException(nameof(settler));
        _options = options ?? new ScrollCaptureOptions();
        if (_options.MaximumFrames < 1 || _options.MaximumPolls < 1 || _options.ConsecutiveRepeatedFramesToStop < 1
            || _options.InitialNoProgressPolls < 1 || _options.MaximumFrames > ScrollCaptureRecovery.MaximumFrames
            || _options.MaximumCompositePixels > ScrollCaptureRecovery.MaximumPixels || _options.MaximumCompositeHeight > ScrollCaptureRecovery.MaximumHeight
            || _options.MaximumCompositePixels < (long)physicalPixelRegion.Width * physicalPixelRegion.Height
            || _options.MaximumCompositeHeight < physicalPixelRegion.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        _stitcher = stitcher ?? new FrameStitcher();
        _diagnostics = diagnostics;
        _tempSessionDirectory = Path.Combine(temporaryRoot ?? throw new ArgumentNullException(nameof(temporaryRoot)), Guid.NewGuid().ToString("N"));
        _persistence = new(_tempSessionDirectory, manifestCommit);
    }

    private readonly ScrollCapturePersistence _persistence;

    public async Task<ScrollCaptureResult> RunAsync(CancellationToken cancellationToken)
    {
        var segments = new List<string>(_options.MaximumFrames);
        var overlaps = new List<int>(_options.MaximumFrames);
        Bitmap? previous = null;
        Bitmap? preview = null;
        var compositeSize = _physicalPixelRegion.Size;
        var reason = ScrollCaptureStopReason.PollLimit;
        var consecutiveUnusableFrames = 0;
        var hasObservedMovement = false;
        var announcedNoMovement = false;
        var stage = "capture";
        var automaticVersion = Volatile.Read(ref _automaticVersion);
        try
        {
            SetState(ScrollCaptureState.Capturing);
            Directory.CreateDirectory(_tempSessionDirectory);
            ThrowIfStopping(cancellationToken);
            _diagnostics?.RecordEvent("capture_started", new { _physicalPixelRegion.Width, _physicalPixelRegion.Height, Mode = _mode.ToString() });
            previous = _capture.Capture(_physicalPixelRegion);
            Persist(previous, segments, 0);
            overlaps.Add(0);
            preview = CreatePreview(null, previous, 0, previous.Size, previous.Size);
            PublishPreview(preview, compositeSize);
            ProgressChanged?.Invoke(this, new ScrollCaptureProgress(segments.Count));
            for (var poll = 0; poll < _options.MaximumPolls; poll++)
            {
                ThrowIfStopping(cancellationToken);
                if (segments.Count >= _options.MaximumFrames) { reason = ScrollCaptureStopReason.FrameLimit; break; }
                if (AutomaticScrollingEnabled)
                {
                    if (automaticVersion != Volatile.Read(ref _automaticVersion)) { consecutiveUnusableFrames=0; automaticVersion=Volatile.Read(ref _automaticVersion); }
                    stage = "input";
                    try { await _input.SendWheelAsync(Center(_physicalPixelRegion), ScrollInputService.DefaultWheelDelta, cancellationToken); }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        SetAutomaticScrolling(false);
                        _diagnostics?.RecordFailure("input", error);
                        ProgressChanged?.Invoke(this, new(segments.Count, Status:"来源已变化，自动滚动已暂停；可手动滚动或完成。", AutomaticScrolling:false));
                    }
                }
                stage = "settle";
                await _settler.WaitForSettleAsync(_options.EffectiveSettleDelay, cancellationToken);
                stage = "capture";
                _diagnostics?.RecordEvent("poll_started", new { Poll = poll, Frames = segments.Count });
                using var current = _capture.Capture(_physicalPixelRegion);
                stage = "analyze";
                var analysisTimer = System.Diagnostics.Stopwatch.StartNew();
                var analysis = await Task.Run(() => _stitcher.Analyze(previous, current, cancellationToken), cancellationToken);
                _diagnostics?.RecordEvent("overlap_analyzed", new { Poll = poll, Disposition = analysis.Disposition.ToString(), analysis.OverlapRows, analysis.Confidence, ElapsedMs = analysisTimer.Elapsed.TotalMilliseconds });
                ThrowIfStopping(cancellationToken);
                if (analysis.Disposition == FrameStitchDisposition.Append)
                {
                    var nextSize = new Size(current.Width, checked(compositeSize.Height + current.Height - analysis.OverlapRows));
                    if (nextSize.Height > _options.MaximumCompositeHeight || (long)nextSize.Width * nextSize.Height > _options.MaximumCompositePixels)
                    {
                        reason = ScrollCaptureStopReason.ImageSizeLimit;
                        _diagnostics?.RecordEvent("image_size_limit", new { nextSize.Width, nextSize.Height });
                        break;
                    }
                    hasObservedMovement = true;
                    announcedNoMovement = false;
                    consecutiveUnusableFrames = 0;
                    stage = "persist";
                    Persist(current, segments, analysis.OverlapRows);
                    overlaps.Add(analysis.OverlapRows);
                    stage = "preview";
                    var nextPreview = CreatePreview(preview, current, analysis.OverlapRows, compositeSize, nextSize);
                    preview.Dispose();
                    preview = nextPreview;
                    compositeSize = nextSize;
                    PublishPreview(preview, compositeSize);
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
                    if (++consecutiveUnusableFrames >= _options.InitialNoProgressPolls)
                    {
                        SetAutomaticScrolling(false);
                        ProgressChanged?.Invoke(this, new(segments.Count, Status:"未检测到滚动，自动滚动已暂停；可手动滚动或完成。", AutomaticScrolling:false));
                        announcedNoMovement = true;
                        continue;
                    }
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
        catch (Exception exception)
        {
            reason = stage == "input" ? ScrollCaptureStopReason.InputFailure : ScrollCaptureStopReason.CaptureFailure;
            _diagnostics?.RecordFailure(stage, exception);
        }
        finally
        {
            previous?.Dispose();
            preview?.Dispose();
        }
        if (reason == ScrollCaptureStopReason.PollLimit && segments.Count >= _options.MaximumFrames)
            reason = ScrollCaptureStopReason.FrameLimit;
        try { _persistence.Finish(reason); }
        catch (Exception error) { _diagnostics?.RecordFailure("manifest", error); }
        SetState(reason is ScrollCaptureStopReason.InputFailure or ScrollCaptureStopReason.CaptureFailure or ScrollCaptureStopReason.LowConfidence
            ? ScrollCaptureState.Failed
            : ScrollCaptureState.Finishing);
        ProgressChanged?.Invoke(this, new ScrollCaptureProgress(segments.Count, reason));
        _diagnostics?.RecordStop(reason);
        return new ScrollCaptureResult(segments, _tempSessionDirectory, reason, overlaps, _diagnostics);
    }

    public void Dispose() { }

    private void Persist(Bitmap bitmap, ICollection<string> segments, int overlap)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var path = Path.Combine(_tempSessionDirectory, $"{segments.Count:D3}.png");
        _persistence.Persist(bitmap, overlap);
        segments.Add(path);
        _diagnostics?.RecordSegment(bitmap.Size);
        _diagnostics?.RecordEvent("segment_persist_duration", new { ElapsedMs = timer.Elapsed.TotalMilliseconds });
    }

    private void PublishPreview(Bitmap preview, Size compositeSize)
    {
        _diagnostics?.RecordPreview(compositeSize);
        _diagnostics?.RecordEvent("preview_ready", new { Width = preview.Width, Height = preview.Height, CompositeWidth = compositeSize.Width, CompositeHeight = compositeSize.Height });
        var handler = PreviewChanged;
        if (handler is null) return;
        using var snapshot = ClonePreservingPixels(preview);
        handler(this, new ScrollCapturePreviewEventArgs(snapshot, compositeSize));
    }

    private void ThrowIfStopping(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _completionRequested) != 0) throw new OperationCanceledException();
    }

    private static Bitmap CreatePreview(Bitmap? previous, Bitmap current, int overlap, Size previousSize, Size size)
    {
        var scale = Math.Min(1d, Math.Min(420d / size.Width, 900d / size.Height));
        var bitmap = new Bitmap(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        if (previous is null) graphics.DrawImage(current, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        else
        {
            var previousHeight = (int)Math.Round(previousSize.Height * bitmap.Height / (double)size.Height);
            graphics.DrawImage(previous, new Rectangle(0, 0, bitmap.Width, previousHeight));
            graphics.DrawImage(current, new Rectangle(0, previousHeight, bitmap.Width, bitmap.Height - previousHeight), new Rectangle(0, overlap, current.Width, current.Height - overlap), GraphicsUnit.Pixel);
        }
        return bitmap;
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

using System.Drawing;
using System.IO;
using System.Text.Json;
using ClipRay.App.Diagnostics;

namespace ClipRay.App.Scrolling;

public sealed record LongCaptureImageSize(int Width, int Height)
{
    public static LongCaptureImageSize From(Size size) => new(size.Width, size.Height);
}

public sealed record LongCaptureRunSnapshot(
    string RunId,
    string BinaryVersion,
    int AcceptedFrameCount,
    IReadOnlyList<LongCaptureImageSize> SegmentSizes,
    IReadOnlyList<LongCaptureImageSize> PreviewSizes,
    LongCaptureImageSize? CompositeSize,
    LongCaptureImageSize? SaveInputSize,
    LongCaptureImageSize? SaveOutputSize,
    LongCaptureImageSize? FinalPngSize,
    string? OutputArtifactId,
    ScrollCaptureStopReason? StopReason,
    long EventCount = 0,
    string? LastStage = null,
    string? FailureType = null,
    bool TraceWriteFailed = false);

/// <summary>只记录一次长截图运行的尺寸和相对 artifact id，不记录绝对路径、像素、OCR 或翻译文本。</summary>
public sealed class LongCaptureRunDiagnostics
{
    private readonly object _gate = new();
    private readonly List<LongCaptureImageSize> _segmentSizes = [];
    private readonly List<LongCaptureImageSize> _previewSizes = [];
    private LongCaptureImageSize? _compositeSize;
    private LongCaptureImageSize? _saveInputSize;
    private LongCaptureImageSize? _saveOutputSize;
    private LongCaptureImageSize? _finalPngSize;
    private string? _outputArtifactId;
    private ScrollCaptureStopReason? _stopReason;
    private readonly string? _eventPath;
    private readonly System.Diagnostics.Stopwatch _timer = System.Diagnostics.Stopwatch.StartNew();
    private long _eventCount;
    private string? _lastStage;
    private string? _failureType;
    private bool _traceWriteFailed;
    private readonly LocalDiagnosticSession? _store;

    public LongCaptureRunDiagnostics(string runId, string binaryVersion, string? diagnosticsRoot = null)
    {
        RunId = string.IsNullOrWhiteSpace(runId) ? throw new ArgumentException("A run id is required.", nameof(runId)) : runId;
        BinaryVersion = string.IsNullOrWhiteSpace(binaryVersion) ? "unknown" : binaryVersion;
        if (diagnosticsRoot is not null)
        {
            var safeRunId = string.Concat(runId.Select(static character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
            _eventPath = Path.Combine(diagnosticsRoot, $"long-capture-{safeRunId}.events.jsonl");
            _store = new(diagnosticsRoot);
        }
        RecordEvent("run_started");
    }

    public string RunId { get; }
    public string BinaryVersion { get; }

    public void RecordEvent(string stage, object? detail = null)
    {
        lock (_gate)
        {
            _lastStage = stage;
            var sequence = ++_eventCount;
            if (_eventPath is null) return;
            try
            {
                if (!_store!.Json(_eventPath,new
                {
                    SchemaVersion = 1, RunId, BinaryVersion, Sequence = sequence,
                    Utc = DateTimeOffset.UtcNow, ElapsedMs = _timer.Elapsed.TotalMilliseconds,
                    Stage = stage, Detail = detail,
                })) _traceWriteFailed=true;
                _traceWriteFailed|=_store.Truncated;
            }
            catch { _traceWriteFailed = true; }
        }
    }

    public void RecordFailure(string stage, Exception exception)
    {
        lock (_gate)
        {
            _failureType = exception.GetType().Name;
            RecordEvent("failure", new { FailedStage = stage, Type = _failureType, exception.HResult });
        }
    }

    public int AcceptedFrameCount { get { lock (_gate) return _segmentSizes.Count; } }
    public IReadOnlyList<LongCaptureImageSize> SegmentSizes { get { lock (_gate) return _segmentSizes.ToArray(); } }
    public IReadOnlyList<LongCaptureImageSize> PreviewSizes { get { lock (_gate) return _previewSizes.ToArray(); } }
    public LongCaptureImageSize? CompositeSize { get { lock (_gate) return _compositeSize; } }
    public LongCaptureImageSize? SaveInputSize { get { lock (_gate) return _saveInputSize; } }
    public LongCaptureImageSize? SaveOutputSize { get { lock (_gate) return _saveOutputSize; } }
    public LongCaptureImageSize? FinalPngSize { get { lock (_gate) return _finalPngSize; } }
    public string? OutputArtifactId { get { lock (_gate) return _outputArtifactId; } }
    public ScrollCaptureStopReason? StopReason { get { lock (_gate) return _stopReason; } }

    public void RecordSegment(Size size)
    {
        lock (_gate)
        {
            _segmentSizes.Add(LongCaptureImageSize.From(size));
            RecordEvent("segment_saved", new { Index = _segmentSizes.Count - 1, size.Width, size.Height });
        }
    }

    public void RecordPreview(Size size)
    {
        lock (_gate) _previewSizes.Add(LongCaptureImageSize.From(size));
    }

    public void RecordComposite(Size size)
    {
        lock (_gate)
        {
            _compositeSize = LongCaptureImageSize.From(size);
            RecordEvent("compose_completed", new { size.Width, size.Height });
        }
    }

    public void RecordSaveInput(Size size)
    {
        lock (_gate)
        {
            _saveInputSize = LongCaptureImageSize.From(size);
            RecordEvent("output_started", new { size.Width, size.Height });
        }
    }

    public void RecordSaved(string outputPath, Size outputSize, Size finalPngSize)
    {
        lock (_gate)
        {
            _outputArtifactId = Path.GetFileName(outputPath);
            _saveOutputSize = LongCaptureImageSize.From(outputSize);
            _finalPngSize = LongCaptureImageSize.From(finalPngSize);
            RecordEvent("output_saved", new { ArtifactId = _outputArtifactId, outputSize.Width, outputSize.Height });
        }
    }

    public void RecordStop(ScrollCaptureStopReason stopReason)
    {
        lock (_gate)
        {
            _stopReason = stopReason;
            RecordEvent("capture_stopped", new { Reason = stopReason.ToString(), AcceptedFrameCount = _segmentSizes.Count });
        }
    }

    public LongCaptureRunSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new LongCaptureRunSnapshot(
                RunId,
                BinaryVersion,
                _segmentSizes.Count,
                _segmentSizes.ToArray(),
                _previewSizes.ToArray(),
                _compositeSize,
                _saveInputSize,
                _saveOutputSize,
                _finalPngSize,
                _outputArtifactId,
                _stopReason, _eventCount, _lastStage, _failureType, _traceWriteFailed);
        }
    }

    internal bool WriteSnapshot(string root,string path) => (_store??new LocalDiagnosticSession(root)).Json(path,Snapshot(),append:false);
}

public static class LongCaptureRunDiagnosticStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Write(string diagnosticsRoot, LongCaptureRunDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsRoot);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Directory.CreateDirectory(diagnosticsRoot);
        var safeRunId = string.Concat(diagnostics.RunId.Select(static character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var path = Path.Combine(diagnosticsRoot, $"long-capture-{safeRunId}.json");
        if (!diagnostics.WriteSnapshot(diagnosticsRoot,path)) throw new IOException("Diagnostic budget.");
        return path;
    }

    public static bool TryWrite(string diagnosticsRoot, LongCaptureRunDiagnostics diagnostics)
    {
        try
        {
            Write(diagnosticsRoot, diagnostics);
            return !diagnostics.Snapshot().TraceWriteFailed;
        }
        catch
        {
            // 诊断失败不能改变截图结果。
            return false;
        }
    }
}

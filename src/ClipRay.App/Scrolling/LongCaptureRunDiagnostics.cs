using System.Drawing;
using System.IO;
using System.Text.Json;

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
    ScrollCaptureStopReason? StopReason);

/// <summary>只记录一次长截图运行的尺寸和相对 artifact id，不记录绝对路径、像素、OCR 或翻译文本。</summary>
public sealed class LongCaptureRunDiagnostics(string runId, string binaryVersion)
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

    public string RunId { get; } = string.IsNullOrWhiteSpace(runId)
        ? throw new ArgumentException("A run id is required.", nameof(runId))
        : runId;

    public string BinaryVersion { get; } = string.IsNullOrWhiteSpace(binaryVersion)
        ? "unknown"
        : binaryVersion;

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
        lock (_gate) _segmentSizes.Add(LongCaptureImageSize.From(size));
    }

    public void RecordPreview(Size size)
    {
        lock (_gate) _previewSizes.Add(LongCaptureImageSize.From(size));
    }

    public void RecordComposite(Size size)
    {
        lock (_gate) _compositeSize = LongCaptureImageSize.From(size);
    }

    public void RecordSaveInput(Size size)
    {
        lock (_gate) _saveInputSize = LongCaptureImageSize.From(size);
    }

    public void RecordSaved(string outputPath, Size outputSize, Size finalPngSize)
    {
        lock (_gate)
        {
            _outputArtifactId = Path.GetFileName(outputPath);
            _saveOutputSize = LongCaptureImageSize.From(outputSize);
            _finalPngSize = LongCaptureImageSize.From(finalPngSize);
        }
    }

    public void RecordStop(ScrollCaptureStopReason stopReason)
    {
        lock (_gate) _stopReason = stopReason;
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
                _stopReason);
        }
    }
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
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(diagnostics.Snapshot(), JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
        return path;
    }

    public static void TryWrite(string diagnosticsRoot, LongCaptureRunDiagnostics diagnostics)
    {
        try
        {
            Write(diagnosticsRoot, diagnostics);
        }
        catch
        {
            // 诊断失败不能改变截图结果。
        }
    }
}

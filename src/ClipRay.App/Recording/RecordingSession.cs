using System.Diagnostics;
using System.Drawing;
using System.IO;
using ClipRay.App.Settings;

namespace ClipRay.App.Recording;

public sealed record RegionRecordingRequest(Rectangle PhysicalPixelRegion);

public enum RecordingSessionState
{
    Created,
    Starting,
    Recording,
    Stopping,
    Stopped,
    Failed,
}

public enum RecordingStream
{
    Video,
    Audio,
}

public enum RecordingStartupStage
{
    AudioDevice,
    MediaFoundation,
    Mp4SinkWriter,
    VideoEncoder,
    AudioEncoder,
    SinkWriterStart,
    GraphicsCapture,
    SystemAudio,
    RecordingUi,
    Selection,
}

public sealed class RecordingStartupException : Exception
{
    public RecordingStartupException(RecordingStartupStage stage, Exception cause)
        : base("Recording startup failed.", cause)
    {
        Stage = stage;
        HResult = cause.HResult;
    }

    public RecordingStartupStage Stage { get; }
}

public static class RecordingFailureText
{
    public static string Format(Exception exception, string? partialPath, string interfaceLanguage = "zh-CN")
    {
        ArgumentNullException.ThrowIfNull(exception);
        string T(string chinese, string english) => AppInterfaceLanguage.Text(interfaceLanguage, chinese, english);
        var failure = FindStartupFailure(exception);
        if (failure?.Stage == RecordingStartupStage.Selection)
            return T("录屏选区必须完整位于一个屏幕内；右侧和下侧最多各裁去 1 像素以取得偶数宽高，裁剪后至少为 2x2 像素。请调整选区后重试。",
                "Keep the entire recording selection within one screen. The right and bottom edges may each lose 1 pixel for even dimensions; the result must be at least 2x2 pixels. Adjust the selection and retry.");
        var stage = failure?.Stage switch
        {
            RecordingStartupStage.AudioDevice => T("系统声音设备", "system audio device"),
            RecordingStartupStage.MediaFoundation => T("Windows 媒体框架", "Windows media framework"),
            RecordingStartupStage.Mp4SinkWriter => T("MP4 写入器", "MP4 writer"),
            RecordingStartupStage.VideoEncoder => T("H.264 视频编码", "H.264 video encoder"),
            RecordingStartupStage.AudioEncoder => T("AAC 音频编码", "AAC audio encoder"),
            RecordingStartupStage.SinkWriterStart => T("编码器启动", "encoder startup"),
            RecordingStartupStage.GraphicsCapture => T("屏幕捕获", "screen capture"),
            RecordingStartupStage.SystemAudio => T("系统声音捕获", "system audio capture"),
            RecordingStartupStage.RecordingUi => T("录屏控制界面", "recording controls"),
            _ => T("未知阶段", "unknown stage"),
        };
        var hresult = unchecked((uint)(failure?.HResult ?? exception.HResult));
        var text = T($"录屏启动失败：{stage}（错误 0x{hresult:X8}）",
            $"Recording could not start: {stage} (error 0x{hresult:X8})");
        return string.IsNullOrWhiteSpace(partialPath)
            ? text
            : T($"{text}。已保留 {partialPath}", $"{text}. Partial recording kept at {partialPath}");
    }

    private static RecordingStartupException? FindStartupFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is RecordingStartupException failure) return failure;
        }
        return null;
    }
}

public static class RecordingStartupGuard
{
    public static void Execute(RecordingStartupStage stage, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (RecordingStartupException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RecordingStartupException(stage, exception);
        }
    }

    public static T Execute<T>(RecordingStartupStage stage, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            return action();
        }
        catch (RecordingStartupException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RecordingStartupException(stage, exception);
        }
    }

    public static async Task ExecuteAsync(RecordingStartupStage stage, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (RecordingStartupException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RecordingStartupException(stage, exception);
        }
    }
}

public interface IRecordingRuntime : IAsyncDisposable
{
    RecordingFaultSignal? Faults => null;
    RecordingStopReport? Report => null;
    void RequestStop() { }
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

public readonly record struct RecordingMonitorMapping(Rectangle MonitorBounds, Rectangle CropInMonitor);

public static class RecordingGeometry
{
    public static RecordingMonitorMapping MapToMonitor(Rectangle selectedRegion, IReadOnlyList<Rectangle> monitorBounds)
    {
        if (selectedRegion.Width <= 0 || selectedRegion.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedRegion));
        }

        foreach (var monitor in monitorBounds)
        {
            if (selectedRegion.Left >= monitor.Left && selectedRegion.Top >= monitor.Top
                && (long)selectedRegion.X + selectedRegion.Width <= (long)monitor.X + monitor.Width
                && (long)selectedRegion.Y + selectedRegion.Height <= (long)monitor.Y + monitor.Height)
                return new RecordingMonitorMapping(monitor, new Rectangle(
                    selectedRegion.X - monitor.X, selectedRegion.Y - monitor.Y, selectedRegion.Width, selectedRegion.Height));
        }
        throw new InvalidOperationException("The recording region must be fully contained in one physical monitor.");
    }

    public static RecordingMonitorMapping Normalize(RecordingMonitorMapping mapping) =>
        mapping with { CropInMonitor = NormalizeForEncoder(mapping.CropInMonitor) };

    public static Rectangle EffectiveRegion(RecordingMonitorMapping mapping) => new(
        checked(mapping.MonitorBounds.X + mapping.CropInMonitor.X), checked(mapping.MonitorBounds.Y + mapping.CropInMonitor.Y),
        mapping.CropInMonitor.Width, mapping.CropInMonitor.Height);

    public static Rectangle NormalizeForEncoder(Rectangle crop)
    {
        var width = crop.Width & ~1;
        var height = crop.Height & ~1;
        if (width < 2 || height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(crop), "The recording region must remain at least 2x2 pixels after even cropping.");
        }

        return new Rectangle(crop.X, crop.Y, width, height);
    }
}

public sealed class RecordingTimeline
{
    private readonly long _epochQpc;
    private readonly long _qpcFrequency;
    private readonly object _sync = new();
    private readonly Dictionary<RecordingStream, long> _lastTimestamp = [];

    public RecordingTimeline(long epochQpc, long qpcFrequency)
    {
        if (qpcFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(qpcFrequency));
        _epochQpc = epochQpc;
        _qpcFrequency = qpcFrequency;
    }

    public static RecordingTimeline StartNow() => new(Stopwatch.GetTimestamp(), Stopwatch.Frequency);

    public long GetTimestamp100Nanoseconds(RecordingStream stream, long qpc)
    {
        if (qpc < _epochQpc) throw new InvalidOperationException("A sample predates the recording epoch.");
        var delta = qpc - _epochQpc;
        var timestamp = checked((long)((decimal)delta * 10_000_000m / _qpcFrequency));
        lock (_sync)
        {
            if (_lastTimestamp.TryGetValue(stream, out var previous) && timestamp <= previous)
            {
                throw new InvalidOperationException($"Non-monotonic {stream} timestamp.");
            }

            _lastTimestamp[stream] = timestamp;
        }

        return timestamp;
    }
}

public sealed record RecordingOutputPaths(string PartialPath, string FinalPath)
{
    public static RecordingOutputPaths Create(string outputRoot, DateTime timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        var directory = Path.Combine(outputRoot, timestamp.ToString("yyyy-MM-dd"));
        var baseStem = $"ClipRay-Recording_{timestamp:yyyy-MM-dd_HH-mm-ss-fff}";
        for (var suffix = 0; ; suffix++)
        {
            var suffixText = suffix == 0 ? string.Empty : $"_{suffix + 1}";
            var stem = baseStem + suffixText;
            var paths = new RecordingOutputPaths(
                Path.Combine(directory, stem + ".partial.mp4"),
                Path.Combine(directory, stem + ".mp4"));
            if (!File.Exists(paths.PartialPath) && !File.Exists(paths.FinalPath)) return paths;
        }
    }
}

public sealed class RecordingFileMover
{
    internal ClipRay.App.Cleanup.CompletedOutputStore CompletedOutputs { get; set; } = new();
    public void Finalize(RecordingOutputPaths paths)
    {
        if (!File.Exists(paths.PartialPath))
        {
            throw new FileNotFoundException("The partial recording does not exist.", paths.PartialPath);
        }

        using var lease = ClipRay.App.Cleanup.CompletedOutputStore.EnterDirectory(Path.GetDirectoryName(paths.FinalPath)!);
        File.Move(paths.PartialPath, paths.FinalPath, overwrite: false);
        _ = CompletedOutputs.TryRecordSaved(paths.FinalPath, ClipRay.App.Cleanup.CleanupFileKind.CompletedRecording);
    }
}

[Flags]
public enum RecordingBorderExtendedStyle : uint
{
    Transparent = 0x00000020,
    ToolWindow = 0x00000080,
    NoActivate = 0x08000000,
}

public static class RecordingBorderPolicy
{
    public const uint DisplayAffinity = 0x00000011;

    public const RecordingBorderExtendedStyle ExtendedWindowStyle =
        RecordingBorderExtendedStyle.Transparent |
        RecordingBorderExtendedStyle.ToolWindow |
        RecordingBorderExtendedStyle.NoActivate;
}

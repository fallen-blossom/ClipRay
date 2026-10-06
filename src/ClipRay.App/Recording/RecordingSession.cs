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
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

public sealed class RecordingSession : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IRecordingRuntime _runtime;
    private Task? _stopTask;

    public RecordingSession(IRecordingRuntime runtime)
    {
        _runtime = runtime;
    }

    public RecordingSessionState State { get; private set; } = RecordingSessionState.Created;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (State != RecordingSessionState.Created)
            {
                throw new InvalidOperationException("A recording session can only be started once.");
            }

            State = RecordingSessionState.Starting;
        }

        try
        {
            await _runtime.StartAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                if (State == RecordingSessionState.Starting)
                {
                    State = RecordingSessionState.Recording;
                }
            }
        }
        catch
        {
            lock (_sync) State = RecordingSessionState.Failed;
            try { await _runtime.DisposeAsync().ConfigureAwait(false); }
            catch { /* 启动失败是首要诊断；清理失败不能覆盖其阶段和 HRESULT。 */ }
            throw;
        }
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task StopCoreAsync()
    {
        lock (_sync)
        {
            if (State is RecordingSessionState.Stopped)
            {
                return;
            }

            State = RecordingSessionState.Stopping;
        }

        try
        {
            await _runtime.StopAsync().ConfigureAwait(false);
            lock (_sync) State = RecordingSessionState.Stopped;
        }
        catch
        {
            lock (_sync) State = RecordingSessionState.Failed;
            throw;
        }
        finally
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class RegionRecordingRuntime : IRecordingRuntime
{
    private readonly IGraphicsCaptureSource _video;
    private readonly ISystemAudioCapture _audio;
    private readonly IMediaFoundationRecorder _recorder;
    private int _stopped;
    private int _disposed;

    public RegionRecordingRuntime(
        IGraphicsCaptureSource video,
        ISystemAudioCapture audio,
        IMediaFoundationRecorder recorder)
    {
        _video = video;
        _audio = audio;
        _recorder = recorder;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RecordingStartupGuard.ExecuteAsync(
            RecordingStartupStage.MediaFoundation,
            () => _recorder.StartAsync(cancellationToken)).ConfigureAwait(false);
        try
        {
            await RecordingStartupGuard.ExecuteAsync(
                RecordingStartupStage.GraphicsCapture,
                () => _video.StartAsync(_recorder.WriteVideoAsync, cancellationToken)).ConfigureAwait(false);
            await RecordingStartupGuard.ExecuteAsync(
                RecordingStartupStage.SystemAudio,
                () => _audio.StartAsync(_recorder.WriteAudioAsync, cancellationToken)).ConfigureAwait(false);
        }
        catch
        {
            try { await StopAsync().ConfigureAwait(false); }
            catch { /* 保留第一个启动失败，partial 仍由上层提示用户。 */ }
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        Exception? failure = null;
        try { await _audio.StopAsync().ConfigureAwait(false); }
        catch (Exception exception) { failure = exception; }
        try { await _video.StopAsync().ConfigureAwait(false); }
        catch (Exception exception) { failure ??= exception; }
        try { await _recorder.StopAsync().ConfigureAwait(false); }
        catch (Exception exception) { failure ??= exception; }
        if (failure is not null) throw failure;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync().ConfigureAwait(false);
        await _audio.DisposeAsync().ConfigureAwait(false);
        await _video.DisposeAsync().ConfigureAwait(false);
        await _recorder.DisposeAsync().ConfigureAwait(false);
    }
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

        var intersection = monitorBounds
            .Select((monitor, index) => new { Monitor = monitor, Index = index, Intersection = Rectangle.Intersect(monitor, selectedRegion) })
            .Where(static candidate => candidate.Intersection.Width > 0 && candidate.Intersection.Height > 0)
            .OrderByDescending(static candidate => (long)candidate.Intersection.Width * candidate.Intersection.Height)
            .ThenBy(static candidate => candidate.Index)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The selected region does not intersect a physical monitor.");

        var crop = new Rectangle(
            intersection.Intersection.X - intersection.Monitor.X,
            intersection.Intersection.Y - intersection.Monitor.Y,
            intersection.Intersection.Width,
            intersection.Intersection.Height);
        return new RecordingMonitorMapping(intersection.Monitor, crop);
    }

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
    public void Finalize(RecordingOutputPaths paths)
    {
        if (!File.Exists(paths.PartialPath))
        {
            throw new FileNotFoundException("The partial recording does not exist.", paths.PartialPath);
        }

        File.Move(paths.PartialPath, paths.FinalPath, overwrite: false);
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

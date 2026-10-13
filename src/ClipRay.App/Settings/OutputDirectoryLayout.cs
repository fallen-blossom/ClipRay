using System.IO;

namespace ClipRay.App.Settings;

public sealed record OutputDirectories(
    string Screenshots,
    string LongCaptures,
    string Recordings);

public static class OutputDirectoryLayout
{
    public static OutputDirectories Create(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        var outputRoot = Path.Combine(Path.GetFullPath(installRoot), "Outputs");
        return new OutputDirectories(
            Path.Combine(outputRoot, "Screenshots"),
            Path.Combine(outputRoot, "LongCaptures"),
            Path.Combine(outputRoot, "Recordings"));
    }

    public static void Ensure(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(settings.ScreenshotDirectory);
        Directory.CreateDirectory(settings.LongCaptureDirectory);
        Directory.CreateDirectory(settings.RecordingDirectory);
    }

    public static IReadOnlyList<string> TryEnsure(AppSettings settings, Action<string>? ensure = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ensure ??= path => Directory.CreateDirectory(path);
        var failures = new List<string>();
        foreach (var (kind, path) in new[] { ("Screenshot", settings.ScreenshotDirectory),
            ("LongCapture", settings.LongCaptureDirectory), ("Recording", settings.RecordingDirectory) })
        {
            try { ensure(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { failures.Add($"{kind}: 0x{unchecked((uint)error.HResult):X8}"); }
        }
        return failures;
    }

    public static bool HasConfirmedDefaultLayout(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DefaultOutputLayoutRoot)) return false;
        var expected = Create(settings.DefaultOutputLayoutRoot);
        return Same(settings.ScreenshotDirectory, expected.Screenshots)
            && Same(settings.LongCaptureDirectory, expected.LongCaptures)
            && Same(settings.RecordingDirectory, expected.Recordings);
    }

    private static bool Same(string path, string expected) => !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase);

    public static bool IsCompleteLegacyLayout(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // This describes a shape only. It is not evidence of default/user intent
        // and must never authorize rebasing unmarked persisted directories.
        if (string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            || string.IsNullOrWhiteSpace(settings.LongCaptureDirectory)
            || string.IsNullOrWhiteSpace(settings.RecordingDirectory))
        {
            return false;
        }
        var screenshotsRoot = Path.GetDirectoryName(Path.GetFullPath(settings.ScreenshotDirectory));
        var longCapturesRoot = Path.GetDirectoryName(Path.GetFullPath(settings.LongCaptureDirectory));
        var recordingsRoot = Path.GetDirectoryName(Path.GetFullPath(settings.RecordingDirectory));
        return screenshotsRoot is not null
            && string.Equals(screenshotsRoot, longCapturesRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(screenshotsRoot, recordingsRoot, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(settings.ScreenshotDirectory).Equals("Screenshots", StringComparison.OrdinalIgnoreCase)
            && IsLegacyLongCaptureDirectoryName(Path.GetFileName(settings.LongCaptureDirectory))
            && Path.GetFileName(settings.RecordingDirectory).Equals("Recordings", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyLongCaptureDirectoryName(string directoryName) =>
        directoryName.Equals("LongCaptures", StringComparison.OrdinalIgnoreCase)
        || directoryName.Equals("Long Captures", StringComparison.OrdinalIgnoreCase);
}

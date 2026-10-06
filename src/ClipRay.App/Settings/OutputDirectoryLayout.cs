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

    public static bool IsCompleteLegacyLayout(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // 旧版 settings 没有“默认/自定义”来源字段；产品迁移契约因此只把这组三个
        // 同根、精确旧名称视为旧默认签名。任一名称或根不同都必须保留为自定义配置。
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

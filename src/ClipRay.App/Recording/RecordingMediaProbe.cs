using System.IO;
using Vortice.MediaFoundation;

namespace ClipRay.App.Recording;

/// <summary>Reads the real presentation length of a finished recording.</summary>
public static class RecordingMediaProbe
{
    /// <summary>
    /// WPF 的 MediaElement 会把成片时长报成固定 1 秒且不随播放更新，
    /// 所以预览进度条的刻度必须从成片本身读，不能用 NaturalDuration。
    /// 这里读容器呈现描述符，不依赖音频样本：系统声音为空时音频流没有样本，
    /// 走 NAudio 的 MediaFoundationReader 会直接抛 0xC00D36B3。
    /// </summary>
    public static TimeSpan? TryReadDuration(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var mediaFoundationStarted = false;
        try
        {
            MediaFactory.MFStartup();
            mediaFoundationStarted = true;
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            var ticks = reader.GetPresentationAttribute<ulong>(
                SourceReaderIndex.MediaSource,
                new MediaAttributeKey<ulong>(PresentationDescriptionAttributeKeys.Duration));
            return ticks > 0 ? TimeSpan.FromTicks((long)ticks) : null;
        }
        catch (Exception)
        {
            // 探测失败时预览条退化为不可拖动，不阻断预览窗。
            return null;
        }
        finally
        {
            if (mediaFoundationStarted) MediaFactory.MFShutdown();
        }
    }
}

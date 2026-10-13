namespace ClipRay.App.Recording;

/// <summary>录制管线的唯一目标帧率：WGC 采样节拍、样本时长与编码器声明都取这里。</summary>
public static class RecordingFrameRatePolicy
{
    public const int FramesPerSecond = 30;

    public const long SampleDuration100Nanoseconds = 10_000_000L / FramesPerSecond;
}

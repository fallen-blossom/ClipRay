namespace ClipRay.App.Recording;

public enum RecordingQuality { Compact, Balanced, High }

/// <summary>A frozen selection shared by capture, timestamps and the encoder for one recording.</summary>
public sealed record RecordingVideoOptions(
    int FramesPerSecond = 60,
    RecordingQuality Quality = RecordingQuality.Balanced,
    bool IncludeCursor = true,
    bool IncludeSystemAudio = true)
{
    public long SampleDuration100Nanoseconds => 10_000_000L / FramesPerSecond;

    public void Validate()
    {
        if (FramesPerSecond is not (15 or 24 or 30 or 60))
            throw new InvalidOperationException("录屏帧率必须为 15、24、30 或 60 fps。");
        if (!Enum.IsDefined(Quality)) throw new InvalidOperationException("录屏画质选项无效。");
    }
}

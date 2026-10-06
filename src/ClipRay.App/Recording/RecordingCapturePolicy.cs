namespace ClipRay.App.Recording;

/// <summary>Capture surface options that change what the finished recording shows.</summary>
public static class RecordingCapturePolicy
{
    /// <summary>WGC composes the pointer into captured frames; recordings should show the mouse.</summary>
    public static bool IncludeCursor => true;
}

namespace ClipRay.App.Recording;

public static class RecordingStartPolicy
{
    public static bool CanOpenSelection(bool recordingActive) => !recordingActive;
}

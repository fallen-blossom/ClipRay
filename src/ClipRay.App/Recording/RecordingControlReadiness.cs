namespace ClipRay.App.Recording;

internal static class RecordingControlReadiness
{
    internal static bool CanStart(bool borderSafe,bool controlExcluded,bool controlOutside) =>
        borderSafe && (controlExcluded || controlOutside);
}

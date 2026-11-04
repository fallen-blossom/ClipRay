using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace ClipRay.App.Recording;

public sealed record CaptureBorderAccessResult(string Status, bool Allowed, int? ErrorCode = null);

/// <summary>One public Windows consent request per process; never edits privacy settings.</summary>
public static class CaptureBorderAccess
{
    private static readonly Lazy<Task<CaptureBorderAccessResult>> Request = new(RequestCoreAsync);

    public static Task<CaptureBorderAccessResult> GetAsync() => Request.Value;

    private static async Task<CaptureBorderAccessResult> RequestCoreAsync()
    {
        if (!ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired")
            || !ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureAccess"))
            return new("Unsupported", false);
        try
        {
            var status = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            return new(status.ToString(), status == AppCapabilityAccessStatus.Allowed);
        }
        catch (Exception error)
        {
            // Appearance is optional. Permission failures retain Windows' border, never stop a capture.
            return new("RequestFailed", false, error.HResult);
        }
    }
}

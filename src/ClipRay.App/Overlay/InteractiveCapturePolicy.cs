namespace ClipRay.App.Overlay;

public enum InteractiveCaptureAction
{
    Open,
    CancelExisting,
}

public static class InteractiveCapturePolicy
{
    public static InteractiveCaptureAction Resolve(bool overlayOpen) => overlayOpen
        ? InteractiveCaptureAction.CancelExisting
        : InteractiveCaptureAction.Open;
}

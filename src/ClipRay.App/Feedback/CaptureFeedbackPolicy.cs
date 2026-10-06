namespace ClipRay.App.Feedback;

public static class CaptureFeedbackPolicy
{
    public static bool ShouldShowAppSuccess(bool hasSystemCaptureFeedback) => !hasSystemCaptureFeedback;
}

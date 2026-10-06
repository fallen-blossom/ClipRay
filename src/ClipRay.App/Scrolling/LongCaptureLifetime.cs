namespace ClipRay.App.Scrolling;

public static class LongCaptureLifetime
{
    public static void Teardown(CancellationTokenSource? cancellation, ScrollCaptureControlWindow? controls)
    {
        if (controls is not null)
        {
            controls.IsNormalTeardown = true;
            controls.Close();
        }
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        cancellation?.Dispose();
    }
}

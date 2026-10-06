namespace ClipRay.App.Infrastructure;

public static class ShutdownSafety
{
    public static bool Try(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

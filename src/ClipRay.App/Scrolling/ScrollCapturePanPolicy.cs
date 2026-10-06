namespace ClipRay.App.Scrolling;

public static class ScrollCapturePanPolicy
{
    public static double Offset(double startOffset, double startPointer, double currentPointer, double maximumOffset)
    {
        var maximum = Math.Max(0, maximumOffset);
        return Math.Clamp(startOffset + startPointer - currentPointer, 0, maximum);
    }
}

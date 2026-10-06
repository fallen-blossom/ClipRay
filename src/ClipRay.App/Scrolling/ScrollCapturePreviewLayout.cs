using System.Drawing;

namespace ClipRay.App.Scrolling;

public readonly record struct ScrollCapturePreviewPlacement(Rectangle Bounds);

public static class ScrollCapturePreviewLayout
{
    private const int Gap = 8;

    public static ScrollCapturePreviewPlacement Calculate(Rectangle selection, Size composite, Rectangle workArea)
    {
        if (selection.Width <= 0 || selection.Height <= 0) throw new ArgumentOutOfRangeException(nameof(selection));
        if (composite.Width <= 0 || composite.Height <= 0) throw new ArgumentOutOfRangeException(nameof(composite));

        var height = Math.Min(Math.Min(selection.Height, 300), workArea.Height);
        var width = Math.Max(1, (int)Math.Round(composite.Width * (double)height / composite.Height));
        width = Math.Min(width, Math.Min(420, workArea.Width));
        var right = selection.Right + Gap;
        var left = selection.Left - Gap - width;
        var x = right + width <= workArea.Right
            ? right
            : left >= workArea.Left
                ? left
                : Math.Clamp(right, workArea.Left, workArea.Right - width);
        var y = Math.Clamp(selection.Top, workArea.Top, workArea.Bottom - height);
        return new ScrollCapturePreviewPlacement(new Rectangle(x, y, width, height));
    }
}

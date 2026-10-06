using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Translation;

namespace ClipRay.App.Overlay;

/// <summary>
/// Renders the selection overlay's own text elements into a transparent local
/// preview. This does not observe the desktop compositor or prove visibility.
/// </summary>
public static class SelectionTranslationLayerRenderer
{
    public static BitmapSource RenderOverlay(int width, int height,
        IReadOnlyList<InPlaceLayoutItem> items)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        ArgumentNullException.ThrowIfNull(items);

        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            ClipToBounds = true,
        };
        foreach (var item in items)
            canvas.Children.Add(TranslationOverlayElementFactory.CreatePositioned(item, 1, 1));
        canvas.Measure(new System.Windows.Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));

        var layer = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        layer.Render(canvas);
        layer.Freeze();
        return layer;
    }
}

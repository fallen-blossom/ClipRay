using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Translation;

namespace ClipRay.App.Overlay;

/// <summary>Creates the same translated text element used by the visible selection overlay.</summary>
public static class TranslationOverlayElementFactory
{
    public static FrameworkElement CreatePositioned(
        InPlaceLayoutItem item,
        double pixelScaleX,
        double pixelScaleY)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(pixelScaleX) || pixelScaleX <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelScaleX));
        if (!double.IsFinite(pixelScaleY) || pixelScaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelScaleY));

        var layout = item.TextLayout ?? TranslationTextLayoutPolicy.CreatePreview(item.Bounds.Height);
        var text = new TranslatedTextElement(item, layout, pixelScaleX, pixelScaleY);
        var container = new Grid { ClipToBounds = true };
        if (item.MaskedPatchPng is { Length: > 0 } maskedBytes)
        {
            container.Children.Add(new System.Windows.Controls.Image
            {
                Source = LoadBitmap(maskedBytes),
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            });
        }
        else
        {
            // Preview and legacy callers do not have a pixel mask. Production
            // region translation always supplies MaskedPatchPng.
            container.Background = CreateBackground(item);
        }
        container.Children.Add(text);

        Canvas.SetLeft(container, item.Bounds.X / pixelScaleX);
        Canvas.SetTop(container, item.Bounds.Y / pixelScaleY);
        container.Width = Math.Max(0, Math.Max(1, item.Bounds.Width / pixelScaleX));
        container.Height = Math.Max(0, Math.Max(1, item.Bounds.Height / pixelScaleY));
        return container;
    }

    // Both the interactive overlay and PNG export draw the exact FormattedText
    // measured by the layout policy. TextBlock has different wrapping/baseline
    // behavior and could clip text that had passed the export layout checks.
    private sealed class TranslatedTextElement(InPlaceLayoutItem item,
        TranslationTextLayout layout, double scaleX, double scaleY) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            if (!layout.ShouldRender) return;
            drawing.PushTransform(new ScaleTransform(1 / scaleX, 1 / scaleY));
            if (Math.Abs(layout.RotationDegrees) > .001)
                drawing.PushTransform(new RotateTransform(layout.RotationDegrees,
                    item.Bounds.Width / 2, item.Bounds.Height / 2));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, item.Bounds.Width, item.Bounds.Height)));
            var color = item.ForegroundColor;
            var formatted = TranslationTextLayoutPolicy.Format(item.Text, item.Bounds.Width, layout,
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B)));
            drawing.DrawText(formatted, new System.Windows.Point(0, 0));
            drawing.Pop();
            if (Math.Abs(layout.RotationDegrees) > .001) drawing.Pop();
            drawing.Pop();
        }
    }

    private static BitmapSource LoadBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static System.Windows.Media.Brush CreateBackground(InPlaceLayoutItem item)
    {
        if (item.BackgroundPatchPng is { Length: > 0 } bytes)
            return new ImageBrush(LoadBitmap(bytes)) { Stretch = Stretch.Fill };

        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            item.BackgroundColor.A, item.BackgroundColor.R, item.BackgroundColor.G, item.BackgroundColor.B));
    }
}

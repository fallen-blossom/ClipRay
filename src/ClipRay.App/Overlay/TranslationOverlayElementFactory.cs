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
        var text = new TextBlock
        {
            Text = item.Text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = layout.FontWeight switch
            {
                SourceFontWeight.Bold => FontWeights.Bold,
                SourceFontWeight.SemiBold => FontWeights.SemiBold,
                _ => FontWeights.Normal,
            },
            FontStyle = layout.IsItalic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                item.ForegroundColor.R, item.ForegroundColor.G, item.ForegroundColor.B)),
            FontFamily = new System.Windows.Media.FontFamily(layout.FontFamilyName),
            FontSize = layout.FontSizePixels / pixelScaleY,
            LineHeight = layout.LineHeightPixels / pixelScaleY,
            TextAlignment = layout.Alignment switch
            {
                SourceTextAlignment.Center => TextAlignment.Center,
                SourceTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new RotateTransform(layout.RotationDegrees),
        };
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

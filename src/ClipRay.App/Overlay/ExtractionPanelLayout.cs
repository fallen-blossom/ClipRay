using ClipRay.Core.Geometry;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClipRay.App.Overlay;

public readonly record struct ExtractionPanelLayout(
    double X,
    double Y,
    double Width,
    double Height,
    bool IsOnRight);

public static class ExtractionPanelLayoutPolicy
{
    public const double MinimumWidth = 300;
    public const double PreferredWidth = 360;
    public const double MaximumWidth = 720;
    public const double TextBoxHorizontalMargin = 36;
    public const double MinimumHeight = 168;
    public const double MaximumHeight = 640;
    public const double ChromeHeight = 124;
    public const double MinimumTextHeight = 44;
    public const double Gap = 12;
    public const double ScreenMargin = 16;

    public static ExtractionPanelLayout Calculate(
        double screenWidth,
        double screenHeight,
        PixelRect selection,
        double contentDesiredHeight,
        double contentDesiredWidth = 0)
    {
        if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));

        var viewportWidth = Math.Max(1, screenWidth - ScreenMargin * 2);
        var minimumWidth = Math.Min(MinimumWidth, viewportWidth);
        var maximumWidth = Math.Max(minimumWidth, Math.Min(MaximumWidth, viewportWidth));
        var availableRight = Math.Max(0, screenWidth - selection.Right - Gap - ScreenMargin);
        var availableLeft = Math.Max(0, selection.Left - Gap - ScreenMargin);
        var isOnRight = availableRight >= availableLeft;
        var sideSpace = isOnRight ? availableRight : availableLeft;
        var desiredWidth = contentDesiredWidth > 0
            ? Math.Max(PreferredWidth, contentDesiredWidth + TextBoxHorizontalMargin)
            : PreferredWidth;
        var width = Math.Clamp(
            Math.Min(desiredWidth, Math.Max(minimumWidth, sideSpace)),
            minimumWidth,
            maximumWidth);

        var viewportHeight = Math.Max(1, screenHeight - ScreenMargin * 2);
        var minimumHeight = Math.Min(MinimumHeight, viewportHeight);
        var maximumHeight = Math.Max(minimumHeight, Math.Min(MaximumHeight, viewportHeight));
        var desiredHeight = ChromeHeight + Math.Max(MinimumTextHeight, contentDesiredHeight);
        var height = Math.Clamp(desiredHeight, minimumHeight, maximumHeight);

        var x = isOnRight
            ? selection.Right + Gap
            : selection.Left - width - Gap;
        x = Math.Clamp(x, ScreenMargin, Math.Max(ScreenMargin, screenWidth - width - ScreenMargin));
        var y = Math.Clamp(selection.Top, ScreenMargin, Math.Max(ScreenMargin, screenHeight - height - ScreenMargin));
        return new ExtractionPanelLayout(x, y, width, height, isOnRight);
    }
}

public readonly record struct ExtractionTextSize(double Width, double Height);

public static class ExtractionTextMeasurement
{
    public static ExtractionTextSize Measure(
        string? text,
        double fontSize,
        double maxTextWidth,
        double pixelsPerDip,
        string fontFamily = "Segoe UI")
    {
        if (fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (maxTextWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maxTextWidth));
        if (pixelsPerDip <= 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerDip));
        var formatted = new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(fontFamily),
            fontSize,
            System.Windows.Media.Brushes.Black,
            pixelsPerDip)
        {
            MaxTextWidth = maxTextWidth,
            Trimming = TextTrimming.None,
        };
        return new ExtractionTextSize(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }
}

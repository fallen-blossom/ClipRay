using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public sealed record TranslationTextLayout(
    bool ShouldRender,
    double FontSizePixels,
    double LineHeightPixels,
    SourceFontWeight FontWeight,
    SourceFontCategory FontCategory,
    bool IsItalic,
    SourceTextAlignment Alignment,
    double RotationDegrees,
    string FontFamilyName);

public static class TranslationTextLayoutPolicy
{
    internal static double MeasureSourceInkRatio(string text, SourceFontCategory category)
    {
        var style = new SourceVisualStyle(System.Drawing.Color.Black,100,150,
            SourceFontWeight.Normal,category,false,SourceTextAlignment.Left,0,VisualStyleConfidence.High);
        var geometry = Format(text,100_000,100,150,style,FontFamilyFor(category),System.Windows.Media.Brushes.Black)
            .BuildGeometry(new System.Windows.Point(0,0));
        return Math.Clamp(geometry.Bounds.Height / 100, .45, 1.35);
    }
    public const double VisibleGlyphRatio = 0.72;
    public const double MinimumReadableFontSizePixels = 4;
    private const double MinimumRelativeScale = 0.55;
    private const double CompactMinimumRelativeScale = 0.4;
    private const double FontStepPixels = 0.5;

    public static TranslationTextLayout Fit(string text, PixelRect bounds, SourceVisualStyle style) =>
        FitCore(text, bounds, style, null, MinimumRelativeScale);

    public static TranslationTextLayout FitCompact(string text, PixelRect bounds, SourceVisualStyle style) =>
        FitCore(text, bounds, style, null, CompactMinimumRelativeScale);

    public static TranslationTextLayout FitAtSize(string text, PixelRect bounds,
        SourceVisualStyle style, double fontSizePixels, double lineHeightPixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(style);
        if (!double.IsFinite(fontSizePixels) || fontSizePixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSizePixels));
        if (!double.IsFinite(lineHeightPixels) || lineHeightPixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(lineHeightPixels));
        var family = FontFamilyFor(style.FontCategory);
        var render = false;
        if (style.Confidence != VisualStyleConfidence.Low && bounds.Width > 0 && bounds.Height > 0)
        {
            // The production renderer uses this same wrapped FormattedText.
            // An unwrapped-width estimate can invent extra lines and reject a
            // paragraph that already fits at the established session font.
            var measured = Measure(text, bounds.Width, fontSizePixels, lineHeightPixels, style, family);
            var height = Math.Max(measured.Height, lineHeightPixels);
            var angle = Math.Abs(style.RotationDegrees) * Math.PI / 180;
            var width = Math.Abs(measured.Width * Math.Cos(angle)) + Math.Abs(height * Math.Sin(angle));
            var rotatedHeight = Math.Abs(measured.Width * Math.Sin(angle)) + Math.Abs(height * Math.Cos(angle));
            render = width <= bounds.Width + .5 && rotatedHeight <= bounds.Height + .5;
        }
        return new(render,fontSizePixels,lineHeightPixels,style.FontWeight,style.FontCategory,
            style.IsItalic,style.Alignment,style.RotationDegrees,family);
    }

    public static TranslationTextLayout FitAtMost(
        string text,
        PixelRect bounds,
        SourceVisualStyle style,
        double maximumFontSizePixels)
    {
        if (maximumFontSizePixels <= 0) throw new ArgumentOutOfRangeException(nameof(maximumFontSizePixels));
        return FitCore(text, bounds, style, maximumFontSizePixels, MinimumRelativeScale);
    }

    private static TranslationTextLayout FitCore(
        string text,
        PixelRect bounds,
        SourceVisualStyle style,
        double? maximumFontSizePixels,
        double minimumRelativeScale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(style);
        // Keep the source font size for ordinary Chinese text. Matching only its
        // taller visible glyphs made Chinese unnecessarily smaller than English.
        // Narrow metadata retains the conservative compact fallback.
        var visibleGlyphRatio = ContainsCjk(text) && minimumRelativeScale == CompactMinimumRelativeScale
            ? 0.9 : VisibleGlyphRatio;
        var initialFontSize = Math.Max(1, style.EstimatedFontSizePixels ?? style.GlyphHeightPixels / visibleGlyphRatio);
        var startingFontSize = Math.Min(initialFontSize, maximumFontSizePixels ?? initialFontSize);
        var fontFamily = FontFamilyFor(style.FontCategory);
        if (style.Confidence == VisualStyleConfidence.Low || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return Create(false, startingFontSize, style, fontFamily);
        }

        var minimum = Math.Min(startingFontSize, Math.Max(4, initialFontSize * minimumRelativeScale));
        for (var size = startingFontSize; size + 0.001 >= minimum; size -= FontStepPixels)
        {
            var lineHeight = CalculateLineHeight(size, initialFontSize, style.LineHeightPixels);
            var measured = Measure(text, bounds.Width, size, lineHeight, style, fontFamily);
            var unwrappedWidth = Measure(text, 100_000, size, lineHeight, style, fontFamily).Width;
            var conservativeLineCount = Math.Max(1, Math.Ceiling(unwrappedWidth / Math.Max(1, bounds.Width)));
            var measuredHeight = Math.Max(measured.Height, conservativeLineCount * lineHeight);
            var angle = Math.Abs(style.RotationDegrees) * Math.PI / 180;
            var rotatedWidth = Math.Abs(measured.Width * Math.Cos(angle)) + Math.Abs(measuredHeight * Math.Sin(angle));
            var rotatedHeight = Math.Abs(measured.Width * Math.Sin(angle)) + Math.Abs(measuredHeight * Math.Cos(angle));
            if (rotatedWidth <= bounds.Width + 0.5 && rotatedHeight <= bounds.Height + 0.5)
            {
                return new TranslationTextLayout(
                    true,
                    size,
                    lineHeight,
                    style.FontWeight,
                    style.FontCategory,
                    style.IsItalic,
                    style.Alignment,
                    style.RotationDegrees,
                    fontFamily);
            }
        }

        return Create(false, initialFontSize, style, fontFamily);
    }

    public static TranslationTextLayout CreatePreview(double boundsHeight)
    {
        var glyphHeight = Math.Max(1, boundsHeight * VisibleGlyphRatio);
        var style = new SourceVisualStyle(
            System.Drawing.Color.White,
            glyphHeight,
            boundsHeight,
            SourceFontWeight.SemiBold,
            SourceFontCategory.SansSerif,
            false,
            SourceTextAlignment.Left,
            0,
            VisualStyleConfidence.Medium);
        return Create(true, glyphHeight / VisibleGlyphRatio, style, FontFamilyFor(style.FontCategory));
    }

    private static TranslationTextLayout Create(
        bool shouldRender,
        double fontSize,
        SourceVisualStyle style,
        string fontFamily) => new(
            shouldRender,
            fontSize,
            CalculateLineHeight(fontSize, fontSize, style.LineHeightPixels),
            style.FontWeight,
            style.FontCategory,
            style.IsItalic,
            style.Alignment,
            style.RotationDegrees,
            fontFamily);

    private static (double Width, double Height) Measure(
        string text,
        double width,
        double fontSize,
        double lineHeight,
        SourceVisualStyle style,
        string fontFamily)
    {
        var formatted = Format(text, width, fontSize, lineHeight, style, fontFamily, System.Windows.Media.Brushes.Black);
        return (formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }

    internal static FormattedText Format(string text, double width, TranslationTextLayout layout, System.Windows.Media.Brush foreground) =>
        Format(text, width, layout.FontSizePixels, layout.LineHeightPixels,
            new SourceVisualStyle(System.Drawing.Color.White, layout.FontSizePixels, layout.LineHeightPixels,
                layout.FontWeight, layout.FontCategory, layout.IsItalic, layout.Alignment,
                layout.RotationDegrees, VisualStyleConfidence.High), layout.FontFamilyName, foreground);

    private static FormattedText Format(string text, double width, double fontSize, double lineHeight,
        SourceVisualStyle style, string fontFamily, System.Windows.Media.Brush foreground)
    {
        var typeface = new Typeface(
            new System.Windows.Media.FontFamily(fontFamily),
            style.IsItalic ? FontStyles.Italic : FontStyles.Normal,
            ToWpfWeight(style.FontWeight),
            FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foreground,
            1)
        {
            MaxTextWidth = Math.Max(1, width),
            LineHeight = lineHeight,
            TextAlignment = style.Alignment switch
            {
                SourceTextAlignment.Center => TextAlignment.Center,
                SourceTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };
        return formatted;
    }

    private static double CalculateLineHeight(double fontSize, double sourceFontSize, double sourceLineHeight)
    {
        var ratio = sourceFontSize <= 0 ? 1.2 : Math.Clamp(sourceLineHeight / sourceFontSize, 0.95, 1.5);
        return fontSize * ratio;
    }

    private static FontWeight ToWpfWeight(SourceFontWeight weight) => weight switch
    {
        SourceFontWeight.Bold => FontWeights.Bold,
        SourceFontWeight.SemiBold => FontWeights.SemiBold,
        _ => FontWeights.Normal,
    };

    private static string FontFamilyFor(SourceFontCategory category) => category switch
    {
        SourceFontCategory.Monospace => "Cascadia Mono, Consolas, Microsoft YaHei UI",
        SourceFontCategory.Serif => "Georgia, Times New Roman, Microsoft YaHei UI",
        _ => "Segoe UI, Microsoft YaHei UI",
    };

    private static bool ContainsCjk(string text) => text.Any(static value =>
        value is >= '\u3400' and <= '\u9FFF'
        or >= '\uF900' and <= '\uFAFF');
}

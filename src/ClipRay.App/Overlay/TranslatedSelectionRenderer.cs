using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ClipRay.App.Overlay;

/// <summary>将译文直接合成到选区位图；不会捕获 Overlay 窗口或任何 ClipRay 控件。</summary>
public static class TranslatedSelectionRenderer
{
    public static BitmapSource RenderOverlay(int width, int height, IReadOnlyList<InPlaceLayoutItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            foreach (var item in items) DrawItem(drawing, item, 0, 0);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    public static Bitmap Render(
        Bitmap source,
        PixelRect selection,
        IReadOnlyList<InPlaceLayoutItem> items)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(items);
        var crop = ToCropRectangle(selection, source.Width, source.Height);
        using var original = source.Clone(crop, DrawingPixelFormat.Format32bppPArgb);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(BitmapSourceFactory.Create(original), new Rect(0, 0, crop.Width, crop.Height));
            foreach (var item in items.Where(item => Intersects(selection, item.Bounds)))
            {
                DrawItem(drawing, item, crop.Left, crop.Top);
            }
        }

        var target = new RenderTargetBitmap(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var encoded = new Bitmap(stream);
        return new Bitmap(encoded);
    }

    private static void DrawItem(DrawingContext drawing, InPlaceLayoutItem item, int cropLeft, int cropTop)
    {
        var bounds = new Rect(
            item.Bounds.X - cropLeft,
            item.Bounds.Y - cropTop,
            item.Bounds.Width,
            item.Bounds.Height);
        if (item.MaskedPatchPng is { Length: > 0 } masked)
        {
            drawing.DrawImage(Decode(masked), bounds);
        }
        else if (item.BackgroundPatchPng is { Length: > 0 } patch)
        {
            drawing.DrawImage(Decode(patch), bounds);
        }
        else
        {
            drawing.DrawRectangle(
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    item.BackgroundColor.A,
                    item.BackgroundColor.R,
                    item.BackgroundColor.G,
                    item.BackgroundColor.B)),
                null,
                bounds);
        }

        var layout = item.TextLayout ?? TranslationTextLayoutPolicy.CreatePreview(item.Bounds.Height);
        if (!layout.ShouldRender) return;
        var text = TranslationTextLayoutPolicy.Format(item.Text, bounds.Width, layout,
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(item.ForegroundColor.A,
                item.ForegroundColor.R, item.ForegroundColor.G, item.ForegroundColor.B)));
        drawing.PushTransform(new TranslateTransform(bounds.X, bounds.Y));
        if (Math.Abs(layout.RotationDegrees) > 0.001)
        {
            drawing.PushTransform(new RotateTransform(
                layout.RotationDegrees,
                bounds.Width / 2,
                bounds.Height / 2));
        }
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width, bounds.Height)));
        drawing.DrawText(text, new System.Windows.Point(0, 0));
        drawing.Pop();
        if (Math.Abs(layout.RotationDegrees) > 0.001) drawing.Pop();
        drawing.Pop();
    }

    private static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        return new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
    }

    private static bool Intersects(PixelRect first, PixelRect second) =>
        first.Left < second.Right
        && first.Right > second.Left
        && first.Top < second.Bottom
        && first.Bottom > second.Top;

    private static Rectangle ToCropRectangle(PixelRect rect, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp((int)Math.Floor(rect.Left), 0, imageWidth - 1);
        var top = Math.Clamp((int)Math.Floor(rect.Top), 0, imageHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right), left + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom), top + 1, imageHeight);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}

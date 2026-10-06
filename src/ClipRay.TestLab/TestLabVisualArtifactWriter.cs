using System.Drawing;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;

namespace ClipRay.TestLab;

public static class TestLabVisualArtifactWriter
{
    public static void Write(
        Bitmap source,
        IReadOnlyList<InPlaceLayoutItem> items,
        bool success,
        string category,
        string path)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            WriteOnSta(source, items, success, category, path);
            return;
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                WriteOnSta(source, items, success, category, path);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void WriteOnSta(
        Bitmap source,
        IReadOnlyList<InPlaceLayoutItem> items,
        bool success,
        string category,
        string path)
    {
        var canvas = new Canvas
        {
            Width = source.Width,
            Height = source.Height,
            ClipToBounds = true,
        };
        canvas.Children.Add(new System.Windows.Controls.Image
        {
            Source = BitmapSourceFactory.Create(source),
            Width = source.Width,
            Height = source.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        });
        foreach (var item in items)
            canvas.Children.Add(TranslationOverlayElementFactory.CreatePositioned(item, 1, 1));
        canvas.Measure(new System.Windows.Size(source.Width, source.Height));
        canvas.Arrange(new Rect(0, 0, source.Width, source.Height));

        var foreground = new RenderTargetBitmap(source.Width, source.Height, 96, 96, PixelFormats.Pbgra32);
        foreground.Render(canvas);
        var encoder = new PngBitmapEncoder();
        // The PNG is pixel evidence, including for partial outcomes. Keep status in the report;
        // a failure banner here would alter source pixels and contaminate the quality oracle.
        encoder.Frames.Add(BitmapFrame.Create(foreground));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

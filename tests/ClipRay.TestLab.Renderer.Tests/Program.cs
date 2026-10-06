using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using ClipRay.TestLab;

Exception? failure = null;
var thread = new Thread(() =>
{
try
{
using var source = new Bitmap(360, 140, PixelFormat.Format32bppArgb);
using (var graphics = Graphics.FromImage(source))
{
    graphics.Clear(Color.FromArgb(255, 12, 18, 30));
    graphics.FillRectangle(Brushes.SlateGray, 10, 48, 250, 52);
}

var firstBounds = new PixelRect(18, 54, 126, 35);
var secondBounds = new PixelRect(155, 54, 126, 35);
var thirdBounds = new PixelRect(18, 104, 126, 28);
var firstBlock = new OcrTextBlock("First", firstBounds, []);
var secondBlock = new OcrTextBlock("Second", secondBounds, []);
var thirdBlock = new OcrTextBlock("Third", thirdBounds, []);
var layout = new TranslationTextLayout(true, 18, 22, SourceFontWeight.SemiBold,
    SourceFontCategory.SansSerif, false, SourceTextAlignment.Left, 0,
    "Segoe UI, Microsoft YaHei UI");
var items = new InPlaceLayoutItem[]
{
    new(firstBlock, "第一段", firstBounds, InPlaceBackgroundMode.ComplexBacking,
        Color.Black, Color.White, TextLayout: layout,
        MaskedPatchPng: CreateMaskedPatch(126, 35)),
    new(secondBlock, "第二段", secondBounds, InPlaceBackgroundMode.ComplexBacking,
        Color.Black, Color.Yellow, BackgroundPatchPng: CreateSolidPatch(126, 35, Color.Navy),
        TextLayout: layout with { Alignment = SourceTextAlignment.Right }),
    new(thirdBlock, "第三段", thirdBounds, InPlaceBackgroundMode.FlatSampled,
        Color.FromArgb(255, 45, 30, 25), Color.Cyan,
        TextLayout: layout with { FontSizePixels = 15, LineHeightPixels = 18 }),
};

var outputRoot = Path.Combine(Path.GetTempPath(), "clipray-testlab-renderer-parity-" + Guid.NewGuid().ToString("N"));
var successPath = Path.Combine(outputRoot, "success.png");
var failedPath = Path.Combine(outputRoot, "failed.png");

var scaled = TranslationOverlayElementFactory.CreatePositioned(items[0], 1.25, 1.5);
if (scaled is not Grid scaledGrid || !scaledGrid.ClipToBounds)
    throw new InvalidOperationException("The foreground element lost its clipped container.");
AssertNear(firstBounds.X / 1.25, Canvas.GetLeft(scaledGrid));
AssertNear(firstBounds.Y / 1.5, Canvas.GetTop(scaledGrid));
AssertNear(firstBounds.Width / 1.25, scaledGrid.Width);
AssertNear(firstBounds.Height / 1.5, scaledGrid.Height);
if (scaledGrid.Children[^1] is not TextBlock scaledText)
    throw new InvalidOperationException("The foreground element lost its TextBlock.");
AssertNear(layout.FontSizePixels / 1.5, scaledText.FontSize);
AssertNear(layout.LineHeightPixels / 1.5, scaledText.LineHeight);

var referenceCanvas = new Canvas { Width = source.Width, Height = source.Height, ClipToBounds = true };
referenceCanvas.Children.Add(new System.Windows.Controls.Image
{
    Source = BitmapSourceFactory.Create(source),
    Width = source.Width,
    Height = source.Height,
    Stretch = System.Windows.Media.Stretch.Fill,
});
foreach (var item in items)
    referenceCanvas.Children.Add(TranslationOverlayElementFactory.CreatePositioned(item, 1, 1));
referenceCanvas.Measure(new System.Windows.Size(source.Width, source.Height));
referenceCanvas.Arrange(new Rect(0, 0, source.Width, source.Height));
var directForeground = new RenderTargetBitmap(source.Width, source.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
directForeground.Render(referenceCanvas);
var expectedEncoder = new PngBitmapEncoder();
expectedEncoder.Frames.Add(BitmapFrame.Create(directForeground));
using var expectedStream = new MemoryStream();
expectedEncoder.Save(expectedStream);
expectedStream.Position = 0;
using var expected = new Bitmap(expectedStream);

TestLabVisualArtifactWriter.Write(source, items, true, "completed", successPath);
using var actual = new Bitmap(successPath);
if (actual.Width != expected.Width || actual.Height != expected.Height)
    throw new InvalidOperationException("TestLab changed the foreground image dimensions.");
for (var y = 0; y < expected.Height; y++)
for (var x = 0; x < expected.Width; x++)
{
    if (actual.GetPixel(x, y).ToArgb() != expected.GetPixel(x, y).ToArgb())
        throw new InvalidOperationException($"TestLab differs from the shared foreground element at ({x},{y}).");
}
if (CountPixels(actual, secondBounds, static color => color.R > 180 && color.G > 180 && color.B < 80) < 4
    || CountPixels(actual, thirdBounds, static color => color.R < 100 && color.G > 150 && color.B > 150) < 4)
    throw new InvalidOperationException("The translated text or its source color is absent.");

TestLabVisualArtifactWriter.Write(source, items, false, "layout-incomplete", failedPath);
using var failed = new Bitmap(failedPath);
if (failed.Width != source.Width || failed.Height != source.Height)
    throw new InvalidOperationException("The partial-result image changed dimensions.");
for (var y = 0; y < expected.Height; y++)
for (var x = 0; x < expected.Width; x++)
    if (failed.GetPixel(x, y).ToArgb() != expected.GetPixel(x, y).ToArgb())
        throw new InvalidOperationException($"A failure label altered pixel evidence at ({x},{y}).");
if (source.GetPixel(340, 110).ToArgb() != Color.FromArgb(255, 12, 18, 30).ToArgb())
    throw new InvalidOperationException("Rendering mutated the source image.");

Console.WriteLine("TESTLAB_FOREGROUND_PARITY_PASSED: 96 DPI foreground elements match; partial images retain source pixels.");
}
catch (Exception ex)
{
    failure = ex;
}
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure is not null) throw failure;

static byte[] CreateSolidPatch(int width, int height, Color color)
{
    using var patch = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(patch)) graphics.Clear(color);
    using var stream = new MemoryStream();
    patch.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static byte[] CreateMaskedPatch(int width, int height)
{
    using var patch = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(patch))
    {
        graphics.Clear(Color.Transparent);
        graphics.FillRectangle(Brushes.Black, 4, 6, width - 8, height - 12);
    }
    using var stream = new MemoryStream();
    patch.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static void AssertNear(double expected, double actual)
{
    if (Math.Abs(expected - actual) > .001)
        throw new InvalidOperationException($"Foreground DPI geometry differs: {expected} vs {actual}.");
}

static int CountPixels(Bitmap bitmap, PixelRect bounds, Func<Color, bool> matches)
{
    var count = 0;
    for (var y = (int)bounds.Top; y < (int)bounds.Bottom; y++)
    for (var x = (int)bounds.Left; x < (int)bounds.Right; x++)
        if (matches(bitmap.GetPixel(x, y))) count++;
    return count;
}

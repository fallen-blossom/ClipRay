using System.Drawing;
using ClipRay.App.LiveTranslation;

internal static class LiveTranslationScreenCaptureTests
{
    private static readonly IReadOnlyList<Rectangle> Monitors =
    [
        new Rectangle(0, 0, 1920, 1080),
        new Rectangle(1920, 0, 1920, 1080),
    ];

    public static async Task SamePhysicalRegionSamplesVisibleAtoBtoA()
    {
        var region = new Rectangle(20, 30, 40, 20);
        var visibleColor = Color.Red;
        var calls = new List<Rectangle>();
        await using var source = await LiveTranslationScreenCapture.StartForRegionAsync(
            region,
            CancellationToken.None,
            sampledRegion =>
            {
                calls.Add(sampledRegion);
                var bitmap = new Bitmap(sampledRegion.Width, sampledRegion.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(visibleColor);
                return bitmap;
            },
            () => Monitors);

        using var first = source.Capture(region);
        visibleColor = Color.Blue;
        using var second = source.Capture(region);
        visibleColor = Color.Red;
        using var third = source.Capture(region);

        if (first.GetPixel(0, 0).ToArgb() != Color.Red.ToArgb()
            || second.GetPixel(0, 0).ToArgb() != Color.Blue.ToArgb()
            || third.GetPixel(0, 0).ToArgb() != Color.Red.ToArgb())
            throw new Exception("The fixed physical region did not reflect the current A→B→A pixels.");
        if (ReferenceEquals(first, second) || ReferenceEquals(second, third)
            || calls.Count != 3 || calls.Any(call => call != region))
            throw new Exception("Screen capture reused a frame or changed its physical region.");
    }

    public static async Task RejectsInvalidCrossMonitorOrChangedRegion()
    {
        var calls = 0;
        Bitmap Capture(Rectangle region)
        {
            calls++;
            return new Bitmap(region.Width, region.Height);
        }

        await ExpectThrowsAsync<ArgumentOutOfRangeException>(() =>
            LiveTranslationScreenCapture.StartForRegionAsync(
                new Rectangle(10, 10, 0, 20), CancellationToken.None, Capture, () => Monitors));
        await ExpectThrowsAsync<ArgumentOutOfRangeException>(() =>
            LiveTranslationScreenCapture.StartForRegionAsync(
                new Rectangle(10, 10, 20, -1), CancellationToken.None, Capture, () => Monitors));
        await ExpectThrowsAsync<InvalidOperationException>(() =>
            LiveTranslationScreenCapture.StartForRegionAsync(
                new Rectangle(1900, 10, 40, 20), CancellationToken.None, Capture, () => Monitors));
        await ExpectThrowsAsync<InvalidOperationException>(() =>
            LiveTranslationScreenCapture.StartForRegionAsync(
                new Rectangle(-100, 10, 40, 20), CancellationToken.None, Capture, () => Monitors));

        var region = new Rectangle(10, 10, 40, 20);
        await using var source = await LiveTranslationScreenCapture.StartForRegionAsync(
            region, CancellationToken.None, Capture, () => Monitors);
        ExpectThrows<ArgumentOutOfRangeException>(() => source.Capture(new Rectangle(11, 10, 40, 20)));
        if (calls != 0)
            throw new Exception("An invalid physical region reached the capture delegate.");
    }

    public static async Task CapturesDesktopAndMultipleWindowPixelsWithoutWindowIdentity()
    {
        var region = new Rectangle(100, 100, 90, 30);
        await using var source = await LiveTranslationScreenCapture.StartForRegionAsync(
            region,
            CancellationToken.None,
            sampledRegion =>
            {
                var bitmap = new Bitmap(sampledRegion.Width, sampledRegion.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Green); // Desktop or the first window.
                graphics.FillRectangle(Brushes.Blue, 30, 0, 30, sampledRegion.Height);
                graphics.FillRectangle(Brushes.Yellow, 60, 0, 30, sampledRegion.Height);
                return bitmap;
            },
            () => Monitors);

        using var bitmap = source.Capture(region);
        if (bitmap.GetPixel(0, 0).ToArgb() != Color.Green.ToArgb()
            || bitmap.GetPixel(45, 0).ToArgb() != Color.Blue.ToArgb()
            || bitmap.GetPixel(75, 0).ToArgb() != Color.Yellow.ToArgb())
            throw new Exception("A screen region crossing desktop or window content was altered or rejected.");
    }

    public static async Task RejectsChangedTopologyAndWrongSizedFrame()
    {
        var region = new Rectangle(10, 10, 40, 20);
        IReadOnlyList<Rectangle> monitors = Monitors;
        var calls = 0;
        await using var source = await LiveTranslationScreenCapture.StartForRegionAsync(
            region,
            CancellationToken.None,
            _ =>
            {
                calls++;
                return new Bitmap(20, 20);
            },
            () => monitors);

        ExpectThrows<InvalidOperationException>(() => source.Capture(region));
        if (calls != 1)
            throw new Exception("A frame with incorrect physical dimensions was not checked.");
        monitors = [new Rectangle(100, 100, 1920, 1080)];
        ExpectThrows<InvalidOperationException>(() => source.Capture(region));
        if (calls != 1)
            throw new Exception("A detached monitor region still reached the capture delegate.");
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new Exception($"Expected {typeof(TException).Name}.");
    }

    private static void ExpectThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new Exception($"Expected {typeof(TException).Name}.");
    }
}

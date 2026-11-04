using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ClipRay.App.Capture;
using ClipRay.App.LiveTranslation;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using Image = System.Windows.Controls.Image;

// One normal, visible, generated page through the actual local translation and
// WPF overlay. The fixture owns its window; no clipboard or user page is read.
internal static class LiveWindowReadabilityAcceptance
{
    internal static int Run(string[] args)
    {
        var output = Path.GetFullPath(args[1]);
        var root = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Startup += async (_, _) =>
        {
            Window? sourceWindow = null;
            LiveTranslationOverlayWindow? overlay = null;
            LiveTranslationStopControlWindow? control = null;
            var code = 1;
            try
            {
                using var fixture = DailyTranslationPageFixture.Create(out _);
                var image = new Image { Source = BitmapSourceFactory.Create(fixture), Stretch = Stretch.Fill };
                sourceWindow = new Window { Title = "ClipRay translation acceptance fixture", Content = image,
                    Width = 960, Height = 465, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false,
                    Topmost = true, ShowActivated = true };
                sourceWindow.Show();
                sourceWindow.Activate();
                await Task.Delay(500);
                var sourceHandle = new WindowInteropHelper(sourceWindow).Handle;
                if (GetForegroundWindow() != sourceHandle) throw new InvalidOperationException("fixture is not foreground");
                var topLeft = image.PointToScreen(new System.Windows.Point(0, 0));
                var bottomRight = image.PointToScreen(new System.Windows.Point(image.ActualWidth, image.ActualHeight));
                var region = Rectangle.FromLTRB((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
                    (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y));
                using var captured = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(captured))
                    graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty, region.Size);
                captured.Save(Path.Combine(output, "visible-fixture-source.png"), ImageFormat.Png);
                using var ocr = OcrService.CreateForCommonMachine();
                var model = MachineTranslationModelPaths.Resolve(root);
                using var process = new BundledMachineTranslationProcess(
                    Path.Combine(root, "machine-translation", "runtime", "python.exe"),
                    Path.Combine(AppContext.BaseDirectory, "machine-translation", "machine_translation_worker.py"),
                    model, workerTimeout: TimeSpan.FromSeconds(10), keepAlive: true,
                    startupTimeout: TimeSpan.FromSeconds(30));
                using var provider = new LocalMachineTranslationProvider(process,
                    supportsCommonLanguages: MachineTranslationModelPaths.IsCommonLanguageModel(model));
                var orchestrator = new TranslationApplicationOrchestrator(
                    (bitmap, token) => ocr.RecognizeAsync(bitmap, token),
                    (_, requests, token) => provider.TranslateManyAsync(requests, token));
                var bounds = new PixelRect(0, 0, captured.Width, captured.Height);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var outcome = await orchestrator.ExecuteAsync(captured, TranslationRouteKind.Machine,
                    "简体中文", blocks => SelectionCapturePolicy.ConstrainToSelection(blocks, bounds, bounds), deadline.Token);
                if (outcome.LayoutItems.Count < 5) throw new InvalidOperationException("too few visible translated regions");
                if (outcome.LayoutItems.Any(item => item.TextLayout is not { ShouldRender: true, FontSizePixels: >= 9 }))
                    throw new InvalidOperationException("unreadable translated region");
                File.WriteAllText(Path.Combine(output, "typography.json"), System.Text.Json.JsonSerializer.Serialize(
                    outcome.LayoutItems.Select(item => new { Source = item.SourceRegion?.SourceText, item.Text,
                        SourceColor = item.SourceStyle?.ForegroundColor.ToArgb(), item.SourceStyle?.EstimatedFontSizePixels,
                        Layout = item.TextLayout is null ? null : new { item.TextLayout.FontSizePixels,
                            Runs = item.TextLayout.StyleRuns.Select(run => new { run.Start, run.Length,
                                Color = run.Style.ForegroundColor.ToArgb(), run.Style.EstimatedFontSizePixels }) } }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                using (var rendered = TranslatedSelectionRenderer.Render(captured, bounds, outcome.LayoutItems))
                    rendered.Save(Path.Combine(output, "visible-fixture-render.png"), ImageFormat.Png);

                overlay = new LiveTranslationOverlayWindow(region, showBorder: false, showInRecordings: false);
                overlay.Show();
                if (!overlay.DisplayAffinityApplied) throw new InvalidOperationException("overlay capture exclusion unavailable");
                var frame = LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine, outcome);
                if (!overlay.Apply(frame, 1) || !await overlay.AcknowledgePresentationAsync(1))
                    throw new InvalidOperationException("overlay did not present a render cycle");
                if (!GetWindowRect(new WindowInteropHelper(overlay).Handle, out var rectangle)
                    || Math.Abs(rectangle.Left - region.Left) > 1 || Math.Abs(rectangle.Top - region.Top) > 1
                    || Math.Abs(rectangle.Right - region.Right) > 1 || Math.Abs(rectangle.Bottom - region.Bottom) > 1)
                    throw new InvalidOperationException("visible overlay bounds differ from the source region");

                control = new LiveTranslationStopControlWindow(region);
                if (!control.CanPlaceOutside) throw new InvalidOperationException("fixture leaves no room for controls");
                control.Show();
                control.SetReadingFallback("这是一条未能原位显示的译文。");
                var button = (System.Windows.Controls.Button)control.FindName("ReadingButton");
                if (button.Visibility != Visibility.Visible) throw new InvalidOperationException("reading entry is hidden");
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                var reader = application.Windows.OfType<TranslationReadingWindow>().SingleOrDefault();
                if (reader?.Content is not System.Windows.Controls.TextBox { IsReadOnly: true } text
                    || !text.Text.Contains("未能原位显示"))
                    throw new InvalidOperationException("reading window did not show selectable translation");
                control.ClearReadingFallback();
                if (button.Visibility != Visibility.Collapsed || reader.IsVisible)
                    throw new InvalidOperationException("stale reading window did not close");
                control.Close();
                control = null;
                sourceWindow.Activate();
                await CheckVisibleRefreshAsync(sourceWindow, sourceHandle, image, overlay, fixture, region,
                    orchestrator, ocr, deadline.Token);
                Console.WriteLine($"VISIBLE_LIVE_WINDOW_PASSED region={region.Width}x{region.Height} "
                    + $"ocr={outcome.OcrBlocks.Count} layouts={outcome.LayoutItems.Count} "
                    + $"fallback={frame.ReadingItems.Count}");
                code = 0;
            }
            catch (Exception error) { Console.Error.WriteLine("VISIBLE_LIVE_WINDOW_FAILED " + error); }
            finally
            {
                control?.Close();
                overlay?.Close();
                sourceWindow?.Close();
                application.Shutdown(code);
            }
        };
        return application.Run();
    }

    private static async Task CheckVisibleRefreshAsync(Window sourceWindow, IntPtr sourceHandle, Image image,
        LiveTranslationOverlayWindow overlay, Bitmap fixture, Rectangle region,
        TranslationApplicationOrchestrator orchestrator, OcrService ocr, CancellationToken cancellationToken)
    {
        var initial = new TaskCompletionSource<InPlaceLayoutItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource<InPlaceLayoutItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changed = false;
        var clears = 0;
        using var session = new LiveTranslationSession(region,
            bounds =>
            {
                if (GetForegroundWindow() != sourceHandle) throw new InvalidOperationException("fixture lost foreground");
                var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                return bitmap;
            },
            async (bitmap, token) =>
            {
                var bounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height);
                var result = await orchestrator.ExecuteAsync(bitmap, TranslationRouteKind.Machine, "简体中文",
                    blocks => SelectionCapturePolicy.ConstrainToSelection(blocks, bounds, bounds), token);
                return LiveTranslationFrame.FromOutcome(TranslationRouteKind.Machine, result);
            }, TranslationRouteKind.Machine, validateLayoutRegions: true,
            verifySource: (bitmap, token) => ocr.RecognizeAsync(bitmap, token), scheduleRegions: true);
        session.FrameReady += (_, candidate) => _ = PresentAsync(candidate);
        session.FrameInvalidated += (_, _) =>
        {
            if (Volatile.Read(ref changed)) Interlocked.Increment(ref clears);
            var generation = session.Generation;
            _ = sourceWindow.Dispatcher.InvokeAsync(() =>
            {
                if (!session.CanInvalidateDisplay(generation)) return;
                overlay.Clear();
                session.MarkDisplayApplied(generation);
            });
        };
        async Task PresentAsync(LiveTranslationFrameReadyEventArgs candidate)
        {
            try
            {
                await sourceWindow.Dispatcher.InvokeAsync(async () =>
                {
                    if (!session.CanPresent(candidate) || !overlay.Apply(candidate.Frame, candidate.Generation)) return;
                    session.MarkDisplayApplied(candidate.Generation);
                    if (!await overlay.AcknowledgePresentationAsync(candidate.Generation))
                        throw new InvalidOperationException("refresh overlay render was not observed");
                    session.MarkDisplayPresented(candidate.Generation);
                    var unchanged = candidate.Frame.LayoutItems.FirstOrDefault(item =>
                        item.SourceRegion?.SourceText.Contains("Choose", StringComparison.OrdinalIgnoreCase) == true
                        && item.SourceRegion.SourceText.Contains("language", StringComparison.OrdinalIgnoreCase));
                    if (unchanged is null) return;
                    initial.TrySetResult(unchanged);
                    if (Volatile.Read(ref changed) && candidate.Frame.SourceRegions.Any(source =>
                            source.SourceText.Contains("complete now", StringComparison.OrdinalIgnoreCase)))
                        refreshed.TrySetResult(unchanged);
                }).Task.Unwrap();
            }
            catch (Exception error) { failed.TrySetResult(error); }
        }
        var running = session.StartAsync(cancellationToken);
        try
        {
            var first = await AwaitResult(initial.Task, failed.Task, cancellationToken);
            using var replacement = new Bitmap(fixture);
            using (var graphics = Graphics.FromImage(replacement))
            using (var font = new Font("Segoe UI", 22, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(35, 35, 35)))
            {
                graphics.FillRectangle(System.Drawing.Brushes.White, 665, 87, 540, 34);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("The update is complete now.", font, brush, 670, 90, StringFormat.GenericTypographic);
            }
            Volatile.Write(ref changed, true);
            image.Source = BitmapSourceFactory.Create(replacement);
            var second = await AwaitResult(refreshed.Task, failed.Task, cancellationToken);
            if (Volatile.Read(ref clears) != 0) throw new InvalidOperationException("unrelated source update cleared the whole overlay");
            if (Math.Abs(first.Bounds.X - second.Bounds.X) > 1 || Math.Abs(first.Bounds.Y - second.Bounds.Y) > 1
                || Math.Abs(first.TextLayout!.FontSizePixels - second.TextLayout!.FontSizePixels) > 1)
                throw new InvalidOperationException("unchanged text moved or changed font during source refresh");
            Console.WriteLine("VISIBLE_LIVE_REFRESH_PASSED unchanged-region-stable");
        }
        finally { session.Stop(); await running; }
    }

    private static async Task<T> AwaitResult<T>(Task<T> result, Task<Exception> failed, CancellationToken token)
    {
        var completed = await Task.WhenAny(result, failed, Task.Delay(TimeSpan.FromSeconds(20), token));
        if (completed == failed) throw await failed;
        if (completed != result) throw new InvalidOperationException("visible live refresh did not complete");
        return await result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
}

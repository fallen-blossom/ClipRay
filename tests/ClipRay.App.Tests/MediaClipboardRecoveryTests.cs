using System.Collections.Concurrent;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Recording;
using ClipRay.App.Scrolling;
using ClipRay.App.Settings;

internal static class MediaClipboardRecoveryTests
{
    public static void NativeRecoveryProbe(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("An absolute probe root is required.");
        Directory.CreateDirectory(root);
        var settings = AppSettings.CreateDefault(root);
        OutputDirectoryLayout.Ensure(settings);
        var previous = System.Windows.Clipboard.GetDataObject();
        var restored = false;
        string? ownedPath = null;
        try
        {
            using var bitmap = CreateFixture();
            // Screen captures are opaque; Bgr32 padding is not a color channel.
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                bitmap.SetPixel(x, y, Color.FromArgb(255, pixel.R, pixel.G, pixel.B));
            }
            var delivered = new ImageOutputService(settings.ScreenshotDirectory, true).SaveWithDelivery(bitmap);
            ownedPath = delivered.Path;
            Check(delivered.ClipboardCopied && System.Windows.Clipboard.ContainsImage(), "Native first image delivery failed");
            var marker = new System.Windows.DataObject();
            marker.SetText("ClipRay native recovery probe");
            marker.SetData(MediaClipboardDataFactory.CloudFormat, new MemoryStream(BitConverter.GetBytes(0)));
            System.Windows.Clipboard.SetDataObject(marker, true);
            var restarted = new RecentCaptureService(settings);
            var imageItem = restarted.GetRecent().Single(item => item.Path == delivered.Path);
            Check(restarted.TryCopy(imageItem), "Native image recovery after replacement failed");
            var recovered = System.Windows.Clipboard.GetImage() ?? throw new Exception("Recovered image missing");
            Check(recovered.PixelWidth == 9 && recovered.PixelHeight == 13, "Native recovery resized the image");
            var original = BitmapSourceFactory.Create(bitmap);
            File.WriteAllText(Path.Combine(root, "native-image-formats.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                source_format = original.Format.ToString(), recovered_format = recovered.Format.ToString(),
            }));
            var originalPixels = ReadBgra(original);
            var recoveredPixels = ReadBgra(recovered);
            Check(originalPixels.SequenceEqual(recoveredPixels), "Native clipboard changed image pixels");
            Check(System.Windows.Clipboard.GetFileDropList().Cast<string>().Single() == delivered.Path, "Recovered saved-file path changed");
            using var transparent = CreateFixture();
            var alphaDelivery = new ImageOutputService(settings.ScreenshotDirectory, true).SaveWithDelivery(transparent);
            ownedPath = alphaDelivery.Path;
            var pngStream = System.Windows.Clipboard.GetData(MediaClipboardDataFactory.PngFormat) as MemoryStream
                ?? throw new Exception("Native PNG format missing");
            pngStream.Position = 0;
            var alphaDecoded = new PngBitmapDecoder(pngStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Check(ReadBgra(BitmapSourceFactory.Create(transparent)).SequenceEqual(ReadBgra(alphaDecoded)), "Native PNG lost transparency or colors");
            var paths = RecordingOutputPaths.Create(settings.RecordingDirectory, DateTime.Now);
            CreateVideo(paths);
            var videoItem = new RecentCaptureService(settings).GetRecent().Single(item => item.Path == paths.FinalPath);
            Check(new RecentCaptureService(settings).TryCopy(videoItem), "Native video recovery failed");
            ownedPath = paths.FinalPath;
            Check(System.Windows.Clipboard.GetFileDropList().Cast<string>().Single() == paths.FinalPath, "Native video file list changed");
            var cloud = System.Windows.Clipboard.GetData(MediaClipboardDataFactory.CloudFormat) as MemoryStream;
            Check(cloud is not null && BitConverter.ToInt32(cloud.ToArray()) == 0, "Native cloud exclusion flag missing");
            File.WriteAllText(Path.Combine(root, "native-media-recovery.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                image_restored_after_text = true, same_pixels = true, png_alpha_preserved = true, video_file_restored = true,
                video_duration_ticks = RecordingMediaProbe.TryReadDuration(paths.FinalPath)?.Ticks,
                local_history_requested = true, cloud_upload_requested = false,
                win_v_collection = "not-assessed", message_application_receipt = "not-assessed",
            }));
        }
        finally
        {
            // Do not replace newer clipboard data from the user or another application.
            if (ownedPath is not null && System.Windows.Clipboard.ContainsFileDropList()
                && System.Windows.Clipboard.GetFileDropList().Cast<string>().SequenceEqual(new[] { ownedPath }))
            {
                if (previous is null) System.Windows.Clipboard.Clear();
                else System.Windows.Clipboard.SetDataObject(previous, true);
                restored = true;
            }
            Console.WriteLine($"NATIVE_MEDIA_RECOVERY previous_clipboard_restored={restored}; fixtures_kept=true");
        }
    }

    private static byte[] ReadBgra(BitmapSource source)
    {
        var normalized = new FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        normalized.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }

    public static void Run()
    {
        ClipboardImageFormatTests.Run();
        var root = Path.Combine(Path.GetTempPath(), "ClipRay-media-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = AppSettings.CreateDefault(root);
            OutputDirectoryLayout.Ensure(settings);
            using var fixture = CreateFixture();
            var saved = new ImageOutputService(settings.ScreenshotDirectory, false).SavePng(fixture, new DateTime(2026, 10, 9, 10, 0, 0));
            RecoversExactImageAndFormats(saved);
            RejectsInvalidImagesWithoutChangingInjectedClipboard(root, saved);
            RecoversOnlyOwnedCompletedMedia(settings, saved);
            PreparedCopiesPublishOnCallerWithoutReadingAgain(settings, saved);
            PreparedCopiesRejectChangedAndMissingFiles(settings, saved);
            AsyncPreviewCopyRespectsCallerAndAdmission(saved);
            PreviewCopyDoesNotPublishAfterClose(saved);
            RejectsReparsePoints(root, settings, saved);
            AsyncSavingReturnsToCallerAndPreservesFailures(settings);
            Console.WriteLine("PASS: media payload, replacement/recovery, restart, real MP4, malformed/missing paths, reparse points, worker preparation/UI publication, async preview copy and close admission (no system clipboard or shown windows).");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Bitmap CreateFixture()
    {
        var image = new Bitmap(9, 13);
        image.SetPixel(0, 0, Color.FromArgb(255, 11, 22, 33));
        image.SetPixel(8, 12, Color.FromArgb(255, 44, 55, 66));
        return image;
    }

    private static void RecoversExactImageAndFormats(string path)
    {
        System.Windows.DataObject? current = null;
        Check(SavedMediaClipboard.TryCopyPng(path, data => current = data), "First image copy failed");
        AssertImage(current!, path);
        current = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "replacement");
        Check(SavedMediaClipboard.TryCopyPng(path, data => current = data), "Saved image could not be restored after replacement");
        AssertImage(current!, path);
    }

    private static void AssertImage(System.Windows.DataObject data, string path)
    {
        AssertFile(data, path);
        var bitmap = data.GetImage() ?? throw new Exception("Bitmap representation is absent");
        Check(bitmap.IsFrozen && bitmap.PixelWidth == 9 && bitmap.PixelHeight == 13, "Original image dimensions changed");
        var png = (MemoryStream?)data.GetData(MediaClipboardDataFactory.PngFormat) ?? throw new Exception("PNG representation is absent");
        Check(png.ToArray().SequenceEqual(File.ReadAllBytes(path)), "PNG representation differs from saved file");
        using var copy = new MemoryStream(png.ToArray());
        var decoded = new PngBitmapDecoder(copy, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var pixel = new byte[4];
        var converted = new FormatConvertedBitmap(decoded, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(new Int32Rect(8, 12, 1, 1), pixel, 4, 0);
        Check(pixel.SequenceEqual(new byte[] { 66, 55, 44, 255 }), "Last pixel was lost or changed");
    }

    private static void AssertFile(System.Windows.DataObject data, string path)
    {
        Check(data.GetFileDropList().Cast<string>().Single() == path, "File drop payload changed");
        Check(Dword(data, "Preferred DropEffect") == 1, "Explorer copy effect is absent");
        Check(Dword(data, MediaClipboardDataFactory.HistoryFormat) == 1, "Local history was not requested");
        Check(Dword(data, MediaClipboardDataFactory.CloudFormat) == 0, "Cloud synchronization was not disabled");
    }

    private static int Dword(System.Windows.DataObject data, string format) =>
        BitConverter.ToInt32(((MemoryStream?)data.GetData(format) ?? throw new Exception(format + " is absent")).ToArray());

    private static void RejectsInvalidImagesWithoutChangingInjectedClipboard(string root, string valid)
    {
        var malformed = Path.Combine(root, "malformed.png");
        var truncated = Path.Combine(root, "truncated.png");
        File.WriteAllBytes(malformed, [1, 2, 3]);
        File.WriteAllBytes(truncated, File.ReadAllBytes(valid)[..40]);
        var calls = 0;
        foreach (var path in new[] { malformed, truncated, Path.Combine(root, "missing.png"), "https://example.invalid/image.png", "relative.png", @"\\server\share\image.png" })
            Check(!SavedMediaClipboard.TryCopyPng(path, _ => calls++), "Unsafe or malformed image was accepted: " + path);
        Check(calls == 0, "A rejected image changed clipboard state");
        Check(!SavedMediaClipboard.TryCopyPng(valid, _ => throw new InvalidOperationException("clipboard-busy")), "Publish failure was reported as copied");
        Check(File.Exists(valid), "Publish failure removed the persisted image");
    }

    private static void RecoversOnlyOwnedCompletedMedia(AppSettings settings, string saved)
    {
        var stamp = new DateTime(2026, 10, 9, 10, 1, 0);
        using var image = CreateFixture();
        var longPath = new ImageOutputService(settings.LongCaptureDirectory, false, OutputArtifactKind.LongCapture).SavePng(image, stamp);
        File.Copy(saved, Path.Combine(Path.GetDirectoryName(saved)!, "unrelated.png"));
        File.Copy(saved, Path.Combine(settings.ScreenshotDirectory, "ClipRay-Screenshot_2026-10-09_10-03-00-000.png"));
        var broken = Path.Combine(Path.GetDirectoryName(saved)!, "ClipRay-Screenshot_2026-10-09_10-02-00-000.png");
        File.WriteAllBytes(broken, [1, 2, 3]);
        var nested = Path.Combine(settings.ScreenshotDirectory, "unrelated-folder");
        Directory.CreateDirectory(nested);
        File.Copy(saved, Path.Combine(nested, "ClipRay-Screenshot_2026-10-09_10-04-00-000.png"));

        var paths = RecordingOutputPaths.Create(settings.RecordingDirectory, stamp.AddSeconds(1));
        CreateVideo(paths);
        var partial = Path.Combine(Path.GetDirectoryName(paths.FinalPath)!, "ClipRay-Recording_2026-10-09_10-05-00-000.partial.mp4");
        File.Copy(paths.FinalPath, partial);
        var corruptVideo = Path.Combine(Path.GetDirectoryName(paths.FinalPath)!, "ClipRay-Recording_2026-10-09_10-06-00-000.mp4");
        File.WriteAllBytes(corruptVideo, [1, 2, 3]);
        File.SetLastWriteTimeUtc(longPath, DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(paths.FinalPath, DateTime.UtcNow);

        var service = new RecentCaptureService(settings);
        var items = service.GetRecent();
        Check(items.Count == 4, "History included unrelated, broken or partial media, or lost valid output");
        Check(items.Any(x => x.Kind == RecentCaptureKind.LongCapture && x.Path == longPath), "Long capture was not recovered");
        Check(items[0].Path == paths.FinalPath, "Results were not sorted newest first");
        Check(service.GetRecent(1).Single().Path == paths.FinalPath, "Bounded recent query failed");
        Check(new RecentCaptureService(settings).GetRecent().Select(x => x.Path).SequenceEqual(items.Select(x => x.Path)), "New service did not recover disk outputs");
        var video = items.Single(x => x.Kind == RecentCaptureKind.Recording);
        System.Windows.DataObject? copied = null;
        Check(service.TryCopy(video, data => copied = data), "Completed real MP4 was not copied");
        AssertFile(copied!, video.Path);
        Check(!copied!.ContainsImage(), "Video copy invented an image representation");
        var outside = new RecentCaptureItem(Path.Combine(nested, "ClipRay-Screenshot_2026-10-09_10-04-00-000.png"), RecentCaptureKind.Screenshot, DateTime.UtcNow, 1);
        Check(!service.TryCopy(outside, _ => throw new Exception("Out-of-root copy invoked publisher")), "Out-of-root entry was accepted");
        File.Delete(longPath);
        Check(!service.TryCopy(items.Single(x => x.Path == longPath), _ => throw new Exception("Missing copy invoked publisher")), "Stale deleted entry was accepted");
        Check(File.Exists(saved) && File.Exists(paths.FinalPath) && File.Exists(broken), "Listing or copy removed files");
    }

    private static void CreateVideo(RecordingOutputPaths paths)
    {
        var recorder = new MediaFoundationRecorder(paths, 32, 32, new AudioCaptureFormat(48_000, 2, 32, true));
        try
        {
            recorder.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            var pixels = new byte[32 * 32 * 4];
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var duration = RecordingFrameRatePolicy.SampleDuration100Nanoseconds;
            for (var i = 0; i < 3; i++)
                recorder.WriteVideoAsync(new VideoRecordingSample(pixels, 32, 32, i * duration, duration)).AsTask().GetAwaiter().GetResult();
            recorder.StopAsync().GetAwaiter().GetResult();
        }
        finally { recorder.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        Check(RecordingMediaProbe.TryReadDuration(paths.FinalPath) is { Ticks: > 0 }, "Synthetic MP4 did not finalize correctly");
    }

    private static void PreparedCopiesPublishOnCallerWithoutReadingAgain(AppSettings settings, string saved)
    {
        var service = new RecentCaptureService(settings);
        var items = service.GetRecent();
        var owner = Environment.CurrentManagedThreadId;
        var previous = SynchronizationContext.Current;
        using var context = new PumpingContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            foreach (var item in new[] { items.Single(x => x.Path == saved), items.Single(x => x.Kind == RecentCaptureKind.Recording) })
            {
                var worker = 0;
                var published = 0;
                var prepared = context.Complete(Task.Run(() =>
                {
                    worker = Environment.CurrentManagedThreadId;
                    Check(service.TryPrepareCopy(item, out var payload), "Worker preparation failed");
                    return payload!;
                }));
                Check(worker != owner && published == 0, "Preparation ran on UI or published the clipboard");
                Check(prepared.Image is null || prepared.Image.IsFrozen, "Worker image was not frozen");
                // Deny every media read during publication. Metadata remains available.
                // This catches a second decoder/probe rather than merely timing the copy.
                using var locked = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.None);
                Check(service.TryPublishCopy(prepared, data =>
                {
                    published++;
                    Check(Environment.CurrentManagedThreadId == owner, "Publication left the caller UI context");
                    AssertFile(data, item.Path);
                    if (prepared.Image is not null)
                    {
                        Check(ReferenceEquals(prepared.Image, data.GetImage()), "Publication decoded a new image");
                        Check(prepared.Image.PixelWidth == 9 && prepared.Image.PixelHeight == 13, "Worker image dimensions changed");
                        Check(data.GetData(MediaClipboardDataFactory.PngFormat) is MemoryStream { Length: > 0 }, "Prepared PNG bytes are absent");
                    }
                    else Check(!data.ContainsImage(), "Prepared video invented an image");
                }), "Publication tried to read/decode the locked media again");
                Check(published == 1, "Prepared payload was not published exactly once");
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void PreparedCopiesRejectChangedAndMissingFiles(AppSettings settings, string saved)
    {
        var service = new RecentCaptureService(settings);
        var folder = Path.GetDirectoryName(saved)!;
        var path = Path.Combine(folder, "ClipRay-Screenshot_2026-10-09_10-07-00-000.png");
        File.Copy(saved, path);
        var info = new FileInfo(path);
        var item = new RecentCaptureItem(path, RecentCaptureKind.Screenshot, info.LastWriteTimeUtc, info.Length);
        Check(service.TryPrepareCopy(item, out var prepared), "Valid file was not prepared");
        File.SetLastWriteTimeUtc(path, info.LastWriteTimeUtc.AddSeconds(5));
        var calls = 0;
        Check(!service.TryPublishCopy(prepared!, _ => calls++), "Changed file version was published");
        Check(service.TryPrepareCopy(item, out prepared), "Changed valid file could not be prepared again");
        File.Delete(path);
        Check(!service.TryPublishCopy(prepared!, _ => calls++), "Missing prepared file was published");
        Check(!service.TryPrepareCopy(item, out _), "Missing file was prepared");
        File.WriteAllBytes(path, [1, 2, 3]);
        Check(!service.TryPrepareCopy(item, out _), "Corrupt PNG was prepared");
        Check(service.TryPrepareCopy(new RecentCaptureItem(saved, RecentCaptureKind.Screenshot, DateTime.UtcNow, 1), out prepared), "Original image could not be prepared");
        var otherSettings = settings with { ScreenshotDirectory = settings.RecordingDirectory, LongCaptureDirectory = settings.RecordingDirectory };
        Check(!new RecentCaptureService(otherSettings).TryPublishCopy(prepared!, _ => calls++), "Payload from another output root was published");
        Check(calls == 0 && File.Exists(saved) && File.Exists(path), "Rejected publication changed clipboard or removed output files");
    }

    private static void AsyncPreviewCopyRespectsCallerAndAdmission(string saved)
    {
        var previous = SynchronizationContext.Current;
        using var context = new PumpingContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var owner = Environment.CurrentManagedThreadId;
            var calls = 0;
            Check(context.Complete(SavedMediaClipboard.TryCopyPngAsync(saved, () => true, data =>
            {
                calls++;
                Check(Environment.CurrentManagedThreadId == owner, "Async preview copy left its UI caller");
                AssertImage(data, saved);
                Check(data.GetData(System.Windows.DataFormats.Dib, false) is MemoryStream { Length: > 40 }, "Async copy lost native DIB");
            })), "Async preview copy failed");
            Check(calls == 1, "Async preview copied more than once");
            var task = SavedMediaClipboard.TryCopyPngAsync(saved, () => false, _ => calls++);
            Check(!context.Complete(task) && calls == 1, "Closed application/preview published late clipboard data");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void PreviewCopyDoesNotPublishAfterClose(string saved)
    {
        _ = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var previous = SynchronizationContext.Current;
        using var context = new PumpingContext();
        SynchronizationContext.SetSynchronizationContext(context);
        using var bitmap = CreateFixture();
        var held = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var published = 0;
        var window = new ScrollCaptureResultWindow(bitmap, ScrollCaptureStopReason.CompletedByUser, saved, false,
            copyAgainAsync: async canPublish =>
            {
                calls++;
                await held.Task;
                if (!canPublish()) return false;
                published++;
                return true;
            });
        try
        {
            var copy = typeof(ScrollCaptureResultWindow).GetMethod("CopyAgain", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            copy.Invoke(window, null);
            copy.Invoke(window, null);
            Check(calls == 1 && !window.LastCopyOperation.IsCompleted, "Preview started duplicate copy preparation");
            window.Close();
            held.SetResult(true);
            context.Complete(Finish());
            Check(published == 0 && !window.IsVisible, "Closed preview published a late copy or reopened");
            async Task<bool> Finish() { await window.LastCopyOperation; return true; }
        }
        finally { held.TrySetResult(false); window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void RejectsReparsePoints(string root, AppSettings settings, string saved)
    {
        var link = Path.Combine(settings.ScreenshotDirectory, "2026-10-10");
        var outside = Path.Combine(root, "linked-output");
        Directory.CreateDirectory(outside);
        var name = "ClipRay-Screenshot_2026-10-10_10-00-00-000.png";
        File.Copy(saved, Path.Combine(outside, name));
        // NTFS directory junctions exercise the same reparse guard without requiring
        // the machine's symbolic-link privilege or a visible helper window.
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(outside);
        using (var process = Process.Start(start) ?? throw new Exception("Junction helper did not start"))
        {
            process.WaitForExit();
            Check(process.ExitCode == 0 && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Junction fixture did not materialize");
        }
        try
        {
            Check(!SavedMediaClipboard.TryCopyPng(Path.Combine(link, name), _ => throw new Exception("Link copy invoked publisher")), "Reparse ancestor was followed");
            Check(new RecentCaptureService(settings).GetRecent().All(x => !x.Path.StartsWith(link, StringComparison.OrdinalIgnoreCase)), "History followed a reparse directory");
        }
        finally { Directory.Delete(link); }
    }

    private static void AsyncSavingReturnsToCallerAndPreservesFailures(AppSettings settings)
    {
        var previous = SynchronizationContext.Current;
        using var context = new PumpingContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var ownerThread = Environment.CurrentManagedThreadId;
            var copiedThread = 0;
            var sourceFrozen = false;
            using var image = CreateFixture();
            var output = new ImageOutputService(settings.LongCaptureDirectory, true, OutputArtifactKind.LongCapture,
                source => { copiedThread = Environment.CurrentManagedThreadId; sourceFrozen = source.IsFrozen; });
            var diagnostics = new LongCaptureRunDiagnostics("media-recovery-test", "synthetic-test");
            var pending = output.SaveWithDeliveryAsync(image, diagnostics);
            var result = context.Complete(pending);
            Check(result.ClipboardCopied && copiedThread == ownerThread && sourceFrozen, "Async save did not publish a frozen source on the caller context");
            Check(diagnostics.FinalPngSize is { Width: 9, Height: 13 } && File.Exists(result.Path), "Async diagnostics did not match persisted pixels");
            var failed = context.Complete(new ImageOutputService(settings.ScreenshotDirectory, true, clipboard: _ => throw new InvalidOperationException()).SaveWithDeliveryAsync(image));
            Check(!failed.ClipboardCopied && File.Exists(failed.Path), "Async publish failure lost a successful save");
            var disabled = context.Complete(new ImageOutputService(settings.ScreenshotDirectory, false, clipboard: _ => throw new Exception("Disabled copy ran")).SaveWithDeliveryAsync(image));
            Check(!disabled.ClipboardRequested && !disabled.ClipboardCopied && File.Exists(disabled.Path), "Disabled async copy changed behavior");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private sealed class PumpingContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Action, object? State)> _queue = new();
        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
        internal T Complete<T>(Task<T> task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var next, 100)) next.Action(next.State);
            if (!task.IsCompleted) throw new TimeoutException("Async delivery did not return to the caller");
            return task.GetAwaiter().GetResult();
        }
        public void Dispose() => _queue.Dispose();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

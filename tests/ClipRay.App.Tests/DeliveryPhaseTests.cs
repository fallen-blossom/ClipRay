using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClipRay.App.Capture;
using ClipRay.App.Editing;
using ClipRay.App.Overlay;
using ClipRay.App.Scrolling;
using ClipRay.App.Recording;
using ClipRay.Core.Geometry;

internal static class DeliveryPhaseTests
{
    internal static int Run()
    {
        var previous = SynchronizationContext.Current;
        var autoInstall = System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall;
        System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = false;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var application = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources["AppFont"] = new System.Windows.Media.FontFamily("Segoe UI");
            using var temp = new BackgroundTask6Tests.Temp();
            using var bitmap = new Bitmap(24, 24);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.Coral);
            foreach (var mode in new[] { "set-failed", "flush-failed", "persisted" })
            {
                var writes = 0;
                var output = new ImageOutputService(temp.Root, true, clipboard: _ =>
                {
                    writes++;
                    if (mode == "set-failed") throw new IOException("synthetic-set");
                    if (mode == "flush-failed") throw new ClipboardPersistenceException(new IOException("synthetic-flush"));
                });
                var result = output.SaveWithDelivery(bitmap);
                Check(File.Exists(result.Path), "Clipboard failure lost committed PNG");
                Check(writes == 1, "Persistence recovery repeated Set");
                Check(result.ClipboardCopied == (mode != "set-failed"), "Published clipboard was reported as not copied");
                Check(result.ClipboardState == (mode == "set-failed" ? ClipboardPublicationState.NotPublished : mode == "flush-failed" ? ClipboardPublicationState.Published : ClipboardPublicationState.Persisted), "Delivery phase was lost");
                Check(SavedMediaClipboard.TryPreparePng(result.Path, out var prepared), "Saved PNG could not be prepared");
                writes = 0;
                var copy = SavedMediaClipboard.PublishWithOutcome(prepared!, _ =>
                {
                    writes++;
                    if (mode == "set-failed") throw new IOException("synthetic-set");
                    if (mode == "flush-failed") throw new ClipboardPersistenceException(new IOException("synthetic-flush"));
                });
                Check(copy.State == result.ClipboardState && writes == 1, "Saved-media copy discarded the publication phase or retried Set");
                if (mode == "flush-failed")
                    Check(SavedMediaClipboard.TryPublish(prepared!, _ => throw new ClipboardPersistenceException(new IOException("synthetic-flush"))), "Legacy bool copy API reported a published clipboard as failed");
                var status = ScrollCaptureResultWindow.SavedStatus("en", true, result.ClipboardCopied, result.ClipboardPersistenceConfirmed);
                Check(mode != "flush-failed" || (status.Contains("copied") && status.Contains("unconfirmed") && !status.Contains("not copied")), "Result window lied about a published clipboard");
            }
            var video = Path.Combine(temp.Root, "synthetic.mp4");
            File.WriteAllBytes(video, [1, 2, 3]);
            foreach (var state in new[] { ClipboardPublicationState.NotPublished, ClipboardPublicationState.Published, ClipboardPublicationState.Persisted })
            {
                var clipboard = new RecordingClipboard(state);
                var delivered = RecordingResultDelivery.CopyFileWithOutcome(video, clipboard);
                Check(delivered.State == state && clipboard.Calls == 1 && File.Exists(video), "Recording delivery lost the phase, retried Set, or removed the file");
            }
            RecordingFailureRecoveryIsReachable(temp.Root);
            RenderFailureKeepsCanvas();
            Console.WriteLine("PASS delivery-phases/publication-render-recovery");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL delivery-phases: " + error); return 1; }
        finally { SynchronizationContext.SetSynchronizationContext(previous); System.Windows.Forms.WindowsFormsSynchronizationContext.AutoInstall = autoInstall; }
    }

    private static void RecordingFailureRecoveryIsReachable(string root)
    {
        var clipboard = new RecordingClipboard(ClipboardPublicationState.NotPublished);
        var playback = new NoRecordingPlayback();
        string? copied = null, opened = null;
        var failCopy = false;
        var failOpen = false;
        var path = Path.Combine(root, "not-finalized.mp4");
        var message = "Finalization failed. Partial path: " + Path.Combine(root, new string('x', 120) + ".partial.mp4");
        var preview = new RecordingPreviewWindow(path, clipboard, false, true, "en", playback,
            copyRecoveryPath: folder => { if (failCopy) throw new IOException("synthetic-copy"); copied = folder; },
            openRecoveryFolder: folder => { if (failOpen) throw new IOException("synthetic-open"); opened = folder; });
        try
        {
            var panel = BackgroundTask3LifetimeTests.Field<StackPanel>(preview, "_failurePanel");
            var details = BackgroundTask3LifetimeTests.Field<System.Windows.Controls.TextBox>(preview, "_failureDetails");
            var copy = BackgroundTask3LifetimeTests.Field<System.Windows.Controls.Button>(preview, "_copyFailureFolder");
            var open = BackgroundTask3LifetimeTests.Field<System.Windows.Controls.Button>(preview, "_openFailureFolder");
            var transport = BackgroundTask3LifetimeTests.Field<StackPanel>(preview, "_transportActions");
            Check(panel.Visibility == Visibility.Collapsed, "Failure actions appeared during normal pending delivery");
            preview.Fail(message);
            Check(panel.Visibility == Visibility.Visible && details.IsReadOnly && details.IsEnabled
                && details.Text.Contains(message, StringComparison.Ordinal) && details.Text.Contains(root, StringComparison.Ordinal)
                && copy.IsEnabled && open.IsEnabled && !preview.IsPending,
                "Failed pending recording hid or truncated its recoverable error/path/actions");
            copy.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            open.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(copied == root && opened == root && clipboard.Calls == 0 && playback.Plays == 0
                && transport.Visibility == Visibility.Collapsed && !transport.IsEnabled && !preview.SeekEnabled,
                "Failure recovery exposed playback/file-copy or used a different output folder");
            failCopy = true; failOpen = true;
            copy.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(preview.Status.Contains("Could not copy", StringComparison.Ordinal), "Recovery path copy failure escaped or lacked retry feedback");
            open.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(preview.Status.Contains("Could not open", StringComparison.Ordinal) && details.Text.Contains(message, StringComparison.Ordinal),
                "Recovery folder failure lost the original full error/path");
        }
        finally { preview.Close(); }
    }

    private static void RenderFailureKeepsCanvas()
    {
        using var frame = new CapturedFrame(new Bitmap(24, 24), new(0, 0, 24, 24));
        var overlay = new SelectionOverlayWindow(frame, new PixelRect(0, 0, 24, 24), clipboardTextSink: new NoClipboard(),
            windowCandidateSource: new NoWindows(), uiElementCandidateSource: new NoElements());
        try
        {
            // Break the actual crop/render input after the overlay has its visual.
            // This exercises the render catch, before any delivery delegate runs.
            frame.Bitmap.Dispose();
            var delivered = 0;
            overlay.CaptureDeliveryAsync = _ => { delivered++; return Task.FromResult(true); };
            var operation = (Task)typeof(SelectionOverlayWindow).GetMethod("ConfirmSelectionCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, null)!;
            var confirm = (System.Windows.Controls.Button)overlay.FindName("ConfirmToolButton");
            Check(!confirm.IsEnabled && confirm.Content is System.Windows.Controls.ProgressBar { IsIndeterminate: true }, "Save pending was invisible before rendering");
            Pump(operation);
            Check(delivered == 0 && confirm.IsEnabled && !overlay.LifetimeToken.IsCancellationRequested, "Render failure delivered, closed edits, or left retry disabled");
            var status = (TextBlock)overlay.FindName("ToolStatusText");
            Check(status.Text.Contains("保留") || status.Text.Contains("kept"), "Render failure provided no recovery feedback");
        }
        finally { overlay.Close(); }
    }

    private static void Pump(Task operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!operation.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Delivery operation did not complete");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        operation.GetAwaiter().GetResult();
    }
    private sealed class NoRecordingPlayback : IRecordingPreviewPlayback
    {
        public TimeSpan Position { get; set; }
        public TimeSpan NaturalDuration => TimeSpan.Zero;
        internal int Plays;
        public void Play() => Plays++;
        public void Pause() { }
        public void Close() { }
    }
    private sealed class RecordingClipboard(ClipboardPublicationState state) : IRecordingFileClipboard
    {
        internal int Calls;
        public void SetFile(string path)
        {
            Calls++;
            if (state == ClipboardPublicationState.NotPublished) throw new IOException("synthetic-set");
            if (state == ClipboardPublicationState.Published) throw new ClipboardPersistenceException(new IOException("synthetic-flush"));
        }
    }
    private sealed class NoWindows : IWindowCandidateSource { public IEnumerable<WindowSnapCandidate> Enumerate() => []; }
    private sealed class NoElements : IUiElementCandidateSource { public IReadOnlyList<PixelRect> GetHierarchy(System.Drawing.Point point, IntPtr root) => []; }
    private sealed class NoClipboard : IClipboardTextSink { public void SetText(string text) => throw new Exception("Real clipboard is forbidden"); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

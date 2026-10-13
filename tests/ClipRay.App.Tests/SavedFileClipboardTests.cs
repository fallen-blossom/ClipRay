using System.Drawing;
using System.IO;
using ClipRay.App.Capture;
using ClipRay.App.Recording;
using Clipboard = System.Windows.Clipboard;

internal static class SavedFileClipboardTests
{
    public static void Run()
    {
        try { RunCore(); }
        catch (Exception exception)
        {
            // Do not print exception messages or clipboard data; retain the failing call site.
            Console.Error.WriteLine($"SAVED_FILE_CLIPBOARD_FAILURE: {exception.GetType().Name} HResult={exception.HResult:X8}");
            Console.Error.WriteLine(exception.StackTrace);
            throw;
        }
    }

    private static void RunCore()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClipRay 剪贴板文件 " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var previous = Clipboard.GetDataObject();
        try
        {
            foreach (var kind in new[] { OutputArtifactKind.Screenshot, OutputArtifactKind.LongCapture })
            {
                using var bitmap = new Bitmap(8, kind == OutputArtifactKind.LongCapture ? 80 : 8);
                var output = new ImageOutputService(root, true, kind);
                var delivery = output.SaveWithDelivery(bitmap);
                Check(delivery.ClipboardCopied, "Clipboard delivery failed");
                Check(Clipboard.ContainsFileDropList(), kind + " must provide a saved file");
                Check(Clipboard.GetFileDropList().Cast<string>().Single() == delivery.Path, "Wrong saved path");
                Check(Clipboard.ContainsImage(), "Image paste must remain supported");
                var image = Clipboard.GetImage() ?? throw new InvalidOperationException("Pasted image missing");
                Check(image.PixelHeight == bitmap.Height, "Pasted image dimensions changed");
                var effect = Clipboard.GetData("Preferred DropEffect") as MemoryStream
                    ?? throw new InvalidOperationException("Copy effect missing");
                var effectBytes = effect.ToArray();
                var effectValue = effectBytes.Length >= sizeof(int) ? BitConverter.ToInt32(effectBytes) : -1;
                Check(effectValue == 1,
                    $"Explorer must copy rather than move (value={effectValue}, bytes={Convert.ToHexString(effectBytes)})");
                var destination = Path.Combine(root, "pasted-" + kind + ".png");
                File.Copy(Clipboard.GetFileDropList()[0]!, destination);
                Check(File.ReadAllBytes(delivery.Path).SequenceEqual(File.ReadAllBytes(destination)), "File content changed");
            }
            Clipboard.SetText("keep-existing-clipboard");
            using (var bitmap = new Bitmap(8, 8))
            {
                new ImageOutputService(root, false).SaveWithDelivery(bitmap);
                Check(Clipboard.GetText() == "keep-existing-clipboard", "Disabled copy changed clipboard");
                var failed = new ImageOutputService(root, true, clipboard: _ => throw new InvalidOperationException()).SaveWithDelivery(bitmap);
                Check(File.Exists(failed.Path) && !failed.ClipboardCopied, "Clipboard failure lost saved file");
                var blocked = Path.Combine(root, "blocked");
                File.WriteAllText(blocked, "not a directory");
                try { new ImageOutputService(blocked, true).SaveWithDelivery(bitmap); throw new Exception("Save should fail"); }
                catch (IOException) { }
                Check(Clipboard.GetText() == "keep-existing-clipboard", "Failed save changed clipboard");
            }
            var video = Path.Combine(root, "recording.mp4");
            File.WriteAllBytes(video, new byte[] { 1, 2, 3 });
            Check(RecordingResultDelivery.TryCopyFile(video, new WindowsRecordingFileClipboard()), "Video file delivery failed");
            Check(Clipboard.GetFileDropList().Cast<string>().Single() == video, "Wrong video path");
            Check(!RecordingResultDelivery.TryCopyFile(Path.Combine(root, "missing.mp4"), new WindowsRecordingFileClipboard()), "Missing file copied");
            Check(Clipboard.GetFileDropList().Cast<string>().Single() == video, "Missing video changed clipboard");
        }
        finally
        {
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (previous is not null) Clipboard.SetDataObject(previous, true);
                        else Clipboard.Clear();
                        break;
                    }
                    catch (System.Runtime.InteropServices.COMException) when (attempt < 4)
                    {
                        Thread.Sleep(40 * (attempt + 1));
                    }
                }
            }
            finally { Directory.Delete(root, true); }
        }
        Console.WriteLine("PASS: screenshot, long capture, video, image compatibility, disabled copy and failure paths");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            Console.Error.WriteLine("SAVED_FILE_CLIPBOARD_ASSERTION: " + message);
            throw new InvalidOperationException(message);
        }
    }
}

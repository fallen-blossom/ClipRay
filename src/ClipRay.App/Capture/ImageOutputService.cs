using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using ClipRay.App.Infrastructure;
using ClipRay.App.Scrolling;

namespace ClipRay.App.Capture;

public enum OutputArtifactKind
{
    Screenshot,
    LongCapture,
}

public sealed record ImageDeliveryOutcome(string Path, bool ClipboardRequested, bool ClipboardCopied);

public sealed class ImageOutputService
{
    private readonly string _outputRoot;
    private readonly OutputArtifactKind _kind;
    private readonly bool _copyToClipboard;
    private readonly Action<System.Windows.Media.Imaging.BitmapSource>? _clipboard;

    public ImageOutputService()
        : this(WorkspaceLocator.Find(AppContext.BaseDirectory))
    {
    }

    public ImageOutputService(string workspaceRoot)
    {
        _outputRoot = Path.Combine(workspaceRoot, "artifacts", "screenshots");
        _kind = OutputArtifactKind.Screenshot;
        _copyToClipboard = true;
    }

    public ImageOutputService(
        string outputRoot,
        bool copyToClipboard,
        OutputArtifactKind kind = OutputArtifactKind.Screenshot,
        Action<System.Windows.Media.Imaging.BitmapSource>? clipboard = null)
    {
        _outputRoot = Path.GetFullPath(outputRoot);
        _kind = kind;
        _copyToClipboard = copyToClipboard;
        _clipboard = clipboard;
    }

    public string SavePng(Bitmap bitmap, DateTime timestamp)
    {
        var folder = Path.Combine(_outputRoot, timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);

        var label = _kind == OutputArtifactKind.LongCapture ? "LongCapture" : "Screenshot";
        var fileName = $"ClipRay-{label}_{timestamp:yyyy-MM-dd_HH-mm-ss-fff}";
        for (var suffix = 0; ; suffix++)
        {
            var suffixText = suffix == 0 ? string.Empty : $"_{suffix + 1}";
            var path = Path.Combine(folder, $"{fileName}{suffixText}.png");
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(path))
            {
                continue;
            }

            try
            {
                using (stream)
                {
                    bitmap.Save(stream, ImageFormat.Png);
                }

                return path;
            }
            catch
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // 不能因清理失败掩盖 bitmap encoder 的原始异常。
                }

                throw;
            }
        }
    }

    public string SaveAndCopy(Bitmap bitmap, LongCaptureRunDiagnostics? diagnostics = null)
    {
        var delivered = SaveWithDelivery(bitmap, diagnostics);
        if (delivered.ClipboardRequested && !delivered.ClipboardCopied)
            throw new InvalidOperationException("image-saved-clipboard-unavailable");
        return delivered.Path;
    }

    public ImageDeliveryOutcome SaveWithDelivery(Bitmap bitmap, LongCaptureRunDiagnostics? diagnostics = null)
    {
        diagnostics?.RecordSaveInput(bitmap.Size);
        var path = SavePng(bitmap, DateTime.Now);
        if (diagnostics is not null)
        {
            using var saved = new Bitmap(path);
            diagnostics.RecordSaved(path, bitmap.Size, saved.Size);
        }
        var copied = false;
        if (_copyToClipboard)
        {
            try
            {
                var source = BitmapSourceFactory.Create(bitmap);
                if (_clipboard is not null) _clipboard(source);
                else CopyWithRetry(source, path);
                copied = true;
            }
            catch { /* Saving already succeeded; do not misreport it as lost. */ }
        }
        return new(path, _copyToClipboard, copied);
    }

    public bool TryCopySavedFile(Bitmap bitmap, string path)
    {
        try
        {
            CopyWithRetry(BitmapSourceFactory.Create(bitmap), path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyWithRetry(System.Windows.Media.Imaging.BitmapSource source, string path)
    {
        // Publish both representations in one clipboard update: Explorer receives
        // the saved PNG, while image consumers can still paste the bitmap.
        var data = new System.Windows.DataObject();
        data.SetImage(source);
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { Path.GetFullPath(path) });
        using var dropEffect = new MemoryStream(BitConverter.GetBytes(1)); // DROPEFFECT_COPY
        data.SetData("Preferred DropEffect", dropEffect);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 3)
            {
                Thread.Sleep(40 * (attempt + 1));
            }
        }
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows;
using ClipRay.App.Infrastructure;
using ClipRay.App.Scrolling;
using ClipRay.App.Cleanup;

namespace ClipRay.App.Capture;

public enum OutputArtifactKind
{
    Screenshot,
    LongCapture,
}

public sealed record ImageDeliveryOutcome(string Path, bool ClipboardRequested, bool ClipboardCopied,
    string? DiagnosticFailure = null, string? ClipboardFailure = null);

public sealed class ImageOutputService
{
    private readonly string _outputRoot;
    private readonly OutputArtifactKind _kind;
    private readonly bool _copyToClipboard;
    private readonly Action<System.Windows.Media.Imaging.BitmapSource>? _clipboard;
    internal Action<string, Bitmap, LongCaptureRunDiagnostics>? SavedDiagnosticOverride { get; set; }
    internal Action<Bitmap>? BeforeSaveOverride { get; set; }
    internal Func<string, (System.Windows.Media.Imaging.BitmapSource? Source, byte[]? Png)>? ClipboardPreparationOverride { get; set; }
    internal CompletedOutputStore CompletedOutputs { get; set; } = new();

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

    public string SavePng(Bitmap bitmap, DateTime timestamp, LongCaptureRunDiagnostics? diagnostics = null)
        => SaveCommitted(bitmap, timestamp, diagnostics).Path;

    private SavedImage SaveCommitted(Bitmap bitmap, DateTime timestamp, LongCaptureRunDiagnostics? diagnostics)
    {
        BeforeSaveOverride?.Invoke(bitmap);
        string? diagnosticFailure = null;
        try { diagnostics?.RecordSaveInput(bitmap.Size); }
        catch { diagnosticFailure = "diagnostic-unavailable"; }
        var folder = Path.Combine(_outputRoot, timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        using var outputLease = CompletedOutputStore.EnterDirectory(folder);

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
            if (!CompletedOutputs.TryRecordSaved(path, CleanupFileKind.CompletedImage))
                diagnosticFailure = "completed-output-receipt-unavailable";
            if (diagnostics is not null)
            {
                try
                {
                    if (SavedDiagnosticOverride is {} observe) observe(path, bitmap, diagnostics);
                    else
                    {
                        using var saved = new Bitmap(path);
                        diagnostics.RecordSaved(path, bitmap.Size, saved.Size);
                    }
                }
                catch { diagnosticFailure = "diagnostic-readback-unavailable"; }
            }
            return new(path, diagnosticFailure);
        }
    }

    public string SaveAndCopy(Bitmap bitmap, LongCaptureRunDiagnostics? diagnostics = null)
    {
        var delivered = SaveWithDelivery(bitmap, diagnostics);
        return delivered.Path;
    }

    public ImageDeliveryOutcome SaveWithDelivery(Bitmap bitmap, LongCaptureRunDiagnostics? diagnostics = null)
    {
        return Deliver(Prepare(SaveCommitted(bitmap, DateTime.Now, diagnostics)));
    }

    public async Task<ImageDeliveryOutcome> SaveWithDeliveryAsync(Bitmap bitmap, LongCaptureRunDiagnostics? diagnostics = null, Func<bool>? canPublish = null)
    {
        // Encoder and image conversion work belong off the dispatcher. Only the frozen
        // payload crosses back to the caller's context; clipboard publication stays there.
        var prepared = await Task.Run(() => Prepare(SaveCommitted(bitmap, DateTime.Now, diagnostics)));
        return Deliver(prepared,canPublish);
    }

    private PreparedImage Prepare(SavedImage saved)
    {
        if (!_copyToClipboard) return new(saved, null, null, null, null);
        // Prepare failure must not turn a successfully saved image into a save failure.
        try
        {
            if (ClipboardPreparationOverride is {} prepare)
            {
                var result = prepare(saved.Path);
                return new(saved, result.Source, result.Png, null, result.Source is null ? "clipboard-preparation-unavailable" : null);
            }
            SavedMediaFile.TryReadPng(saved.Path, out var source, out var png);
            var dib = source is null ? null : MediaClipboardDataFactory.EncodeDib(source);
            return new(saved, source, png, dib, source is null ? "clipboard-preparation-unavailable" : null);
        }
        catch { return new(saved, null, null, null, "clipboard-preparation-unavailable"); }
    }

    private ImageDeliveryOutcome Deliver(PreparedImage prepared,Func<bool>? canPublish = null)
    {
        var copied = false;
        var clipboardFailure = prepared.Failure;
        if (_copyToClipboard && canPublish is not null)
        {
            try { if (!canPublish()) return new(prepared.Saved.Path,true,false,prepared.Saved.DiagnosticFailure,"application-exiting"); }
            catch { return new(prepared.Saved.Path,true,false,prepared.Saved.DiagnosticFailure,"clipboard-admission-unavailable"); }
        }
        if (_copyToClipboard && prepared.Source is { } source)
        {
            try
            {
                if (_clipboard is not null) _clipboard(source);
                else MediaClipboardPublisher.Publish(MediaClipboardDataFactory.CreateImage(source, prepared.Saved.Path, prepared.Png, prepared.Dib));
                copied = true;
            }
            catch { clipboardFailure = "clipboard-unavailable"; }
        }
        return new(prepared.Saved.Path, _copyToClipboard, copied, prepared.Saved.DiagnosticFailure, clipboardFailure);
    }

    public bool TryCopySavedFile(Bitmap bitmap, string path)
    {
        // Kept for existing preview callbacks; the persisted file owns the later copy.
        return TryCopySavedFile(path);
    }

    public bool TryCopySavedFile(string path, Action<System.Windows.DataObject>? publish = null) =>
        SavedMediaClipboard.TryCopyPng(path, publish);

    private sealed record SavedImage(string Path, string? DiagnosticFailure);
    private sealed record PreparedImage(SavedImage Saved, System.Windows.Media.Imaging.BitmapSource? Source, byte[]? Png, byte[]? Dib, string? Failure);
}

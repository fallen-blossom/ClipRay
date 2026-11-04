using System.Drawing;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Scrolling;
using ClipRay.App.Settings;

namespace ClipRay.App;

internal interface ILongCaptureRecoveryPresentation
{
    Task<bool> ConfirmSaveAsync(ScrollCaptureRecoveryCandidate candidate, BitmapSource preview, string language);
    void ShowSaved(Bitmap bitmap, BitmapSource preview, ImageDeliveryOutcome saved, ScrollCaptureStopReason reason, string language, Func<bool> copy);
}
internal sealed record LongCaptureRecoveryDelivery(ImageDeliveryOutcome? Saved = null, bool Cancelled = false,
    bool Blocked = false, string? Failure = null, string? PreviewFailure = null, string? CleanupFailure = null);

public partial class App
{
    private bool _longCaptureRecoveryBusy;
    internal ILongCaptureRecoveryPresentation? RecoveryPresentationOverride { get; set; }
    internal Action<string>? RecoveryMarkDeliveredOverride { get; set; }
    internal Task? LastRecoveryOperation { get; private set; }
    internal Task? LastRecoveryLoad { get; private set; }
    internal Func<Func<Bitmap>,Task<Bitmap>>? RecoveryCompositeOverride { get; set; }

    internal async Task<LongCaptureRecoveryDelivery> RecoverLongCaptureAsync(string directory)
    {
        if (!CanDeliverImageOutput || _longCaptureCancellation is not null || _longCaptureRecoveryBusy) return new(Blocked:true);
        _longCaptureRecoveryBusy = true;
        var settings = _settings!;
        var output = _longCaptureOutputService!;
        var presentation = RecoveryPresentationOverride ?? new RecoveryPresentation(this);
        ImageDeliveryOutcome? saved = null;
        string? cleanupFailure = null;
        string? previewFailure = null;
        ScrollCaptureProcessingWindow? progress = null;
        IDisposable? physical=EnterImageOutput();
        if (physical is null) { _longCaptureRecoveryBusy=false;return new(Blocked:true); }
        try
        {
            if (RecoveryPresentationOverride is null) progress = new ScrollCaptureProcessingWindow(settings.InterfaceLanguage);
            progress?.Show();
            progress?.SetStage("正在验证并合成恢复片段…", "Validating and composing recovery pieces…");
            var recovery = new ScrollCaptureRecovery(_paths!.LongCaptureTempRoot);
            var manifest = await Task.Run(() => recovery.ReadValidated(directory));
            using var bitmap = await (RecoveryCompositeOverride is {} compose ? compose(() => recovery.CreateComposite(directory)) : Task.Run(() => recovery.CreateComposite(directory)));
            progress?.SetStage("正在准备恢复预览…", "Preparing recovery preview…");
            var preview = await Task.Run(() => BitmapSourceFactory.Create(bitmap));
            physical.Dispose();physical=null;
            if (!CanDeliverImageOutput) return new(Cancelled:true);
            var candidate = new ScrollCaptureRecoveryCandidate(directory, manifest.CreatedUtc, bitmap.Width, bitmap.Height, manifest.Segments.Count,true,"recoverable");
            progress?.Hide();
            if (!await presentation.ConfirmSaveAsync(candidate,preview,settings.InterfaceLanguage) || !CanDeliverImageOutput) return new(Cancelled:true);
            physical=EnterImageOutput();
            if (physical is null) return new(Blocked:true);
            progress?.SetStage("正在保存 PNG 并准备复制…", "Saving PNG and preparing clipboard…");
            progress?.Show();
            saved = await output.SaveWithDeliveryAsync(bitmap,canPublish:()=>CanDeliverImageOutput);
            ReportImageDelivery(saved,settings);
            try
            {
                // A completed PNG, not a manifest flag or preview callback, grants delivery.
                await Task.Run(() => (RecoveryMarkDeliveredOverride ?? recovery.MarkDelivered)(directory));
            }
            catch { cleanupFailure="recovery-status-unavailable"; }
            physical.Dispose();physical=null;
            if (!CanDeliverImageOutput) return new(saved,CleanupFailure:cleanupFailure);
            progress?.Dispose();
            try { presentation.ShowSaved(bitmap,preview,saved,manifest.StopReason ?? ScrollCaptureStopReason.CompletedByUser,
                settings.InterfaceLanguage,() => CanDeliverImageOutput && output.TryCopySavedFile(saved.Path)); }
            catch { previewFailure="result-preview-unavailable"; }
            if (cleanupFailure is not null || previewFailure is not null)
                OutputWarning(AppInterfaceLanguage.Text(settings.InterfaceLanguage,
                    "PNG 已保存；" + (previewFailure is not null ? "结果预览未能显示。" : "") + (cleanupFailure is not null ? "恢复状态未能更新，原片段仍保留。" : ""),
                    "PNG saved; " + (previewFailure is not null ? "result preview unavailable. " : "") + (cleanupFailure is not null ? "recovery status unchanged; pieces retained." : "")));
            return new(saved,PreviewFailure:previewFailure,CleanupFailure:cleanupFailure);
        }
        catch
        {
            OutputWarning(AppInterfaceLanguage.Text(settings.InterfaceLanguage, saved is null ? "长截图未能恢复或保存；原片段仍保留。" : "PNG 已保存；后续交付未完成，原片段仍保留。",
                saved is null ? "Recovery or save failed; original pieces retained." : "PNG saved; later delivery incomplete, pieces retained."));
            return new(saved,Failure:saved is null ? "recovery-or-save-unavailable" : null,PreviewFailure:saved is null ? null : "delivery-unavailable");
        }
        finally { progress?.Dispose();physical?.Dispose();_longCaptureRecoveryBusy=false; }
    }

    private sealed class RecoveryPresentation(App owner) : ILongCaptureRecoveryPresentation
    {
        public Task<bool> ConfirmSaveAsync(ScrollCaptureRecoveryCandidate candidate,BitmapSource preview,string language)
            => Task.FromResult(new ScrollCaptureRecoveryWindow(candidate,preview,language).ShowDialog() == true);
        public void ShowSaved(Bitmap bitmap,BitmapSource preview,ImageDeliveryOutcome saved,ScrollCaptureStopReason reason,string language,Func<bool> copy)
            => new ScrollCaptureResultWindow(bitmap,reason,saved.Path,saved.ClipboardCopied,copy,language,saved.ClipboardRequested,preview,
                clipboardPersistenceConfirmed: saved.ClipboardPersistenceConfirmed,
                copyAgainWithOutcomeAsync: canPublish => SavedMediaClipboard.CopyPngWithOutcomeAsync(saved.Path, () => owner.CanDeliverImageOutput && canPublish())).ShowDialog();
    }
}

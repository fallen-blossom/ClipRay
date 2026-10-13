using System.Drawing;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Scrolling;
using ClipRay.App.Settings;
using Forms = System.Windows.Forms;

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

    private Forms.ToolStripMenuItem CreateLongCaptureRecoveryMenu()
    {
        var menu = new Forms.ToolStripMenuItem("恢复长截图");
        var version = 0;
        menu.DropDownClosed += (_,_) => version++;
        menu.DropDownItems.Add(AppUi("打开后加载", "Open to load")).Enabled=false;
        menu.DropDownOpening += (_,_) => LastRecoveryLoad=LoadAsync();
        async Task LoadAsync()
        {
            var current = ++version;
            menu.DropDownItems.Clear();
            if (!CanDeliverImageOutput || _longCaptureCancellation is not null || _longCaptureRecoveryBusy)
            {
                menu.DropDownItems.Add(AppUi("捕获或恢复尚未结束", "Capture or recovery is still running")).Enabled=false;
                return;
            }
            menu.DropDownItems.Add(AppUi("正在加载…", "Loading…")).Enabled=false;
            try
            {
                var recovery = new ScrollCaptureRecovery(_paths!.LongCaptureTempRoot);
                using var owner=EnterImageOutput();
                if (owner is null) return;
                var candidates = await Task.Run(() => recovery.List());
                owner.Dispose();
                if (!CanDeliverImageOutput || menu.IsDisposed || current != version) return;
                menu.DropDownItems.Clear();
                if (candidates.Count == 0) menu.DropDownItems.Add(AppUi("暂无保留的长截图", "No retained scrolling captures")).Enabled=false;
                foreach (var candidate in candidates)
                {
                    var label = candidate.CanRecover
                        ? $"{candidate.CreatedUtc!.Value.ToLocalTime():MM-dd HH:mm:ss} · {candidate.Width}×{candidate.Height} · {candidate.FrameCount} " + AppUi("帧", "frames")
                        : AppUi("材料已保留，无法可靠恢复", "Materials retained; reliable recovery unavailable");
                    if (candidate.Status == "indexed-subset") label += AppUi(" · 仅恢复已记录片段", " · Indexed pieces only");
                    var item = new Forms.ToolStripMenuItem(label) { Enabled=candidate.CanRecover,
                        ToolTipText=candidate.CanRecover ? AppUi("预览后选择保存；关闭仍保留片段。", "Preview and choose to save; close keeps pieces.")
                            : AppUi("旧格式、未记录、损坏或重定向材料未删除。", "Legacy, unindexed, corrupt or redirected materials were kept.") };
                    item.Click += (_,_) => LastRecoveryOperation = RecoverLongCaptureAsync(candidate.Directory);
                    menu.DropDownItems.Add(item);
                }
            }
            catch
            {
                if (!CanDeliverImageOutput || menu.IsDisposed || current != version) return;
                menu.DropDownItems.Clear();
                menu.DropDownItems.Add(AppUi("暂时无法读取；材料仍保留", "Cannot read now; materials retained")).Enabled=false;
            }
        }
        return menu;
    }

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
        IDisposable? physical=EnterImageOutput();
        if (physical is null) { _longCaptureRecoveryBusy=false;return new(Blocked:true); }
        try
        {
            var recovery = new ScrollCaptureRecovery(_paths!.LongCaptureTempRoot);
            var manifest = await Task.Run(() => recovery.ReadValidated(directory));
            using var bitmap = await (RecoveryCompositeOverride is {} compose ? compose(() => recovery.CreateComposite(directory)) : Task.Run(() => recovery.CreateComposite(directory)));
            var preview = await Task.Run(() => BitmapSourceFactory.Create(bitmap));
            physical.Dispose();physical=null;
            if (!CanDeliverImageOutput) return new(Cancelled:true);
            var candidate = new ScrollCaptureRecoveryCandidate(directory, manifest.CreatedUtc, bitmap.Width, bitmap.Height, manifest.Segments.Count,true,"recoverable");
            if (!await presentation.ConfirmSaveAsync(candidate,preview,settings.InterfaceLanguage) || !CanDeliverImageOutput) return new(Cancelled:true);
            physical=EnterImageOutput();
            if (physical is null) return new(Blocked:true);
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
        finally { physical?.Dispose();_longCaptureRecoveryBusy=false; }
    }

    private sealed class RecoveryPresentation(App owner) : ILongCaptureRecoveryPresentation
    {
        public Task<bool> ConfirmSaveAsync(ScrollCaptureRecoveryCandidate candidate,BitmapSource preview,string language)
            => Task.FromResult(new ScrollCaptureRecoveryWindow(candidate,preview,language).ShowDialog() == true);
        public void ShowSaved(Bitmap bitmap,BitmapSource preview,ImageDeliveryOutcome saved,ScrollCaptureStopReason reason,string language,Func<bool> copy)
            => new ScrollCaptureResultWindow(bitmap,reason,saved.Path,saved.ClipboardCopied,copy,language,saved.ClipboardRequested,preview,
                canPublish => SavedMediaClipboard.TryCopyPngAsync(saved.Path, () => owner.CanDeliverImageOutput && canPublish())).ShowDialog();
    }
}

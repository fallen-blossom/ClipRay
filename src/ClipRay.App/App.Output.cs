using System.Drawing;
using ClipRay.App.Capture;
using ClipRay.App.Feedback;
using ClipRay.App.Settings;
using ClipRay.App.Infrastructure;
using Forms = System.Windows.Forms;

namespace ClipRay.App;

public partial class App
{
    private AsyncResourceLifetime? _imageOutputLifetime;
    private Task? _imageOutputRetirement;
    private bool _imageOutputAdmissionClosed;
    private bool CanDeliverImageOutput => !_imageOutputAdmissionClosed;
    private IDisposable? EnterImageOutput()
    {
        if (!CanDeliverImageOutput) return null;
        return (_imageOutputLifetime ??= new()).Enter();
    }
    private void RequestImageOutputRetirement()
    {
        _imageOutputAdmissionClosed=true;
        if (_imageOutputRetirement is not null) return;
        _imageOutputRetirement=(_imageOutputLifetime ??= new()).RetireAsync(() => Task.CompletedTask);
        _translationRetirements.Track("image-output",_imageOutputRetirement);
    }
    internal Action<ImageDeliveryOutcome?, AppSettings>? ImageDeliveryObserver { get; set; }
    internal Action<bool, Rectangle[]>? ImageFeedbackOverride { get; set; }
    internal Action<string>? OutputWarningOverride { get; set; }

    internal async Task<ImageDeliveryOutcome?> RunQuickCaptureAsync(Func<Task<CapturedFrame>> capture,
        Func<CapturedFrame, IEnumerable<Rectangle>> successBounds, bool honorSystemFeedback = false)
    {
        using var owner=EnterImageOutput();
        if (owner is null) return null;
        var settings = _settings!;
        var output = _outputService!;
        ImageDeliveryOutcome? delivery = null;
        try
        {
            using var frame = await capture();
            if (!CanDeliverImageOutput) return null;
            delivery = await output.SaveWithDeliveryAsync(frame.Bitmap,canPublish:()=>CanDeliverImageOutput);
            if (!CanDeliverImageOutput) return delivery;
            ReportImageDelivery(delivery, settings);
            if (settings.ShowBorderFeedback && (!honorSystemFeedback || CaptureFeedbackPolicy.ShouldShowAppSuccess(frame.HasSystemCaptureFeedback)))
            {
                var bounds = successBounds(frame).ToArray();
                if (ImageFeedbackOverride is {} feedback) feedback(true, bounds);
                else _feedbackService.ShowSuccess(bounds);
            }
            return delivery;
        }
        catch
        {
            if (delivery is not null)
            {
                OutputWarning(AppInterfaceLanguage.Text(settings.InterfaceLanguage, "PNG 已保存；完成反馈未能显示。", "PNG saved; completion feedback unavailable."));
                return delivery;
            }
            if (!CanDeliverImageOutput) return null;
            OutputWarning(AppInterfaceLanguage.Text(settings.InterfaceLanguage, "截图未保存，请检查输出目录后重试。", "Capture was not saved; check the output folder and retry."));
            if (settings.ShowBorderFeedback)
            {
                try
                {
                    if (ImageFeedbackOverride is {} feedback) feedback(false, []);
                    else _feedbackService.ShowFailure(Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds);
                }
                catch { }
            }
            return null;
        }
        finally { try { ImageDeliveryObserver?.Invoke(delivery, settings); } catch { } }
    }

    private void ReportImageDelivery(ImageDeliveryOutcome delivery, AppSettings settings)
    {
        if (!CanDeliverImageOutput) return;
        var message = AppInterfaceLanguage.Text(settings.InterfaceLanguage,
            delivery.ClipboardRequested && !delivery.ClipboardCopied ? "PNG 已保存；未复制到剪贴板，可从最近截取重试。" : "PNG 已保存。",
            delivery.ClipboardRequested && !delivery.ClipboardCopied ? "PNG saved; not copied. Retry from Recent captures." : "PNG saved.");
        if (delivery.DiagnosticFailure is not null)
            message += AppInterfaceLanguage.Text(settings.InterfaceLanguage, " 诊断回读未完成。", " Diagnostic readback unavailable.");
        if ((delivery.ClipboardRequested && !delivery.ClipboardCopied) || delivery.DiagnosticFailure is not null) OutputWarning(message);
    }

    private void OutputWarning(string message)
    {
        if (!CanDeliverImageOutput) return;
        try
        {
            if (OutputWarningOverride is {} notify) notify(message);
            else
            {
                SetTrayTooltip("ClipRay | " + message);
                _trayIcon?.ShowBalloonTip(4000, "ClipRay", message, Forms.ToolTipIcon.Warning);
            }
        }
        catch { }
    }
}

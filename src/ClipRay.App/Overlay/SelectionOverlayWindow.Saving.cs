using System.Drawing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using ClipRay.App.Editing;
using ClipRay.App.Capture;
using ClipRay.App.Translation;
using ClipRay.App.Settings;

namespace ClipRay.App.Overlay;

public partial class SelectionOverlayWindow
{
    /// <summary>Capture the settings/output callback synchronously at confirmation, before rendering can yield.</summary>
    public Func<Func<Bitmap, Task<bool>>>? CaptureDeliverySnapshotFactory { get; set; }

    // Both the toolbar and keyboard invoke OverlayTool.Confirm; the snapshot is
    // owned until delivery completes, independently of later edits or closure.
    private async Task ConfirmSelectionCoreAsync()
    {
        if (_captureDeliveryPending || _lifetime.IsCancellationRequested) return;
        if (_mode == InteractionMode.Edit)
        {
            CommitActiveEdit();
            _mode = InteractionMode.None;
            ReleaseMouseCapture();
        }
        CommitTextEditor();
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels) return;
        if (_launchMode == OverlayLaunchMode.Recording) { RequestRecording(); return; }
        var operationId = Guid.NewGuid().ToString("N");
        var stage = "render";
        var revision = _captureInputRevision;
        var lifetime = _lifetime.Token;
        var originalContent = ConfirmToolButton.Content;
        var originalTip = ConfirmToolButton.ToolTip;
        var originalName = AutomationProperties.GetName(ConfirmToolButton);
        _captureDeliveryPending = true;
        ConfirmToolButton.IsEnabled = false;
        ConfirmToolButton.Content = new System.Windows.Controls.ProgressBar { IsIndeterminate = true, Width = 28, Height = 6 };
        ConfirmToolButton.ToolTip = SavingText("正在保存…", "Saving…");
        AutomationProperties.SetName(ConfirmToolButton, SavingText("正在保存…", "Saving…"));
        ToolStatusText.Text = SavingText("正在保存截图；新的修改会保留在画布中。", "Saving capture; further edits stay on the canvas.");
        ToolStatus.Visibility = Visibility.Visible;
        try
        {
            // Freeze delivery and editing inputs at the click boundary. A render
            // yield must not retarget the saved snapshot to later settings/edits.
            var deliver = CaptureDeliverySnapshotFactory?.Invoke() ?? CaptureDeliveryAsync;
            var synchronous = CaptureDelivery;
            var render = CreateDeliveryRendererSnapshot();
            // Give the pending visual a rendering opportunity before compositing.
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
            if (lifetime.IsCancellationRequested) return;
            MediaDeliveryTrace.Record("capture_render_started", operationId, new { Revision = revision });
            using var bitmap = render();
            MediaDeliveryTrace.Record("capture_render_completed", operationId, new { Revision = revision, bitmap.Width, bitmap.Height });
            stage = "delivery";
            var saved = true;
            if (deliver is not null) saved = await deliver(bitmap);
            else if (synchronous is not null) saved = synchronous(bitmap);
            else
            {
                // Legacy event handlers own their bitmap, as before.
                var legacy = (Bitmap)bitmap.Clone();
                try { if (CaptureConfirmed is { } confirmed) confirmed.Invoke(this, legacy); else legacy.Dispose(); }
                catch { legacy.Dispose(); throw; }
            }
            MediaDeliveryTrace.Record("capture_delivery_completed", operationId, new { Saved = saved, Revision = revision, CurrentRevision = _captureInputRevision });
            if (lifetime.IsCancellationRequested) return;
            if (!saved)
            {
                ShowTranslationStatus(SavingText("保存未完成，编辑内容已保留；请重试保存或调整输出目录。", "Save did not complete. Edits are kept; retry or change the output folder."));
                return;
            }
            if (revision != _captureInputRevision)
            {
                ShowTranslationStatus(SavingText("已保存先前版本；当前修改尚未保存，请再次确认。", "An earlier version was saved. Current edits are unsaved; confirm again."));
                return;
            }
            Close();
        }
        catch (Exception error)
        {
            MediaDeliveryTrace.Record("capture_" + stage + "_failed", operationId, new { Type = error.GetType().Name, error.HResult, Revision = revision });
            if (!lifetime.IsCancellationRequested)
                ShowTranslationStatus(SavingText("截图生成或保存失败，编辑内容已保留；请重试或调整输出目录。", "Capture rendering or saving failed. Edits are kept; retry or change the output folder."));
        }
        finally
        {
            _captureDeliveryPending = false;
            ConfirmToolButton.Content = originalContent;
            ConfirmToolButton.ToolTip = originalTip;
            ConfirmToolButton.IsEnabled = true;
            AutomationProperties.SetName(ConfirmToolButton, originalName);
        }
    }

    private string SavingText(string chinese, string english) => AppInterfaceLanguage.Text(_interactionSettings.InterfaceLanguage, chinese, english);

    private Func<Bitmap> CreateDeliveryRendererSnapshot()
    {
        var selection = _selection;
        var translatedItems = _translationDisplayState is { ExportMode: TranslationSelectionExportMode.Translated } translation
            ? translation.Items.ToArray() : null;
        EditLayer? edits = null;
        if (_editLayer is { } layer && layer.Selection == selection)
        {
            edits = new EditLayer(selection);
            foreach (var operation in layer.Operations)
            {
                switch (operation)
                {
                    case PenEditOperation pen:
                        edits.AddPenStroke(new PenStroke(pen.Stroke.Points, pen.Stroke.Color, pen.Stroke.Width));
                        break;
                    case MosaicEditOperation mosaic:
                        edits.AddMosaicStroke(new MosaicStroke(mosaic.Stroke.Points, mosaic.Stroke.BrushSize, mosaic.Stroke.BlockSize));
                        break;
                    case AnnotationEditOperation annotation:
                        edits.AddAnnotation(annotation.Annotation);
                        break;
                }
            }
        }
        return () =>
        {
            if (translatedItems is not null)
            {
                var translated = TranslatedSelectionRenderer.Render(_frame.Bitmap, selection, translatedItems);
                if (edits is null) return translated;
                try { return EditRenderer.CompositeSelection(translated, _frame.Bitmap, edits); }
                finally { translated.Dispose(); }
            }
            return edits is not null ? EditRenderer.Composite(_frame.Bitmap, edits)
                : _frame.Bitmap.Clone(ToCropRectangle(selection, _frame.Bitmap.Width, _frame.Bitmap.Height),
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        };
    }
}

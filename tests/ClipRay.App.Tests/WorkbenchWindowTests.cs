using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Ocr;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;

internal static class WorkbenchWindowTests
{
    public static void Run()
    {
        VerifyGroupingWindow();
        VerifyOcrEvidence();
        VerifyOcrReviewWindow();
        VerifyBatchReentry();
        VerifyLocalizedOcrRecovery();
        VerifyMissingModelPreventsRequest();
        using var source = new Bitmap(600, 400);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(System.Drawing.Color.White);
            using var font = new Font("Segoe UI", 18);
            graphics.DrawString("A useful translation workbench.", font, System.Drawing.Brushes.Black, 20, 30);
        }
        var block = new OcrTextBlock("A useful translation workbench.", new PixelRect(20, 30, 520, 40), []);
        var translationCalls = 0;
        var ocrCalls = 0;
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => { ocrCalls++; return Task.FromResult(new OcrRecognitionResult(true, [block], "completed")); },
            (_, _, _) => { translationCalls++; return Task.FromResult<IReadOnlyList<TranslationResult>>([new(true, "实用的翻译工作台。", "completed",
                ExecutionEvidence: new("ctranslate2", "fixture", null, "auto", "cpu / int8", "cuda-inference-failed"))]); },
            (_, _, _) => []);
        using var document = new TranslationWorkbenchDocument(source, new PixelRect(0, 0, 600, 400),
            new PixelRect(0, 0, 600, 400), TranslationRouteKind.Machine, "zh", orchestrator, blocks => blocks);
        string? copied = null;
        var window = new TranslationWorkbenchWindow(document, text => copied = text);
        try
        {
            var beforeOcr = (System.Windows.Controls.ComboBox)window.FindName("OcrLanguageBox");
            if (!beforeOcr.IsEnabled || document.Session is not null) throw new Exception("OCR language must be selectable before recognition.");
            if (beforeOcr.Items.Count > 1)
            {
                beforeOcr.SelectedIndex = 1;
                if (document.WindowsOcrLanguage != ((WorkbenchLanguageOption)beforeOcr.SelectedItem).Code
                    || document.TargetLanguage != "zh" || document.Session is not null)
                    throw new Exception("OCR language changed translation settings or started recognition.");
                beforeOcr.SelectedIndex = 0;
            }
            document.RecognizeAsync().GetAwaiter().GetResult();
            var initialRegionItem = ((ListBox)window.FindName("RegionList")).Items[0];
            var initialRegionButton = ((Canvas)window.FindName("RegionCanvas")).Children[0];
            var draft = (TextBox)window.FindName("DraftBox");
            var ocrLanguage = (System.Windows.Controls.ComboBox)window.FindName("OcrLanguageBox");
            if (ocrLanguage.IsEnabled || document.WindowsOcrLanguage is not null)
                throw new Exception("Recognized document must retain its independent OCR setting.");
            draft.Text = "Corrected sentence.";
            if (document.Session!.Regions[0].Draft != draft.Text) throw new Exception("Draft edit did not reach session.");
            var ocrEvidenceText = ((TextBox)window.FindName("EvidenceBox")).Text;
            if (!ocrEvidenceText.Contains("A useful translation workbench.") || !ocrEvidenceText.Contains("未提供模型信息"))
                throw new Exception("OCR snapshot or explicit missing evidence disappeared after draft edit.");
            Click("CopySourceButton");
            if (copied != "Corrected sentence.") throw new Exception("Source copy lost user correction.");
            Click("CopyAllSourceButton");
            if (copied != "Corrected sentence." || translationCalls != 0)
                throw new Exception("Copying source must use current draft without translation.");
            if (!TranslationWorkbenchWindow.TranslateRegionCommand.CanExecute(null, window))
                throw new Exception("Translation command unavailable for selected draft.");
            TranslationWorkbenchWindow.TranslateRegionCommand.Execute(null, window);
            if (!ReferenceEquals(initialRegionItem, ((ListBox)window.FindName("RegionList")).Items[0])
                || !ReferenceEquals(initialRegionButton, ((Canvas)window.FindName("RegionCanvas")).Children[0]))
                throw new Exception("State refresh replaced keyboard focus targets.");
            if (TranslationWorkbenchWindow.CancelCurrentCommand.CanExecute(null, window))
                throw new Exception("Cancel command must be inactive without current work.");
            Click("CopyButton");
            if (copied != "实用的翻译工作台。") throw new Exception("Copy did not use readable translation after layout rejection.");
            var manualTranslation = (TextBox)window.FindName("ManualTranslationBox");
            manualTranslation.Text = "人工改好的译文。";
            Click("ApplyManualTranslationButton");
            if (document.Session.Regions[0].Translation != "人工改好的译文。" || translationCalls != 1)
                throw new Exception("Manual translation edit called the provider or lost corrected text.");
            Click("CopyButton");
            if (copied != "人工改好的译文。") throw new Exception("Copy did not use the manual correction.");
            Click("RestoreTranslationButton");
            if (document.Session.Regions[0].Translation != "实用的翻译工作台。"
                || translationCalls != 1)
                throw new Exception("Undo manual translation edit lost the model result or retriggered the provider.");
            var uiLanguage = (System.Windows.Controls.ComboBox)window.FindName("InterfaceLanguageBox");
            var savedDraft = draft.Text;
            var savedTranslation = ((TextBox)window.FindName("ResultBox")).Text;
            var savedRevision = document.Session.Regions[0].Revision;
            uiLanguage.SelectedIndex = 1;
            if (window.Title != "ClipRay Translation Workbench"
                || ((Button)window.FindName("CopySourceButton")).Content.ToString() != "Copy region source"
                || !((TextBox)window.FindName("EvidenceBox")).Text.Contains("Attempt ")
                || !((TextBox)window.FindName("EvidenceBox")).Text.Contains("Inference backend: cpu / int8")
                || !((TextBox)window.FindName("EvidenceBox")).Text.Contains("Backend fallback reason: cuda-inference-failed")
                || !((TextBlock)window.FindName("RegionStatus")).Text.StartsWith("Translation available"))
                throw new Exception($"English UI missing: {window.Title}; {((Button)window.FindName("CopySourceButton")).Content}; {((TextBox)window.FindName("EvidenceBox")).Text}; {((TextBlock)window.FindName("RegionStatus")).Text}");
            Click("CopySourceButton");
            if (copied != savedDraft || !((TextBlock)window.FindName("StatusText")).Text.StartsWith("Source copied"))
                throw new Exception("English source-copy flow changed user text or lost feedback.");
            Render("en");
            Render("en-narrow", 720, 480);
            ((ScrollViewer)window.FindName("DetailsScroller")).ScrollToVerticalOffset(760);
            Render("en-manual-edit", 900, 700);
            ((ScrollViewer)window.FindName("DetailsScroller")).ScrollToTop();
            uiLanguage.SelectedIndex = 0;
            if (window.Title != "ClipRay 翻译工作台" || draft.Text != savedDraft
                || ((TextBox)window.FindName("ResultBox")).Text != savedTranslation
                || document.Session.Regions[0].Revision != savedRevision
                || translationCalls != 1 || ocrCalls != 1 || document.WindowsOcrLanguage is not null
                || document.Session.Regions[0].TargetLanguage != "zh"
                || !ReferenceEquals(initialRegionItem, ((ListBox)window.FindName("RegionList")).Items[0])
                || !ReferenceEquals(initialRegionButton, ((Canvas)window.FindName("RegionCanvas")).Children[0]))
                throw new Exception("Interface language changed document state, engine calls or stable controls.");
            Render("zh-CN");
            Render("zh-CN-narrow", 720, 480);
            var canvas = (Canvas)window.FindName("RegionCanvas");
            var hit = (Button)canvas.Children[0];
            if (Canvas.GetLeft(hit) != document.Session.Regions[0].Original.Bounds.X)
                throw new Exception("Region hit target lost source coordinates.");
            hit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var list = (ListBox)window.FindName("RegionList");
            if (((ListBoxItem)list.SelectedItem).Tag is not Guid selected || selected != document.Session.Regions[0].Id)
                throw new Exception("Image selection did not synchronize list.");
            var toggle = (System.Windows.Controls.CheckBox)window.FindName("TranslatedView");
            toggle.IsChecked = true;
            if (((System.Windows.Controls.Image)window.FindName("TranslationImage")).Source is null)
                throw new Exception("Translated view was not rendered.");
            var preview = ((System.Windows.Controls.Image)window.FindName("TranslationImage")).Source;
            ((Button)canvas.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ReferenceEquals(preview, ((System.Windows.Controls.Image)window.FindName("TranslationImage")).Source))
                throw new Exception("Selecting a region unnecessarily rebuilt translated pixels.");
            toggle.IsChecked = false;
            toggle.IsChecked = true;
            if (!ReferenceEquals(preview, ((System.Windows.Controls.Image)window.FindName("TranslationImage")).Source))
                throw new Exception("View toggle unnecessarily rebuilt translated pixels.");
            toggle.IsChecked = false;
            if (translationCalls != 1) throw new Exception("View switching requested translation.");
            var id = document.Session.Regions[0].Id;
            document.Session.TranslateAsync(id, (_, input, _) => Task.FromResult(new WorkbenchTranslationOutput(id,
                new(true, "译文", "completed"), new InPlaceLayoutItem(
                    new ClipRay.App.Ocr.OcrTextBlock(input.Region.SourceText, input.Region.Bounds, []),
                    "译文", input.Region.Bounds, InPlaceBackgroundMode.FlatSampled,
                    System.Drawing.Color.White, System.Drawing.Color.Black, SourceRegion: input.Region))))
                .GetAwaiter().GetResult();
            toggle.IsChecked = true;
            var acceptedPreview = ((System.Windows.Controls.Image)window.FindName("TranslationImage")).Source;
            if (ReferenceEquals(preview, acceptedPreview)) throw new Exception("New layout reused stale pixels.");
            document.Session.Preserve(id);
            if (ReferenceEquals(acceptedPreview, ((System.Windows.Controls.Image)window.FindName("TranslationImage")).Source))
                throw new Exception("Preserving original left translated pixels cached.");
            toggle.IsChecked = false;
            var sourceLanguage = (System.Windows.Controls.ComboBox)window.FindName("SourceLanguageBox");
            sourceLanguage.SelectedItem = sourceLanguage.Items.Cast<WorkbenchLanguageOption>().Single(language => language.Code == "en");
            if (document.Session.Regions[0].SourceLanguage != "en" || document.Session.Regions[0].Translation != "")
                throw new Exception("Language change did not invalidate the previous translation.");
            var targets = (System.Windows.Controls.ComboBox)window.FindName("TargetLanguageBox");
            if (targets.Items.Cast<WorkbenchLanguageOption>().Any(language => language.Code != "zh"))
                throw new Exception("Machine route advertises an unsupported target.");

        }
        finally { window.ReleaseDocument(); }
        source.SetPixel(20, 30, System.Drawing.Color.Magenta);
        using var padded = new TranslationWorkbenchDocument(source, new PixelRect(50, 70, 600, 400),
            new PixelRect(70, 100, 100, 80), TranslationRouteKind.Machine, "zh", orchestrator, blocks => blocks);
        using var exported = WorkbenchImageExport.Render(padded);
        if (exported.Width != 100 || exported.Height != 80
            || exported.GetPixel(0, 0).ToArgb() != System.Drawing.Color.Magenta.ToArgb())
            throw new Exception("Export leaked padded pixels or lost selection origin.");
        void Render(string language, int width = 1140, int height = 700)
        {
            var content = (FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(width, height));
            content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            if (width == 720 && (((ScrollViewer)window.FindName("PreviewScroller")).ActualHeight < 100
                || ((ListBox)window.FindName("RegionList")).ActualHeight < 60))
                throw new Exception("Compact window must retain usable preview and region list heights.");
            var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
            var path = Path.Combine(Path.GetTempPath(), $"clipray-workbench-wpf-{language}.png");
            using (var stream = File.Create(path)) encoder.Save(stream);
            Console.WriteLine($"Workbench WPF offscreen checks passed; image: {path}");
        }
        void Click(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void VerifyOcrEvidence()
    {
        var bounds = new PixelRect(0, 0, 160, 30);
        var evidence = new OcrWordEvidence("fixture-engine", "fixture-model", "Latin", .42, "reported-digest");
        var block = new OcrTextBlock("Original OCR", bounds, [
            new OcrWord("Original", bounds, evidence),
            new OcrWord("OCR", bounds, evidence with { RawScore = double.NaN })]);
        var region = new VisualTextRegion("fixture", block.Text, [block], bounds,
            VisualTextRole.Uncertain, VisualStructureConfidence.Low);
        var text = WorkbenchOcrEvidence.Describe(region, "en");
        if (!WorkbenchOcrEvidence.NeedsStructureReview(region)
            || WorkbenchOcrEvidence.NeedsStructureReview(region with { Role = VisualTextRole.Body, Confidence = VisualStructureConfidence.High })
            || !text.Contains("Original OCR") || !text.Contains("not accuracy")
            || !text.Contains("0.42") || !text.Contains("Some words have no raw score.")
            || !text.Contains("not independently verified") || text.Contains("NaN") || text.Contains("42%"))
            throw new Exception("OCR provenance must distinguish raw scores, absent scores and structure review.");
        var mixed = region with { Role = VisualTextRole.Body, Confidence = VisualStructureConfidence.High,
            Lines = [block with { Words = [new OcrWord("Original OCR", bounds, evidence with { AlternativeText = "OCR" })] }] };
        if (!WorkbenchOcrEvidence.NeedsStructureReview(mixed)
            || !WorkbenchOcrEvidence.Describe(mixed, "en").Contains("Alternative: OCR")
            || !WorkbenchOcrEvidence.Describe(mixed, "zh-CN").Contains("另一候选：OCR"))
            throw new Exception("Mixed-script alternatives must remain visible even in high-confidence structure.");
        var mixedLine = new OcrTextBlock("Show translation 日本", bounds,
            [new OcrWord("Show translation 日本", bounds, evidence with { AlternativeText = "Show translation" })]);
        var split = VisualTextRegionBuilder.Build([mixedLine]);
        if (split.Count < 2 || split.Any(part => !WorkbenchOcrEvidence.NeedsStructureReview(part)
                || !WorkbenchOcrEvidence.Describe(part, "en").Contains("Alternative: Show translation")))
            throw new Exception("Region splitting discarded original OCR disagreement provenance.");
    }

    private static void VerifyGroupingWindow()
    {
        using var source = new Bitmap(600, 240);
        using (var graphics = Graphics.FromImage(source))
        using (var font = new Font("Segoe UI", 18, System.Drawing.GraphicsUnit.Pixel))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.DrawString("First paragraph is complete.", font, System.Drawing.Brushes.Black, 20, 20);
            graphics.DrawString("Second paragraph is complete.", font, System.Drawing.Brushes.Black, 20, 100);
        }
        var calls = 0;
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true,
                [new("First paragraph is complete.", new(20, 20, 350, 30), []),
                 new("Second paragraph is complete.", new(20, 100, 350, 30), [])], "completed")),
            (_, _, _) => { calls++; return Task.FromException<IReadOnlyList<TranslationResult>>(new Exception("Grouping must not translate automatically")); });
        using var document = new TranslationWorkbenchDocument(source, new(0, 0, 600, 240), new(0, 0, 600, 240),
            TranslationRouteKind.Machine, "zh", orchestrator, blocks => blocks);
        var window = new TranslationWorkbenchWindow(document, _ => { }, interfaceLanguage: "en");
        try
        {
            document.RecognizeAsync().GetAwaiter().GetResult();
            var list = (ListBox)window.FindName("RegionList");
            ((ListBoxItem)list.Items[1]).IsSelected = true;
            var merge = (Button)window.FindName("MergeButton");
            var preview = (TextBox)window.FindName("MergePreviewBox");
            if (!merge.IsEnabled || !preview.Text.Contains("First paragraph") || !preview.Text.Contains("Second paragraph"))
                throw new Exception("Grouping selection or preview was lost during refresh.");
            merge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)window.FindName("DraftBox")).Text = "Combined edit";
            ((Button)window.FindName("UndoGroupButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (document.Session!.Regions.Count != 2) throw new Exception("Grouping undo button failed.");
            ((Button)window.FindName("RedoGroupButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (document.Session.Regions.Single().Draft != "Combined edit" || calls != 0)
                throw new Exception("Grouping redo discarded edits or invoked translation.");
            ((TextBox)window.FindName("DraftBox")).CaretIndex = 9;
            var split = (Button)window.FindName("SplitButton");
            if (!split.IsEnabled || !((TextBox)window.FindName("SplitPreviewBox")).Text.Contains("Second paragraph"))
                throw new Exception("Split preview did not bind OCR boundary and draft caret.");
            ((Expander)window.FindName("GroupingPanel")).IsExpanded = true;
            var splitContent = (FrameworkElement)window.Content;
            splitContent.Measure(new System.Windows.Size(900, 850));
            splitContent.Arrange(new Rect(0, 0, 900, 850)); splitContent.UpdateLayout();
            DependencyObject? parent = split;
            while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
            ((ScrollViewer?)parent)?.ScrollToBottom(); splitContent.UpdateLayout();
            var splitImage = new RenderTargetBitmap(900, 850, 96, 96, PixelFormats.Pbgra32); splitImage.Render(splitContent);
            var splitEncoder = new PngBitmapEncoder(); splitEncoder.Frames.Add(BitmapFrame.Create(splitImage));
            using (var splitStream = File.Create(Path.Combine(Path.GetTempPath(), "clipray-workbench-split-preview.png"))) splitEncoder.Save(splitStream);
            split.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (document.Session.Regions.Count != 2 || string.Concat(document.Session.Regions.Select(region => region.Draft)) != "Combined edit")
                throw new Exception("Split button discarded draft text.");
            ((Button)window.FindName("UndoGroupButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Expander)window.FindName("GroupingPanel")).IsExpanded = true;
            ((Button)window.FindName("UndoGroupButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((ListBoxItem)list.Items[1]).IsSelected = true;
            var content = (FrameworkElement)window.Content;
            foreach (var width in new[] { 1140, 900 })
            {
                content.Measure(new System.Windows.Size(width, 850));
                content.Arrange(new Rect(0, 0, width, 850)); content.UpdateLayout();
                merge.BringIntoView(); content.UpdateLayout();
                if (!merge.IsEnabled || preview.ActualWidth <= 0 || merge.ActualWidth <= 0
                    || !preview.Text.Contains("Second paragraph")) throw new Exception("Narrow grouping preview is unavailable.");
                var bitmap = new RenderTargetBitmap(width, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(Path.GetTempPath(), $"clipray-workbench-grouping-{width}.png")); encoder.Save(stream);
            }
        }
        finally { window.ReleaseDocument(); }
        if (((TextBox)window.FindName("MergePreviewBox")).Text.Length != 0
            || ((TextBox)window.FindName("SplitPreviewBox")).Text.Length != 0
            || ((System.Windows.Controls.ComboBox)window.FindName("SplitLineBox")).Items.Count != 0)
            throw new Exception("Closed workbench retained private grouping previews.");
    }

    private static void VerifyOcrReviewWindow()
    {
        using var source = new Bitmap(600, 200);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(System.Drawing.Color.White);
            using var font = new Font("Segoe UI", 18);
            graphics.DrawString("Review this text first", font, System.Drawing.Brushes.Black, 20, 20);
        }
        var bounds = new PixelRect(20, 20, 300, 30);
        var block = new OcrTextBlock("Review this text first", bounds,
            [new OcrWord("Review this text first", bounds, new("fixture-engine", "fixture-model", "Latin", .42))]);
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, _, _) => Task.FromException<IReadOnlyList<TranslationResult>>(new Exception("Reviewing OCR must not trigger translation")), (_, _, _) => []);
        using var document = new TranslationWorkbenchDocument(source, new PixelRect(0, 0, 600, 200),
            new PixelRect(0, 0, 600, 200), TranslationRouteKind.Machine, "zh", orchestrator, items => items);
        var window = new TranslationWorkbenchWindow(document, _ => { }, interfaceLanguage: "en");
        if (window.InterfaceLanguage != "en" || window.Title != "ClipRay Translation Workbench")
            throw new Exception("New workbench did not inherit configured UI language.");
        ((System.Windows.Controls.ComboBox)window.FindName("InterfaceLanguageBox")).SelectedIndex = 0;
        try
        {
            document.RecognizeAsync().GetAwaiter().GetResult();
            var warning = (TextBlock)window.FindName("OcrReviewText");
            var language = (System.Windows.Controls.ComboBox)window.FindName("InterfaceLanguageBox");
            if (warning.Visibility != Visibility.Visible || !warning.Text.StartsWith("区域结构需核对"))
                throw new Exception("Uncertain region has no visible review guidance.");
            ((TextBox)window.FindName("DraftBox")).Text = "My corrected text";
            language.SelectedIndex = 1;
            if (!warning.Text.StartsWith("Region structure needs review")
                || !((TextBox)window.FindName("EvidenceBox")).Text.Contains("Original OCR text (unchanged by edits):")
                || !((TextBox)window.FindName("EvidenceBox")).Text.Contains("Review this text first"))
                throw new Exception("OCR review guidance or immutable source lost after edit/language switch.");
            var content = (FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(1140, 700));
            content.Arrange(new Rect(0, 0, 1140, 700)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1140, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetTempPath(), "clipray-workbench-ocr-review.png")); encoder.Save(stream);
        }
        finally { window.ReleaseDocument(); }
    }

    private static void VerifyLocalizedOcrRecovery()
    {
        using var source = new Bitmap(100, 100);
        var calls = 0;
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => { calls++; return Task.FromResult(new OcrRecognitionResult(false, [], "fixture-ocr-failure")); },
            (_, _, _) => Task.FromException<IReadOnlyList<TranslationResult>>(new Exception("OCR recovery must not call translation.")), (_, _, _) => []);
        using var document = new TranslationWorkbenchDocument(source, new PixelRect(0, 0, 100, 100),
            new PixelRect(0, 0, 100, 100), TranslationRouteKind.Machine, "zh", orchestrator, blocks => blocks);
        var window = new TranslationWorkbenchWindow(document, _ => { }, new TranslationFileCheck("fixture", ["python.exe"]));
        try
        {
            var languages = (System.Windows.Controls.ComboBox)window.FindName("InterfaceLanguageBox");
            foreach (var language in new[] { 1, 0 })
            {
                languages.SelectedIndex = language;
                var fileStatus = ((TextBlock)window.FindName("FileCheckText")).Text;
                if (!fileStatus.Contains("python.exe") || !fileStatus.StartsWith(language == 1 ? "Local translation files missing:" : "本地翻译缺少文件："))
                    throw new Exception("Missing runtime files were not displayed in the selected UI language.");
                document.RecognizeAsync().GetAwaiter().GetResult();
                var status = ((TextBlock)window.FindName("StatusText")).Text;
                if (!status.StartsWith(language == 1 ? "OCR failed." : "识别失败。")
                    || !status.Contains("fixture-ocr-failure")
                    || !((Button)window.FindName("RecognizeButton")).IsEnabled
                    || !((System.Windows.Controls.ComboBox)window.FindName("OcrLanguageBox")).IsEnabled)
                    throw new Exception("Localized OCR failure lost evidence or retry controls.");
            }
            if (calls != 2) throw new Exception("UI language switching unexpectedly called OCR.");
        }
        finally { window.ReleaseDocument(); }
    }

    private static void VerifyMissingModelPreventsRequest()
    {
        using var source = new Bitmap(300, 120);
        var block = new OcrTextBlock("Read this text.", new PixelRect(10, 10, 200, 30), []);
        var translationCalls = 0;
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, [block], "completed")),
            (_, _, _) => { translationCalls++; return Task.FromResult<IReadOnlyList<TranslationResult>>([new(true, "阅读此文本。", "completed")]); },
            (_, _, _) => []);
        using var document = new TranslationWorkbenchDocument(source, new PixelRect(0, 0, 300, 120),
            new PixelRect(0, 0, 300, 120), TranslationRouteKind.Machine, "zh", orchestrator, items => items);
        document.RecognizeAsync().GetAwaiter().GetResult();
        var window = new TranslationWorkbenchWindow(document, _ => { }, new TranslationFileCheck("fixture", ["python.exe"]));
        try
        {
            if (((Button)window.FindName("TranslateButton")).IsEnabled
                || ((Button)window.FindName("TranslateAllButton")).IsEnabled
                || TranslationWorkbenchWindow.TranslateRegionCommand.CanExecute(null, window))
                throw new Exception("Missing local files still permit translation requests.");
            ((Button)window.FindName("TranslateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)window.FindName("TranslateAllButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (translationCalls != 0 || document.Session!.Regions[0].State != WorkbenchRegionState.Pending)
                throw new Exception("Disabled translation controls still started a provider request.");
        }
        finally { window.ReleaseDocument(); }
    }

    private static void VerifyBatchReentry()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
        using var source = new Bitmap(600, 400);
        var blocks = new[] { new OcrTextBlock("First paragraph.", new PixelRect(20, 20, 200, 20), []),
            new OcrTextBlock("Second paragraph.", new PixelRect(20, 250, 200, 20), []) };
        var pending = new TaskCompletionSource<IReadOnlyList<TranslationResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var orchestrator = new TranslationApplicationOrchestrator(
            (_, _) => Task.FromResult(new OcrRecognitionResult(true, blocks, "completed")),
            (_, _, _) => { calls++; return pending.Task; }, (_, _, _) => []);
        using var document = new TranslationWorkbenchDocument(source, new PixelRect(0, 0, 600, 400),
            new PixelRect(0, 0, 600, 400), TranslationRouteKind.Machine, "zh", orchestrator, items => items);
        document.RecognizeAsync().GetAwaiter().GetResult();
        var window = new TranslationWorkbenchWindow(document, _ => { }, new TranslationFileCheck("fixture", []));
        try
        {
            if (document.Session!.Regions.Count != 2) throw new Exception("Batch fixture must have two regions.");
            var regionList = (ListBox)window.FindName("RegionList");
            var manualDraft = (TextBox)window.FindName("ManualTranslationBox");
            manualDraft.Text = "未应用的人工译文";
            regionList.SelectedIndex = 1;
            regionList.SelectedIndex = 0;
            if (manualDraft.Text != "未应用的人工译文")
                throw new Exception("Changing regions discarded an unapplied manual translation draft.");
            document.Session.Edit(document.Session.Regions[0].Id, "Updated first paragraph.");
            if (manualDraft.Text == "未应用的人工译文")
                throw new Exception("An OCR source edit retained a stale manual translation draft.");
            var button = (Button)window.FindName("TranslateAllButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (button.IsEnabled) throw new Exception("Running batch permits reentry.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var second = document.Session.Regions[1].Id;
            document.Session.Preserve(second);
            pending.SetResult([new(true, "第一段。", "completed")]);
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            timer.Tick += (_, _) =>
            {
                // Once the first operation has finished, queue a low-priority drain behind its UI continuation.
                if (document.Session.Regions[0].State != WorkbenchRegionState.Translating || timeout.ElapsedMilliseconds > 3000)
                    dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
            if (calls != 1 || document.Session.Regions[1].State != WorkbenchRegionState.Preserved
                || document.Session.Regions[0].State == WorkbenchRegionState.Translating)
                throw new Exception("Batch duplicated requests or ignored a later preserve action.");
        }
        finally { window.ReleaseDocument(); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}

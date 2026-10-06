using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Input;
using ClipRay.App.Overlay;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;

namespace ClipRay.App.Translation;

public partial class TranslationWorkbenchWindow : Window
{
    public static RoutedUICommand TranslateRegionCommand { get; } = new("Translate region", "TranslateRegion", typeof(TranslationWorkbenchWindow),
        new InputGestureCollection { new KeyGesture(Key.Enter, ModifierKeys.Control) });
    public static RoutedUICommand CancelCurrentCommand { get; } = new("Cancel current task", "CancelCurrent", typeof(TranslationWorkbenchWindow),
        new InputGestureCollection { new KeyGesture(Key.Escape) });
    private readonly TranslationWorkbenchDocument _document;
    private readonly Action<string> _copy;
    private readonly TranslationFileCheck? _fileCheck;
    private TranslationWorkbenchSession? _observedSession;
    private Guid? _selected;
    private readonly HashSet<Guid> _groupSelection = [];
    private Guid? _splitRegion;
    private Guid? _manualEditRegion;
    private string _manualEditBase = "";
    private long _manualEditRevision;
    private readonly Dictionary<Guid, (long Revision, string Text)> _manualDrafts = [];
    private bool _refreshing;
    private bool _closed;
    private bool _translatingAll;
    private InPlaceLayoutItem[]? _renderedLayouts;
    private ImageSource? _renderedTranslation;

    public TranslationWorkbenchWindow(TranslationWorkbenchDocument document, Action<string>? copy = null, TranslationFileCheck? fileCheck = null, string interfaceLanguage = "zh-CN")
    {
        _document = document;
        _fileCheck = fileCheck;
        _copy = copy ?? (text => System.Windows.Clipboard.SetText(text));
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(TranslateRegionCommand, async (_, _) =>
        { if (_selected is Guid id && _document.Session?.Regions.FirstOrDefault(region => region.Id == id) is { } region
              && HasAvailableLanguage(region)) await _document.TranslateAsync(id); },
            (_, e) => e.CanExecute = !_closed && TranslateButton.IsEnabled));
        CommandBindings.Add(new CommandBinding(CancelCurrentCommand, (_, _) =>
        {
            if (_document.State == WorkbenchRecognitionState.Recognizing) _document.CancelRecognition();
            else if (_selected is Guid id) _document.Session?.Cancel(id);
        }, (_, e) => e.CanExecute = !_closed && (_document.State == WorkbenchRecognitionState.Recognizing || CancelButton.IsEnabled)));
        InterfaceLanguageBox.ItemsSource = new[] { new WorkbenchLanguageOption("zh-CN", "简体中文"), new WorkbenchLanguageOption("en", "English") };
        InterfaceLanguageBox.SelectedIndex = ClipRay.App.Settings.AppInterfaceLanguage.Normalize(interfaceLanguage) == "en" ? 1 : 0;
        using var bitmap = document.CopySource();
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); SourceImage.Source = image;
        ImageSurface.Width = bitmap.Width; ImageSurface.Height = bitmap.Height;
        _document.Changed += DocumentStateChanged;
        Closed += (_, _) => ReleaseDocument();
        Refresh();
    }

    public string InterfaceLanguage { get; private set; } = "zh-CN";
    private string T(string text) => WorkbenchLocalization.Text(text, InterfaceLanguage);
    private string T(string chinese, string english) => ClipRay.App.Settings.AppInterfaceLanguage.Text(InterfaceLanguage, chinese, english);
    private string F(string text, params object[] args) => string.Format(T(text), args);

    private void InterfaceLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_closed || InterfaceLanguageBox.SelectedItem is not WorkbenchLanguageOption option) return;
        InterfaceLanguage = option.Code;
        _refreshing = true;
        try
        {
            WorkbenchLocalization.Apply(Resources, InterfaceLanguage);
            OcrLanguageBox.ItemsSource = new[] { new WorkbenchLanguageOption("auto", T("自动识别（本地引擎）")) }
                .Concat(ClipRay.App.Ocr.OcrService.InstalledWindowsLanguages().Select(tag => new WorkbenchLanguageOption(tag, $"Windows OCR · {tag}"))).ToArray();
            SourceLanguageBox.ItemsSource = WorkbenchLanguageOptions.Sources(_document.Route, _document.CommonMachineModel)
                .Select(item => item with { Label = T(item.Label) }).ToArray();
            TargetLanguageBox.ItemsSource = WorkbenchLanguageOptions.Targets(_document.Route)
                .Select(item => item with { Label = T(item.Label) }).ToArray();
        }
        finally { _refreshing = false; }
        Refresh();
    }

    public void ReleaseDocument()
    {
        if (_closed) return;
        _closed = true;
        _document.Changed -= DocumentStateChanged;
        if (_observedSession is not null) _observedSession.Changed -= DocumentStateChanged;
        _document.Dispose();
        SourceImage.Source = null; TranslationImage.Source = null; RegionCanvas.Children.Clear();
        _renderedLayouts = null; _renderedTranslation = null;
        DraftBox.Clear(); ResultBox.Clear(); ManualTranslationBox.Clear(); EvidenceBox.Clear(); RegionList.Items.Clear();
        MergePreviewBox.Clear(); SplitPreviewBox.Clear(); SplitLineBox.Items.Clear();
        _groupSelection.Clear(); _manualDrafts.Clear(); _selected = null; _splitRegion = null;
        _manualEditRegion = null; _manualEditBase = ""; _manualEditRevision = 0;
        MergeButton.IsEnabled = SplitButton.IsEnabled = UndoGroupButton.IsEnabled = RedoGroupButton.IsEnabled = false;
    }

    private void DocumentStateChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(() => { if (!_closed) Refresh(); });
    }

    private void Refresh()
    {
        if (_closed) return;
        _refreshing = true;
        try
        {
            var session = _document.Session;
            OcrLanguageBox.IsEnabled = session is null && _document.State != WorkbenchRecognitionState.Recognizing;
            OcrLanguageBox.SelectedItem = OcrLanguageBox.Items.Cast<WorkbenchLanguageOption>()
                .FirstOrDefault(option => option.Code == (_document.WindowsOcrLanguage ?? "auto"));
            if (!ReferenceEquals(session, _observedSession))
            {
                if (_observedSession is not null) _observedSession.Changed -= DocumentStateChanged;
                _observedSession = session;
                if (session is not null) session.Changed += DocumentStateChanged;
            }
            var regions = session?.Regions ?? [];
            FileCheckText.Text = _fileCheck?.Describe(InterfaceLanguage) ?? string.Empty;
            FileCheckText.Visibility = _fileCheck is null ? Visibility.Collapsed : Visibility.Visible;
            if (!regions.Any(region => region.Id == _selected)) _selected = regions.FirstOrDefault()?.Id;
            _groupSelection.RemoveWhere(id => !regions.Any(region => region.Id == id));
            if (!RegionList.Items.Cast<ListBoxItem>().Select(item => (Guid)item.Tag).SequenceEqual(regions.Select(region => region.Id)))
                RegionList.Items.Clear();
            foreach (var region in regions)
            {
                var item = RegionList.Items.Cast<ListBoxItem>().FirstOrDefault(item => (Guid)item.Tag == region.Id);
                if (item is null) { item = new ListBoxItem { Tag = region.Id }; RegionList.Items.Add(item); }
                item.Content = $"{StateLabel(region.State)} · {region.Draft.ReplaceLineEndings(" ")}";
                item.IsSelected = _groupSelection.Count > 0 ? _groupSelection.Contains(region.Id) : region.Id == _selected;
            }
            var selected = regions.FirstOrDefault(region => region.Id == _selected);
            GroupingPanel.Header = T("修正区域分组");
            GroupingHelp.Text = T("按住 Ctrl 或 Shift 选择多个区域，核对下方顺序后合并。撤销和重做仅影响分组，编辑内容会保留在对应分组中。");
            MergeButton.Content = T("合并所选区域");
            UndoGroupButton.Content = T("撤销分组");
            RedoGroupButton.Content = T("重做分组");
            System.Windows.Automation.AutomationProperties.SetName(MergePreviewBox, T("合并原文预览"));
            MergeButton.IsEnabled = false;
            MergePreviewBox.Text = "";
            if (session is not null && _groupSelection.Count >= 2)
            {
                try
                {
                    MergePreviewBox.Text = string.Join("\n\n", session.PreviewMerge(_groupSelection.ToArray()).Select(region => region.Draft));
                    MergeButton.IsEnabled = !_translatingAll;
                }
                catch (InvalidOperationException) { MergePreviewBox.Text = T("请先将所选区域的源语言、目标语言和翻译路线设置一致。"); }
            }
            UndoGroupButton.IsEnabled = !_translatingAll && session?.CanUndoGrouping == true;
            RedoGroupButton.IsEnabled = !_translatingAll && session?.CanRedoGrouping == true;
            SourceLanguageBox.SelectedItem = SourceLanguageBox.Items.Cast<WorkbenchLanguageOption>()
                .FirstOrDefault(language => language.Code == WorkbenchLanguageOptions.Code(selected?.SourceLanguage ?? "auto"));
            TargetLanguageBox.SelectedItem = TargetLanguageBox.Items.Cast<WorkbenchLanguageOption>()
                .FirstOrDefault(language => language.Code == WorkbenchLanguageOptions.Code(selected?.TargetLanguage ?? _document.TargetLanguage));
            SourceLanguageBox.IsEnabled = TargetLanguageBox.IsEnabled = selected is not null;
            RefreshImage(regions);
            if (DraftBox.Text != (selected?.Draft ?? "")) DraftBox.Text = selected?.Draft ?? "";
            SplitHelp.Text = T("在原始识别行之间选择图片边界，再把草稿光标放在文字切分处。核对两部分后拆分；可撤销。拆开完整句子可能丢失上下文并导致错译。");
            SplitButton.Content = T("按预览拆分");
            System.Windows.Automation.AutomationProperties.SetName(SplitLineBox, T("拆分图片边界"));
            System.Windows.Automation.AutomationProperties.SetName(SplitPreviewBox, T("拆分预览"));
            if (_splitRegion != selected?.Id)
            {
                _splitRegion = selected?.Id;
                SplitLineBox.Items.Clear();
                if (selected is not null)
                    for (var index = 1; index < selected.Original.Lines.Count; index++)
                        SplitLineBox.Items.Add(new ComboBoxItem { Tag = index,
                            Content = $"{index} · {selected.Original.Lines[index - 1].Text}" });
                SplitLineBox.SelectedIndex = SplitLineBox.Items.Count > 0 ? 0 : -1;
            }
            SplitLineBox.IsEnabled = SplitLineBox.Items.Count > 0 && _groupSelection.Count <= 1;
            ResultBox.Text = selected?.Translation ?? "";
            if (_manualEditRegion != selected?.Id || _manualEditRevision != selected?.Revision
                || ManualTranslationBox.Text == _manualEditBase)
            {
                _manualEditRegion = selected?.Id;
                _manualEditRevision = selected?.Revision ?? 0;
                _manualEditBase = selected?.Translation ?? "";
                var draft = selected is not null
                    && _manualDrafts.TryGetValue(selected.Id, out var saved)
                    && saved.Revision == selected.Revision ? saved.Text : _manualEditBase;
                if (selected is not null && draft == _manualEditBase) _manualDrafts.Remove(selected.Id);
                if (ManualTranslationBox.Text != draft) ManualTranslationBox.Text = draft;
            }
            else _manualEditBase = selected?.Translation ?? "";
            ManualTranslationHelp.Text = T("模型译错时，可在这里修改译文并应用到当前译图；原始 OCR 和模型尝试仍可查。关闭后不保存。",
                "Edit an incorrect translation here and apply it to this image. Original OCR and model attempts remain visible. Edits are not saved after closing.");
            ApplyManualTranslationButton.Content = T("应用修正译文", "Apply corrected translation");
            RestoreTranslationButton.Content = T("撤销本次译文修正", "Undo translation edit");
            System.Windows.Automation.AutomationProperties.SetName(ManualTranslationBox, T("修正当前区域译文", "Edit this region's translation"));
            ManualTranslationBox.IsEnabled = selected is not null;
            ApplyManualTranslationButton.IsEnabled = selected is not null
                && !string.IsNullOrWhiteSpace(ManualTranslationBox.Text)
                && ManualTranslationBox.Text != selected.Translation;
            RestoreTranslationButton.IsEnabled = selected?.ManualBaseline is not null;
            var needsReview = selected is not null && WorkbenchOcrEvidence.NeedsStructureReview(selected.Original);
            OcrReviewText.Text = needsReview ? T("区域结构需核对：请对照原图检查文字和分段。修正草稿不会改变原始识别记录。") : "";
            OcrReviewText.Visibility = needsReview ? Visibility.Visible : Visibility.Collapsed;
            EvidenceBox.Text = selected is null ? "" : string.Join(Environment.NewLine,
                new[] { WorkbenchOcrEvidence.Describe(selected.Original, InterfaceLanguage),
                    T("以下记录仅在本会话中保留；路线不等于已核验模型版本。") }
                .Concat(session!.Attempts.Where(attempt => attempt.RegionId == selected.Id).Reverse().Select(attempt =>
                    F("尝试 {0} · 原文版本 {1} · {2} → {3}\n", attempt.Attempt, attempt.Revision, attempt.SourceLanguage, attempt.TargetLanguage)
                    + $"{T(TranslationRouteCatalog.Get(attempt.RequestedRoute).ShortName)} · {attempt.ElapsedMilliseconds} ms · "
                    + (attempt.Completed ? attempt.CurrentAtCompletion ? T("完成时有效") : T("已失效，未用于当前结果") : T("进行中"))
                    + (attempt.ExecutionEvidence is { } evidence
                        ? F("\n调用引擎：{0} / {1}\n请求后端：{2}（未核验设备驻留）\n模型校验：{3}", evidence.EngineId, evidence.ModelId, evidence.RequestedBackend, evidence.VerifiedModelSha256 ?? T("未核验"))
                            + (evidence.ActualBackend is null ? "" : "\n" + T("推理完成后端：") + evidence.ActualBackend)
                            + (evidence.BackendFallbackReason is null ? "" : "\n" + T("后端回退原因：") + evidence.BackendFallbackReason)
                        : T("\n实际模型：未提供执行证据（可能未调用模型）"))
                    + F("\n结果：{0}\n输入：{1}", attempt.Status, attempt.SourceText))));
            RegionHeading.Text = selected is null ? T("识别后选择文字区域") : F("区域 {0}", Array.FindIndex(regions.ToArray(), region => region.Id == selected.Id) + 1);
            RegionStatus.Text = selected is null ? "" : StateLabel(selected.State);
            if (selected?.ManualBaseline is not null)
                RegionStatus.Text += T(" · 译文经手动修正", " · Translation edited manually");
            if (selected?.State == WorkbenchRegionState.Failed && selected.Detail is
                "machine-dependency-unavailable" or "machine-runtime-load-failed" or "machine-component-unavailable")
                RegionStatus.Text += T("：本地翻译运行环境缺失或无法加载。请修复本地组件后重试，原文仍保留。");
            if (selected?.State == WorkbenchRegionState.LayoutRejected) RegionStatus.Text += selected.Detail == "identity-pixels-preserved"
                ? T("：账号信息保留原图，避免覆盖徽标；译文仍可阅读和复制。")
                : T("：图片内放不下或背景不适合替换，译文仍可阅读和复制。");
            DraftBox.IsEnabled = RestoreButton.IsEnabled = PreserveButton.IsEnabled = selected is not null;
            TranslateButton.IsEnabled = selected is not null && !string.IsNullOrWhiteSpace(selected.Draft) && HasAvailableLanguage(selected);
            CancelButton.IsEnabled = selected?.State == WorkbenchRegionState.Translating;
            CopyButton.IsEnabled = !string.IsNullOrEmpty(selected?.Translation);
            CopySourceButton.IsEnabled = !string.IsNullOrEmpty(selected?.Draft);
            CopyAllSourceButton.IsEnabled = regions.Any(region => !string.IsNullOrEmpty(region.Draft));
            CopyAllButton.IsEnabled = regions.Any(region => region.Translation.Length > 0);
            TranslateAllButton.IsEnabled = !_translatingAll && regions.Any(region => CanTranslate(region) && HasAvailableLanguage(region));
            RecognizeButton.IsEnabled = session is null && _document.State != WorkbenchRecognitionState.Recognizing;
            StopOcrButton.IsEnabled = _document.State == WorkbenchRecognitionState.Recognizing;
            StatusText.Text = _document.State switch
            {
                WorkbenchRecognitionState.Ready => T("原图已保留。点击识别开始。"),
                WorkbenchRecognitionState.Recognizing => T("正在本地识别文字…"),
                WorkbenchRecognitionState.Empty => T("未识别到文字。原图仍保留，可以重试。"),
                WorkbenchRecognitionState.Failed => T("识别失败。原图仍保留，可调整识别语言后重试。 ") + _document.RecognitionDetail,
                WorkbenchRecognitionState.Cancelled => T("识别已取消，可以重试。"),
                _ => F("共 {0} 个区域 · {1} · 目标语言 {2}", regions.Count, T(TranslationRouteCatalog.Get(_document.Route).ShortName), _document.TargetLanguage)
            };
        }
        finally { _refreshing = false; }
        RefreshSplitPreview();
        CommandManager.InvalidateRequerySuggested();
    }

    private static bool CanTranslate(WorkbenchRegion region) => region.State is WorkbenchRegionState.Pending
        or WorkbenchRegionState.Modified or WorkbenchRegionState.Failed or WorkbenchRegionState.Cancelled
        && !string.IsNullOrWhiteSpace(region.Draft);
    private void RefreshImage(IReadOnlyList<WorkbenchRegion> regions)
    {
        if (!RegionCanvas.Children.OfType<Button>().Select(button => (Guid)button.Tag).SequenceEqual(regions.Select(region => region.Id)))
            RegionCanvas.Children.Clear();
        foreach (var region in regions)
        {
            var existing = RegionCanvas.Children.OfType<Button>().FirstOrDefault(button => (Guid)button.Tag == region.Id);
            if (existing is not null)
            {
                existing.BorderBrush = region.Id == _selected ? Brushes.DeepSkyBlue : Brushes.SlateGray;
                existing.BorderThickness = new Thickness(region.Id == _selected ? 2 : 1);
                existing.ToolTip = region.Draft;
                System.Windows.Automation.AutomationProperties.SetName(existing, F("选择区域：{0}", region.Draft));
                continue;
            }
            var bounds = region.Original.Bounds;
            var button = new Button
            {
                Tag = region.Id, Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height),
                Padding = new Thickness(0), Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderBrush = region.Id == _selected ? Brushes.DeepSkyBlue : Brushes.SlateGray,
                BorderThickness = new Thickness(region.Id == _selected ? 2 : 1),
                ToolTip = region.Draft
            };
            // An explicit template keeps the source visible beneath standard button hover states.
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            System.Windows.Automation.AutomationProperties.SetName(button, F("选择区域：{0}", region.Draft));
            button.Click += (_, _) => { _groupSelection.Clear(); _selected = region.Id; Refresh(); };
            Canvas.SetLeft(button, bounds.X); Canvas.SetTop(button, bounds.Y);
            RegionCanvas.Children.Add(button);
        }
        var layouts = regions.Where(region => region.State == WorkbenchRegionState.Translated && region.Layout is not null)
            .Select(region => region.Layout!).ToArray();
        if (_renderedLayouts is null || layouts.Length != _renderedLayouts.Length
            || layouts.Where((layout, index) => !ReferenceEquals(layout, _renderedLayouts[index])).Any())
        {
            _renderedLayouts = layouts;
            _renderedTranslation = null;
        }
        if (TranslatedView.IsChecked == true)
            _renderedTranslation ??= TranslatedSelectionRenderer.RenderOverlay(
                (int)ImageSurface.Width, (int)ImageSurface.Height, layouts);
        TranslationImage.Source = TranslatedView.IsChecked == true ? _renderedTranslation : null;
    }
    private void ViewChanged(object sender, RoutedEventArgs e) { if (!_closed && IsInitialized) Refresh(); }
    private void OcrLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _closed || OcrLanguageBox.SelectedItem is not WorkbenchLanguageOption option) return;
        _document.ConfigureOcrLanguage(option.Code == "auto" ? null : option.Code);
    }
    private bool HasAvailableLanguage(WorkbenchRegion region) =>
        (_fileCheck is null || _fileCheck.MissingFiles.Count == 0)
        && WorkbenchLanguageOptions.Sources(region.Route, _document.CommonMachineModel).Any(language => language.Code == WorkbenchLanguageOptions.Code(region.SourceLanguage))
        && WorkbenchLanguageOptions.Targets(region.Route).Any(language => language.Code == WorkbenchLanguageOptions.Code(region.TargetLanguage));
    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _closed || _selected is not Guid id
            || SourceLanguageBox.SelectedItem is not WorkbenchLanguageOption source
            || TargetLanguageBox.SelectedItem is not WorkbenchLanguageOption target) return;
        _document.Session?.Configure(id, source.Code, target.Code, _document.Route);
    }
    private string StateLabel(WorkbenchRegionState state) => state switch
    {
        WorkbenchRegionState.Pending => T("待翻译"), WorkbenchRegionState.Modified => T("原文已修改"),
        WorkbenchRegionState.Translating => T("翻译中"), WorkbenchRegionState.Translated => T("已翻译"),
        WorkbenchRegionState.Preserved => T("保留原文"), WorkbenchRegionState.Failed => T("翻译失败，可重试"),
        WorkbenchRegionState.LayoutRejected => T("译文已取得"), _ => T("已取消")
    };
    private async void RecognizeClick(object sender, RoutedEventArgs e) => await _document.RecognizeAsync();
    private void StopOcrClick(object sender, RoutedEventArgs e) => _document.CancelRecognition();
    private async void TranslateClick(object sender, RoutedEventArgs e)
    {
        if (_selected is Guid id && _document.Session?.Regions.FirstOrDefault(region => region.Id == id) is { } region
            && HasAvailableLanguage(region)) await _document.TranslateAsync(id);
    }
    private async void TranslateAllClick(object sender, RoutedEventArgs e)
    {
        if (_translatingAll || _closed) return;
        _translatingAll = true;
        var ids = _document.Session?.Regions.Where(region => CanTranslate(region) && HasAvailableLanguage(region)).Select(region => region.Id).ToArray() ?? [];
        Refresh();
        try
        {
            foreach (var id in ids)
            {
                if (_closed) break;
                var current = _document.Session?.Regions.FirstOrDefault(region => region.Id == id);
                if (current is null || !CanTranslate(current) || !HasAvailableLanguage(current)) continue;
                await _document.TranslateAsync(id);
            }
        }
        finally { _translatingAll = false; if (!_closed) Refresh(); }
    }
    private void RegionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _groupSelection.Clear();
        foreach (var item in RegionList.SelectedItems.Cast<ListBoxItem>()) _groupSelection.Add((Guid)item.Tag);
        if (e.AddedItems.OfType<ListBoxItem>().LastOrDefault()?.Tag is Guid id) _selected = id;
        else if (_selected is Guid selected && !_groupSelection.Contains(selected))
            _selected = _groupSelection.Count > 0 ? _groupSelection.First() : null;
        Refresh();
    }
    private void MergeClick(object sender, RoutedEventArgs e)
    {
        if (!MergeButton.IsEnabled || _document.Session is not { } session) return;
        _selected = session.Merge(_groupSelection.ToArray());
        _groupSelection.Clear();
        Refresh();
    }
    private void SplitSelectionChanged(object sender, RoutedEventArgs e)
    { if (!_refreshing && !_closed && IsInitialized) RefreshSplitPreview(); }
    private void RefreshSplitPreview()
    {
        if (SplitButton is null) return;
        SplitButton.IsEnabled = false;
        SplitPreviewBox.Text = T("选择一个含多行识别文字的区域，并把草稿光标放在两部分之间。");
        if (_selected is not Guid id || _document.Session is not { } session || _groupSelection.Count > 1
            || SplitLineBox.SelectedItem is not ComboBoxItem { Tag: int count }) return;
        try
        {
            var parts = session.PreviewSplit(id, count, DraftBox.SelectionStart);
            SplitPreviewBox.Text = string.Join("\n\n", parts.Select((part, index) =>
                F("第 {0} 部分\n原始识别：{1}\n草稿：{2}", index + 1, part.Original.SourceText, part.Draft)));
            SplitButton.IsEnabled = !_translatingAll;
        }
        catch (ArgumentException) { }
    }
    private void SplitClick(object sender, RoutedEventArgs e)
    {
        if (!SplitButton.IsEnabled || _selected is not Guid id || _document.Session is not { } session
            || SplitLineBox.SelectedItem is not ComboBoxItem { Tag: int count }) return;
        _selected = session.Split(id, count, DraftBox.SelectionStart)[0];
        _groupSelection.Clear();
        Refresh();
    }
    private void UndoGroupClick(object sender, RoutedEventArgs e)
    { if (UndoGroupButton.IsEnabled) { _document.Session?.UndoGrouping(); _groupSelection.Clear(); Refresh(); } }
    private void RedoGroupClick(object sender, RoutedEventArgs e)
    { if (RedoGroupButton.IsEnabled) { _document.Session?.RedoGrouping(); _groupSelection.Clear(); Refresh(); } }
    private void DraftChanged(object sender, TextChangedEventArgs e)
    { if (!_refreshing && !_closed && _selected is Guid id) _document.Session?.Edit(id, DraftBox.Text); }
    private void RestoreClick(object sender, RoutedEventArgs e) { if (_selected is Guid id) _document.Session?.RestoreOriginal(id); }
    private void ManualTranslationChanged(object sender, TextChangedEventArgs e)
    {
        if (_refreshing || _closed) return;
        var selected = _document.Session?.Regions.FirstOrDefault(region => region.Id == _selected);
        if (selected is not null)
        {
            if (ManualTranslationBox.Text == selected.Translation) _manualDrafts.Remove(selected.Id);
            else _manualDrafts[selected.Id] = (selected.Revision, ManualTranslationBox.Text);
        }
        ApplyManualTranslationButton.IsEnabled = selected is not null
            && !string.IsNullOrWhiteSpace(ManualTranslationBox.Text)
            && ManualTranslationBox.Text != selected.Translation;
    }
    private void ApplyManualTranslationClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not Guid id || !ApplyManualTranslationButton.IsEnabled) return;
        _document.EditTranslation(id, ManualTranslationBox.Text);
        _manualDrafts.Remove(id);
        _manualEditRegion = null;
        Refresh();
    }
    private void RestoreTranslationClick(object sender, RoutedEventArgs e)
    {
        if (_selected is not Guid id) return;
        _document.Session?.RestoreTranslationBeforeManualEdit(id);
        _manualDrafts.Remove(id);
        _manualEditRegion = null;
        Refresh();
    }
    private void PreserveClick(object sender, RoutedEventArgs e) { if (_selected is Guid id) _document.Session?.Preserve(id); }
    private void CancelClick(object sender, RoutedEventArgs e) { if (_selected is Guid id) _document.Session?.Cancel(id); }
    private void CopyClick(object sender, RoutedEventArgs e) => Copy(ResultBox.Text);
    private void CopySourceClick(object sender, RoutedEventArgs e) => Copy(DraftBox.Text, T("原文已复制（包含本次修正）。"));
    private void CopyAllSourceClick(object sender, RoutedEventArgs e) => Copy(string.Join(Environment.NewLine + Environment.NewLine,
        _document.Session?.Regions.Where(region => region.Draft.Length > 0).Select(region => region.Draft) ?? []), T("原文已复制（包含本次修正）。"));
    private void CopyAllClick(object sender, RoutedEventArgs e) => Copy(string.Join(Environment.NewLine + Environment.NewLine,
        _document.Session?.Regions.Where(region => region.Translation.Length > 0).Select(region => region.Translation) ?? []));
    private void Copy(string text, string? status = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { _copy(text); StatusText.Text = status ?? T("译文已复制。"); }
        catch { StatusText.Text = T("剪贴板暂不可用，请重试。"); }
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("导出选区译图"), Filter = T("PNG 图片") + "|*.png", DefaultExt = ".png",
            FileName = "ClipRay-translation.png", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var bitmap = WorkbenchImageExport.Render(_document);
            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
            StatusText.Text = T("译图已导出；未能安全排版的区域保留原图。");
        }
        catch { StatusText.Text = T("导出失败，请检查保存位置后重试。"); }
    }
}

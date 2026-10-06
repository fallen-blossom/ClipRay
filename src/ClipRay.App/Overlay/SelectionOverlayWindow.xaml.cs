using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipRay.App.Capture;
using ClipRay.App.Editing;
using ClipRay.App.Scrolling;
using ClipRay.App.Recording;
using ClipRay.App.Settings;
using ClipRay.App.Translation;
using ClipRay.App.LiveTranslation;
using ClipRay.Core.Geometry;
using DrawingRectangle = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace ClipRay.App.Overlay;

public partial class SelectionOverlayWindow : Window
{
    private const double MinimumSelectionPixels = 8;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly CapturedFrame _frame;
    private readonly PixelRect _pixelBounds;
    private readonly Ellipse[] _handles;
    private readonly WindowSnapService _windowSnapService;
    private readonly DoubleEscapePolicy _doubleEscapePolicy = new();
    private readonly DispatcherTimer _escapeHintTimer;
    private readonly bool _showPreviewHint;
    private readonly IClipboardTextSink _clipboardTextSink;
    private readonly AppSettings _interactionSettings;
    private readonly OverlayLaunchMode _launchMode;
    private InteractionMode _mode;
    private ResizeHandle _activeHandle;
    private PixelPoint _dragStart;
    private PixelRect _selection;
    private PixelRect _selectionAtDragStart;
    private PixelRect _snapSelection;
    private EditLayer? _editLayer;
    private List<PixelPoint>? _activeEditPoints;
    private EditTool _activeEditTool;
    private IntPtr _windowHandle;
    private bool _isSnapProposal;
    private readonly OverlaySnapInteraction _snapInteraction = new();
    private CancellationTokenSource? _snapRefinementDelay;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly OverlayTranslationSequencer _translationSequencer = new();
    private TranslationDisplayState? _translationDisplayState;
    private bool _longCaptureActive;
    private bool _moveToolActive;
    private IReadOnlyList<PixelRect> _snapCandidates = [];
    private int _snapCandidateIndex;
    private PixelPoint _lastSnapPoint;
    private SnapDetectionMode _snapDetectionMode = SnapDetectionMode.WindowFirst;
    private TranslationRouteKind _translationRoute;

    public SelectionOverlayWindow(
        CapturedFrame frame,
        PixelRect? initialSelection = null,
        bool showPreviewHint = false,
        IClipboardTextSink? clipboardTextSink = null,
        AppSettings? interactionSettings = null,
        OverlayLaunchMode launchMode = OverlayLaunchMode.Capture)
    {
        _frame = frame;
        _pixelBounds = new PixelRect(0, 0, frame.Bitmap.Width, frame.Bitmap.Height);
        _selection = initialSelection is { } selection ? ClampSelection(selection) : default;
        _showPreviewHint = showPreviewHint;
        _clipboardTextSink = clipboardTextSink ?? new WpfClipboardTextSink();
        _interactionSettings = interactionSettings ?? new AppSettings();
        _launchMode = launchMode;
        _translationRoute = ResolveConfiguredRoute(_interactionSettings);
        _windowSnapService = new WindowSnapService(new PixelRect(
            frame.ScreenBounds.X,
            frame.ScreenBounds.Y,
            frame.ScreenBounds.Width,
            frame.ScreenBounds.Height));
        _escapeHintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _escapeHintTimer.Tick += OnEscapeHintTimerTick;
        InitializeComponent();
        CaptureLocalization.ApplyStatic(this, _interactionSettings.InterfaceLanguage);
        foreach (var entry in new[] { (PenWidthInput, "画笔粗细", "Pen width"), (MosaicBrushInput, "马赛克笔刷大小", "Mosaic brush size"),
            (MosaicBlockInput, "马赛克像素大小", "Mosaic pixel size") })
            System.Windows.Automation.AutomationProperties.SetName(entry.Item1, AppInterfaceLanguage.Text(_interactionSettings.InterfaceLanguage, entry.Item2, entry.Item3));
        System.Windows.Automation.AutomationProperties.SetName(SelectionWidthInput, AppInterfaceLanguage.Text(_interactionSettings.InterfaceLanguage, "选区宽度", "Selection width"));
        System.Windows.Automation.AutomationProperties.SetName(SelectionHeightInput, AppInterfaceLanguage.Text(_interactionSettings.InterfaceLanguage, "选区高度", "Selection height"));
        System.Windows.Automation.AutomationProperties.SetName(ExtractedTextBox, T("文字提取"));
        if (_launchMode == OverlayLaunchMode.Recording)
        {
            CaptureEditingTools.Visibility = Visibility.Collapsed;
        }

        _handles =
        [
            TopLeftHandle,
            TopHandle,
            TopRightHandle,
            RightHandle,
            BottomRightHandle,
            BottomHandle,
            BottomLeftHandle,
            LeftHandle,
        ];

        FrozenImage.Source = BitmapSourceFactory.Create(frame.Bitmap);
        SourceInitialized += PositionOverCapturedScreen;
        Loaded += (_, _) =>
        {
            Focus();
            UpdateVisuals();
            if (_showPreviewHint)
            {
                EscapeHint.Visibility = Visibility.Visible;
            }
            if (_launchMode == OverlayLaunchMode.Recording)
            {
                ToolStatusText.Text = T("请框选录屏区域，按 Enter 或点击确认开始录屏");
                ToolStatus.Visibility = Visibility.Visible;
            }
        };
        Closed += (_, _) =>
        {
            if (TranslateToolButton.ContextMenu is { } routeMenu)
            {
                routeMenu.IsOpen = false;
                TranslateToolButton.ContextMenu = null;
            }
            CancelSnapRefinement();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _translationSequencer.Close();
            _escapeHintTimer.Stop();
            _frame.Dispose();
        };
        AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnMouseLeftButtonDown),
            handledEventsToo: true);
        ToolFeedbackPanel.SizeChanged += (_, _) => PositionToolFeedbackForCurrentToolbar();
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseWheel += OnMouseWheel;
    }

    public event EventHandler<Bitmap>? CaptureConfirmed;

    private string T(string source) => CaptureLocalization.Text(_interactionSettings.InterfaceLanguage, source);
    public Func<Bitmap, bool>? CaptureDelivery { get; set; }

    public event EventHandler<RoutedUICommand>? ToolRequested;

    public event EventHandler<LongCaptureRequest>? LongCaptureRequested;

    public event EventHandler? LongCaptureStopRequested;

    public event EventHandler<RegionRecordingRequest>? RecordingRequested;

    /// <summary>The subscriber owns and must dispose each delivered capture.</summary>
    public event EventHandler<TranslationCapture>? TranslationRequested;
    public event EventHandler<LiveTranslationStartRequest>? LiveTranslationRequested;

    public event EventHandler<TranslationCapture>? TextExtractionRequested;

    public CancellationToken LifetimeToken => _lifetime.Token;

    private void PositionOverCapturedScreen(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var bounds = _frame.ScreenBounds;
        SetWindowPos(_windowHandle, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpShowWindow);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var point = ToPixel(e.GetPosition(OverlayCanvas));
        var pressedSnapProposal = _isSnapProposal && Contains(_selection, point);
        StopSnapTracking();

        if (IsControlSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var isResizeHandle = TryGetHandle(e.OriginalSource as DependencyObject, out var resizeHandle);
        if (_activeEditTool == EditTool.Picker && OverlayPickerInputRouter.TryRoutePicker(
                _activeEditTool,
                _selection,
                point,
                isResizeHandle,
                _frame.Bitmap,
                EnsureEditLayer(),
                _clipboardTextSink,
                out var pickerResult))
        {
            ApplyPickerResult(pickerResult);
            e.Handled = true;
            return;
        }

        if (OverlayMovePolicy.ShouldBeginLockedMove(_moveToolActive, _selection, point, 7 * PixelScaleX))
        {
            _dragStart = point;
            _selectionAtDragStart = _selection;
            _mode = InteractionMode.Move;
            Cursor = System.Windows.Input.Cursors.SizeAll;
            CaptureMouse();
            ResetDoubleEscape();
            e.Handled = true;
            return;
        }

        if (_moveToolActive)
        {
            e.Handled = true;
            return;
        }

        if (OverlayInputPolicy.ShouldBeginEdit(
                _activeEditTool,
                isResizeHandle,
                !_selection.IsEmpty && SelectionDrawableBounds.Contains(_selection, point)))
        {
            _activeEditPoints = [];
            StrokePointCollection.AppendClampedDistinct(_activeEditPoints, point, _selection);
            _mode = InteractionMode.Edit;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        _dragStart = point;
        _selectionAtDragStart = _selection;
        ResetDoubleEscape();

        if (pressedSnapProposal)
        {
            _mode = InteractionMode.PendingSnapDecision;
            _snapSelection = _selection;
            _isSnapProposal = false;
        }
        else if (isResizeHandle)
        {
            _mode = InteractionMode.Resize;
            _activeHandle = resizeHandle;
        }
        else if (!_selection.IsEmpty && Contains(_selection, point))
        {
            _mode = InteractionMode.PendingRedraw;
        }
        else
        {
            _mode = InteractionMode.Draw;
            _selection = new PixelRect(point.X, point.Y, 0, 0);
            _isSnapProposal = false;
        }

        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var point = Clamp(ToPixel(e.GetPosition(OverlayCanvas)));
        if (_mode == InteractionMode.None)
        {
            UpdateMosaicBrushCursor(point);
            UpdatePickerHud(point);
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (_moveToolActive)
                {
                    Cursor = OverlayMovePolicy.ShouldBeginLockedMove(true, _selection, point, 7 * PixelScaleX)
                        ? System.Windows.Input.Cursors.SizeAll
                        : System.Windows.Input.Cursors.Arrow;
                }
                else
                {
                    UpdateSnapProposal(point);
                }
            }

            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (_mode == InteractionMode.Edit)
        {
            if (SelectionDrawableBounds.Contains(_selection, point))
            {
                StrokePointCollection.AppendClampedDistinct(_activeEditPoints!, point, _selection);
                if (_activeEditTool == EditTool.Mosaic) UpdateEditPreview();
            }

            UpdateMosaicBrushCursor(point);

            e.Handled = true;
            return;
        }

        if (_mode == InteractionMode.PendingRedraw)
        {
            if (!HasMaterialDrag(point))
            {
                e.Handled = true;
                return;
            }

            _mode = InteractionMode.Draw;
            _selection = SelectionMath.FromPoints(_dragStart, point);
            _isSnapProposal = false;
        }

        if (_mode == InteractionMode.PendingSnapDecision)
        {
            var outcome = OverlaySnapInteraction.ResolveProposalPress(
                _dragStart,
                point,
                released: false,
                MinimumSelectionPixels);
            if (outcome == OverlaySnapPressOutcome.Pending)
            {
                e.Handled = true;
                return;
            }

            _mode = InteractionMode.Draw;
            _selection = SelectionMath.FromPoints(_dragStart, point);
        }

        _selection = _mode switch
        {
            InteractionMode.Draw => SelectionMath.FromPoints(_dragStart, point),
            InteractionMode.Move => SelectionMath.Move(
                _selectionAtDragStart,
                point.X - _dragStart.X,
                point.Y - _dragStart.Y,
                _pixelBounds),
            InteractionMode.Resize => SelectionMath.Resize(
                _selectionAtDragStart,
                _activeHandle,
                point,
                _pixelBounds,
                MinimumSelectionPixels),
            _ => _selection,
        };

        UpdateVisuals();
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == InteractionMode.None)
        {
            return;
        }

        if (_mode == InteractionMode.Edit)
        {
            StrokePointCollection.AppendClampedDistinct(
                _activeEditPoints!,
                Clamp(ToPixel(e.GetPosition(OverlayCanvas))),
                _selection);
            CommitActiveEdit();
        }

        if (_mode == InteractionMode.PendingSnapDecision)
        {
            _selection = _snapSelection;
        }

        _mode = InteractionMode.None;
        _activeHandle = ResizeHandle.None;
        Cursor = _moveToolActive ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Cross;
        ReleaseMouseCapture();
        ResetDoubleEscape();
        UpdateVisuals();
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = HotkeyCapturePolicy.ResolveEventKey(
            e.Key,
            e.SystemKey,
            e.ImeProcessedKey,
            e.DeadCharProcessedKey);
        var modifiers = HotkeyCapturePolicy.ReadCurrentModifiers();
        UpdateSnapDetectionMode(modifiers);
        if (key is Key.LeftCtrl or Key.RightCtrl)
        {
            e.Handled = true;
            return;
        }
        if (!_longCaptureActive && OverlayLaunchInputPolicy.IsImmediateCancel(_launchMode, key, modifiers))
        {
            Close();
            e.Handled = true;
            return;
        }

        if (!_longCaptureActive && OverlayLaunchInputPolicy.IsConfirm(
                _launchMode,
                _interactionSettings.ConfirmCaptureHotkey,
                _interactionSettings.ConfirmCaptureSecondaryHotkey,
                key,
                modifiers))
        {
            ConfirmSelection();
            e.Handled = true;
            return;
        }

        if (_longCaptureActive && HotkeyCapturePolicy.MatchesAny(
                _interactionSettings.StopLongCaptureHotkey,
                _interactionSettings.StopLongCaptureSecondaryHotkey,
                key,
                modifiers))
        {
            LongCaptureStopRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (!_longCaptureActive
            && key == Key.Z
            && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RedoLastEdit();
            e.Handled = true;
            return;
        }

        if (!_longCaptureActive
            && key == Key.Z
            && modifiers == ModifierKeys.Control)
        {
            UndoLastEdit();
            e.Handled = true;
            return;
        }

        if (!HotkeyCapturePolicy.MatchesAny(
                _interactionSettings.CancelCaptureHotkey,
                _interactionSettings.CancelCaptureSecondaryHotkey,
                key,
                modifiers)) return;

        if (_doubleEscapePolicy.Press(DateTimeOffset.UtcNow) is DoubleEscapeResult.Close)
        {
            Close();
        }
        else
        {
            ShowEscapeHint();
        }

        e.Handled = true;
    }

    private void UndoLastEdit()
    {
        if (_editLayer?.UndoLast() != true) return;
        ToolStatusText.Text = T("已撤回上一步");
        ToolStatus.Visibility = Visibility.Visible;
        UpdateEditPreview();
    }

    private void RedoLastEdit()
    {
        if (_editLayer?.RedoLast() != true) return;
        ToolStatusText.Text = T("已重做上一步");
        ToolStatus.Visibility = Visibility.Visible;
        UpdateEditPreview();
    }

    private void OnOverlayToolExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == OverlayTool.Cancel)
        {
            Close();
        }
        else if (e.Command == OverlayTool.Confirm)
        {
            ConfirmSelection();
        }
        else if (e.Command == OverlayTool.Move)
        {
            SelectMoveTool();
        }
        else if (e.Command == OverlayTool.Pen)
        {
            SelectEditingTool(EditTool.Pen);
        }
        else if (e.Command == OverlayTool.Mosaic)
        {
            SelectEditingTool(EditTool.Mosaic);
        }
        else if (e.Command == OverlayTool.Picker)
        {
            SelectEditingTool(EditTool.Picker);
        }
        else if (e.Command == OverlayTool.ExtractText)
        {
            RequestTextExtraction();
        }
        else if (e.Command == OverlayTool.LongCapture)
        {
            StartLongCapture(ScrollCaptureMode.Manual);
        }
        else if (e.Command == OverlayTool.Translate)
        {
            RequestTranslation();
        }
        else if (e.Command == OverlayTool.LiveTranslate)
        {
            if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels) return;
            var request = new LiveTranslationStartRequest(new PixelRect(
                _selection.X + _frame.ScreenBounds.X, _selection.Y + _frame.ScreenBounds.Y,
                _selection.Width, _selection.Height), ResolveConfiguredLiveRoute(_interactionSettings),
                _interactionSettings.TranslationTargetLanguage);
            Close();
            LiveTranslationRequested?.Invoke(this, request);
        }
        else if (e.Command == OverlayTool.Record)
        {
            RequestRecording();
        }
        else if (e.Command == OverlayTool.Undo)
        {
            UndoLastEdit();
        }
        else if (e.Command is RoutedUICommand command)
        {
            ToolRequested?.Invoke(this, command);
            ToolStatusText.Text = $"{command.Text}将在后续任务提供";
            ToolStatus.Visibility = Visibility.Visible;
        }

        e.Handled = true;
    }

    public void ShowLongCaptureChoicesForPreview() => ShowLongCaptureChoice();

    public void ShowLongCaptureProgress(int frameCount, string status)
    {
        ToolStatusText.Text = $"{status}：{frameCount} 帧";
        ToolStatus.Visibility = Visibility.Visible;
        StopLongCaptureButton.Visibility = Visibility.Visible;
    }

    public void RestoreAfterLongCapture(string status)
    {
        _longCaptureActive = false;
        IsHitTestVisible = true;
        StopLongCaptureButton.Visibility = Visibility.Collapsed;
        ToolStatusText.Text = status;
        ToolStatus.Visibility = Visibility.Visible;
        Show();
        Activate();
    }

    private void ShowLongCaptureChoice()
    {
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels) return;
        PenProperties.Visibility = Visibility.Collapsed;
        MosaicProperties.Visibility = Visibility.Collapsed;
        LongCaptureProperties.Visibility = Visibility.Visible;
        ToolProperties.Visibility = Visibility.Visible;
        ToolStatusText.Text = T("选择滚动方式");
        ToolStatus.Visibility = Visibility.Visible;
        UpdateVisuals();
    }

    private void OnManualLongCapture(object sender, RoutedEventArgs e) => StartLongCapture(ScrollCaptureMode.Manual);

    private void OnAutomaticLongCapture(object sender, RoutedEventArgs e) => StartLongCapture(ScrollCaptureMode.Automatic);

    private void OnStopLongCapture(object sender, RoutedEventArgs e) => LongCaptureStopRequested?.Invoke(this, EventArgs.Empty);

    private void StartLongCapture(ScrollCaptureMode mode)
    {
        var region = new DrawingRectangle(
            _frame.ScreenBounds.X + (int)_selection.X,
            _frame.ScreenBounds.Y + (int)_selection.Y,
            (int)_selection.Width,
            (int)_selection.Height);
        _longCaptureActive = true;
        LongCaptureRequested?.Invoke(this, new LongCaptureRequest(region, mode));
    }

    public bool ShowTranslation(long version, PixelRect selectionSnapshot, IReadOnlyList<InPlaceLayoutItem> items)
    {
        if (!IsLoaded || !_translationSequencer.IsLatest(version)) return false;
        TranslationLayer.Children.Clear();
        if (items.Count == 0)
        {
            _translationDisplayState = null;
            TranslationLayer.Visibility = Visibility.Collapsed;
            TranslationToggle.Visibility = Visibility.Collapsed;
            return true;
        }
        var mappedItems = items
            .Select(item => item with
            {
                Bounds = OverlayTranslationSequencer.MapToScreen(selectionSnapshot, item.Bounds),
            })
            .ToArray();
        foreach (var item in mappedItems)
        {
            TranslationLayer.Children.Add(TranslationOverlayElementFactory.CreatePositioned(
                item, PixelScaleX, PixelScaleY));
        }
        _translationDisplayState = new TranslationDisplayState(TranslationDisplayMode.Translated, mappedItems);
        ApplyTranslationDisplayState();
        TranslationToggle.Visibility = Visibility.Visible;
        return true;
    }

    public void ShowTranslationStatus(string status, string? privacyNotice = null)
    {
        ToolStatusText.Text = status;
        ToolStatusText.ToolTip = privacyNotice;
        ToolStatus.Visibility = Visibility.Visible;
    }

    public void HideTranslationStatus()
    {
        ToolStatusText.ToolTip = null;
        ToolStatus.Visibility = Visibility.Collapsed;
    }

    public bool TryShowTranslationStatus(long version, string status, string? privacyNotice = null)
    {
        if (!IsLoaded || !_translationSequencer.IsLatest(version)) return false;
        ShowTranslationStatus(status, privacyNotice);
        return true;
    }

    private void OnTranslationToggle(object sender, RoutedEventArgs e)
    {
        if (_translationDisplayState is null)
        {
            var showTranslation = TranslationLayer.Visibility != Visibility.Visible;
            TranslationLayer.Visibility = showTranslation ? Visibility.Visible : Visibility.Collapsed;
            TranslationToggle.Content = showTranslation ? T("显示原文") : T("显示译文");
            return;
        }
        _translationDisplayState = _translationDisplayState.Toggle();
        ApplyTranslationDisplayState();
    }

    private void ApplyTranslationDisplayState()
    {
        if (_translationDisplayState is null) return;
        TranslationLayer.Visibility = _translationDisplayState.Mode == TranslationDisplayMode.Translated
            ? Visibility.Visible
            : Visibility.Collapsed;
        TranslationToggle.Content = T(_translationDisplayState.ToggleLabel);
    }

    private void RequestTranslation()
    {
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels) return;
        var request = _translationSequencer.Begin(_selection);
        var captureBounds = SelectionCapturePolicy.CreateOcrCaptureBounds(request.Selection, _pixelBounds);
        var bitmap = _frame.Bitmap.Clone(
            ToCropRectangle(captureBounds, _frame.Bitmap.Width, _frame.Bitmap.Height),
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        TranslationCaptureDispatcher.Dispatch(
            TranslationRequested,
            this,
            new TranslationCapture(request.Version, bitmap, request.Selection, captureBounds, _translationRoute));
    }

    private void OnTranslateToolRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowTranslationRouteMenuVisualPreview();
    }

    public void ShowTranslationRouteMenuVisualPreview()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = TranslateToolButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            Style = (Style)FindResource("DarkContextMenuStyle"),
        };
        foreach (var option in TranslationRouteCatalog.Options)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = T(option.DisplayName),
                IsCheckable = true,
                IsChecked = option.Kind == _translationRoute,
                Tag = option.Kind,
                Style = (Style)FindResource("DarkMenuItemStyle"),
            };
            item.Click += (_, _) =>
            {
                _translationRoute = (TranslationRouteKind)item.Tag;
                var routeName = T(TranslationRouteCatalog.Get(_translationRoute).ShortName);
                TranslateToolButton.ToolTip = AppInterfaceLanguage.Text(_interactionSettings.InterfaceLanguage,
                    $"翻译：{routeName}（右键切换）", $"Translate: {routeName} (right-click to change)");
            };
            menu.Items.Add(item);
        }
        TranslateToolButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    public void CloseTranslationRouteMenuVisualPreview()
    {
        if (TranslateToolButton.ContextMenu is not { } menu) return;
        menu.IsOpen = false;
        TranslateToolButton.ContextMenu = null;
    }

    private static TranslationRouteKind ResolveConfiguredRoute(AppSettings settings) =>
        settings.Provider == "Local LMT"
            ? TranslationRouteKind.LocalLmt
            : settings.Provider == "Machine"
                ? TranslationRouteKind.Machine
                : TranslationRouteKind.DeepSeekFlash;

    private static TranslationRouteKind ResolveConfiguredLiveRoute(AppSettings settings) =>
        settings.LiveTranslationProvider == "Machine"
            ? TranslationRouteKind.Machine
            : TranslationRouteKind.DeepSeekFlash;

    private void RequestTextExtraction()
    {
        if (TextExtractionPanel.Visibility == Visibility.Visible)
        {
            TextExtractionPanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels) return;
        var request = _translationSequencer.Begin(_selection);
        var captureBounds = SelectionCapturePolicy.CreateOcrCaptureBounds(request.Selection, _pixelBounds);
        var bitmap = _frame.Bitmap.Clone(
            ToCropRectangle(captureBounds, _frame.Bitmap.Width, _frame.Bitmap.Height),
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        ExtractedTextBox.Text = string.Empty;
        ExtractionStatusText.Text = T("正在运行本地 OCR 快速路径…");
        TextExtractionPanel.Visibility = Visibility.Visible;
        PositionTextExtractionPanel();
        UpdateVisuals();
        TranslationCaptureDispatcher.Dispatch(
            TextExtractionRequested,
            this,
            new TranslationCapture(request.Version, bitmap, request.Selection, captureBounds));
    }

    public bool ShowExtractedText(long version, string text, string status)
    {
        if (!IsLoaded || !_translationSequencer.IsLatest(version)) return false;
        ExtractedTextBox.Text = text;
        ExtractionStatusText.Text = status;
        TextExtractionPanel.Visibility = Visibility.Visible;
        PositionTextExtractionPanel();
        UpdateVisuals();
        return true;
    }

    public void ShowModernVisualPreview()
    {
        SelectEditingTool(EditTool.Mosaic);
        ExtractedTextBox.Text = "我要深色背景，现代、简约风格。\r\n线条图案装饰可以有，但不要显得陈旧。\r\n移动工具锁定尺寸后可拖动边框。\r\n文字提取必须按视觉行保留换行。";
        ExtractionStatusText.Text = "已识别 4 行 · PP-OCR 主路径";
        TextExtractionPanel.Visibility = Visibility.Visible;
        PositionTextExtractionPanel();
        UpdateMosaicBrushCursor(new PixelPoint(_selection.X + _selection.Width / 2, _selection.Y + _selection.Height / 2));
    }

    public void ShowColorVisualPreview() => SelectEditingTool(EditTool.Pen);

    public void ShowPickerVisualPreview()
    {
        SelectEditingTool(EditTool.Picker);
        UpdatePickerHud(new PixelPoint(_selection.X + _selection.Width / 2, _selection.Y + _selection.Height / 2));
    }

    public void ShowTranslationFocusVisualPreview()
    {
        var request = _translationSequencer.Begin(_selection);
        var firstBounds = new PixelRect(12, 10, Math.Max(40, _selection.Width - 24), 20);
        var secondBounds = new PixelRect(12, 38, Math.Max(40, _selection.Width - 24), 20);
        ShowTranslation(request.Version, request.Selection, [
            new InPlaceLayoutItem(
                new ClipRay.App.Ocr.OcrTextBlock("preview", firstBounds, []),
                "Translation stays in the frozen overlay",
                firstBounds,
                InPlaceBackgroundMode.ComplexBacking,
                System.Drawing.Color.FromArgb(210, 13, 18, 28),
                System.Drawing.Color.White),
            new InPlaceLayoutItem(
                new ClipRay.App.Ocr.OcrTextBlock("预览", secondBounds, []),
                "原位翻译 · 按整张截图坐标覆盖",
                secondBounds,
                InPlaceBackgroundMode.ComplexBacking,
                System.Drawing.Color.FromArgb(210, 13, 18, 28),
                System.Drawing.Color.White),
        ]);
    }

    private void OnCopyExtractedText(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ExtractedTextBox.Text)) return;
        var copied = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _clipboardTextSink.SetText(ExtractedTextBox.Text);
                copied = true;
                break;
            }
            catch (ExternalException)
            {
                if (attempt < 2) Thread.Sleep(25);
            }
        }

        ExtractionStatusText.Text = copied ? T("已按行复制到剪贴板") : T("剪贴板暂忙，请稍后重试");
    }

    private void OnCloseTextExtraction(object sender, RoutedEventArgs e)
    {
        _translationSequencer.CancelPending();
        TextExtractionPanel.Visibility = Visibility.Collapsed;
    }

    private void RequestRecording()
    {
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels) return;
        var region = new DrawingRectangle(
            _frame.ScreenBounds.X + (int)_selection.X,
            _frame.ScreenBounds.Y + (int)_selection.Y,
            (int)_selection.Width,
            (int)_selection.Height);
        IsHitTestVisible = false;
        Hide();
        RecordingRequested?.Invoke(this, new RegionRecordingRequest(region));
    }

    public void RestoreAfterRecordingFailure(string status)
    {
        IsHitTestVisible = true;
        ShowTranslationStatus(status);
        Show();
        Activate();
    }

    private void ConfirmSelection()
    {
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels)
        {
            return;
        }

        if (_launchMode == OverlayLaunchMode.Recording)
        {
            RequestRecording();
            return;
        }

        Bitmap bitmap;
        if (_translationDisplayState is { } translation)
        {
            if (translation.ExportMode == TranslationSelectionExportMode.Original)
            {
                bitmap = _editLayer is { } originalEdits && originalEdits.Selection == _selection
                    ? EditRenderer.Composite(_frame.Bitmap, originalEdits)
                    : _frame.Bitmap.Clone(
                        ToCropRectangle(_selection, _frame.Bitmap.Width, _frame.Bitmap.Height),
                        System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            }
            else
            {
                try
                {
                    var translated = TranslatedSelectionRenderer.Render(_frame.Bitmap, _selection, translation.Items);
                    if (_editLayer is { } translatedEdits && translatedEdits.Selection == _selection)
                    {
                        try
                        {
                            bitmap = EditRenderer.CompositeSelection(translated, _frame.Bitmap, translatedEdits);
                        }
                        finally
                        {
                            translated.Dispose();
                        }
                    }
                    else
                    {
                        bitmap = translated;
                    }
                }
                catch
                {
                    ShowTranslationStatus("译文截图生成失败，请切换显示原文后重试");
                    return;
                }
            }
        }
        else
        {
            bitmap = _editLayer is { } layer && layer.Selection == _selection
                ? EditRenderer.Composite(_frame.Bitmap, layer)
                : _frame.Bitmap.Clone(
                    ToCropRectangle(_selection, _frame.Bitmap.Width, _frame.Bitmap.Height),
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }
        if (CaptureDelivery is not null)
        {
            using (bitmap)
            {
                try { if (!CaptureDelivery(bitmap)) return; }
                catch
                {
                    ShowTranslationStatus("保存失败，编辑内容已保留；请重试保存或调整输出目录。");
                    return;
                }
            }
        }
        else CaptureConfirmed?.Invoke(this, bitmap);
        Close();
    }

    private void SelectEditingTool(EditTool tool)
    {
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels)
        {
            return;
        }

        _moveToolActive = false;
        MoveToolButton.Background = System.Windows.Media.Brushes.Transparent;
        var layer = EnsureEditLayer();
        if (_activeEditTool == tool)
        {
            layer.SwitchTool(EditTool.None);
            _activeEditTool = EditTool.None;
            PenProperties.Visibility = Visibility.Collapsed;
            MosaicProperties.Visibility = Visibility.Collapsed;
            SizeProperties.Visibility = Visibility.Collapsed;
            ToolProperties.Visibility = Visibility.Collapsed;
            PickerHud.Visibility = Visibility.Collapsed;
            ToolStatusText.Text = T("编辑模式已关闭：可拖动选区或调整手柄");
            ToolStatus.Visibility = Visibility.Visible;
            UpdateVisuals();
            return;
        }

        layer.SwitchTool(tool);
        _activeEditTool = tool;
        PenProperties.Visibility = tool is EditTool.Pen or EditTool.Picker ? Visibility.Visible : Visibility.Collapsed;
        MosaicProperties.Visibility = tool == EditTool.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        SizeProperties.Visibility = Visibility.Collapsed;
        ToolProperties.Visibility = tool == EditTool.Picker ? Visibility.Collapsed : Visibility.Visible;
        PenColorInput.Text = $"#{layer.PenColor.R:X2}{layer.PenColor.G:X2}{layer.PenColor.B:X2}";
        UpdateSelectedPenColor(layer.PenColor);
        if (tool != EditTool.Picker) PickerHud.Visibility = Visibility.Collapsed;
        ToolStatusText.Text = tool switch
        {
            EditTool.Pen => T("画笔：在选区内拖动绘制"),
            EditTool.Mosaic => T("马赛克：在选区内拖动涂抹"),
            _ => T("取色：点击冻结截图中的像素"),
        };
        ToolStatus.Visibility = Visibility.Visible;
        UpdateVisuals();
    }

    private void SelectMoveTool()
    {
        if (_selection.Width < MinimumSelectionPixels || _selection.Height < MinimumSelectionPixels) return;
        _moveToolActive = !_moveToolActive;
        _activeEditTool = EditTool.None;
        _editLayer?.SwitchTool(EditTool.None);
        PenProperties.Visibility = Visibility.Collapsed;
        MosaicProperties.Visibility = Visibility.Collapsed;
        SizeProperties.Visibility = Visibility.Collapsed;
        ToolProperties.Visibility = Visibility.Collapsed;
        MosaicBrushCursor.Visibility = Visibility.Collapsed;
        PickerHud.Visibility = Visibility.Collapsed;
        MoveToolButton.Background = _moveToolActive
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 75, 116))
            : System.Windows.Media.Brushes.Transparent;
        Cursor = _moveToolActive ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Cross;
        ToolStatusText.Text = _moveToolActive ? T("移动：尺寸已锁定，拖动框内或边框") : T("移动模式已关闭");
        ToolStatus.Visibility = Visibility.Visible;
        UpdateVisuals();
    }

    private EditLayer EnsureEditLayer()
    {
        if (_editLayer is null)
        {
            _editLayer = new EditLayer(_selection);
        }
        else
        {
            _editLayer.UpdateSelection(_selection);
        }

        return _editLayer;
    }

    private void CommitActiveEdit()
    {
        if (_activeEditPoints is not { Count: > 0 } points)
        {
            return;
        }

        var layer = EnsureEditLayer();
        if (_activeEditTool == EditTool.Pen)
        {
            layer.AddPenStroke(new PenStroke(points, layer.PenColor, layer.PenWidth));
        }
        else if (_activeEditTool == EditTool.Mosaic)
        {
            layer.AddMosaicStroke(new MosaicStroke(points, layer.MosaicBrushSize, layer.MosaicBlockSize));
        }

        _activeEditPoints = null;
        UpdateEditPreview();
    }

    private void ApplyPickerResult(SourceColorPickResult result)
    {
        PenColorInput.Text = result.Text;
        UpdateSelectedPenColor(EnsureEditLayer().PenColor);
        ToolStatusText.Text = result.Copied
            ? AppInterfaceLanguage.Text(_interactionSettings.InterfaceLanguage, $"已复制 {result.Text}", $"Copied {result.Text}")
            : AppInterfaceLanguage.Text(_interactionSettings.InterfaceLanguage, $"已取色 {result.Text}，剪贴板暂不可用", $"Picked {result.Text}; clipboard unavailable");
        ToolStatus.Visibility = Visibility.Visible;
    }

    private void OnPenColorChanged(object sender, TextChangedEventArgs e)
    {
        if (_editLayer is null)
        {
            return;
        }

        try
        {
            _editLayer.SetPenColor(System.Drawing.ColorTranslator.FromHtml(PenColorInput.Text));
            UpdateSelectedPenColor(_editLayer.PenColor);
        }
        catch (ArgumentException)
        {
        }
    }

    private void OnPreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        UpdateSnapDetectionMode(HotkeyCapturePolicy.ReadCurrentModifiers());
        var key = HotkeyCapturePolicy.ResolveEventKey(
            e.Key,
            e.SystemKey,
            e.ImeProcessedKey,
            e.DeadCharProcessedKey);
        if (key is Key.LeftCtrl or Key.RightCtrl) e.Handled = true;
    }

    private void UpdateSnapDetectionMode(ModifierKeys modifiers)
    {
        var mode = SnapDetectionModePolicy.Resolve(modifiers);
        if (mode == _snapDetectionMode) return;

        _snapDetectionMode = mode;
        if (_snapInteraction.IsTracking && _isSnapProposal)
            UpdateSnapProposal(_lastSnapPoint);
    }

    private void OnPresetColor(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value }) SetPenColor(System.Drawing.ColorTranslator.FromHtml(value));
    }

    private void OnOpenColorPalette(object sender, RoutedEventArgs e)
    {
        var current = EnsureEditLayer().PenColor;
        using var dialog = new Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = current,
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) SetPenColor(dialog.Color);
    }

    private void SetPenColor(System.Drawing.Color color)
    {
        EnsureEditLayer().SetPenColor(color);
        PenColorInput.Text = ColorDisplay.Format(color).Hex;
        UpdateSelectedPenColor(color);
    }

    private void UpdateSelectedPenColor(System.Drawing.Color color) =>
        SelectedPenColor.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));

    private void UpdatePickerHud(PixelPoint point)
    {
        if (_activeEditTool != EditTool.Picker || _selection.IsEmpty || !Contains(_selection, point))
        {
            PickerHud.Visibility = Visibility.Collapsed;
            return;
        }

        var x = Math.Clamp((int)Math.Floor(point.X), 0, _frame.Bitmap.Width - 1);
        var y = Math.Clamp((int)Math.Floor(point.Y), 0, _frame.Bitmap.Height - 1);
        var color = _frame.Bitmap.GetPixel(x, y);
        var display = ColorDisplay.Format(color);
        PickerHudHex.Text = display.Hex;
        PickerHudRgb.Text = display.Rgb;
        PickerHudSwatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
        var dip = ToDip(point);
        Canvas.SetLeft(PickerHud, Math.Min(Math.Max(8, dip.X + 18), Math.Max(8, ActualWidth - 166)));
        Canvas.SetTop(PickerHud, Math.Min(Math.Max(8, dip.Y + 18), Math.Max(8, ActualHeight - 66)));
        PickerHud.Visibility = Visibility.Visible;
    }

    private void OnToggleSizeEditor(object sender, RoutedEventArgs e)
    {
        PenProperties.Visibility = Visibility.Collapsed;
        MosaicProperties.Visibility = Visibility.Collapsed;
        LongCaptureProperties.Visibility = Visibility.Collapsed;
        SizeProperties.Visibility = SizeProperties.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ToolProperties.Visibility = SizeProperties.Visibility;
        SelectionWidthInput.Text = Math.Round(_selection.Width).ToString(System.Globalization.CultureInfo.InvariantCulture);
        SelectionHeightInput.Text = Math.Round(_selection.Height).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UpdateVisuals();
    }

    private void OnApplySelectionSize(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(SelectionWidthInput.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(SelectionHeightInput.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var height)
            || width < MinimumSelectionPixels || height < MinimumSelectionPixels)
        {
            ToolStatusText.Text = T("请输入不小于 8 的宽和高");
            ToolStatus.Visibility = Visibility.Visible;
            return;
        }

        _selection = SelectionSizePolicy.ResizeAroundCenter(_selection, width, height, _pixelBounds);
        _isSnapProposal = false;
        UpdateVisuals();
    }

    private void OnPenWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_editLayer is not null)
        {
            _editLayer.PenWidth = (float)e.NewValue;
        }
    }

    private void OnMosaicBrushChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_editLayer is not null)
        {
            _editLayer.MosaicBrushSize = Math.Max(1, (int)Math.Round(e.NewValue));
        }
        if (MosaicBrushValue is not null) MosaicBrushValue.Text = Math.Round(e.NewValue).ToString();
    }

    private void OnMosaicBlockChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_editLayer is not null)
        {
            _editLayer.MosaicBlockSize = Math.Max(1, (int)Math.Round(e.NewValue));
        }
        if (MosaicBlockValue is not null) MosaicBlockValue.Text = Math.Round(e.NewValue).ToString();
    }

    private void UpdateSnapProposal(PixelPoint point)
    {
        if (!_snapInteraction.IsTracking
            || _windowHandle == IntPtr.Zero
            || (!_selection.IsEmpty && !_isSnapProposal))
        {
            return;
        }

        _lastSnapPoint = point;
        var screenPoint = new System.Drawing.Point(
            _frame.ScreenBounds.X + (int)Math.Round(point.X),
            _frame.ScreenBounds.Y + (int)Math.Round(point.Y));
        var immediate = _windowSnapService.TryGetCandidate(screenPoint, _windowHandle);
        if (SnapProposalUpdatePolicy.ShouldReplaceWithImmediate(
                _selection,
                _isSnapProposal,
                point,
                immediate))
        {
            _snapCandidates = [immediate!.Value];
            _snapCandidateIndex = 0;
            ApplySnapCandidate();
        }
        else if (immediate is null && _isSnapProposal)
        {
            _snapCandidates = [];
            _snapCandidateIndex = 0;
            ApplySnapCandidate();
        }

        var requestVersion = _snapInteraction.BeginRefinement();
        CancelSnapRefinement();
        _snapRefinementDelay = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = RefreshSnapCandidatesAsync(requestVersion, screenPoint, _snapRefinementDelay.Token);
    }

    private async Task RefreshSnapCandidatesAsync(
        long requestVersion,
        System.Drawing.Point screenPoint,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PixelRect> candidates;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(45), cancellationToken);
            candidates = await Task.Run(
                () => _windowSnapService.GetCandidates(screenPoint, _windowHandle, _snapDetectionMode),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_lifetime.IsCancellationRequested || !_snapInteraction.CanAcceptRefinement(requestVersion))
        {
            return;
        }

        _snapCandidates = candidates;
        _snapCandidateIndex = 0;
        ApplySnapCandidate();
    }

    private void ApplySnapCandidate()
    {
        if (!_snapInteraction.IsTracking) return;

        if (_snapCandidates.Count > 0)
        {
            var bounds = _snapCandidates[_snapCandidateIndex];
            _selection = new PixelRect(
                bounds.X - _frame.ScreenBounds.X,
                bounds.Y - _frame.ScreenBounds.Y,
                bounds.Width,
                bounds.Height);
            _isSnapProposal = true;
            UpdateVisuals();
        }
        else if (_isSnapProposal)
        {
            _selection = default;
            _isSnapProposal = false;
            UpdateVisuals();
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_snapInteraction.IsTracking || !_isSnapProposal || _snapCandidates.Count < 2) return;
        var direction = e.Delta < 0 ? 1 : -1;
        _snapCandidateIndex = (_snapCandidateIndex + direction + _snapCandidates.Count) % _snapCandidates.Count;
        ApplySnapCandidate();
        e.Handled = true;
    }

    private void StopSnapTracking()
    {
        _snapInteraction.StopTracking();
        CancelSnapRefinement();
        _snapCandidates = [];
        _snapCandidateIndex = 0;
    }

    private void CancelSnapRefinement()
    {
        var cancellation = Interlocked.Exchange(ref _snapRefinementDelay, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private bool HasMaterialDrag(PixelPoint point) =>
        WindowSnapService.HasMaterialDrag(_dragStart, point, MinimumSelectionPixels);

    private void ShowEscapeHint()
    {
        EscapeHint.Visibility = Visibility.Visible;
        _escapeHintTimer.Stop();
        _escapeHintTimer.Start();
    }

    private void OnEscapeHintTimerTick(object? sender, EventArgs e) => ResetDoubleEscape();

    private void ResetDoubleEscape()
    {
        _doubleEscapePolicy.Reset();
        _escapeHintTimer.Stop();
        EscapeHint.Visibility = Visibility.Collapsed;
    }

    private void UpdateVisuals()
    {
        RefreshEditPreviewForCurrentSelection();
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (TextExtractionPanel.Visibility == Visibility.Visible)
        {
            PositionTextExtractionPanel();
        }

        if (_selection.IsEmpty)
        {
            SetElement(DimLeft, 0, 0, ActualWidth, ActualHeight);
            SelectionBorder.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            EditPreview.Visibility = Visibility.Collapsed;
            ToolProperties.Visibility = Visibility.Collapsed;
            ToolStatus.Visibility = Visibility.Collapsed;
            SetHandlesVisibility(Visibility.Collapsed);
            return;
        }

        var rect = ToDip(_selection);
        SetElement(DimLeft, 0, 0, rect.X, ActualHeight);
        SetElement(DimTop, rect.X, 0, rect.Width, rect.Y);
        SetElement(DimRight, rect.Right, 0, Math.Max(0, ActualWidth - rect.Right), ActualHeight);
        SetElement(DimBottom, rect.X, rect.Bottom, rect.Width, Math.Max(0, ActualHeight - rect.Bottom));
        SetElement(SelectionBorder, rect.X, rect.Y, rect.Width, rect.Height);
        SelectionBorder.Visibility = Visibility.Visible;
        if (_editLayer is not null)
        {
            SetElement(EditPreview, rect.X, rect.Y, rect.Width, rect.Height);
        }

        PositionHandles(rect);
        SetHandlesVisibility(_moveToolActive ? Visibility.Collapsed : Visibility.Visible);

        if (_selection.Width >= MinimumSelectionPixels && _selection.Height >= MinimumSelectionPixels)
        {
            SizeLabel.Text = $"{Math.Round(_selection.Width)} × {Math.Round(_selection.Height)}";
            Toolbar.Visibility = Visibility.Visible;
            Toolbar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var toolbarSize = Toolbar.DesiredSize;
            var toolbarPoint = SelectionMath.PlaceToolbar(
                _selection,
                toolbarSize.Width * PixelScaleX,
                toolbarSize.Height * PixelScaleY,
                _pixelBounds,
                8 * PixelScaleY,
                GetToolbarObstacles());
            var toolbarDip = ToDip(toolbarPoint);
            Canvas.SetLeft(Toolbar, toolbarDip.X);
            Canvas.SetTop(Toolbar, toolbarDip.Y);
            PositionToolProperties(toolbarDip, toolbarSize);
            PositionToolFeedback(toolbarDip, toolbarSize);
        }
        else
        {
            Toolbar.Visibility = Visibility.Collapsed;
        }
    }

    private void PositionHandles(Rect rect)
    {
        var points = new[]
        {
            new System.Windows.Point(rect.Left, rect.Top),
            new System.Windows.Point(rect.Left + rect.Width / 2, rect.Top),
            new System.Windows.Point(rect.Right, rect.Top),
            new System.Windows.Point(rect.Right, rect.Top + rect.Height / 2),
            new System.Windows.Point(rect.Right, rect.Bottom),
            new System.Windows.Point(rect.Left + rect.Width / 2, rect.Bottom),
            new System.Windows.Point(rect.Left, rect.Bottom),
            new System.Windows.Point(rect.Left, rect.Top + rect.Height / 2),
        };

        for (var index = 0; index < _handles.Length; index++)
        {
            Canvas.SetLeft(_handles[index], points[index].X - _handles[index].Width / 2);
            Canvas.SetTop(_handles[index], points[index].Y - _handles[index].Height / 2);
        }
    }

    private void PositionToolProperties(System.Windows.Point toolbarPoint, System.Windows.Size toolbarSize)
    {
        if (ToolProperties.Visibility != Visibility.Visible)
        {
            return;
        }

        ToolProperties.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var top = Math.Min(ActualHeight - ToolProperties.DesiredSize.Height, toolbarPoint.Y + toolbarSize.Height + 6);
        Canvas.SetLeft(ToolProperties, toolbarPoint.X);
        Canvas.SetTop(ToolProperties, Math.Max(0, top));
    }

    private void PositionToolFeedbackForCurrentToolbar()
    {
        if (Toolbar.Visibility != Visibility.Visible) return;
        var left = Canvas.GetLeft(Toolbar);
        var top = Canvas.GetTop(Toolbar);
        if (double.IsNaN(left) || double.IsNaN(top)) return;
        Toolbar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        PositionToolFeedback(new System.Windows.Point(left, top), Toolbar.DesiredSize);
    }

    private void PositionToolFeedback(System.Windows.Point toolbarPoint, System.Windows.Size toolbarSize)
    {
        ToolFeedbackPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        if (ToolFeedbackPanel.DesiredSize.Width <= 0 || ToolFeedbackPanel.DesiredSize.Height <= 0) return;
        var anchor = CreateToolFeedbackAnchor(toolbarPoint, toolbarSize);
        var placement = FloatingOverlayLayout.PlaceAdjacent(
            anchor,
            ToolFeedbackPanel.DesiredSize.Width,
            ToolFeedbackPanel.DesiredSize.Height,
            ActualWidth,
            ActualHeight,
            6,
            _translationDisplayState?.Mode == TranslationDisplayMode.Translated
                ? new[] { new PixelRect(
                    _selection.X / PixelScaleX,
                    _selection.Y / PixelScaleY,
                    _selection.Width / PixelScaleX,
                    _selection.Height / PixelScaleY) }
                : null);
        Canvas.SetLeft(ToolFeedbackPanel, placement.Left);
        Canvas.SetTop(ToolFeedbackPanel, placement.Top);
    }

    private PixelRect CreateToolFeedbackAnchor(System.Windows.Point toolbarPoint, System.Windows.Size toolbarSize)
    {
        var left = toolbarPoint.X;
        var top = toolbarPoint.Y;
        var right = toolbarPoint.X + toolbarSize.Width;
        var bottom = toolbarPoint.Y + toolbarSize.Height;
        if (ToolProperties.Visibility == Visibility.Visible)
        {
            ToolProperties.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var propertiesLeft = Canvas.GetLeft(ToolProperties);
            var propertiesTop = Canvas.GetTop(ToolProperties);
            if (!double.IsNaN(propertiesLeft) && !double.IsNaN(propertiesTop))
            {
                left = Math.Min(left, propertiesLeft);
                top = Math.Min(top, propertiesTop);
                right = Math.Max(right, propertiesLeft + ToolProperties.DesiredSize.Width);
                bottom = Math.Max(bottom, propertiesTop + ToolProperties.DesiredSize.Height);
            }
        }
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private IReadOnlyList<PixelRect> GetToolbarObstacles()
    {
        var obstacles = new List<PixelRect>();
        if (TextExtractionPanel.Visibility == Visibility.Visible)
        {
            var margin = TextExtractionPanel.Margin;
            var width = Math.Max(TextExtractionPanel.ActualWidth, TextExtractionPanel.Width);
            var height = Math.Max(TextExtractionPanel.ActualHeight, TextExtractionPanel.Height);
            obstacles.Add(new PixelRect(
                margin.Left * PixelScaleX,
                margin.Top * PixelScaleY,
                width * PixelScaleX,
                height * PixelScaleY));
        }

        if (ToolProperties.Visibility == Visibility.Visible)
        {
            var left = Canvas.GetLeft(ToolProperties);
            var top = Canvas.GetTop(ToolProperties);
            if (!double.IsNaN(left) && !double.IsNaN(top))
            {
                obstacles.Add(new PixelRect(
                    left * PixelScaleX,
                    top * PixelScaleY,
                    ToolProperties.ActualWidth * PixelScaleX,
                    ToolProperties.ActualHeight * PixelScaleY));
            }
        }

        return obstacles;
    }

    private void PositionTextExtractionPanel()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var selectionDip = ToDip(_selection);
        var contentDesiredWidth = MeasureUnwrappedExtractionTextWidth();
        var initial = ExtractionPanelLayoutPolicy.Calculate(
            ActualWidth,
            ActualHeight,
            new PixelRect(selectionDip.X, selectionDip.Y, selectionDip.Width, selectionDip.Height),
            ExtractionPanelLayoutPolicy.MinimumTextHeight,
            contentDesiredWidth);
        TextExtractionPanel.Width = initial.Width;
        var textWidth = Math.Max(
            1,
            initial.Width
            - ExtractionPanelLayoutPolicy.TextBoxHorizontalMargin
            - ExtractedTextBox.Padding.Left
            - ExtractedTextBox.Padding.Right
            - ExtractedTextBox.BorderThickness.Left
            - ExtractedTextBox.BorderThickness.Right);
        var measured = ExtractionTextMeasurement.Measure(
            ExtractedTextBox.Text,
            ExtractedTextBox.FontSize,
            textWidth,
            VisualTreeHelper.GetDpi(this).PixelsPerDip,
            ExtractedTextBox.FontFamily.Source);
        var contentDesiredHeight = Math.Max(
            ExtractionPanelLayoutPolicy.MinimumTextHeight,
            measured.Height
            + ExtractedTextBox.Padding.Top
            + ExtractedTextBox.Padding.Bottom
            + ExtractedTextBox.BorderThickness.Top
            + ExtractedTextBox.BorderThickness.Bottom);
        var layout = ExtractionPanelLayoutPolicy.Calculate(
            ActualWidth,
            ActualHeight,
            new PixelRect(selectionDip.X, selectionDip.Y, selectionDip.Width, selectionDip.Height),
            contentDesiredHeight,
            contentDesiredWidth);
        TextExtractionPanel.Width = layout.Width;
        TextExtractionPanel.Height = layout.Height;
        TextExtractionPanel.Margin = new Thickness(layout.X, layout.Y, 0, 0);
    }

    private double MeasureUnwrappedExtractionTextWidth()
    {
        var measured = ExtractionTextMeasurement.Measure(
            ExtractedTextBox.Text,
            ExtractedTextBox.FontSize,
            100_000,
            VisualTreeHelper.GetDpi(this).PixelsPerDip,
            ExtractedTextBox.FontFamily.Source);
        return Math.Max(
            1,
            measured.Width
            + ExtractedTextBox.Padding.Left
            + ExtractedTextBox.Padding.Right
            + ExtractedTextBox.BorderThickness.Left
            + ExtractedTextBox.BorderThickness.Right);
    }

    private void UpdateEditPreview()
    {
        var activeMosaic = _activeEditTool == EditTool.Mosaic && _activeEditPoints is { Count: > 0 }
            ? new MosaicStroke(_activeEditPoints, EnsureEditLayer().MosaicBrushSize, EnsureEditLayer().MosaicBlockSize)
            : null;
        if (_editLayer is null || (_editLayer.PenStrokes.Count == 0 && _editLayer.MosaicStrokes.Count == 0 && activeMosaic is null))
        {
            EditPreview.Visibility = Visibility.Collapsed;
            return;
        }

        using var composited = activeMosaic is null
            ? EditRenderer.Composite(_frame.Bitmap, _editLayer)
            : EditRenderer.CompositePreview(_frame.Bitmap, _editLayer, activeMosaic);
        EditPreview.Source = BitmapSourceFactory.Create(composited);
        EditPreview.Visibility = Visibility.Visible;
    }

    private void UpdateMosaicBrushCursor(PixelPoint point)
    {
        if (_activeEditTool != EditTool.Mosaic || _selection.IsEmpty || !Contains(_selection, point))
        {
            MosaicBrushCursor.Visibility = Visibility.Collapsed;
            return;
        }
        var diameter = EnsureEditLayer().MosaicBrushSize;
        MosaicBrushCursor.Width = diameter / PixelScaleX;
        MosaicBrushCursor.Height = diameter / PixelScaleY;
        var dip = ToDip(point);
        Canvas.SetLeft(MosaicBrushCursor, dip.X - MosaicBrushCursor.Width / 2);
        Canvas.SetTop(MosaicBrushCursor, dip.Y - MosaicBrushCursor.Height / 2);
        MosaicBrushCursor.Visibility = Visibility.Visible;
    }

    private void RefreshEditPreviewForCurrentSelection()
    {
        if (_editLayer is null || _selection.IsEmpty)
        {
            return;
        }

        if (_editLayer.Selection != _selection)
        {
            _editLayer.UpdateSelection(_selection);
            UpdateEditPreview();
        }
    }

    private void SetHandlesVisibility(Visibility visibility)
    {
        foreach (var handle in _handles)
        {
            handle.Visibility = visibility;
        }
    }

    private PixelPoint ToPixel(System.Windows.Point point) => new(
        Math.Clamp(point.X * PixelScaleX, 0, _frame.Bitmap.Width),
        Math.Clamp(point.Y * PixelScaleY, 0, _frame.Bitmap.Height));

    private Rect ToDip(PixelRect rect) => new(
        rect.X / PixelScaleX,
        rect.Y / PixelScaleY,
        rect.Width / PixelScaleX,
        rect.Height / PixelScaleY);

    private System.Windows.Point ToDip(PixelPoint point) => new(point.X / PixelScaleX, point.Y / PixelScaleY);

    private double PixelScaleX => _frame.Bitmap.Width / Math.Max(1, ActualWidth);

    private double PixelScaleY => _frame.Bitmap.Height / Math.Max(1, ActualHeight);

    private PixelPoint Clamp(PixelPoint point) => new(
        Math.Clamp(point.X, _pixelBounds.Left, _pixelBounds.Right),
        Math.Clamp(point.Y, _pixelBounds.Top, _pixelBounds.Bottom));

    private PixelRect ClampSelection(PixelRect selection)
    {
        var left = Math.Clamp(selection.Left, _pixelBounds.Left, _pixelBounds.Right);
        var top = Math.Clamp(selection.Top, _pixelBounds.Top, _pixelBounds.Bottom);
        var right = Math.Clamp(selection.Right, left, _pixelBounds.Right);
        var bottom = Math.Clamp(selection.Bottom, top, _pixelBounds.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static bool Contains(PixelRect rect, PixelPoint point) =>
        point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

    private static bool TryGetHandle(DependencyObject? source, out ResizeHandle handle)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Tag: string tag }
                && Enum.TryParse(tag, out handle))
            {
                return true;
            }
        }

        handle = ResizeHandle.None;
        return false;
    }

    private bool IsControlSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, Toolbar)
                || ReferenceEquals(current, ToolProperties)
                || ReferenceEquals(current, TextExtractionPanel)
                || ReferenceEquals(current, TranslationToggle))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetElement(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private static DrawingRectangle ToCropRectangle(PixelRect rect, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp((int)Math.Floor(rect.Left), 0, imageWidth - 1);
        var top = Math.Clamp((int)Math.Floor(rect.Top), 0, imageHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(rect.Right), left + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom), top + 1, imageHeight);
        return DrawingRectangle.FromLTRB(left, top, right, bottom);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private enum InteractionMode
    {
        None,
        Draw,
        Move,
        Resize,
        Edit,
        PendingRedraw,
        PendingSnapDecision,
    }

    private sealed class WpfClipboardTextSink : IColorClipboardTextSink
    {
        public void SetText(string text) => System.Windows.Clipboard.SetText(text);

        public void SetColorText(string text)
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.UnicodeText, text);
            data.SetData("ClipRay.ColorPicker.v1", "1");
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
    }
}

public sealed record OverlayTranslationRequest(long Version, PixelRect Selection);

public sealed class OverlayTranslationSequencer
{
    private long _version;
    private bool _closed;

    public OverlayTranslationRequest Begin(PixelRect selection)
    {
        if (_closed) throw new ObjectDisposedException(nameof(OverlayTranslationSequencer));
        return new OverlayTranslationRequest(++_version, selection);
    }

    public bool IsLatest(long version) => !_closed && version == _version;

    public void CancelPending()
    {
        if (!_closed) _version++;
    }

    public void Close()
    {
        _closed = true;
        _version++;
    }

    public static PixelRect MapToScreen(PixelRect selection, PixelRect relative) =>
        new(selection.X + relative.X, selection.Y + relative.Y, relative.Width, relative.Height);
}

public sealed class TranslationCapture : EventArgs, IDisposable
{
    public TranslationCapture(
        long version,
        Bitmap bitmap,
        PixelRect selection,
        PixelRect? captureBounds = null,
        TranslationRouteKind route = TranslationRouteKind.LocalLmt)
    {
        Version = version;
        Bitmap = bitmap;
        Selection = selection;
        CaptureBounds = captureBounds ?? selection;
        Route = route;
    }

    public long Version { get; }
    public Bitmap Bitmap { get; }
    public PixelRect Selection { get; }
    public PixelRect CaptureBounds { get; }
    public TranslationRouteKind Route { get; }
    public void Dispose() => Bitmap.Dispose();
}

public static class TranslationCaptureDispatcher
{
    public static void Dispatch(EventHandler<TranslationCapture>? handler, object sender, TranslationCapture capture)
    {
        if (handler is null)
        {
            capture.Dispose();
            return;
        }

        try
        {
            handler(sender, capture);
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }
}

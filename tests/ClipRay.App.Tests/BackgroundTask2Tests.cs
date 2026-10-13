using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipRay.App.Capture;
using ClipRay.App.Editing;
using ClipRay.App.Ocr;
using ClipRay.App.Overlay;
using ClipRay.App.Translation;
using ClipRay.Core.Geometry;
using Color = System.Drawing.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

internal static class BackgroundTask2Tests
{
    // Explicit managed/offscreen cases: no Show, input dispatch, screen capture, clipboard or providers.
    internal static (string Name, Action Run)[] CreateCases() => new (string Name, Action Run)[]
        {
            ("edited-translation", () => EditedPixelsReachCapture(extract: false)),
            ("edited-extraction", () => EditedPixelsReachCapture(extract: true)),
            ("padding", PaddingKeepsRetainedMasks),
            ("padding-left-stamp-translation", () => PaddingKeepsPenOverhang("left-stamp", extract: false)),
            ("padding-left-stamp-extraction", () => PaddingKeepsPenOverhang("left-stamp", extract: true)),
            ("padding-top-stamp-translation", () => PaddingKeepsPenOverhang("top-stamp", extract: false)),
            ("padding-top-stamp-extraction", () => PaddingKeepsPenOverhang("top-stamp", extract: true)),
            ("padding-left-line-translation", () => PaddingKeepsPenOverhang("left-line", extract: false)),
            ("padding-left-line-extraction", () => PaddingKeepsPenOverhang("left-line", extract: true)),
            ("padding-top-line-translation", () => PaddingKeepsPenOverhang("top-line", extract: false)),
            ("padding-top-line-extraction", () => PaddingKeepsPenOverhang("top-line", extract: true)),
            ("capture-bounds", CaptureReplayKeepsIntegerBounds),
            ("preview-export", PreviewAndExportAgree),
            ("size-state", SizeHandlerInvalidatesOldResults),
            ("snap-state", SnapHandlerInvalidatesOldResults),
            ("edit-history", EditHandlersInvalidateOldResults),
            ("fold", ExtractionFoldCancels),
            ("close", ExtractionCloseCancels),
            ("textbox-enter", TextBoxEnterAppliesSize),
            ("textbox-control", TextBoxKeepsControlInput),
            ("draw-state", () => DragHandlerInvalidatesOldResults("Draw")),
            ("move-state", () => DragHandlerInvalidatesOldResults("Move")),
            ("resize-state", () => DragHandlerInvalidatesOldResults("Resize")),
            ("redraw-state", () => DragHandlerInvalidatesOldResults("PendingRedraw")),
            ("snap-drag-state", () => DragHandlerInvalidatesOldResults("PendingSnapDecision")),
            ("undo-state", () => HistoryHandlerInvalidatesOldResults(redo: false)),
            ("redo-state", () => HistoryHandlerInvalidatesOldResults(redo: true)),
            ("late-results", LateResultsCannotPublish),
            ("route-state", RouteChangeInvalidatesOldResults),
            ("request-cancel", ReplacementAndFoldCancelActualRequests),
            ("textbox-undo", TextBoxUndoKeepsCanvasHistory),
            ("canvas-undo", CanvasUndoRemainsAvailable),
            ("snap-wheel", WheelSnapInvalidatesOldResults),
            ("active-preview", ActiveMosaicKeepsTheTranslationVisible),
            ("routed-size-enter", RoutedTextBoxEnterUsesTheProductionBinding),
            ("routed-move", RoutedMouseMoveUsesTheProductionBinding),
        };

    public static int Run(string[] args)
    {
        var tests = CreateCases();
        var index = Array.IndexOf(args, "--case");
        if (index >= 0)
        {
            if (index + 1 >= args.Length) return 2;
            var names = args[index + 1].Split('|');
            var unknown = names.Where(name => !tests.Any(test => test.Name == name)).ToArray();
            if (unknown.Length > 0) { Console.Error.WriteLine("Unknown task-2 case: " + string.Join('|', unknown)); return 2; }
            tests = tests.Where(test => names.Contains(test.Name, StringComparer.Ordinal)).ToArray();
        }
        var failures = 0;
        EnsureResources();
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine($"PASS task-2/{test.Name}"); }
            catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL task-2/{test.Name}: {error}"); }
        }
        Console.WriteLine($"TASK_2_BACKGROUND {tests.Length - failures}/{tests.Length} passed");
        return failures == 0 ? 0 : 1;
    }

    private static void EditedPixelsReachCapture(bool extract)
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var layer = new EditLayer(fixture.Selection);
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(32, 32)], 40, 8));
        layer.AddPenStroke(new PenStroke([new PixelPoint(8, 8)], Color.Red, 3));
        Set(fixture.Overlay, "_editLayer", layer);
        using var expected = EditRenderer.Composite(fixture.Source, layer);
        TranslationCapture? capture = null;
        if (extract) fixture.Overlay.TextExtractionRequested += (_, value) => capture = value;
        else fixture.Overlay.TranslationRequested += (_, value) => capture = value;
        Invoke(fixture.Overlay, extract ? "RequestTextExtraction" : "RequestTranslation");
        using (capture)
        {
            Require(capture is not null, "Production request was not dispatched.");
            Require(capture!.Selection == fixture.Selection && capture.CaptureBounds == fixture.Selection, "Capture coordinates changed.");
            RequirePixels(expected, capture.Bitmap, "Recognition capture ignored the current edits.");
        }
    }

    private static void PaddingKeepsRetainedMasks()
    {
        using var fixture = new Fixture(new PixelRect(8, 8, 48, 48));
        var layer = new EditLayer(fixture.Selection);
        layer.AddPenStroke(new PenStroke([new PixelPoint(17, 23)], Color.Red, 3));
        Set(fixture.Overlay, "_editLayer", layer);
        ApplySize(fixture.Overlay, 24, 24);
        Require(Field<PixelRect>(fixture.Overlay, "_selection") == new PixelRect(20, 20, 24, 24), "Size handler did not shrink around the center.");
        TranslationCapture? capture = null;
        fixture.Overlay.TextExtractionRequested += (_, value) => capture = value;
        Invoke(fixture.Overlay, "RequestTextExtraction");
        using (capture)
        {
            Require(capture is not null && capture.CaptureBounds.Left < capture.Selection.Left, "OCR padding was lost.");
            var x = 17 - (int)capture!.CaptureBounds.X;
            var y = 23 - (int)capture.CaptureBounds.Y;
            Require(capture.Bitmap.GetPixel(x, y).ToArgb() == Color.Red.ToArgb(), "OCR padding reintroduced a previously covered source pixel outside the resized selection.");
            Require(layer.PenStrokes[0].Points[0] == new PixelPoint(17, 23), "Retained mask geometry moved.");
        }
    }

    private static void PaddingKeepsPenOverhang(string shape, bool extract)
    {
        using var fixture = new Fixture(new PixelRect(8, 8, 48, 48));
        var (points, covered) = shape switch
        {
            // Legacy selector names retained; rounded dots now extend equally around their center.
            "left-stamp" => (new PixelPoint[] { new(12, 23) }, new PixelPoint(14, 23)),
            "top-stamp" => (new PixelPoint[] { new(23, 12) }, new PixelPoint(23, 14)),
            // End caps do not reach the middle of the crop: these require the wide line itself.
            "left-line" => (new PixelPoint[] { new(12, 10), new(12, 54) }, new PixelPoint(14, 26)),
            "top-line" => (new PixelPoint[] { new(10, 12), new(54, 12) }, new PixelPoint(26, 14)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var layer = new EditLayer(fixture.Selection);
        layer.AddPenStroke(new PenStroke(points, Color.Red, 8));
        Set(fixture.Overlay, "_editLayer", layer);
        using var original = EditRenderer.Composite(fixture.Source, layer);
        Require(original.GetPixel((int)(covered.X - fixture.Selection.X), (int)(covered.Y - fixture.Selection.Y)).ToArgb() == Color.Red.ToArgb(),
            $"{shape}: original selection did not establish the covered pixel.");
        Require(fixture.Source.GetPixel((int)covered.X, (int)covered.Y).ToArgb() != Color.Red.ToArgb(),
            $"{shape}: original-mask oracle matches the raw pixel.");
        ApplySize(fixture.Overlay, 24, 24);
        var selection = new PixelRect(20, 20, 24, 24);
        var bounds = new PixelRect(14, 14, 36, 36);
        Require(Field<PixelRect>(fixture.Overlay, "_selection") == selection, "Size handler did not establish the padded selection.");
        using var expected = original.Clone(new Rectangle(6, 6, 36, 36), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        TranslationCapture? capture = null;
        if (extract) fixture.Overlay.TextExtractionRequested += (_, value) => capture = value;
        else fixture.Overlay.TranslationRequested += (_, value) => capture = value;
        Invoke(fixture.Overlay, extract ? "RequestTextExtraction" : "RequestTranslation");
        using (capture)
        {
            Require(capture is not null && capture.Selection == selection && capture.CaptureBounds == bounds,
                $"{shape}: production input changed selection/capture coordinates.");
            Require(capture!.Bitmap.Width == bounds.Width && capture.Bitmap.Height == bounds.Height,
                $"{shape}: production bitmap dimensions differ from CaptureBounds.");
            Require(capture!.Bitmap.GetPixel((int)(covered.X - bounds.X), (int)(covered.Y - bounds.Y)).ToArgb() == Color.Red.ToArgb(),
                $"{shape}: production input recovered a raw pixel beneath the retained pen overhang.");
            RequirePixels(expected, capture.Bitmap, $"{shape}: padded input differs from the original covered crop.");
            Require(layer.PenStrokes.Single().Points.SequenceEqual(points), "Capture moved retained pen geometry.");
        }
    }

    private static void CaptureReplayKeepsIntegerBounds()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        foreach (var edited in new[] { false, true })
        {
            var layer = new EditLayer(fixture.Selection);
            if (edited)
            {
                layer.AddPenStroke(new PenStroke([new(0, 0), new(63, 0), new(63, 63), new(0, 63)], Color.Red, 8));
                layer.AddMosaicStroke(new MosaicStroke([new(6, 6)], 12, 8));
                layer.AddPenStroke(new PenStroke([new(32, 32)], Color.Blue, 3));
            }
            using var original = EditRenderer.Composite(fixture.Source, layer);
            foreach (var bounds in new PixelRect[]
            {
                new(0, 0, 1, 1), new(63, 0, 1, 1), new(0, 63, 1, 1), new(63, 63, 1, 1), new(32, 32, 1, 1),
                new(0, 12, 2, 8), new(62, 12, 2, 8), new(12, 0, 8, 2), new(12, 62, 8, 2), new(3, 3, 8, 8),
            })
            {
                using var expected = original.Clone(new Rectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height),
                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var capture = EditRenderer.CompositeCapture(fixture.Source, layer, bounds);
                Require(capture.Width == bounds.Width && capture.Height == bounds.Height, "Clamped work area changed capture dimensions.");
                RequirePixels(expected, capture, $"Capture pixels differ at source boundary {bounds}, edited={edited}.");
                Require(layer.Selection == fixture.Selection, "Recognition crop changed retained selection geometry.");
            }
        }
    }

    private static void PreviewAndExportAgree()
    {
        foreach (var original in new[] { false, true })
        {
            using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
            var layer = new EditLayer(fixture.Selection);
            layer.AddPenStroke(new PenStroke([new PixelPoint(8, 8)], Color.Red, 3));
            layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(32, 32)], 14, 8));
            Set(fixture.Overlay, "_editLayer", layer);
            SetDisplay(fixture.Overlay, fixture.Selection);
            Invoke(fixture.Overlay, "UpdateEditPreview");
            if (original) Invoke(fixture.Overlay, "OnTranslationToggle", fixture.Overlay, new RoutedEventArgs());
            var canvas = Find<Canvas>(fixture.Overlay, "OverlayCanvas");
            var preview = Find<Image>(fixture.Overlay, "EditPreview");
            foreach (UIElement child in canvas.Children) child.Visibility = ReferenceEquals(child, preview) ? Visibility.Visible : Visibility.Collapsed;
            preview.Width = 64; preview.Height = 64; Canvas.SetLeft(preview, 0); Canvas.SetTop(preview, 0);
            var root = (Grid)fixture.Overlay.Content;
            root.Measure(new Size(64, 64)); root.Arrange(new Rect(0, 0, 64, 64));
            var rendered = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(root);
            Bitmap? exported = null;
            fixture.Overlay.CaptureDelivery = bitmap => { exported = new Bitmap(bitmap); return false; };
            Invoke(fixture.Overlay, "ConfirmSelection");
            using (exported)
            {
                Require(exported is not null, "Production export did not deliver pixels.");
                for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
                    Require(ReadPixel(rendered, x, y) == exported!.GetPixel(x, y).ToArgb(), $"Preview/export pixel mismatch at {x},{y}, original={original}.");
                Require(exported!.GetPixel(3, 3).ToArgb() == (original ? fixture.Source.GetPixel(3, 3).ToArgb() : Color.Blue.ToArgb()), "Unedited translation pixel was covered.");
            }
        }
    }

    private static void SizeHandlerInvalidatesOldResults()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var request = SeedOldResult(fixture);
        ApplySize(fixture.Overlay, 32, 32);
        AssertInvalidated(fixture, request);
        Require(Field<PixelRect>(fixture.Overlay, "_selection") == new PixelRect(16, 16, 32, 32), "Selection size did not apply.");
    }

    private static void SnapHandlerInvalidatesOldResults()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var request = SeedOldResult(fixture);
        Set(fixture.Overlay, "_snapCandidates", new PixelRect[] { new(16, 16, 32, 32) });
        Invoke(fixture.Overlay, "ApplySnapCandidate");
        AssertInvalidated(fixture, request);
        Require(Field<PixelRect>(fixture.Overlay, "_selection") == new PixelRect(16, 16, 32, 32), "Synthetic snap candidate was not applied.");
    }

    private static void EditHandlersInvalidateOldResults()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        Set(fixture.Overlay, "_activeEditTool", EditTool.Pen);
        Set(fixture.Overlay, "_activeEditPoints", new List<PixelPoint> { new(8, 8) });
        var request = SeedOldResult(fixture);
        Invoke(fixture.Overlay, "CommitActiveEdit");
        AssertInvalidated(fixture, request);
        request = SeedOldResult(fixture);
        Invoke(fixture.Overlay, "UndoLastEdit");
        AssertInvalidated(fixture, request);
        request = SeedOldResult(fixture);
        Invoke(fixture.Overlay, "RedoLastEdit");
        AssertInvalidated(fixture, request);
        Require(Field<EditLayer>(fixture.Overlay, "_editLayer").PenStrokes.Single().Points.Single() == new PixelPoint(8, 8), "Undo/redo lost retained geometry.");
    }

    private static void ExtractionFoldCancels()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        fixture.Overlay.TextExtractionRequested += (_, capture) => capture.Dispose();
        Invoke(fixture.Overlay, "RequestTextExtraction");
        var request = Sequencer(fixture).Begin(fixture.Selection);
        Invoke(fixture.Overlay, "RequestTextExtraction");
        Require(!Sequencer(fixture).IsLatest(request.Version), "Tool fold left the extraction request valid.");
        Require(Find<FrameworkElement>(fixture.Overlay, "TextExtractionPanel").Visibility == Visibility.Collapsed, "Tool did not fold.");
    }

    private static void ExtractionCloseCancels()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var request = Sequencer(fixture).Begin(fixture.Selection);
        Invoke(fixture.Overlay, "OnCloseTextExtraction", fixture.Overlay, new RoutedEventArgs());
        Require(!Sequencer(fixture).IsLatest(request.Version), "Close lost its existing cancellation behavior.");
    }

    private static void TextBoxEnterAppliesSize()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var deliveries = 0;
        fixture.Overlay.CaptureDelivery = _ => { deliveries++; return false; };
        Find<TextBox>(fixture.Overlay, "SelectionWidthInput").Text = "32";
        var height = Find<TextBox>(fixture.Overlay, "SelectionHeightInput"); height.Text = "32";
        var key = KeyEvent(height, Key.Enter);
        Invoke(fixture.Overlay, "OnPreviewKeyDown", fixture.Overlay, key);
        Require(deliveries == 0, "Enter in the size field saved the canvas.");
        Require(key.Handled && Field<PixelRect>(fixture.Overlay, "_selection") == new PixelRect(16, 16, 32, 32), "Enter did not apply the dimensions.");
    }

    private static void TextBoxKeepsControlInput()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var input = Find<TextBox>(fixture.Overlay, "SelectionWidthInput");
        foreach (var handler in new[] { "OnPreviewKeyDown", "OnPreviewKeyUp" })
        {
            var key = KeyEvent(input, Key.LeftCtrl);
            Invoke(fixture.Overlay, handler, fixture.Overlay, key);
            Require(!key.Handled, "Editable TextBox lost normal Control input.");
        }
    }

    private static void DragHandlerInvalidatesOldResults(string mode)
    {
            using var fixture = new Fixture(new PixelRect(8, 8, 32, 32));
            var request = SeedOldResult(fixture);
            SetEnum(fixture.Overlay, "_mode", mode);
            Set(fixture.Overlay, "_dragStart", new PixelPoint(8, 8));
            Set(fixture.Overlay, "_selectionAtDragStart", fixture.Selection);
            Set(fixture.Overlay, "_activeHandle", ResizeHandle.BottomRight);
            fixture.Overlay.ReadPointerOverride = _ => new PixelPoint(48, 48);
            fixture.Overlay.ReadLeftButton = _ => MouseButtonState.Pressed;
            var pointer = new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent };
            Invoke(fixture.Overlay, "OnMouseMove", fixture.Overlay, pointer);
            Require(Field<PixelRect>(fixture.Overlay, "_selection") != fixture.Selection, $"{mode} did not change the selection.");
            AssertInvalidated(fixture, request);
    }

    private static void HistoryHandlerInvalidatesOldResults(bool redo)
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var layer = new EditLayer(fixture.Selection);
        layer.AddMosaicStroke(new MosaicStroke([new PixelPoint(32, 32)], 12, 8));
        if (redo) layer.UndoLast();
        Set(fixture.Overlay, "_editLayer", layer);
        var request = SeedOldResult(fixture);
        Invoke(fixture.Overlay, redo ? "RedoLastEdit" : "UndoLastEdit");
        AssertInvalidated(fixture, request);
    }

    private static void LateResultsCannotPublish()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var loaded = typeof(FrameworkElement).GetProperty("IsLoadedCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require(loaded is not null, "Cannot set the managed loaded cache for the offscreen commit oracle.");
        loaded!.SetValue(fixture.Overlay, true);
        TranslationCapture? capture = null;
        try
        {
            Require(fixture.Overlay.IsLoaded, "Late-result oracle would be vacuous without a loaded flag.");
            fixture.Overlay.TranslationRequested += (_, value) => capture = value;
            Invoke(fixture.Overlay, "RequestTranslation");
            Require(capture is not null, "Production translation entry did not dispatch.");
            var item = new InPlaceLayoutItem(new OcrTextBlock("source", new PixelRect(4, 4, 20, 10), []), "result", new PixelRect(4, 4, 20, 10),
                InPlaceBackgroundMode.ComplexBacking, Color.Blue, Color.White);
            Require(fixture.Overlay.ShowTranslation(capture!.Version, capture.CaptureBounds, [item]), "Current production request was refused before mutation.");
            ApplySize(fixture.Overlay, 32, 32);
            Require(capture.Token.IsCancellationRequested, "Size mutation did not cancel the real dispatched request token.");
            // An uncooperative recognizer/provider may still hold the bitmap after cancellation.
            _ = capture.Bitmap.GetPixel(0, 0);
            Require(!fixture.Overlay.ShowTranslation(capture.Version, capture.CaptureBounds, [item]), "Late translation republished old pixels.");
            Require(!fixture.Overlay.TryShowTranslationStatus(capture.Version, "old status"), "Late progress republished old status.");
            Require(!fixture.Overlay.ShowExtractedText(capture.Version, "old text", "old status"), "Late OCR reopened the panel.");
            Require(Find<Canvas>(fixture.Overlay, "TranslationLayer").Children.Count == 0, "Old pixels survived late completion.");
            capture.Dispose(); capture = null;
            Invoke(fixture.Overlay, "RequestTranslation");
            Require(capture is not null && !capture.Token.IsCancellationRequested, "The next request was blocked by cancelled work.");
            Require(fixture.Overlay.ShowTranslation(capture!.Version, capture.CaptureBounds, [item]), "The next current request cannot commit.");
        }
        finally { capture?.Dispose(); loaded.SetValue(fixture.Overlay, false); }
    }

    private static void RouteChangeInvalidatesOldResults()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var request = SeedOldResult(fixture);
        Invoke(fixture.Overlay, "SelectTranslationRoute", TranslationRouteKind.DeepSeekFlash);
        AssertInvalidated(fixture, request);
        TranslationCapture? capture = null;
        fixture.Overlay.TranslationRequested += (_, value) => capture = value;
        Invoke(fixture.Overlay, "RequestTranslation");
        using (capture) Require(capture?.Route == TranslationRouteKind.DeepSeekFlash && !capture.Token.IsCancellationRequested, "New route was not snapshotted by the next request.");
    }

    private static void ReplacementAndFoldCancelActualRequests()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var captures = new List<TranslationCapture>();
        fixture.Overlay.TranslationRequested += (_, value) => captures.Add(value);
        fixture.Overlay.TextExtractionRequested += (_, value) => captures.Add(value);
        try
        {
            Invoke(fixture.Overlay, "RequestTranslation");
            Invoke(fixture.Overlay, "RequestTextExtraction");
            Require(captures[0].Token.IsCancellationRequested && !captures[1].Token.IsCancellationRequested, "Replacing translation with OCR did not cancel only the old request.");
            Invoke(fixture.Overlay, "RequestTextExtraction");
            Require(captures[1].Token.IsCancellationRequested, "Tool fold did not cancel the dispatched OCR token.");
            Invoke(fixture.Overlay, "RequestTextExtraction");
            Require(!captures[2].Token.IsCancellationRequested, "Reopened extraction cannot start a new request.");
            Invoke(fixture.Overlay, "OnCloseTextExtraction", fixture.Overlay, new RoutedEventArgs());
            Require(captures[2].Token.IsCancellationRequested, "Panel close did not cancel its dispatched OCR token.");
            foreach (var capture in captures) _ = capture.Bitmap.GetPixel(0, 0);
        }
        finally { foreach (var capture in captures) capture.Dispose(); }
    }

    private static void TextBoxUndoKeepsCanvasHistory()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var layer = new EditLayer(fixture.Selection);
        layer.AddPenStroke(new PenStroke([new PixelPoint(8, 8)], Color.Red, 3));
        Set(fixture.Overlay, "_editLayer", layer);
        var input = Find<TextBox>(fixture.Overlay, "SelectionWidthInput");
        input.ApplyTemplate();
        input.IsUndoEnabled = false; input.IsUndoEnabled = true;
        input.Text = "64";
        input.BeginChange(); input.SelectAll(); input.SelectedText = "32"; input.EndChange();
        Require(input.CanUndo, "Synthetic field edit did not create native TextBox history.");
        foreach (var modifiers in new[] { ModifierKeys.Control, ModifierKeys.Control | ModifierKeys.Shift })
        {
            fixture.Overlay.ReadModifiers = () => modifiers;
            var key = KeyEvent(input, Key.Z);
            Invoke(fixture.Overlay, "OnPreviewKeyDown", fixture.Overlay, key);
            Require(!key.Handled && layer.PenStrokes.Count == 1, "Field undo/redo was intercepted by canvas editing.");
        }
        ApplicationCommands.Undo.Execute(null, input);
        Require(input.Text == "64" && layer.PenStrokes.Count == 1, "Native field undo changed the canvas or failed.");
        ApplicationCommands.Redo.Execute(null, input);
        Require(input.Text == "32" && layer.PenStrokes.Count == 1, "Native field redo changed the canvas or failed.");
    }

    private static void CanvasUndoRemainsAvailable()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var layer = new EditLayer(fixture.Selection);
        layer.AddPenStroke(new PenStroke([new PixelPoint(8, 8)], Color.Red, 3));
        Set(fixture.Overlay, "_editLayer", layer);
        fixture.Overlay.ReadModifiers = () => ModifierKeys.Control;
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, new SyntheticPresentationSource(), 0, Key.Z)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = fixture.Overlay };
        Invoke(fixture.Overlay, "OnPreviewKeyDown", fixture.Overlay, key);
        Require(key.Handled && layer.PenStrokes.Count == 0, "Canvas undo shortcut was lost.");
        fixture.Overlay.ReadModifiers = () => ModifierKeys.Control | ModifierKeys.Shift;
        key = new KeyEventArgs(Keyboard.PrimaryDevice, new SyntheticPresentationSource(), 0, Key.Z)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = fixture.Overlay };
        Invoke(fixture.Overlay, "OnPreviewKeyDown", fixture.Overlay, key);
        Require(key.Handled && layer.PenStrokes.Count == 1, "Canvas redo shortcut was lost.");
    }

    private static void WheelSnapInvalidatesOldResults()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var request = SeedOldResult(fixture);
        Set(fixture.Overlay, "_isSnapProposal", true);
        Set(fixture.Overlay, "_snapCandidates", new PixelRect[] { fixture.Selection, new(16, 16, 32, 32) });
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent };
        Invoke(fixture.Overlay, "OnMouseWheel", fixture.Overlay, wheel);
        Require(wheel.Handled && Field<PixelRect>(fixture.Overlay, "_selection") == new PixelRect(16, 16, 32, 32), "Wheel did not choose the next synthetic snap candidate.");
        AssertInvalidated(fixture, request);
    }

    private static void ActiveMosaicKeepsTheTranslationVisible()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        Set(fixture.Overlay, "_editLayer", new EditLayer(fixture.Selection));
        Set(fixture.Overlay, "_activeEditTool", EditTool.Mosaic);
        Set(fixture.Overlay, "_activeEditPoints", new List<PixelPoint> { new(32, 32) });
        SetDisplay(fixture.Overlay, fixture.Selection);
        Invoke(fixture.Overlay, "UpdateEditPreview");
        var preview = Find<Image>(fixture.Overlay, "EditPreview");
        var source = (BitmapSource)preview.Source;
        Require(ReadPixel(new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0), 3, 3) == 0, "Active mosaic preview is opaque outside its stroke.");
        Require(ReadPixel(new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0), 32, 32) == fixture.Source.GetPixel(32, 32).ToArgb(), "Active mosaic lost its source pixel semantics.");
    }

    private static void RoutedTextBoxEnterUsesTheProductionBinding()
    {
        using var fixture = new Fixture(new PixelRect(0, 0, 64, 64));
        var request = SeedOldResult(fixture);
        var deliveries = 0;
        fixture.Overlay.CaptureDelivery = _ => { deliveries++; return false; };
        var input = Find<TextBox>(fixture.Overlay, "SelectionWidthInput");
        input.Text = "32"; Find<TextBox>(fixture.Overlay, "SelectionHeightInput").Text = "32";
        var key = KeyEvent(input, Key.Enter);
        input.RaiseEvent(key);
        Require(ReferenceEquals(key.OriginalSource, input), "Routed input did not retain its field source.");
        Require(key.Handled && deliveries == 0 && Field<PixelRect>(fixture.Overlay, "_selection") == new PixelRect(16, 16, 32, 32), "The bound Window preview handler did not apply size safely.");
        AssertInvalidated(fixture, request);
    }

    private static void RoutedMouseMoveUsesTheProductionBinding()
    {
        using var fixture = new Fixture(new PixelRect(8, 8, 32, 32));
        var request = SeedOldResult(fixture);
        SetEnum(fixture.Overlay, "_mode", "Move");
        Set(fixture.Overlay, "_dragStart", new PixelPoint(8, 8));
        Set(fixture.Overlay, "_selectionAtDragStart", fixture.Selection);
        fixture.Overlay.ReadPointerOverride = _ => new PixelPoint(16, 16);
        fixture.Overlay.ReadLeftButton = _ => MouseButtonState.Pressed;
        var pointer = new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent };
        fixture.Overlay.RaiseEvent(pointer);
        Require(pointer.Handled && Field<PixelRect>(fixture.Overlay, "_selection") == new PixelRect(16, 16, 32, 32), "The bound MouseMove handler did not move the selection.");
        AssertInvalidated(fixture, request);
    }

    private static OverlayTranslationRequest SeedOldResult(Fixture fixture)
    {
        SetDisplay(fixture.Overlay, Field<PixelRect>(fixture.Overlay, "_selection"));
        Find<FrameworkElement>(fixture.Overlay, "TextExtractionPanel").Visibility = Visibility.Visible;
        return Sequencer(fixture).Begin(Field<PixelRect>(fixture.Overlay, "_selection"));
    }
    private static void AssertInvalidated(Fixture fixture, OverlayTranslationRequest request)
    {
        Require(!Sequencer(fixture).IsLatest(request.Version), "Production mutation left the old request valid.");
        Require(request.Token.IsCancellationRequested, "Production mutation did not cancel the pending token.");
        Require(Field<object?>(fixture.Overlay, "_translationDisplayState") is null, "Old displayed translation survived input mutation.");
        Require(Find<Canvas>(fixture.Overlay, "TranslationLayer").Children.Count == 0, "Old translation pixels survived input mutation.");
        Require(Find<FrameworkElement>(fixture.Overlay, "TranslationToggle").Visibility == Visibility.Collapsed, "Old toggle remained available.");
        Require(Find<FrameworkElement>(fixture.Overlay, "TextExtractionPanel").Visibility == Visibility.Collapsed, "Old extraction remained displayed.");
    }
    private static void SetDisplay(SelectionOverlayWindow overlay, PixelRect selection)
    {
        var item = new InPlaceLayoutItem(new OcrTextBlock("source", selection, []), "", selection,
            InPlaceBackgroundMode.ComplexBacking, Color.Blue, Color.White);
        Set(overlay, "_translationDisplayState", new TranslationDisplayState(TranslationDisplayMode.Translated, [item]));
        var canvas = Find<Canvas>(overlay, "TranslationLayer");
        canvas.Children.Clear();
        canvas.Children.Add(new System.Windows.Shapes.Rectangle { Width = selection.Width, Height = selection.Height, Fill = System.Windows.Media.Brushes.Blue });
        Canvas.SetLeft(canvas.Children[0], selection.X); Canvas.SetTop(canvas.Children[0], selection.Y);
        canvas.Visibility = Visibility.Visible;
        Find<FrameworkElement>(overlay, "TranslationToggle").Visibility = Visibility.Visible;
    }
    private static void ApplySize(SelectionOverlayWindow overlay, int width, int height)
    {
        Find<TextBox>(overlay, "SelectionWidthInput").Text = width.ToString();
        Find<TextBox>(overlay, "SelectionHeightInput").Text = height.ToString();
        Invoke(overlay, "OnApplySelectionSize", overlay, new RoutedEventArgs());
    }
    private static KeyEventArgs KeyEvent(TextBox source, Key key) => new(Keyboard.PrimaryDevice, new SyntheticPresentationSource(), 0, key)
    { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = source };
    private static int ReadPixel(BitmapSource source, int x, int y)
    {
        var pixel = new byte[4]; source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]).ToArgb();
    }
    private static void RequirePixels(Bitmap expected, Bitmap actual, string message)
    {
        Require(expected.Size == actual.Size, "Image dimensions differ.");
        for (var y = 0; y < expected.Height; y++) for (var x = 0; x < expected.Width; x++)
            Require(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), message + $" Pixel {x},{y}.");
    }
    private static OverlayTranslationSequencer Sequencer(Fixture fixture) => Field<OverlayTranslationSequencer>(fixture.Overlay, "_translationSequencer");
    private static T Find<T>(FrameworkElement overlay, string name) => (T)overlay.FindName(name);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void SetEnum(object target, string name, string value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void EnsureResources()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application();
        application.Resources["AppFont"] = new FontFamily("Segoe UI, Microsoft YaHei UI");
    }
    private sealed class Fixture : IDisposable
    {
        public Fixture(PixelRect selection)
        {
            Selection = selection;
            Source = new Bitmap(64, 64);
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) Source.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
            Overlay = new SelectionOverlayWindow(new CapturedFrame(new Bitmap(Source), new Rectangle(0, 0, 64, 64)), selection, clipboardTextSink: new ForbiddenClipboard(),
                windowCandidateSource: new EmptyWindows(), uiElementCandidateSource: new EmptyElements());
            Overlay.ReadModifiers = () => ModifierKeys.None;
        }
        public Bitmap Source { get; }
        public PixelRect Selection { get; }
        public SelectionOverlayWindow Overlay { get; }
        public void Dispose() { Overlay.Close(); Source.Dispose(); }
    }
    private sealed class ForbiddenClipboard : IClipboardTextSink { public void SetText(string text) => throw new Exception("Clipboard must not be used."); }
    private sealed class EmptyWindows : IWindowCandidateSource { public IEnumerable<WindowSnapCandidate> Enumerate() => []; }
    private sealed class EmptyElements : IUiElementCandidateSource { public IReadOnlyList<PixelRect> GetHierarchy(System.Drawing.Point point,IntPtr window) => []; }
    private sealed class SyntheticPresentationSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
}

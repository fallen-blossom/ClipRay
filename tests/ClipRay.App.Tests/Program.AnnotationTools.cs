using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipRay.App.Capture;
using ClipRay.App.Editing;
using ClipRay.App.Overlay;
using ClipRay.App.Scrolling;
using ClipRay.Core.Geometry;
using ClipRay.PreparedCli;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

internal static partial class Program
{
    private static RegisteredCase[] CreateAnnotationCases() =>
    [
        new("live-pen-before-release", AnnotationLivePen),
        new("smooth-pen-pixels", AnnotationSmoothPen),
        new("pen-selection-contracts", AnnotationPenSelectionContracts),
        new("annotation-menu-layout", AnnotationMenuLayout),
        new("tapered-arrow-pixels", AnnotationArrowPixels),
        new("arrow-origin-preview-only", AnnotationArrowOrigin),
        new("straight-line-input-and-export", AnnotationLine),
        new("shapes-text-undo-redo", AnnotationHistory),
        new("text-entry-cancel-commit", AnnotationTextEntry),
        new("shared-color-and-pixel-units", AnnotationProperties),
        new("long-capture-buttons", AnnotationScrollButtons),
    ];

    private static void AnnotationSmoothPen()
    {
        using var source = new Bitmap(140, 100);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(24, 24, 24));
        var layer = new EditLayer(new PixelRect(0, 0, 140, 100));
        layer.AddPenStroke(new PenStroke([new(30, 30), new(95, 30)], Color.Gold, 12));
        layer.AddPenStroke(new PenStroke([new(30, 70)], Color.Gold, 12));
        using var output = EditRenderer.Composite(source, layer);
        var folder = Path.Combine(Path.GetTempPath(), "clipray-smooth-pen-preview");
        Directory.CreateDirectory(folder);
        output.Save(Path.Combine(folder, "pen.png"));
        Equal(source.GetPixel(100, 40).ToArgb(), output.GetPixel(100, 40).ToArgb());
        Equal(source.GetPixel(39, 79).ToArgb(), output.GetPixel(39, 79).ToArgb());
        Equal(true, output.GetPixel(27, 69).R > 100);
        Equal(true, output.GetPixel(29, 25).R > 100);
        using var overlay = EditRenderer.RenderOverlay(source, layer);
        Equal(0, overlay.GetPixel(100, 40).A);
        Equal(true, Enumerable.Range(24, 13).Any(y => Enumerable.Range(23, 10).Any(x => overlay.GetPixel(x, y).A is > 0 and < 255)));
    }

    private static void AnnotationPenSelectionContracts()
    {
        MapsPenPointsFromSourcePixelsIntoSelectedOutput();
        CompositesRetainedEditsInDrawingOrder();
        PreservesRetainedEditsWhenTheSelectionIsAdjusted();
        DrawsAPenStrokeCrossingTheSelectedBoundary();
        ClipsRetainedEditsToTheSelectedRegion();
    }

    private static object? AnnotationInvoke(object target, string method, params object[] args)
    {
        try { return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(target, args); }
        catch (TargetInvocationException error) { Console.Error.WriteLine(error.InnerException); throw; }
    }

    private static SelectionOverlayWindow AnnotationOverlay()
    {
        EnsureWpfResources();
        var window = new SelectionOverlayWindow(new CapturedFrame(new Bitmap(320, 180), new System.Drawing.Rectangle(0, 0, 320, 180)),
            new PixelRect(0, 0, 320, 180), clipboardTextSink: new AnnotationForbiddenClipboard(),
            windowCandidateSource: new AnnotationEmptyWindows(), uiElementCandidateSource: new AnnotationEmptyElements())
            { Width = 320, Height = 180 };
        window.ReadModifiers = () => ModifierKeys.None;
        window.Measure(new System.Windows.Size(320, 180));
        window.Arrange(new Rect(0, 0, 320, 180));
        return window;
    }

    private static void AnnotationLivePen()
    {
        var window = AnnotationOverlay();
        try
        {
            AnnotationInvoke(window, "SelectEditingTool", EditTool.Pen);
            GetPrivateField<EditLayer>(window, "_editLayer").PenWidth = 24;
            var pointer = new PixelPoint(30, 30);
            window.ReadPointerOverride = _ => pointer;
            window.ReadLeftButton = _ => MouseButtonState.Pressed;
            AnnotationInvoke(window, "OnMouseLeftButtonDown", window,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            var dot = (System.Windows.Shapes.Ellipse)window.FindName("LivePenDot");
            var line = (System.Windows.Shapes.Polyline)window.FindName("LivePen");
            Equal(Visibility.Visible, dot.Visibility);
            Equal(true, Math.Abs(Canvas.GetLeft(dot) + dot.Width / 2 - line.Points[0].X) < 1e-9);
            Equal(true, Math.Abs(Canvas.GetTop(dot) + dot.Height / 2 - line.Points[0].Y) < 1e-9);
            pointer = new(35, 30);
            AnnotationInvoke(window, "OnMouseMove", window, new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 1) { RoutedEvent = Mouse.MouseMoveEvent });
            Equal(Visibility.Visible, ((FrameworkElement)window.FindName("ActiveEditPreview")).Visibility);
            Equal(2, ((System.Windows.Shapes.Polyline)window.FindName("LivePen")).Points.Count);
            Equal(System.Windows.Media.PenLineCap.Round, line.StrokeStartLineCap);
            Equal(System.Windows.Media.PenLineCap.Round, line.StrokeEndLineCap);
            Equal(Visibility.Collapsed, dot.Visibility);
            Equal(false, GetPrivateField<EditLayer>(window, "_editLayer").HasEdits);
            AnnotationInvoke(window, "OnMouseLeftButtonUp", window,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 2, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Equal(true, GetPrivateField<EditLayer>(window, "_editLayer").HasEdits);
            Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("ActiveEditPreview")).Visibility);
        }
        finally { window.Close(); }
    }

    private static void AnnotationMenuLayout()
    {
        var window = AnnotationOverlay();
        try
        {
            AnnotationInvoke(window, "SelectEditingTool", EditTool.Arrow);
            var menu = (System.Windows.Controls.ContextMenu)AnnotationInvoke(window, "CreateAnnotationMenu")!;
            Equal(false, menu.IsOpen);
            Equal(window.FindResource("DarkContextMenuStyle"), menu.Style);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Equal(true, items.Select(item => item.Header).SequenceEqual(new[] { "画笔", "直线", "箭头", "矩形", "圆形", "椭圆", "文字框", "关闭标注" }));
            foreach (var item in items)
            {
                Equal(window.FindResource("AnnotationMenuItemStyle"), item.Style);
                item.ApplyTemplate();
                item.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Equal(true, item.DesiredSize.Width <= 150);
                var check = (System.Windows.Shapes.Path)item.Template.FindName("CheckGlyph", item);
                Equal(item.Header.Equals("箭头") ? Visibility.Visible : Visibility.Collapsed, check.Visibility);
            }
            Equal(false, menu.IsOpen);
            items.Single(item => item.Header.Equals("直线")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Equal(EditTool.Line, GetPrivateField<EditTool>(window, "_activeEditTool"));
            items.Single(item => item.Header.Equals("矩形")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Equal(EditTool.Rectangle, GetPrivateField<EditTool>(window, "_activeEditTool"));
            items[^1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Equal(EditTool.None, GetPrivateField<EditTool>(window, "_activeEditTool"));
        }
        finally { window.Close(); }
    }

    private static void AnnotationArrowPixels()
    {
        using var source = new Bitmap(240, 110);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(24, 24, 24));
        var layer = new EditLayer(new PixelRect(0, 0, 240, 110));
        layer.AddAnnotation(new(AnnotationKind.Arrow, new(10, 30), new(225, 30), Color.Red, 3));
        layer.AddAnnotation(new(AnnotationKind.Arrow, new(225, 95), new(15, 55), Color.Red, 3));
        using var output = EditRenderer.Composite(source, layer);
        int RedPixels(int x) => Enumerable.Range(10, 40).Count(y => output.GetPixel(x, y).R > 100);
        Equal(true, RedPixels(175) > RedPixels(30));
        var reverse = AnnotationGeometry.Arrow(new(200, 30), new(10, 30), 3);
        Equal(new PixelPoint(10, 30), reverse[3]);
        var circle = AnnotationGeometry.ConstrainCircle(new(20, 20), new(100, 60), new(0, 0, 110, 70));
        Equal(circle.X - 20, circle.Y - 20);
        Equal(true, circle.Y < 70);
        var folder = Path.Combine(Path.GetTempPath(), "clipray-annotation-tools-preview");
        Directory.CreateDirectory(folder);
        output.Save(Path.Combine(folder, "tapered-arrows.png"));
    }

    private static void AnnotationArrowOrigin()
    {
        var window = AnnotationOverlay();
        try
        {
            AnnotationInvoke(window, "SelectEditingTool", EditTool.Arrow);
            var pointer = new PixelPoint(40, 40);
            window.ReadPointerOverride = _ => pointer;
            window.ReadLeftButton = _ => MouseButtonState.Pressed;
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            AnnotationInvoke(window, "OnMouseLeftButtonDown", window, down);
            var dot = (System.Windows.Shapes.Ellipse)window.FindName("ArrowOriginDot");
            Equal(Visibility.Visible, dot.Visibility);
            Equal(true, dot.Width >= 8 && dot.Height == dot.Width);
            var beforeX = Canvas.GetLeft(dot); var beforeY = Canvas.GetTop(dot);
            pointer = new(170, 40);
            AnnotationInvoke(window, "OnMouseMove", window, new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
            Equal(beforeX, Canvas.GetLeft(dot)); Equal(beforeY, Canvas.GetTop(dot));
            AnnotationInvoke(window, "OnMouseLeftButtonUp", window,
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
            Equal(Visibility.Collapsed, dot.Visibility);
            var layer = GetPrivateField<EditLayer>(window, "_editLayer");
            Equal(1, layer.Operations.Count);
            Equal(AnnotationKind.Arrow, ((AnnotationEditOperation)layer.Operations.Single()).Annotation.Kind);
            using var source = new Bitmap(320, 180);
            using var exported = EditRenderer.Composite(source, layer);
            Equal(source.GetPixel(40, 44).ToArgb(), exported.GetPixel(40, 44).ToArgb());
            pointer = new(70, 80);
            AnnotationInvoke(window, "OnMouseLeftButtonDown", window, down);
            Equal(Visibility.Visible, dot.Visibility);
            Equal(true, (bool)AnnotationInvoke(window, "HandleAnnotationKey", Key.Escape, ModifierKeys.None)!);
            Equal(Visibility.Collapsed, dot.Visibility);
            Equal(1, layer.Operations.Count);
        }
        finally { window.Close(); }
    }

    private static void AnnotationLine()
    {
        foreach (var (start, end) in new[] { (new PixelPoint(30, 40), new PixelPoint(170, 40)),
            (new PixelPoint(80, 130), new PixelPoint(80, 20)), (new PixelPoint(200, 130), new PixelPoint(30, 30)) })
        {
            var window = AnnotationOverlay();
            try
            {
                AnnotationInvoke(window, "SelectEditingTool", EditTool.Line);
                var layer = GetPrivateField<EditLayer>(window, "_editLayer");
                layer.PenWidth = 6;
                var pointer = start;
                window.ReadPointerOverride = _ => pointer;
                window.ReadLeftButton = _ => MouseButtonState.Pressed;
                AnnotationInvoke(window, "OnMouseLeftButtonDown", window,
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                pointer = end;
                AnnotationInvoke(window, "OnMouseMove", window, new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
                var shape = (System.Windows.Shapes.Path)window.FindName("LiveShape");
                Equal(true, shape.Data is System.Windows.Media.LineGeometry);
                Equal(Visibility.Collapsed, ((System.Windows.Shapes.Ellipse)window.FindName("ArrowOriginDot")).Visibility);
                AnnotationInvoke(window, "OnMouseLeftButtonUp", window,
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
                var annotation = ((AnnotationEditOperation)layer.Operations.Single()).Annotation;
                Equal(AnnotationKind.Line, annotation.Kind); Equal(start, annotation.Start); Equal(end, annotation.End);
                using var source = new Bitmap(320, 180);
                using var exported = EditRenderer.Composite(source, layer);
                Equal(true, exported.GetPixel((int)((start.X + end.X) / 2), (int)((start.Y + end.Y) / 2)).R > 200);
                Equal(true, layer.UndoLast()); Equal(false, layer.HasEdits); Equal(true, layer.RedoLast());
            }
            finally { window.Close(); }
        }
    }

    private static void AnnotationHistory()
    {
        using var source = new Bitmap(180, 100);
        using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.White);
        var layer = new EditLayer(new PixelRect(0, 0, 180, 100));
        layer.AddAnnotation(new(AnnotationKind.Rectangle, new(10, 10), new(60, 45), Color.Blue, 3));
        layer.AddAnnotation(new(AnnotationKind.Ellipse, new(75, 10), new(135, 45), Color.Green, 3));
        layer.AddAnnotation(new(AnnotationKind.Text, new(10, 55), new(170, 98), Color.Red, 3, "ClipRay 文字", 18));
        using var output = EditRenderer.Composite(source, layer);
        Equal(true, output.GetPixel(10, 20).B > output.GetPixel(10, 20).R);
        Equal(true, output.GetPixel(75, 27).G > output.GetPixel(75, 27).R);
        Equal(true, Enumerable.Range(55, 43).Any(y => Enumerable.Range(10, 150).Any(x => output.GetPixel(x, y).R > output.GetPixel(x, y).G)));
        using var cache = new EditPreviewCache();
        var preview = cache.GetOverlay(source, layer, null);
        Equal(true, preview.GetPixel(10, 20).B > preview.GetPixel(10, 20).R);
        Equal(true, layer.UndoLast()); Equal(true, layer.RedoLast());
        for (var i = 0; i < 3; i++) Equal(true, layer.UndoLast());
        Equal(false, layer.HasEdits);
        Equal(0, cache.GetOverlay(source, layer, null).GetPixel(10, 20).A);
    }

    private static void AnnotationTextEntry()
    {
        var window = AnnotationOverlay();
        try
        {
            AnnotationInvoke(window, "SelectEditingTool", EditTool.Text);
            AnnotationInvoke(window, "StartTextEditor", new PixelPoint(20, 20), new PixelPoint(200, 100));
            var editor = (System.Windows.Controls.TextBox)window.FindName("AnnotationTextEditor");
            Equal(true, editor.AcceptsReturn);
            editor.Text = "第一行\nsecond line";
            var ime = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, new AnnotationPresentationSource(), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = editor };
            typeof(System.Windows.Input.KeyEventArgs).GetMethod("MarkImeProcessed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ime, null);
            AnnotationInvoke(window, "OnPreviewKeyDown", editor, ime);
            Equal(false, ime.Handled);
            Equal("第一行\nsecond line", editor.Text);
            Equal(Visibility.Visible, editor.Visibility);
            Equal(true, (bool)AnnotationInvoke(window, "HandleAnnotationKey", Key.Escape, ModifierKeys.None)!);
            Equal(false, GetPrivateField<EditLayer>(window, "_editLayer").HasEdits);
            AnnotationInvoke(window, "StartTextEditor", new PixelPoint(20, 20), new PixelPoint(200, 100));
            editor.Text = "第一行\nsecond line";
            Equal(false, (bool)AnnotationInvoke(window, "HandleAnnotationKey", Key.Enter, ModifierKeys.None)!);
            Equal(true, (bool)AnnotationInvoke(window, "HandleAnnotationKey", Key.Enter, ModifierKeys.Control)!);
            Equal(true, GetPrivateField<EditLayer>(window, "_editLayer").HasEdits);
            Equal(Visibility.Collapsed, editor.Visibility);
            var layer = GetPrivateField<EditLayer>(window, "_editLayer");
            Equal(true, layer.UndoLast());
            Equal(false, layer.HasEdits);
            var dispatched = false;
            void CheckCapture(object? sender, TranslationCapture capture)
            {
                dispatched = true;
                using (capture.Bitmap)
                    Equal(true, Enumerable.Range(20, 80).Any(y => Enumerable.Range(20, 180).Any(x => capture.Bitmap.GetPixel(x, y).R > 80)));
            }
            window.TextExtractionRequested += CheckCapture;
            window.TranslationRequested += CheckCapture;
            foreach (var request in new[] { "RequestTextExtraction", "RequestTranslation" })
            {
                AnnotationInvoke(window, "StartTextEditor", new PixelPoint(20, 20), new PixelPoint(200, 100));
                editor.Text = "OCR annotation";
                dispatched = false;
                AnnotationInvoke(window, request);
                Equal(true, dispatched);
                Equal(true, layer.HasEdits);
                Equal(Visibility.Collapsed, editor.Visibility);
                Equal(true, layer.UndoLast());
                Equal(false, layer.HasEdits);
            }
        }
        finally { window.Close(); }
    }

    private static void AnnotationProperties()
    {
        var window = AnnotationOverlay();
        try
        {
            foreach (var tool in new[] { EditTool.Arrow, EditTool.Rectangle, EditTool.Circle, EditTool.Ellipse, EditTool.Text })
            {
                AnnotationInvoke(window, "SelectEditingTool", tool);
                Equal(Visibility.Visible, ((FrameworkElement)window.FindName("PenProperties")).Visibility);
                Equal(true, OverlayInputPolicy.ShouldBeginEdit(tool, false, true));
                Equal(false, OverlayInputPolicy.ShouldBeginEdit(tool, true, true));
            }
            var labels = ((StackPanel)window.FindName("SizeProperties")).Children.OfType<TextBlock>().Select(label => label.Text).ToArray();
            Equal(true, labels.SequenceEqual(new[] { "宽 (px)", "高 (px)" }));
            Equal(true, double.IsNaN(((System.Windows.Controls.Button)window.FindName("SizeButton")).Width));
            ((System.Windows.Controls.TextBox)window.FindName("PenColorInput")).Text = "#128BDF";
            Equal(Color.FromArgb(0x12, 0x8B, 0xDF), GetPrivateField<EditLayer>(window, "_editLayer").PenColor);
        }
        finally { window.Close(); }
    }

    private static void AnnotationScrollButtons()
    {
        var window = new ScrollCaptureControlWindow();
        try
        {
            var root = (Grid)((Border)window.Content).Child;
            var buttons = root.Children.OfType<WrapPanel>().Single().Children.OfType<System.Windows.Controls.Button>().ToArray();
            Equal(3, buttons.Length); Equal("取消", buttons[1].ToolTip); Equal("完成", buttons[2].ToolTip);
            var canceled = 0; var finished = 0;
            window.CancelRequested += (_, _) => canceled++;
            window.StopRequested += (_, _) => finished++;
            buttons[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            buttons[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Equal(1, canceled); Equal(1, finished);
            window.IsNormalTeardown = true;
        }
        finally { window.Close(); }
    }

    private sealed class AnnotationForbiddenClipboard : IClipboardTextSink { public void SetText(string text) => throw new InvalidOperationException("Clipboard is forbidden in the background fixture."); }
    private sealed class AnnotationEmptyWindows : IWindowCandidateSource { public IEnumerable<WindowSnapCandidate> Enumerate() => []; }
    private sealed class AnnotationEmptyElements : IUiElementCandidateSource { public IReadOnlyList<PixelRect> GetHierarchy(System.Drawing.Point point, IntPtr window) => []; }
    private sealed class AnnotationPresentationSource : PresentationSource
    {
        public override System.Windows.Media.Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget? GetCompositionTargetCore() => null;
    }
}

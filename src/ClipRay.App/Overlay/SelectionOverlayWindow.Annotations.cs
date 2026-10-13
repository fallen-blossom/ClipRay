using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipRay.App.Editing;
using ClipRay.Core.Geometry;
using Color = System.Windows.Media.Color;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ClipRay.App.Overlay;

public partial class SelectionOverlayWindow
{
    private PixelRect? _pendingTextBounds;
    private void OnAnnotationMenu(object sender, RoutedEventArgs e)
    {
        var menu = CreateAnnotationMenu();
        AnnotationToolButton.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu CreateAnnotationMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = AnnotationToolButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            Style = (Style)FindResource("DarkContextMenuStyle"), MinWidth = 150,
        };
        foreach (var (label, tool) in new[] { ("画笔", EditTool.Pen), ("直线", EditTool.Line), ("箭头", EditTool.Arrow),
            ("矩形", EditTool.Rectangle), ("圆形", EditTool.Circle), ("椭圆", EditTool.Ellipse), ("文字框", EditTool.Text) })
        {
            var item = new MenuItem { Header = T(label), IsCheckable = true, IsChecked = _activeEditTool == tool,
                Style = (Style)FindResource("AnnotationMenuItemStyle") };
            item.Click += (_, _) => { if (_activeEditTool != tool) SelectEditingTool(tool); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator { Style = (Style)FindResource("AnnotationMenuSeparatorStyle") });
        var finish = new MenuItem { Header = T("关闭标注"), Style = (Style)FindResource("AnnotationMenuItemStyle") };
        finish.Click += (_, _) => { if (_activeEditTool != EditTool.None) SelectEditingTool(_activeEditTool); };
        menu.Items.Add(finish);
        return menu;
    }

    private Annotation? ActiveShapeAnnotation()
    {
        if (_activeEditPoints is not { Count: > 0 } points) return null;
        var start = points[0]; var end = points[^1];
        if (_activeEditTool == EditTool.Circle
            || (ReadModifiers().HasFlag(ModifierKeys.Shift) && (_activeEditTool is EditTool.Rectangle or EditTool.Ellipse)))
            end = AnnotationGeometry.ConstrainCircle(start, end, _selection);
        var kind = _activeEditTool switch
        {
            EditTool.Arrow => AnnotationKind.Arrow,
            EditTool.Line => AnnotationKind.Line,
            EditTool.Rectangle or EditTool.Text => AnnotationKind.Rectangle,
            EditTool.Ellipse or EditTool.Circle => AnnotationKind.Ellipse,
            _ => (AnnotationKind?)null,
        };
        var layer = EnsureEditLayer();
        return kind is { } value ? new Annotation(value, start, end, layer.PenColor, layer.PenWidth) : null;
    }

    private System.Windows.Point AnnotationPoint(PixelPoint point) =>
        new((point.X - _selection.X) / PixelScaleX, (point.Y - _selection.Y) / PixelScaleY);

    private void UpdateActiveAnnotationPreview()
    {
        ArrowOriginDot.Visibility = Visibility.Collapsed;
        if (_activeEditPoints is not { Count: > 0 } points || _activeEditTool is EditTool.Mosaic or EditTool.Picker)
        {
            ActiveEditPreview.Visibility = Visibility.Collapsed;
            LivePen.Points.Clear();
            return;
        }
        var dip = ToDip(_selection);
        SetElement(ActiveEditPreview, dip.X, dip.Y, dip.Width, dip.Height);
        ActiveEditPreview.Visibility = Visibility.Visible;
        var layer = EnsureEditLayer();
        var color = layer.PenColor;
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        LivePen.Visibility = _activeEditTool == EditTool.Pen ? Visibility.Visible : Visibility.Collapsed;
        LiveShape.Visibility = _activeEditTool == EditTool.Pen ? Visibility.Collapsed : Visibility.Visible;
        LivePenDot.Visibility = Visibility.Collapsed;
        if (_activeEditTool == EditTool.Pen)
        {
            LivePen.Visibility = points.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            LivePen.Stroke = brush; LivePen.StrokeThickness = layer.PenWidth / PixelScaleX;
            if (LivePen.Points.Count > points.Count) LivePen.Points.Clear();
            for (var i = LivePen.Points.Count; i < points.Count; i++)
            {
                var point = AnnotationPoint(points[i]);
                LivePen.Points.Add(point);
            }
            if (points.Count == 1)
            {
                var point = AnnotationPoint(points[0]);
                SetElement(LivePenDot, point.X - layer.PenWidth / (2 * PixelScaleX),
                    point.Y - layer.PenWidth / (2 * PixelScaleY), layer.PenWidth / PixelScaleX, layer.PenWidth / PixelScaleY);
                LivePenDot.Fill = brush; LivePenDot.Visibility = Visibility.Visible;
            }
            return;
        }
        var annotation = ActiveShapeAnnotation();
        if (annotation is null) return;
        var bounds = annotation.Bounds;
        var rectangle = new Rect(AnnotationPoint(new(bounds.X, bounds.Y)),
            new System.Windows.Size(bounds.Width / PixelScaleX, bounds.Height / PixelScaleY));
        LiveShape.Fill = null; LiveShape.Stroke = brush; LiveShape.StrokeThickness = layer.PenWidth / PixelScaleX;
        if (annotation.Kind == AnnotationKind.Arrow)
        {
            var origin = AnnotationPoint(annotation.Start);
            var diameter = Math.Max(8, layer.PenWidth / PixelScaleX);
            SetElement(ArrowOriginDot, origin.X - diameter / 2, origin.Y - diameter / 2, diameter, diameter);
            ArrowOriginDot.Fill = brush;
            ArrowOriginDot.Visibility = Visibility.Visible;
            var vertices = AnnotationGeometry.Arrow(annotation.Start, annotation.End, annotation.Width);
            var geometry = new StreamGeometry();
            if (vertices.Length > 0)
            {
                using var context = geometry.Open();
                context.BeginFigure(AnnotationPoint(vertices[0]), isFilled: true, isClosed: true);
                context.PolyLineTo(vertices.Skip(1).Select(AnnotationPoint).ToArray(), isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze(); LiveShape.Data = geometry; LiveShape.Fill = brush; LiveShape.Stroke = null;
        }
        else if (annotation.Kind == AnnotationKind.Line)
        {
            LiveShape.StrokeStartLineCap = LiveShape.StrokeEndLineCap = PenLineCap.Round;
            LiveShape.Data = new LineGeometry(AnnotationPoint(annotation.Start), AnnotationPoint(annotation.End));
        }
        else LiveShape.Data = annotation.Kind == AnnotationKind.Ellipse
            ? new EllipseGeometry(rectangle) : new RectangleGeometry(rectangle);
    }

    private void StartTextEditor(PixelPoint start, PixelPoint end)
    {
        CommitTextEditor();
        var bounds = SelectionMath.FromPoints(start, end);
        if (bounds.Width < 6 || bounds.Height < 6)
            bounds = new(start.X, start.Y, Math.Min(260, _selection.Right - start.X), Math.Min(90, _selection.Bottom - start.Y));
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        _pendingTextBounds = bounds;
        var point = ToDip(new PixelPoint(bounds.X, bounds.Y));
        SetElement(AnnotationTextEditor, point.X, point.Y, bounds.Width / PixelScaleX, bounds.Height / PixelScaleY);
        AnnotationTextEditor.Text = "";
        AnnotationTextEditor.FontSize = TextSizeInput.Value / PixelScaleY;
        UpdateAnnotationTextColor();
        AnnotationTextEditor.Visibility = Visibility.Visible;
        AnnotationTextEditor.Focus();
    }

    private void UpdateAnnotationTextColor()
    {
        if (AnnotationTextEditor is null || _editLayer is null) return;
        var c = _editLayer.PenColor;
        AnnotationTextEditor.Foreground = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        if (_pendingTextBounds is not null) InvalidateInputResults();
    }

    private void OnTextSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AnnotationTextEditor is not null) AnnotationTextEditor.FontSize = e.NewValue / PixelScaleY;
        if (_pendingTextBounds is not null) InvalidateInputResults();
    }

    private void OnAnnotationTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_pendingTextBounds is not null) InvalidateInputResults();
    }

    private void CommitTextEditor()
    {
        if (_pendingTextBounds is not { } bounds) return;
        _pendingTextBounds = null;
        if (!string.IsNullOrWhiteSpace(AnnotationTextEditor.Text))
        {
            var layer = EnsureEditLayer();
            layer.AddAnnotation(new Annotation(AnnotationKind.Text, new(bounds.Left, bounds.Top),
                new(bounds.Right, bounds.Bottom), layer.PenColor, layer.PenWidth,
                AnnotationTextEditor.Text, (float)TextSizeInput.Value));
            InvalidateInputResults();
        }
        AnnotationTextEditor.Visibility = Visibility.Collapsed;
        AnnotationTextEditor.Clear();
        UpdateEditPreview();
    }

    private bool HandleAnnotationKey(Key key, ModifierKeys modifiers)
    {
        if (_pendingTextBounds is not null && key == Key.Escape)
        {
            _pendingTextBounds = null; AnnotationTextEditor.Clear();
            AnnotationTextEditor.Visibility = Visibility.Collapsed; ResetDoubleEscape(); Focus(); return true;
        }
        if (_pendingTextBounds is not null && key == Key.Enter && modifiers == ModifierKeys.Control)
        {
            CommitTextEditor(); Focus(); return true;
        }
        if (_mode == InteractionMode.Edit && key == Key.Escape)
        {
            _activeEditPoints = null; _mode = InteractionMode.None;
            ReleaseMouseCapture(); UpdateEditPreview(); ResetDoubleEscape(); return true;
        }
        return false;
    }
}

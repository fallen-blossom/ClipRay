using System.Drawing;
using ClipRay.App.Editing;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Overlay;

public static class OverlayInputPolicy
{
    public static bool ShouldBeginEdit(EditTool tool, bool isResizeHandle, bool isInsideSelection) =>
        tool is EditTool.Pen or EditTool.Mosaic or EditTool.Picker or EditTool.Arrow or EditTool.Line
            or EditTool.Rectangle or EditTool.Ellipse or EditTool.Circle or EditTool.Text
        && !isResizeHandle
        && isInsideSelection;
}

public static class OverlayPickerInputRouter
{
    public static bool TryRoutePicker(
        EditTool tool,
        PixelRect selection,
        PixelPoint point,
        bool isResizeHandle,
        Bitmap source,
        EditLayer layer,
        IClipboardTextSink clipboard,
        out SourceColorPickResult result,
        Action<TimeSpan>? retryDelay = null)
    {
        if (tool != EditTool.Picker || isResizeHandle || !SelectionDrawableBounds.Contains(selection, point))
        {
            result = default;
            return false;
        }

        result = new SourceColorPicker(layer, clipboard, retryDelay).Pick(source, point);
        return true;
    }
}

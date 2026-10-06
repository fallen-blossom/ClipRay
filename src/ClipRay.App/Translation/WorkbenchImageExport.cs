using System.Drawing;
using ClipRay.App.Overlay;
using ClipRay.Core.Geometry;

namespace ClipRay.App.Translation;

public static class WorkbenchImageExport
{
    // Capture bounds include OCR context. Only the user's selected pixels leave the workbench.
    public static Bitmap Render(TranslationWorkbenchDocument document)
    {
        using var source = document.CopySource();
        var selection = document.Selection;
        var bounds = document.CaptureBounds;
        var local = new PixelRect(selection.X - bounds.X, selection.Y - bounds.Y, selection.Width, selection.Height);
        var items = document.Session?.Regions
            .Where(region => region.State == WorkbenchRegionState.Translated && region.Layout is not null)
            .Select(region => region.Layout!).ToArray() ?? [];
        return TranslatedSelectionRenderer.Render(source, local, items);
    }
}

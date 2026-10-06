using System.Drawing;
using System.Windows.Forms;

namespace ClipRay.App;

internal static class TrayMenuTheme
{
    public static void Apply(ContextMenuStrip menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.BackColor = Color.FromArgb(24, 29, 39);
        menu.ForeColor = Color.FromArgb(239, 243, 250);
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()) { RoundedEdges = false };
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(24, 29, 39);
        public override Color ImageMarginGradientBegin => Color.FromArgb(24, 29, 39);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(24, 29, 39);
        public override Color ImageMarginGradientEnd => Color.FromArgb(24, 29, 39);
        public override Color MenuBorder => Color.FromArgb(72, 84, 108);
        public override Color MenuItemBorder => Color.FromArgb(68, 82, 108);
        public override Color MenuItemSelected => Color.FromArgb(52, 64, 84);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(52, 64, 84);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(52, 64, 84);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(67, 81, 106);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(67, 81, 106);
        public override Color SeparatorDark => Color.FromArgb(67, 78, 98);
        public override Color SeparatorLight => Color.FromArgb(24, 29, 39);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClipRay.App;

internal static class TrayMenuTheme
{
    public static void Apply(ToolStripDropDown menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.BackColor = Color.FromArgb(24, 29, 39);
        menu.ForeColor = Color.FromArgb(239, 243, 250);
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()) { RoundedEdges = false };
        if (menu is ToolStripDropDownMenu dropDown)
        {
            dropDown.ShowImageMargin = true;
            dropDown.ShowCheckMargin = false;
        }
    }

    public static Bitmap CreateSettingsIcon()
    {
        var image = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Eight teeth and a hollow center remain legible at the normal tray menu scale.
        var points = Enumerable.Range(0, 32).Select(index =>
        {
            var angle = index * Math.PI / 16 - Math.PI / 2;
            var radius = index % 4 is 0 or 3 ? 7f : 5.4f;
            return new PointF(8 + (float)Math.Cos(angle) * radius, 8 + (float)Math.Sin(angle) * radius);
        }).ToArray();
        using var shape = new GraphicsPath(FillMode.Alternate);
        shape.AddPolygon(points);
        shape.AddEllipse(5.3f, 5.3f, 5.4f, 5.4f);
        using var brush = new SolidBrush(Color.FromArgb(239, 243, 250));
        graphics.FillPath(brush, shape);
        return image;
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

using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace Cells.UI;

// Context menus in the widget's colours: ink selection, square check marks and drawn arrows.
static class Menus
{
    public static Forms.ContextMenuStrip Create(Theme theme) => new()
    {
        Renderer = new FlatRenderer(theme),
        ShowImageMargin = false,
        ShowCheckMargin = true,
        Font = new Font("Arial", 9f),
        BackColor = theme.Gdi(theme.Body),
        ForeColor = theme.Gdi(theme.Ink),
    };

    public static Forms.ToolStripMenuItem Item(string text, Action onClick, bool check = false, bool enabled = true)
    {
        var item = new Forms.ToolStripMenuItem(text) { Checked = check, Enabled = enabled, Padding = new Forms.Padding(2, 3, 2, 3) };
        item.Click += (_, _) => onClick();
        return item;
    }

    public static Forms.ToolStripMenuItem Sub(string text, IEnumerable<Forms.ToolStripItem> items)
    {
        var item = new Forms.ToolStripMenuItem(text) { Padding = new Forms.Padding(2, 3, 2, 3) };
        item.DropDownItems.AddRange(items.ToArray());
        if (item.DropDown is Forms.ToolStripDropDownMenu drop)
        {
            drop.ShowImageMargin = false;
            drop.ShowCheckMargin = true;
        }
        return item;
    }

    public static Forms.ToolStripItem Label(string text) => new Forms.ToolStripMenuItem(text) { Enabled = false, Padding = new Forms.Padding(2, 3, 2, 3) };

    sealed class FlatRenderer(Theme theme) : Forms.ToolStripProfessionalRenderer(new Table(theme))
    {
        Color C(Media.Color c) => theme.Gdi(c);

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? C(theme.Dim) : e.Item.Selected ? C(theme.Body) : C(theme.Ink);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            int s = Math.Max(5, r.Height / 3);
            using var brush = new SolidBrush(e.Item.Selected ? C(theme.Body) : C(theme.Ink));
            e.Graphics.FillRectangle(brush, r.Left + (r.Width - s) / 2, r.Top + (r.Height - s) / 2, s, s);
        }

        protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
        {
            var r = e.ArrowRectangle;
            int h = Math.Max(6, r.Height / 3), w = h / 2 + 1;
            int x = r.Left + (r.Width - w) / 2, y = r.Top + (r.Height - h) / 2;
            using var brush = new SolidBrush(e.Item?.Selected == true ? C(theme.Body) : C(theme.Ink));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPolygon(brush, new[] { new Point(x, y), new Point(x + w, y + h / 2), new Point(x, y + h) });
        }
    }

    sealed class Table(Theme t) : Forms.ProfessionalColorTable
    {
        Color Body => t.Gdi(t.Body);
        Color Ink => t.Gdi(t.Ink);

        public override Color ToolStripDropDownBackground => Body;
        public override Color ImageMarginGradientBegin => Body;
        public override Color ImageMarginGradientMiddle => Body;
        public override Color ImageMarginGradientEnd => Body;
        public override Color MenuBorder => Ink;
        public override Color MenuItemBorder => Ink;
        public override Color MenuItemSelected => Ink;
        public override Color MenuItemSelectedGradientBegin => Ink;
        public override Color MenuItemSelectedGradientEnd => Ink;
        public override Color MenuItemPressedGradientBegin => Ink;
        public override Color MenuItemPressedGradientEnd => Ink;
        public override Color CheckBackground => Body;
        public override Color CheckSelectedBackground => Ink;
        public override Color CheckPressedBackground => Ink;
        public override Color SeparatorDark => t.Gdi(t.Rule);
        public override Color SeparatorLight => Body;
    }
}

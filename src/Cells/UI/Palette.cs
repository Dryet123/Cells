using System.Windows.Media;

namespace Cells.UI;

// Teenage Engineering inspired: flat light-grey body, black hairlines, one bright colour per channel.
public sealed record Theme(string Name, Color Body, Color Hover, Color Ink, Color Dim, Color Faint, Color Rule, Color TickOff, Color Low)
{
    public static readonly Theme Light = new("light",
        Body: C(0xE8E8E3), Hover: C(0xDCDCD6), Ink: C(0x141414), Dim: C(0x7A7A74), Faint: C(0xB3B3AC),
        Rule: C(0xD0D0C9), TickOff: C(0xCACAC3), Low: C(0xE5341D));

    public static readonly Theme Dark = new("dark",
        Body: C(0x161616), Hover: C(0x222221), Ink: C(0xEDEDE8), Dim: C(0x8A8A84), Faint: C(0x4C4C49),
        Rule: C(0x2A2A28), TickOff: C(0x2E2E2C), Low: C(0xFF4F33));

    public static Theme Named(string name) => name == "dark" ? Dark : Light;

    // OP-1 style channel colours; the fourth channel uses the ink colour like the white knob.
    public Color Channel(int index) => (index % 6) switch
    {
        0 => C(0xFF5A1F),
        1 => C(0x2563EB),
        2 => C(0x10A765),
        3 => Ink,
        4 => C(0xE9B10B),
        _ => C(0x8E8E88),
    };

    public static Color C(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public System.Drawing.Color Gdi(Color c) => System.Drawing.Color.FromArgb(c.R, c.G, c.B);
}

static class Paint
{
    static readonly Dictionary<Color, SolidColorBrush> cache = [];

    public static SolidColorBrush Of(Color c)
    {
        if (cache.TryGetValue(c, out var b)) return b;
        b = new SolidColorBrush(c);
        b.Freeze();
        return cache[c] = b;
    }
}

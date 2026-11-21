using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Cells.UI;

// Tray icon showing a level: the app icon's dark square with pixel digits and a thin level bar under them.
public static class TrayGlyph
{
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

    static readonly Color Back = Color.FromArgb(20, 20, 20);
    static readonly Color Digit = Color.FromArgb(232, 232, 227);
    static readonly Color Off = Color.FromArgb(120, 120, 114);
    static readonly Color Track = Color.FromArgb(58, 58, 55);
    static readonly Color Level = Color.FromArgb(255, 90, 31);
    static readonly Color Charge = Color.FromArgb(16, 167, 101);
    static readonly Color Low = Color.FromArgb(255, 79, 51);

    // 3x5 display font; the 1 is a single column so "100" still fits a 16 px icon.
    static readonly Dictionary<char, string[]> Font = new()
    {
        ['0'] = ["111", "101", "101", "101", "111"],
        ['1'] = ["1", "1", "1", "1", "1"],
        ['2'] = ["111", "001", "111", "100", "111"],
        ['3'] = ["111", "001", "111", "001", "111"],
        ['4'] = ["101", "101", "111", "001", "001"],
        ['5'] = ["111", "100", "111", "001", "111"],
        ['6'] = ["111", "100", "111", "101", "111"],
        ['7'] = ["111", "001", "001", "001", "001"],
        ['8'] = ["111", "101", "111", "101", "111"],
        ['9'] = ["111", "101", "111", "001", "111"],
        ['-'] = ["000", "000", "111", "000", "000"],
    };

    public static int IconSize => Math.Max(16, GetSystemMetrics(49));

    public static Bitmap Draw(int size, int? level, bool low, bool charging)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Back);

        int cell = Math.Max(1, size / 8);
        var glyphs = (level?.ToString() ?? "--").Select(ch => Font[ch]).ToList();
        int gap = glyphs.Count >= 3 ? 1 : cell;
        int width = glyphs.Sum(x => x[0].Length * cell) + gap * (glyphs.Count - 1);
        int barGap = Math.Max(1, cell / 2), bar = cell;
        int top = (size - (5 * cell + barGap + bar)) / 2;
        int x = (size - width) / 2;

        using var ink = new SolidBrush(level == null ? Off : low ? Low : Digit);
        foreach (var glyph in glyphs)
        {
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < glyph[row].Length; col++)
                    if (glyph[row][col] == '1') g.FillRectangle(ink, x + col * cell, top + row * cell, cell, cell);
            x += glyph[0].Length * cell + gap;
        }

        int margin = Math.Max(1, cell / 2), barWidth = size - 2 * margin, barTop = top + 5 * cell + barGap;
        using var track = new SolidBrush(Track);
        g.FillRectangle(track, margin, barTop, barWidth, bar);
        if (level is { } value && value > 0)
        {
            using var fill = new SolidBrush(charging ? Charge : low ? Low : Level);
            g.FillRectangle(fill, margin, barTop, Math.Max(1, (int)Math.Round(barWidth * value / 100.0)), bar);
        }
        return bitmap;
    }
}

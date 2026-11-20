using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cells.Core;

namespace Cells.UI;

public sealed record Row(Device Device, string Name, int Channel);

// The device list shared by the desktop widget and the tray card, drawn by hand so it stays crisp and flat.
public sealed class CellsView : FrameworkElement
{
    public const double ViewWidth = 300;
    const double Pad = 14, HeaderHeight = 36, RowHeight = 56, FooterHeight = 30, EmptyHeight = 66, TextLeft = Pad + 36;
    const int Ticks = 40;

    static readonly Typeface Regular = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface Bold = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    IReadOnlyList<Row> rows = [];
    Theme theme = Theme.Light;
    string? footer, stamp;
    (string Title, string Hint) empty = ("NO DEVICES YET", "PLUG IN A RECEIVER OR PAIR OVER BLUETOOTH");
    Func<Device, bool>? picked;
    int lowLevel = 20;
    int hover = -1;
    bool blinkOn;
    readonly DispatcherTimer blink = new() { Interval = TimeSpan.FromMilliseconds(650) };

    public bool Interactive { get; set; }
    public event Action<Row>? RowClicked;
    public event Action<Row?>? RowMenu;

    public CellsView()
    {
        blink.Tick += (_, _) =>
        {
            blinkOn = !blinkOn;
            InvalidateVisual();
        };
        IsVisibleChanged += (_, _) => UpdateBlink();
    }

    // With "pick" set, each row shows a square that is filled when the row is selected, and unselected rows are dimmed.
    public void Show(IReadOnlyList<Row> list, Theme t, int low, string? footerText, string? header,
        Func<Device, bool>? pick = null, (string, string)? emptyText = null)
    {
        rows = list;
        theme = t;
        picked = pick;
        empty = emptyText ?? ("NO DEVICES YET", "PLUG IN A RECEIVER OR PAIR OVER BLUETOOTH");
        lowLevel = low;
        footer = footerText;
        stamp = header;
        if (hover >= rows.Count) hover = -1;
        UpdateBlink();
        InvalidateMeasure();
        InvalidateVisual();
    }

    void UpdateBlink()
    {
        bool need = IsVisible && rows.Any(r => r.Device.Online && r.Device.Charging && r.Device.Level < 100);
        if (need && !blink.IsEnabled) blink.Start();
        if (!need) blink.Stop();
    }

    double BodyHeight => rows.Count == 0 ? EmptyHeight : rows.Count * RowHeight;

    protected override Size MeasureOverride(Size available) =>
        new(ViewWidth, HeaderHeight + BodyHeight + (footer != null ? FooterHeight : 0));

    public Row? RowAt(Point p)
    {
        if (p.Y < HeaderHeight || rows.Count == 0) return null;
        int i = (int)((p.Y - HeaderHeight) / RowHeight);
        return i >= 0 && i < rows.Count ? rows[i] : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!Interactive) return;
        int i = RowAt(e.GetPosition(this)) is { } r ? rows.ToList().IndexOf(r) : -1;
        if (i == hover) return;
        hover = i;
        Cursor = i >= 0 ? Cursors.Hand : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (hover < 0) return;
        hover = -1;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (Interactive && RowAt(e.GetPosition(this)) is { } row) RowClicked?.Invoke(row);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        RowMenu?.Invoke(RowAt(e.GetPosition(this)));
        e.Handled = true;
    }

    double scale = 1;

    // Snaps to whole device pixels so hairlines and ticks don't blur.
    double Snap(double v) => Math.Round(v * scale) / scale;

    Rect SnapRect(double x, double y, double w, double h)
    {
        double x0 = Snap(x), y0 = Snap(y);
        return new Rect(x0, y0, Math.Max(1 / scale, Snap(x + w) - x0), Math.Max(1 / scale, Snap(y + h) - y0));
    }

    protected override void OnRender(DrawingContext dc)
    {
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double width = ViewWidth, height = RenderSize.Height;
        dc.DrawRectangle(Paint.Of(theme.Body), null, new Rect(0, 0, width, height));

        Text(dc, "CELLS", Pad, 23, 10.5, Bold, theme.Ink, 2.4);
        if (stamp != null) Text(dc, stamp, width - Pad, 23, 9, Regular, theme.Dim, 1.2, right: true);
        dc.DrawRectangle(Paint.Of(theme.Ink), null, SnapRect(0, HeaderHeight - 1, width, 1));

        if (rows.Count == 0)
        {
            Text(dc, empty.Title, Pad, HeaderHeight + 27, 10, Bold, theme.Ink, 1);
            Text(dc, empty.Hint, Pad, HeaderHeight + 44, 8.5, Regular, theme.Dim, 0.8);
        }
        for (int i = 0; i < rows.Count; i++) DrawRow(dc, rows[i], i, HeaderHeight + i * RowHeight, width);

        if (footer != null)
        {
            double top = HeaderHeight + BodyHeight;
            dc.DrawRectangle(Paint.Of(theme.Rule), null, SnapRect(0, top, width, 1));
            Text(dc, footer, Pad, top + 19, 8.5, Regular, theme.Dim, 0.9);
        }
    }

    void DrawRow(DrawingContext dc, Row row, int index, double top, double width)
    {
        var d = row.Device;
        if (index == hover) dc.DrawRectangle(Paint.Of(theme.Hover), null, new Rect(0, top, width, RowHeight));
        if (index > 0) dc.DrawRectangle(Paint.Of(theme.Rule), null, SnapRect(Pad, top, width - 2 * Pad, 1));

        bool live = d.Online && d.HasLevel;
        bool low = live && !d.Charging && d.Level <= lowLevel;
        var channel = theme.Channel(row.Channel);
        var accent = !live ? theme.Faint : low ? theme.Low : channel;

        if (picked != null)
        {
            bool on = picked(d);
            var box = SnapRect(Pad + 21, top + 13.5, 8, 8);
            if (on) dc.DrawRectangle(Paint.Of(channel), null, box);
            else dc.DrawRectangle(null, new Pen(Paint.Of(theme.Dim), 1 / scale), new Rect(box.X + 0.5 / scale, box.Y + 0.5 / scale, box.Width - 1 / scale, box.Height - 1 / scale));
            if (!on) dc.PushOpacity(0.4);
            DrawRowBody(dc, row, top, width, live, low, channel, accent);
            if (!on) dc.Pop();
            return;
        }
        dc.DrawEllipse(Paint.Of(d.Online ? channel : theme.Faint), null, new Point(Pad + 25, top + 17.5), 3.5, 3.5);
        DrawRowBody(dc, row, top, width, live, low, channel, accent);
    }

    void DrawRowBody(DrawingContext dc, Row row, double top, double width, bool live, bool low, Color channel, Color accent)
    {
        var d = row.Device;
        Text(dc, (row.Channel + 1).ToString("00"), Pad, top + 21, 9.5, Regular, theme.Dim);

        double numberRight = width - Pad - 12;
        string number = d.HasLevel ? d.Level.ToString(CultureInfo.InvariantCulture) : "--";
        double numberWidth = Text(dc, number, numberRight, top + 36, 27, Regular, live ? (low ? theme.Low : theme.Ink) : theme.Faint, -0.5, right: true);
        if (d.HasLevel) Text(dc, "%", numberRight + 2, top + 21, 9, Bold, live ? theme.Dim : theme.Faint);

        double nameRoom = numberRight - numberWidth - 10 - TextLeft;
        Text(dc, Fit(row.Name.ToUpperInvariant(), 10.5, Bold, 1, nameRoom), TextLeft, top + 21, 10.5, Bold, d.Online ? theme.Ink : theme.Dim, 1);
        Text(dc, Fit(Status(row), 8.5, Regular, 0.8, nameRoom), TextLeft, top + 35, 8.5, Regular, low ? theme.Low : theme.Dim, 0.8);

        // Every tick gets the same whole-pixel width; only the gaps absorb rounding.
        double leftPx = Math.Round(TextLeft * scale), barPx = Math.Round((width - Pad - TextLeft) * scale);
        double tickPx = Math.Max(1, Math.Round(barPx / Ticks * 0.42));
        double y = Snap(top + 43), h = Snap(6);
        int filled = d.HasLevel ? (int)Math.Round(d.Level / 100.0 * Ticks) : 0;
        for (int t = 0; t < Ticks; t++)
        {
            var color = t < filled ? accent : theme.TickOff;
            if (t == filled && live && d.Charging && blinkOn) color = channel;
            double x = Math.Round(leftPx + t * (barPx - tickPx) / (Ticks - 1));
            dc.DrawRectangle(Paint.Of(color), null, new Rect(x / scale, y, tickPx / scale, h));
        }
    }

    string Status(Row row)
    {
        var d = row.Device;
        var parts = new List<string> { KindLabel(d.Kind) };
        if (!d.Online) parts.Add(d.HasLevel ? "LAST " + Ago(d.LastSeen) : "OFF");
        else if (d.Charging) parts.Add(d.Level >= 100 ? "FULL" : "CHARGING");
        else if (d.Level <= lowLevel) parts.Add("LOW");
        if (d.Online && !string.IsNullOrEmpty(d.Detail)) parts.Add(d.Detail.ToUpperInvariant());
        return string.Join("   ", parts);
    }

    static string Ago(DateTime when)
    {
        var span = DateTime.Now - when;
        if (span < TimeSpan.FromMinutes(1)) return "NOW";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}M AGO";
        if (when.Date == DateTime.Today) return when.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (span < TimeSpan.FromDays(7)) return when.ToString("ddd HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant();
        return when.ToString("d MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
    }

    public static string KindLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "MOUSE",
        DeviceKind.Keyboard => "KEYBOARD",
        DeviceKind.Headset => "HEADSET",
        DeviceKind.Earbuds => "EARBUDS",
        DeviceKind.Gamepad => "GAMEPAD",
        DeviceKind.Computer => "THIS PC",
        DeviceKind.Phone => "PHONE",
        DeviceKind.Speaker => "SPEAKER",
        DeviceKind.Pen => "PEN",
        _ => "DEVICE",
    };

    FormattedText Format(string s, double size, Typeface face, Color color) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Paint.Of(color), scale) { };

    double Measure(string s, double size, Typeface face, double spacing)
    {
        if (spacing == 0 || s.Length == 0) return Format(s, size, face, Colors.Black).WidthIncludingTrailingWhitespace;
        double w = 0;
        foreach (char c in s) w += Format(c.ToString(), size, face, Colors.Black).WidthIncludingTrailingWhitespace + spacing;
        return w - spacing;
    }

    string Fit(string s, double size, Typeface face, double spacing, double room)
    {
        if (room <= 0 || Measure(s, size, face, spacing) <= room) return s;
        while (s.Length > 1 && Measure(s + "..", size, face, spacing) > room) s = s[..^1].TrimEnd();
        return s + "..";
    }

    // Draws text with its baseline at y; letter spacing is added by hand since FormattedText has none.
    double Text(DrawingContext dc, string s, double x, double baseline, double size, Typeface face, Color color, double spacing = 0, bool right = false)
    {
        double width = Measure(s, size, face, spacing);
        if (right) x -= width;
        if (spacing == 0)
        {
            var ft = Format(s, size, face, color);
            dc.DrawText(ft, new Point(x, baseline - ft.Baseline));
            return width;
        }
        foreach (char c in s)
        {
            var ft = Format(c.ToString(), size, face, color);
            dc.DrawText(ft, new Point(x, baseline - ft.Baseline));
            x += ft.WidthIncludingTrailingWhitespace + spacing;
        }
        return width;
    }
}

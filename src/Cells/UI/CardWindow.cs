using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Cells.UI;

// The card next to the tray icon. Hovering the icon shows it passively; clicking pins it,
// and then rows can be clicked to choose which devices the hover card lists.
public sealed class CardWindow : Window
{
    readonly Border frame;
    IntPtr hwnd;

    public CellsView View { get; } = new();
    public bool Pinned { get; private set; }

    public CardWindow()
    {
        Title = "Cells";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        frame = new Border { BorderThickness = new Thickness(1), Child = View };
        Content = frame;
        SourceInitialized += (_, _) => Win32.AddExStyle(new WindowInteropHelper(this).Handle, Win32.WS_EX_TOOLWINDOW);
        Deactivated += (_, _) =>
        {
            if (Pinned) Hide();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    public void ApplyTheme(Theme t) => frame.BorderBrush = Paint.Of(t.Ink);

    public void SetMode(bool pin)
    {
        Pinned = pin;
        View.Interactive = pin;
    }

    public void Open(Win32.RECT icon)
    {
        bool pin = Pinned;
        hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (pin) Win32.AddExStyle(hwnd, 0, Win32.WS_EX_NOACTIVATE);
        else Win32.AddExStyle(hwnd, Win32.WS_EX_NOACTIVATE);

        int cx = (icon.Left + icon.Right) / 2, cy = (icon.Top + icon.Bottom) / 2;
        var (work, scale) = Win32.MonitorAt(cx, cy);
        View.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        int w = (int)Math.Ceiling((View.DesiredSize.Width + 2) * scale), h = (int)Math.Ceiling((View.DesiredSize.Height + 2) * scale);
        int gap = (int)(10 * scale);

        int x = cx - w / 2, y;
        if (icon.Top >= work.Bottom - 4) y = work.Bottom - h - gap;
        else if (icon.Bottom <= work.Top + 4) y = work.Top + gap;
        else if (icon.Left >= work.Right - 4)
        {
            x = work.Right - w - gap;
            y = cy - h / 2;
        }
        else if (icon.Right <= work.Left + 4)
        {
            x = work.Left + gap;
            y = cy - h / 2;
        }
        else y = icon.Top - h - gap;
        x = Math.Clamp(x, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
        y = Math.Clamp(y, work.Top + gap, Math.Max(work.Top + gap, work.Bottom - h - gap));

        Win32.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        Show();
        if (pin)
        {
            Win32.SetForegroundWindow(hwnd);
            Activate();
        }
    }
}

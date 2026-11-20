using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Cells.UI;

// Desktop widget: sits under every window, never takes focus and stays out of Alt+Tab.
// "Show desktop" raises the desktop above normal windows, so while the desktop is in the
// foreground the widget lifts itself above it and drops back once another app is focused.
public sealed class WidgetWindow : Window
{
    readonly Border frame;
    readonly ScaleTransform zoom = new(1, 1);
    readonly Win32.WinEventProc hookProc;
    IntPtr hwnd, hook;

    public CellsView View { get; } = new();
    public bool Locked { get; set; }
    public event Action? Moved;

    public WidgetWindow()
    {
        Title = "Cells widget";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        frame = new Border { BorderThickness = new Thickness(1), Child = View, LayoutTransform = zoom };
        Content = frame;
        hookProc = OnForeground;

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, Win32.WS_EX_APPWINDOW);
            hook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, hookProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        };
        Loaded += (_, _) => SendToBack();
        Closed += (_, _) =>
        {
            if (hook != IntPtr.Zero) Win32.UnhookWinEvent(hook);
        };
    }

    public void ApplyTheme(Theme t, double scale)
    {
        frame.BorderBrush = Paint.Of(t.Ink);
        zoom.ScaleX = zoom.ScaleY = scale;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        double left = Left, top = Top;
        if (!Locked)
        {
            DragMove();
            SendToBack();
        }
        if (Math.Abs(Left - left) > 1 || Math.Abs(Top - top) > 1) Moved?.Invoke();
    }

    public void SendToBack()
    {
        if (hwnd != IntPtr.Zero)
            Win32.SetWindowPos(hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    void OnForeground(IntPtr hook, uint ev, IntPtr fg, int idObject, int idChild, uint thread, uint time)
    {
        if (!IsVisible || fg == hwnd) return;
        if (Win32.ClassName(fg) is "WorkerW" or "Progman")
            Win32.SetWindowPos(hwnd, Win32.HWND_TOP, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        else SendToBack();
    }
}

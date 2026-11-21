using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Cells.UI;

// Notification area icon on Shell_NotifyIcon version 4. Without NIF_SHOWTIP the shell sends
// NIN_POPUPOPEN / NIN_POPUPCLOSE instead of drawing its own tooltip, which is what lets us show a custom card.
// The hidden window also receives the broadcasts the app cares about (device changes, resume).
public sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = 0x8001, IconId = 1;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    const int NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, NIN_POPUPOPEN = 0x406, NIN_POPUPCLOSE = 0x407, WM_CONTEXTMENU = 0x7B;
    const int WM_POWERBROADCAST = 0x218, WM_DEVICECHANGE = 0x219, PBT_APMRESUMEAUTOMATIC = 0x12, DBT_DEVNODES_CHANGED = 0x7;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public uint Flags;
        public int CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Identifier
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public Guid Item;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref Identifier id, out Win32.RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);

    readonly HwndSource window;
    readonly int taskbarCreated;
    IntPtr icon;
    bool ownsIcon;
    string tip = "Cells";
    bool added;

    public event Action? HoverStarted, HoverEnded, Clicked, MenuRequested, DevicesChanged, Resumed;

    public IntPtr Handle => window.Handle;

    public TrayIcon(IntPtr icon)
    {
        this.icon = icon;
        window = new HwndSource(new HwndSourceParameters("Cells tray") { Width = 0, Height = 0, WindowStyle = 0, ExtendedWindowStyle = (int)Win32.WS_EX_TOOLWINDOW });
        window.AddHook(WndProc);
        taskbarCreated = (int)RegisterWindowMessageW("TaskbarCreated");
    }

    NotifyIconData Data(uint flags) => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Window = Handle,
        Id = IconId,
        Flags = flags,
        CallbackMessage = CallbackMessage,
        Icon = icon,
        Tip = tip,
        Info = "",
        InfoTitle = "",
    };

    public void Update(string text)
    {
        tip = text.Length > 127 ? text[..127] : text;
        if (!added) Add();
        else
        {
            var data = Data(NIF_ICON | NIF_TIP | NIF_MESSAGE);
            if (!Shell_NotifyIconW(NIM_MODIFY, ref data)) Add();
        }
    }

    // An owned handle is destroyed when it gets replaced; the app logo handle is not owned.
    public void SetIcon(IntPtr handle, bool owned)
    {
        var old = icon;
        bool oldOwned = ownsIcon;
        icon = handle;
        ownsIcon = owned;
        if (added)
        {
            var data = Data(NIF_ICON);
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        if (oldOwned && old != handle) Win32.DestroyIcon(old);
    }

    void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        added = Shell_NotifyIconW(NIM_ADD, ref data);
        data.Version = 4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    public void Notify(string title, string text)
    {
        var data = Data(NIF_INFO);
        data.InfoTitle = title;
        data.Info = text;
        data.InfoFlags = 0x80;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    // Physical pixels; also valid when the icon sits in the overflow flyout.
    public Win32.RECT? Bounds()
    {
        var id = new Identifier { Size = Marshal.SizeOf<Identifier>(), Window = Handle, Id = IconId };
        return Shell_NotifyIconGetRect(ref id, out var rect) == 0 ? rect : null;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case NIN_POPUPOPEN: HoverStarted?.Invoke(); break;
                case NIN_POPUPCLOSE: HoverEnded?.Invoke(); break;
                case NIN_SELECT or NIN_KEYSELECT: Clicked?.Invoke(); break;
                case WM_CONTEXTMENU: MenuRequested?.Invoke(); break;
            }
        }
        else if (msg == taskbarCreated)
        {
            added = false;
            Add();
        }
        else if (msg == WM_DEVICECHANGE && wParam == DBT_DEVNODES_CHANGED) DevicesChanged?.Invoke();
        else if (msg == WM_POWERBROADCAST && wParam == PBT_APMRESUMEAUTOMATIC) Resumed?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        var data = Data(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        if (ownsIcon) Win32.DestroyIcon(icon);
        window.Dispose();
    }
}

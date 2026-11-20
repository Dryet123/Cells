using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Cells.Core;
using Cells.UI;
using Forms = System.Windows.Forms;

namespace Cells;

public partial class App : Application
{
    Settings settings = null!;
    DeviceMonitor monitor = null!;
    TrayIcon tray = null!;
    System.Drawing.Icon trayIcon = null!;
    WidgetWindow widget = null!;
    CardWindow card = null!;
    Mutex? instance;
    EventWaitHandle? showSignal, exitSignal;
    List<Device> devices = [];
    readonly DispatcherTimer refreshSoon = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly HashSet<string> lowNotified = [], fullNotified = [];
    bool exiting;

    Theme Theme => Theme.Named(settings.Theme);
    int LowLevel => settings.LowAlert > 0 ? settings.LowAlert : 20;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) =>
        {
            Log.Error(a.Exception);
            a.Handled = true;
        };
        try { Start(e); }
        catch (Exception ex)
        {
            // Without this a failed start leaves a process with no icon and no windows.
            Log.Error(ex);
            Shutdown();
        }
    }

    void Start(StartupEventArgs e)
    {
        bool Has(string arg) => e.Args.Contains(arg, StringComparer.OrdinalIgnoreCase);

        if (Has("--scan"))
        {
            Scan.WriteReport();
            Shutdown();
            return;
        }
        if (Has("--uninstall"))
        {
            Setup.Uninstall();
            Shutdown();
            return;
        }
#if !DEBUG
        // A downloaded exe offers to install itself; test copies and "--portable" skip that.
        if (!Installer.IsInstalledCopy && !AppPaths.IsCustom && !Has("--portable") && !Has("--exit")
            && Setup.OfferInstall() == Setup.Outcome.Exit)
        {
            Shutdown();
            return;
        }
#endif
        // "--exit" asks a running copy to quit (used by publish.ps1 and the installer).
        bool exitRequest = Has("--exit");
        instance = new Mutex(true, AppPaths.SyncName("Instance"), out bool first);
        if (!first || exitRequest)
        {
            AppPaths.Signal(exitRequest ? "Exit" : "Show");
            Shutdown();
            return;
        }

        settings = Settings.Load();
        SetupAutostart();

        trayIcon = LoadTrayIcon();
        tray = new TrayIcon(trayIcon.Handle);
        card = new CardWindow();
        widget = new WidgetWindow();
        tray.HoverStarted += () =>
        {
            if (card.IsVisible && card.Pinned) return;
            OpenCard(pin: false);
        };
        tray.HoverEnded += () =>
        {
            if (!card.Pinned) card.Hide();
        };
        tray.Clicked += ToggleCard;
        tray.MenuRequested += () => ShowMenu(null);
        tray.DevicesChanged += RefreshSoon;
        tray.Resumed += RefreshSoon;
        card.View.RowClicked += row => ToggleOnHover(row.Device.Key);
        card.View.RowMenu += ShowMenu;
        widget.View.RowMenu += ShowMenu;
        widget.Moved += () =>
        {
            settings.WidgetLeft = widget.Left;
            settings.WidgetTop = widget.Top;
            settings.Save();
        };
        refreshSoon.Tick += (_, _) =>
        {
            refreshSoon.Stop();
            monitor.Refresh();
        };
        clock.Tick += (_, _) => Render();
        clock.Start();

        monitor = new DeviceMonitor { Interval = TimeSpan.FromSeconds(settings.PollSeconds) };
        monitor.Updated += list => Dispatcher.BeginInvoke(() => Apply(list));
        devices = monitor.Snapshot();
        Render();
        PlaceWidget();
        monitor.Start();

        showSignal = Listen("Show", () =>
        {
            SetWidget(true);
            OpenCard(pin: true);
        });
        exitSignal = Listen("Exit", ExitApp);

        if (!settings.WelcomeShown)
        {
            settings.WelcomeShown = true;
            settings.Save();
            tray.Notify("Cells is running", "Hover the icon in the tray to see your devices. Click it to choose which ones, right-click for options.");
        }
    }

    EventWaitHandle Listen(string kind, Action action)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, AppPaths.SyncName(kind));
        ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, false);
        return ev;
    }

    // Only the installed copy registers itself; a portable run never touches autostart.
    void SetupAutostart()
    {
#if !DEBUG
        if (!Installer.IsInstalledCopy || AppPaths.IsCustom) return;
        try
        {
            Installer.EnsureRegistered();
            if (!settings.AutostartRegistered)
            {
                Autostart.SetEnabled(true);
                settings.AutostartRegistered = true;
                settings.Save();
            }
            else Autostart.SyncPath();
        }
        catch (Exception ex) { Log.Error(ex); }
#endif
    }

    void RefreshSoon()
    {
        refreshSoon.Stop();
        refreshSoon.Start();
    }

    void Apply(List<Device> list)
    {
        devices = list;
        Alerts();
        Render();
    }

    List<Row> Rows()
    {
        var ordered = devices.OrderBy(d => d.FirstSeen).ToList();
        return ordered.Select((d, i) => new Row(d, settings.NameOf(d), i))
            .Where(r => !settings.Hidden.Contains(r.Device.Key))
            .ToList();
    }

    bool OnHover(Device d) => !settings.CardHidden.Contains(d.Key);

    void Render()
    {
        if (exiting) return;
        var rows = Rows();
        tray.Update("Cells\n" + string.Join("\n", rows.Where(r => OnHover(r.Device))
            .Select(r => $"{r.Name} {(r.Device.HasLevel ? r.Device.Level + "%" : "off")}")));

        string? stamp = monitor?.LastPoll is { } at && at != default ? at.ToString("HH:mm") : null;
        card.ApplyTheme(Theme);
        if (card.Pinned)
            card.View.Show(rows, Theme, LowLevel, "CLICK A DEVICE TO SHOW OR HIDE IT ON HOVER", stamp, OnHover);
        else
            card.View.Show(rows.Where(r => OnHover(r.Device)).ToList(), Theme, LowLevel, null, stamp,
                emptyText: rows.Count > 0 ? ("NOTHING PICKED", "CLICK THE ICON TO CHOOSE DEVICES") : null);
        widget.ApplyTheme(Theme, settings.WidgetScale);
        widget.Locked = settings.WidgetLocked;
        widget.View.Show(rows, Theme, LowLevel, null, stamp);
    }

    static System.Drawing.Icon LoadTrayIcon()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    void Alerts()
    {
        foreach (var d in devices.Where(d => d.Online && d.HasLevel && !settings.Hidden.Contains(d.Key)))
        {
            var name = settings.NameOf(d);
            if (settings.LowAlert > 0 && !d.Charging && d.Level <= settings.LowAlert)
            {
                if (lowNotified.Add(d.Key)) tray.Notify($"{name} is low", $"{d.Level}% left. Time to charge it.");
            }
            else if (d.Charging || d.Level > settings.LowAlert + 5) lowNotified.Remove(d.Key);

            if (settings.FullAlert && d.Charging && d.Level >= 100)
            {
                if (fullNotified.Add(d.Key)) tray.Notify($"{name} is charged", "It can be unplugged.");
            }
            else if (!d.Charging || d.Level < 95) fullNotified.Remove(d.Key);
        }
    }

    void PlaceWidget()
    {
        var area = SystemParameters.WorkArea;
        double width = (CellsView.ViewWidth + 2) * settings.WidgetScale;
        double left = settings.WidgetLeft ?? area.Right - width - 24, top = settings.WidgetTop ?? area.Top + 24;
        // A saved spot on a monitor that is gone falls back to the default corner.
        if (left < SystemParameters.VirtualScreenLeft - 50 || top < SystemParameters.VirtualScreenTop - 50
            || left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
            || top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            left = area.Right - width - 24;
            top = area.Top + 24;
        }
        widget.Left = left;
        widget.Top = top;
        if (settings.ShowWidget) widget.Show();
    }

    void SetWidget(bool visible)
    {
        settings.ShowWidget = visible;
        settings.Save();
        if (visible)
        {
            widget.Show();
            widget.SendToBack();
        }
        else widget.Hide();
    }

    void ToggleCard()
    {
        if (card.IsVisible && card.Pinned) card.Hide();
        else OpenCard(pin: true);
    }

    void OpenCard(bool pin)
    {
        if (tray.Bounds() is not { } r) return;
        card.Hide();
        card.SetMode(pin);
        Render();
        card.Open(r);
    }

    void ToggleOnHover(string key)
    {
        if (!settings.CardHidden.Remove(key)) settings.CardHidden.Add(key);
        settings.Save();
        Render();
    }

    void Change(Action edit)
    {
        edit();
        settings.Save();
        Render();
    }

    void ShowMenu(Row? row)
    {
        if (card.Pinned) card.Hide();
        var t = Theme;
        var menu = Menus.Create(t);
        if (row != null)
        {
            var d = row.Device;
            menu.Items.Add(Menus.Label(row.Name.ToUpperInvariant()));
            menu.Items.Add(Menus.Item("Show on hover", () => ToggleOnHover(d.Key), check: OnHover(d)));
            menu.Items.Add(Menus.Item("Rename...", () => Rename(row)));
            menu.Items.Add(Menus.Item("Hide", () => Change(() => settings.Hidden.Add(d.Key))));
            if (!d.Online) menu.Items.Add(Menus.Item("Forget", () => monitor.Forget(d.Key)));
            menu.Items.Add(new Forms.ToolStripSeparator());
        }

        menu.Items.Add(Menus.Item("Show widget", () => SetWidget(!settings.ShowWidget), check: settings.ShowWidget));
        menu.Items.Add(Menus.Item("Lock widget position", () => Change(() => settings.WidgetLocked = !settings.WidgetLocked), check: settings.WidgetLocked));
        menu.Items.Add(Menus.Sub("Widget size", new[] { ("Small", 0.85), ("Medium", 1.0), ("Large", 1.25) }
            .Select(s => Menus.Item(s.Item1, () => Change(() => settings.WidgetScale = s.Item2), check: Math.Abs(settings.WidgetScale - s.Item2) < 0.01))));
        menu.Items.Add(Menus.Sub("Theme", new[] { ("Light", "light"), ("Dark", "dark") }
            .Select(s => Menus.Item(s.Item1, () => Change(() => settings.Theme = s.Item2), check: settings.Theme == s.Item2))));

        var rows = Rows();
        if (rows.Count > 0)
            menu.Items.Add(Menus.Sub("Show on hover", rows.Select(r => Menus.Item(r.Name, () => ToggleOnHover(r.Device.Key), check: OnHover(r.Device)))));

        var hidden = devices.Where(d => settings.Hidden.Contains(d.Key)).ToList();
        if (hidden.Count > 0)
            menu.Items.Add(Menus.Sub("Hidden devices", hidden.Select(d => Menus.Item(settings.NameOf(d), () => Change(() => settings.Hidden.Remove(d.Key))))));

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Menus.Sub("Low battery alert", new[] { 0, 10, 15, 20, 30 }
            .Select(v => Menus.Item(v == 0 ? "Off" : $"At {v}%", () => Change(() => settings.LowAlert = v), check: settings.LowAlert == v))));
        menu.Items.Add(Menus.Item("Notify when charged", () => Change(() => settings.FullAlert = !settings.FullAlert), check: settings.FullAlert));
        menu.Items.Add(Menus.Sub("Check every", new[] { ("30 seconds", 30), ("Minute", 60), ("5 minutes", 300) }
            .Select(s => Menus.Item(s.Item1, () => SetInterval(s.Item2), check: settings.PollSeconds == s.Item2))));
        menu.Items.Add(Menus.Item("Refresh now", monitor.Refresh));
        menu.Items.Add(Menus.Item("Save device report", SaveReport));
        if (Installer.IsInstalledCopy && !AppPaths.IsCustom)
            menu.Items.Add(Menus.Item("Start with Windows", () => Autostart.SetEnabled(!Autostart.IsEnabled), check: Autostart.IsEnabled));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Menus.Item("Exit", ExitApp));

        menu.Closed += (_, _) => Dispatcher.BeginInvoke(menu.Dispose);
        Win32.SetForegroundWindow(tray.Handle);
        Win32.GetCursorPos(out var p);
        menu.Show(new System.Drawing.Point(p.X, p.Y));
    }

    void SetInterval(int seconds)
    {
        settings.PollSeconds = seconds;
        settings.Save();
        monitor.Interval = TimeSpan.FromSeconds(seconds);
        monitor.Refresh();
    }

    void Rename(Row row)
    {
        var name = Dialog.Input(Theme, "Rename device", $"Detected as \"{row.Device.Name}\". Leave empty to use that name again.", row.Name);
        if (name == null) return;
        Change(() =>
        {
            if (name.Length == 0 || name == row.Device.Name) settings.Names.Remove(row.Device.Key);
            else settings.Names[row.Device.Key] = name;
        });
    }

    void SaveReport()
    {
        Task.Run(() =>
        {
            try
            {
                var path = monitor.WriteReport();
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error(ex); }
        });
    }

    public void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        clock.Stop();
        monitor.Dispose();
        settings.Save();
        tray.Dispose();
        trayIcon.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        showSignal?.Dispose();
        exitSignal?.Dispose();
        base.OnExit(e);
    }
}

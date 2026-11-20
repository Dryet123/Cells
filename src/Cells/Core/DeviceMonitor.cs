using System.Text.Json;
using Cells.Hid;
using Cells.Providers;

namespace Cells.Core;

// Polls every provider on a background thread and keeps the remembered device list.
public sealed class DeviceMonitor : IDisposable
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static IProvider[] CreateProviders() =>
    [
        new SystemBattery(),
        new PlayStation(),
        new Logitech(),
        new Razer(),
        new RazerHeadsets(),
        new SteelSeries(),
        new Corsair(),
        new HyperX(),
        new Glorious(),
        new Mchose(),
        new Compx(),
        new Bluetooth(),
        new AirPods(),
        new XboxPads(),
        new GenericHid(),
    ];

    readonly IProvider[] providers = CreateProviders();
    readonly List<Device> devices;
    readonly AutoResetEvent wake = new(false);
    readonly Thread thread;
    volatile bool stopping;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
    public DateTime LastPoll { get; private set; }

    // Raised on the polling thread with a copy of the list.
    public event Action<List<Device>>? Updated;

    public DeviceMonitor()
    {
        devices = Load();
        thread = new Thread(Loop) { IsBackground = true, Name = "Cells poll" };
        thread.SetApartmentState(ApartmentState.MTA);
    }

    public void Start() => thread.Start();

    public void Refresh() => wake.Set();

    public List<Device> Snapshot()
    {
        lock (devices) return devices.Select(Copy).ToList();
    }

    public void Forget(string key)
    {
        lock (devices) devices.RemoveAll(d => d.Key == key);
        Save();
        Updated?.Invoke(Snapshot());
    }

    // Two pollers talking to one receiver at once would mix up their replies.
    readonly object busy = new();

    public string WriteReport()
    {
        lock (busy) return Scan.WriteReport();
    }

    void Loop()
    {
        Thread.Sleep(300);
        while (!stopping)
        {
            try { Poll(); }
            catch (Exception ex) { Log.Error(ex); }
            wake.WaitOne(Interval);
        }
    }

    void Poll()
    {
        var ctx = new PollContext(HidBus.List());
        lock (busy)
        {
            foreach (var provider in providers)
            {
                if (stopping) return;
                try { provider.Poll(ctx); }
                catch (Exception ex) { Log.Error(ex); }
            }
        }

        var now = DateTime.Now;
        lock (devices)
        {
            var seen = new HashSet<string>();
            foreach (var r in ctx.Readings)
            {
                seen.Add(r.Key);
                var d = devices.FirstOrDefault(x => x.Key == r.Key);
                if (d == null)
                {
                    d = new Device { Key = r.Key, FirstSeen = now };
                    devices.Add(d);
                }
                d.Name = r.Name;
                d.Kind = r.Kind;
                d.Detail = r.Detail;
                d.Online = r.Reachable;
                if (!r.Reachable) continue;
                d.Level = r.Level;
                d.Charging = r.Charging;
                d.LastSeen = now;
            }
            foreach (var d in devices.Where(d => !seen.Contains(d.Key))) d.Online = false;
        }
        LastPoll = now;
        Save();
        Updated?.Invoke(Snapshot());
    }

    static Device Copy(Device d) => new()
    {
        Key = d.Key, Name = d.Name, Kind = d.Kind, Level = d.Level, Charging = d.Charging, Detail = d.Detail,
        FirstSeen = d.FirstSeen, LastSeen = d.LastSeen, Online = d.Online,
    };

    static List<Device> Load()
    {
        try
        {
            if (File.Exists(AppPaths.Devices))
                return JsonSerializer.Deserialize<List<Device>>(File.ReadAllText(AppPaths.Devices)) ?? [];
        }
        catch (Exception ex) { Log.Error(ex); }
        return [];
    }

    void Save()
    {
        try
        {
            string text;
            lock (devices) text = JsonSerializer.Serialize(devices, Json);
            var tmp = AppPaths.Devices + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, AppPaths.Devices, true);
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    public void Dispose()
    {
        stopping = true;
        wake.Set();
        thread.Join(3000);
    }
}

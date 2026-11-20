using Cells.Core;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace Cells.Providers;

// AirPods and Beats don't share their level with Windows, but broadcast it in Apple "proximity pairing"
// adverts. Anyone's buds can be heard, so this only listens while a pair is connected to this PC and
// takes the strongest nearby signal.
sealed class AirPods : IProvider
{
    static readonly string[] Names = ["airpods", "beats", "powerbeats"];
    const short MinRssi = -65;

    BluetoothLEAdvertisementWatcher? watcher;
    byte[]? last;
    short lastRssi;
    DateTime lastAt;
    readonly object gate = new();

    public string Name => "AirPods";

    public void Poll(PollContext ctx)
    {
        var pods = Bluetooth.ConnectedDevices.FirstOrDefault(d => Names.Any(n => d.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));
        if (pods.Key == null)
        {
            Stop();
            return;
        }
        Start();

        byte[]? data;
        lock (gate) data = DateTime.Now - lastAt < TimeSpan.FromMinutes(2) ? last : null;
        ctx.Note($"  {pods.Name} advert={PollContext.Hex(data, 27)}");
        if (data == null) return;

        bool flip = (data[5] & 0x20) == 0;
        int hi = data[6] >> 4, lo = data[6] & 0x0F, chargingBits = data[7] >> 4, box = data[7] & 0x0F;
        int left = flip ? hi : lo, right = flip ? lo : hi;
        bool leftCharging = (chargingBits & (flip ? 2 : 1)) != 0, rightCharging = (chargingBits & (flip ? 1 : 2)) != 0;

        static string Part(string label, int v) => v <= 10 ? $"{label} {v * 10}" : "";
        var buds = new[] { left, right }.Where(v => v <= 10).ToList();
        if (buds.Count == 0) return;
        var detail = string.Join("  ", new[] { Part("L", left), Part("R", right), Part("Case", box) }.Where(s => s.Length > 0));
        ctx.Replace(new Reading(pods.Key, pods.Name, DeviceKind.Earbuds, buds.Min() * 10, leftCharging || rightCharging, detail));
    }

    void Start()
    {
        if (watcher != null) return;
        try
        {
            watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            watcher.Received += OnReceived;
            watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            watcher = null;
        }
    }

    void Stop()
    {
        if (watcher == null) return;
        try { watcher.Stop(); }
        catch (Exception) { }
        watcher.Received -= OnReceived;
        watcher = null;
    }

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (args.RawSignalStrengthInDBm < MinRssi) return;
        foreach (var m in args.Advertisement.ManufacturerData)
        {
            if (m.CompanyId != 0x004C || m.Data.Length < 9) continue;
            var data = new byte[m.Data.Length];
            DataReader.FromBuffer(m.Data).ReadBytes(data);
            if (data[0] != 0x07) continue;
            lock (gate)
            {
                bool fresh = DateTime.Now - lastAt > TimeSpan.FromSeconds(10);
                if (!fresh && args.RawSignalStrengthInDBm < lastRssi) continue;
                last = data;
                lastRssi = args.RawSignalStrengthInDBm;
                lastAt = DateTime.Now;
            }
        }
    }
}

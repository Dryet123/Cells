using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

public interface IProvider
{
    string Name { get; }
    void Poll(PollContext ctx);
}

public sealed class PollContext(List<HidInfo> hid)
{
    readonly HashSet<string> claimed = [];
    readonly Dictionary<string, Reading> readings = [];

    public IReadOnlyList<HidInfo> Hid { get; } = hid;
    public IEnumerable<Reading> Readings => readings.Values;
    public List<string> Trace { get; } = [];
    public bool Tracing { get; init; }

    public IEnumerable<HidInfo> Vendor(ushort vid) => Hid.Where(h => h.Vid == vid);

    // Physical devices handled by a specific provider are skipped by the generic HID battery reader.
    public void Claim(HidInfo info) => claimed.Add(info.Group);
    public bool IsClaimed(HidInfo info) => claimed.Contains(info.Group);

    // The first reachable reading for a key wins: vendor protocols run before the generic sources.
    public void Add(Reading r)
    {
        if (readings.TryGetValue(r.Key, out var old) && old.Reachable) return;
        readings[r.Key] = r with { Level = Math.Clamp(r.Level, -1, 100) };
    }

    public void Replace(Reading r) => readings[r.Key] = r with { Level = Math.Clamp(r.Level, -1, 100) };

    public bool Has(string key) => readings.TryGetValue(key, out var r) && r.Reachable;

    public void Note(string text)
    {
        if (Tracing) Trace.Add(text);
    }

    public static string Hex(byte[]? data, int count = 24) =>
        data == null ? "null" : Convert.ToHexString(data, 0, Math.Min(count, data.Length)).ToLowerInvariant();
}

static class Curves
{
    // Typical single-cell Li-ion discharge curve under light load.
    public static readonly (int Mv, int Pct)[] LiIon =
        [(4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50), (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0)];

    public static int Percent(int mv, (int Mv, int Pct)[] table)
    {
        if (mv >= table[0].Mv) return table[0].Pct;
        for (int i = 1; i < table.Length; i++)
        {
            var (hiMv, hiPct) = table[i - 1];
            var (loMv, loPct) = table[i];
            if (mv >= loMv) return loPct + (int)Math.Round((double)(mv - loMv) * (hiPct - loPct) / (hiMv - loMv));
        }
        return table[^1].Pct;
    }

    public static string Key(string prefix, HidInfo h) =>
        string.IsNullOrEmpty(h.Serial) ? $"{prefix}:{h.Vid:x4}:{h.Pid:x4}" : $"{prefix}:{h.Vid:x4}:{h.Pid:x4}:{h.Serial.ToLowerInvariant()}";

    public static string Clean(string product, string fallback)
    {
        var name = product.Trim();
        foreach (var tail in new[] { " Receiver", " Dongle", " Wireless Receiver", " USB Receiver", " Transceiver" })
            if (name.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) name = name[..^tail.Length].Trim();
        return name.Length > 0 ? name : fallback;
    }
}

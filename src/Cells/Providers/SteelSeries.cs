using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// SteelSeries Arctis / Nova headsets and Aerox / Prime / Rival wireless mice.
sealed class SteelSeries : IProvider
{
    static readonly HashSet<ushort> Arctis1 = [0x12B3, 0x12B6, 0x12D7, 0x12D5];
    static readonly HashSet<ushort> Arctis7 = [0x1260, 0x12AD, 0x1252, 0x1280];
    static readonly HashSet<ushort> Arctis7Plus = [0x220E, 0x2212, 0x2216, 0x2236];
    static readonly HashSet<ushort> NovaPercent = [0x2269, 0x226D, 0x2232, 0x2253];
    static readonly HashSet<ushort> NovaPro = [0x12E0, 0x225D];
    static readonly HashSet<ushort> Rival = [0x1830, 0x1872, 0x172B, 0x1726];
    // Mouse receiver -> the same mouse on its cable.
    static readonly Dictionary<ushort, ushort> Mice = new()
    {
        [0x1838] = 0x183A, [0x1878] = 0x187A, [0x1890] = 0x1892, [0x1852] = 0x1854, [0x1858] = 0x185A, [0x1840] = 0x1842, [0x1860] = 0x1860,
    };
    const int Off = -1;

    public string Name => "SteelSeries";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Vendor(0x1038).GroupBy(h => h.Group))
        {
            var list = group.ToList();
            ushort pid = list[0].Pid;
            var name = Curves.Clean(list[0].Product, "SteelSeries device");
            var kind = DeviceKind.Headset;
            (int Pct, bool Charging)? result;

            if (Arctis1.Contains(pid))
                result = Parse(Query.Once(Query.ByUsage(list, 0xFF43, 0x0202), [0x06, 0x12], r => r.Length > 3), r => r[2] == 0x01 ? (Off, false) : (r[3], false));
            else if (Arctis7.Contains(pid))
                result = Parse(Query.Once(Query.ByUsage(list, 0xFF43, 0x0202), [0x06, 0x18], r => r.Length > 2), r => r[2] == 0 ? (Off, false) : (r[2], false));
            else if (pid == 0x12C2)
                result = Parse(Query.Once(Query.ByInterface(list, 0), [0x00, 0x20], r => r.Length > 4), r => (Query.Scale(r[3] - 0x64, 0x9A - 0x64), r[4] == 0x01));
            else if (pid == 0x1290)
                result = ProWireless(Query.ByInterface(list, 0));
            else if (NovaPro.Contains(pid))
                result = Parse(Query.Once(Query.ByInterface(list, 4), [0x06, 0xB0], r => r.Length > 15),
                    r => r[15] == 0x01 ? (Off, false) : (Query.Scale(r[6], 8), r[15] == 0x02));
            else if (Query.ByUsage(list, 0xFFC0, 0x0001) is { } nova)
                result = Parse(Query.Once(nova, [0x00, 0xB0], r => r.Length > 4), r => Nova(pid, r));
            else if (Mice.ContainsKey(pid) || Mice.ContainsValue(pid))
            {
                kind = DeviceKind.Mouse;
                result = Aerox(Query.ByInterface(list, 3), pid);
                var receiverPid = Mice.FirstOrDefault(m => m.Value == pid && m.Key != pid).Key;
                if (receiverPid != 0) pid = receiverPid;
            }
            else if (Rival.Contains(pid))
            {
                kind = DeviceKind.Mouse;
                var h = Query.ByInterface(list, 3) ?? Query.FirstVendor(list);
                result = Parse(Query.Once(h, [0x00, 0xAA, 0x01], r => r.Length > 2), r => r[0] <= 100 ? (r[0], r[2] == 1) : null);
            }
            else continue;

            ctx.Claim(list[0]);
            ctx.Note($"  {list[0]} result={result}");
            ctx.Add(new Reading($"steelseries:{pid:x4}", name, Kinds.Guess(name, kind), result?.Pct ?? Off, result?.Charging ?? false));
        }
    }

    static (int, bool)? Parse(byte[]? reply, Func<byte[], (int, bool)?> parse) => reply == null ? null : parse(reply);

    static (int, bool) Nova(ushort pid, byte[] r)
    {
        if (Arctis7Plus.Contains(pid)) return r[1] == 0x01 ? (Off, false) : (Query.Scale(r[2], 4), r[3] == 0x01);
        if (NovaPercent.Contains(pid)) return r[1] == 0x02 ? (Off, false) : (r[3], r[4] == 0x01);
        if (r[3] == 0x00) return (Off, false);
        return (r[2] <= 4 ? Query.Scale(r[2], 4) : r[2], r[3] == 0x01);
    }

    static (int, bool)? ProWireless(HidInfo? h)
    {
        var status = Query.Once(h, [0x00, 0x41, 0xAA], r => r.Length > 0);
        if (status == null) return null;
        if (status[0] == 0x02) return (Off, false);
        var level = Query.Once(h, [0x00, 0x40, 0xAA], r => r.Length > 0);
        return level != null && level[0] <= 4 ? (Query.Scale(level[0], 4), false) : null;
    }

    // Through the receiver the mouse answers D2, on its cable 92. 40 FF means the receiver can't reach it.
    static (int, bool)? Aerox(HidInfo? h, ushort pid)
    {
        if (h == null) return null;
        using var dev = HidHandle.Open(h);
        if (dev == null) return null;
        dev.Drain();
        byte cmd = Mice.ContainsKey(pid) ? (byte)0xD2 : (byte)0x92;
        byte[]? r = null;
        for (int attempt = 0; attempt < 10 && r == null; attempt++)
            r = dev.Exchange([0x00, cmd], 100, x => x.Length > 2 && (x[0] == cmd || x[0] == 0x40 && x[1] == 0xFF));
        if (r == null) return null;
        if (r[0] == 0x40) return (Off, false);
        if (pid is 0x1890 or 0x1892) return r[2] <= 100 ? (r[2], r[1] is 1 or 2) : null;
        if (r[1] == 0) return (Off, false);
        return (Math.Min(100, ((r[1] & 0x7F) - 1) * 5), (r[1] & 0x80) != 0);
    }
}

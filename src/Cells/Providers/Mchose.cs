using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// MCHOSE mice on Realtek chips (A7 Ultra, A7 V2 family, L7 Pro). Feature report 11 on the FF01:0001
// collection: the command byte and the reply are sent inverted (xor 0xFF). Command 03 wakes the link,
// command 06 returns VID, PID, firmware, mode flags, percent and a charging flag.
sealed class Mchose : IProvider
{
    const byte Report = 0x11, Wake = 0x03, Battery = 0x06;
    static readonly HashSet<ushort> Vendors = [0x5253, 0x3837];
    // MagDock charging base, not a mouse.
    static readonly HashSet<(ushort, ushort)> Skip = [(0x3837, 0x1012)];

    public string Name => "MCHOSE";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Hid.Where(h => Vendors.Contains(h.Vid)).GroupBy(h => h.Group))
        {
            var h = group.FirstOrDefault(x => x.UsagePage == 0xFF01 && x.Usage == 0x0001 && x.FeatLen == 65);
            if (h == null || Skip.Contains((h.Vid, h.Pid))) continue;
            ctx.Claim(h);
            using var dev = HidHandle.Open(h, io: false);
            if (dev == null) continue;

            var name = NameOf(h);
            var key = $"mchose:{name.ToLowerInvariant()}";
            dev.SetFeature(Command(Wake));
            Thread.Sleep(120);
            var p = Ask(dev);
            ctx.Note($"  {h} payload={PollContext.Hex(p, 12)}");
            if (p == null)
            {
                ctx.Add(new Reading(key, name, DeviceKind.Mouse, -1));
                continue;
            }
            ctx.Add(new Reading(key, name, DeviceKind.Mouse, p[9], p[10] != 0));
        }
    }

    static byte[] Command(byte cmd)
    {
        var report = new byte[65];
        Array.Fill(report, (byte)0xFF);
        report[0] = Report;
        report[1] = (byte)(cmd ^ 0xFF);
        return report;
    }

    // Returns the decoded payload (VID, PID, firmware, flags, percent, charging), or null when the mouse
    // didn't answer. The receiver keeps the previous reply around, so the echo and VID are checked.
    static byte[]? Ask(HidHandle dev)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!dev.SetFeature(Command(Battery))) return null;
            Thread.Sleep(80);
            for (int read = 0; read < 4; read++)
            {
                var r = dev.GetFeature(Report);
                if (r is not { Length: > 12 } || r[0] != Report) continue;
                bool inverted = r[1] == (Battery ^ 0xFF);
                if (!inverted && r[1] != Battery) continue;
                var p = r[2..].Select(b => inverted ? (byte)(b ^ 0xFF) : b).ToArray();
                ushort vid = (ushort)(p[0] | p[1] << 8);
                if (Vendors.Contains(vid) && p[9] is >= 1 and <= 100) return p;
            }
            Thread.Sleep(200);
        }
        return null;
    }

    static string NameOf(HidInfo h)
    {
        var name = Curves.Clean(h.Product, "");
        bool generic = name.Length == 0 || name.Contains("2.4G", StringComparison.OrdinalIgnoreCase) || name.Contains("Wireless", StringComparison.OrdinalIgnoreCase);
        if (!generic) return name.StartsWith("MCHOSE", StringComparison.OrdinalIgnoreCase) ? name : "MCHOSE " + name;
        return h.Vid == 0x3837 ? "MCHOSE A7 V2" : "MCHOSE A7 Ultra";
    }
}

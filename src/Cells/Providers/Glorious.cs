using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// Mice built on the same supplier firmware: Glorious, Hitscan, G-Wolves and WLMouse.
// 65-byte feature report; command 83 returns status, charging flag and percent.
sealed class Glorious : IProvider
{
    static readonly HashSet<ushort> GloriousPids = [0x2022, 0x2011, 0x2023, 0x2012, 0x2024, 0x2013, 0x2025, 0x2014, 0x2033, 0x2034, 0x2036, 0x201A];
    static readonly HashSet<ushort> GloriousCable = [0x2011, 0x2012, 0x2013, 0x2014, 0x201A];
    static readonly Dictionary<ushort, int> Delays = new() { [0x258A] = 50, [0x3770] = 200, [0x33E4] = 100, [0x36A7] = 100 };

    public string Name => "Glorious";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Hid.Where(h => Delays.ContainsKey(h.Vid)).GroupBy(h => h.Group))
        {
            var h = group.FirstOrDefault(x => x.FeatLen == 65);
            if (h == null || h.Vid == 0x258A && !GloriousPids.Contains(h.Pid) || h.Vid == 0x36A7 && h.Pid != 0xA874) continue;
            ctx.Claim(h);
            using var dev = HidHandle.Open(h, io: false);
            if (dev == null) continue;

            byte[]? r = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!dev.SetFeature(0x00, 0x00, 0x00, 0x02, 0x02, 0x00, 0x83)) break;
                Thread.Sleep(Delays[h.Vid]);
                r = dev.GetFeature(0x00);
                if (r is { Length: > 8 } && r[1] == 0xA0)
                {
                    Thread.Sleep(400);
                    continue;
                }
                break;
            }
            ctx.Note($"  {h} reply={PollContext.Hex(r, 10)}");
            var name = Curves.Clean(h.Product, "Wireless mouse");
            var key = $"glorious:{name.ToLowerInvariant()}";
            bool cable = h.Vid == 0x258A && GloriousCable.Contains(h.Pid);
            if (r is not { Length: > 8 } || r[1] != 0xA1 || r[6] != 0x83 || r[8] > 100)
            {
                if (r is { Length: > 1 } && r[1] == 0xA4) ctx.Add(new Reading(key, name, DeviceKind.Mouse, -1));
                continue;
            }
            ctx.Add(new Reading(key, name, Kinds.Guess(name, DeviceKind.Mouse), Math.Max((int)r[8], 1), cable || r[7] == 0x01));
        }
    }
}

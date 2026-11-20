using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// Razer mice and keyboards: 90-byte feature report (OpenRazer layout).
// [status][transaction id][remaining x2][protocol][size][class][id][80 args][crc][reserved]
sealed class Razer : IProvider
{
    const int Length = 91;
    static readonly HashSet<ushort> Headsets = [0x0528, 0x0550, 0x0552, 0x0555, 0x0565, 0x0576, 0x0577, 0x0579, 0x057A, 0x057C, 0x057D, 0x0A4E, 0x0A55];
    static readonly HashSet<ushort> WirelessKeyboards = [0x025C, 0x0271, 0x0290, 0x0296, 0x02BA, 0x02C9, 0x02D2, 0x02D5];
    static readonly byte[] Probe = [0x1F, 0x3F, 0xFF, 0x08, 0x9F];

    readonly Dictionary<ushort, byte> transactionIds = [];
    readonly HashSet<ushort> noBattery = [];

    public string Name => "Razer";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Vendor(0x1532).GroupBy(h => h.Group))
        {
            var h = group.FirstOrDefault(x => x.FeatLen == Length);
            if (h == null || Headsets.Contains(h.Pid) || noBattery.Contains(h.Pid)) continue;
            ctx.Claim(h);
            using var dev = HidHandle.Open(h, io: false);
            if (dev == null) continue;

            var name = Curves.Clean(h.Product, "Razer device");
            var key = $"razer:{name.ToLowerInvariant()}";
            var level = Ask(dev, h.Pid, 0x80, out int status);
            ctx.Note($"  {h} tid={transactionIds.GetValueOrDefault(h.Pid):x2} status={status} level={PollContext.Hex(level, 12)}");
            if (status == 0x05)
            {
                noBattery.Add(h.Pid);
                continue;
            }
            if (level == null)
            {
                // 04: the receiver is there but the device is off or asleep.
                if (status == 0x04) ctx.Add(new Reading(key, name, Kinds.Guess(name, DeviceKind.Mouse), -1));
                continue;
            }
            bool charging = Ask(dev, h.Pid, 0x84, out _) is { } c && c[10] == 1;
            if (level[10] == 0 && !charging) continue;
            ctx.Add(new Reading(key, name, Kinds.Guess(name, DeviceKind.Mouse), (level[10] * 100 + 127) / 255, charging));
        }
    }

    byte[]? Ask(HidHandle dev, ushort pid, byte command, out int status)
    {
        status = 0;
        IEnumerable<byte> candidates = transactionIds.TryGetValue(pid, out var known) ? [known]
            : WirelessKeyboards.Contains(pid) ? [0x9F]
            : pid == 0x025A ? [0x3F]
            : Probe;
        foreach (var tid in candidates)
        {
            var reply = Exchange(dev, tid, command, out status);
            if (reply == null) continue;
            transactionIds[pid] = tid;
            return reply;
        }
        return null;
    }

    static byte[]? Exchange(HidHandle dev, byte tid, byte command, out int status)
    {
        status = 0;
        var request = new byte[Length];
        request[2] = tid;
        request[6] = 0x02;
        request[7] = 0x07;
        request[8] = command;
        byte crc = 0;
        for (int i = 3; i < 89; i++) crc ^= request[i];
        request[89] = crc;
        if (!dev.SetFeature(request)) return null;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 20 : 40);
            var reply = dev.GetFeature(0x00);
            if (reply == null) return null;
            status = reply[1];
            if (status == 0x01) continue;
            return status == 0x02 && reply[2] == tid && reply[7] == 0x07 && reply[8] == command ? reply : null;
        }
        return null;
    }
}

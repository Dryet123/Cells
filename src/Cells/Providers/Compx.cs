using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// 17-byte vendor report used by many cheap 2.4 GHz mouse chips (Compx, Areson, SinoWealth based):
// report 08, command byte, zero padding and a checksum that makes the whole report sum to 0x55.
// Found by its collection (FF02:0002, 17 bytes) rather than by brand, so unknown rebrands work too.
sealed class Compx : IProvider
{
    const byte LinkStatus = 0x03, Power = 0x04;

    // Areson firmware reports 0..10 instead of percent.
    static readonly HashSet<(ushort, ushort)> TenSteps = [(0x25A7, 0xFA07), (0x25A7, 0xFA08)];

    public string Name => "Compx";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Hid.GroupBy(h => h.Group))
        {
            var target = group.FirstOrDefault(h => h.UsagePage == 0xFF02 && h.Usage == 0x0002 && (h.FeatLen == 17 || h.OutLen == 17));
            if (target == null) continue;
            ctx.Claim(target);

            var handles = new List<HidHandle>();
            try
            {
                foreach (var h in group.Where(h => h.IsVendor && h.InLen >= 17))
                    if (HidHandle.Open(h) is { } listener) handles.Add(listener);
                var sender = handles.FirstOrDefault(x => x.Info == target) ?? HidHandle.Open(target, io: target.FeatLen != 17);
                if (sender == null) continue;
                if (!handles.Contains(sender)) handles.Add(sender);
                foreach (var x in handles) x.Drain();

                var link = Ask(sender, handles, LinkStatus);
                var power = Ask(sender, handles, Power);
                ctx.Note($"  {target} link={PollContext.Hex(link, 17)} power={PollContext.Hex(power, 17)}");

                var name = Curves.Clean(target.Product, "Wireless mouse");
                if (name.Contains("2.4G", StringComparison.OrdinalIgnoreCase)) name = "Wireless mouse";
                var key = Curves.Key("compx", target);
                bool asleep = link != null && link[6] == 0;
                if (power == null || asleep || power[6] > 100 && !TenSteps.Contains((target.Vid, target.Pid)))
                {
                    ctx.Add(new Reading(key, name, DeviceKind.Mouse, -1));
                    continue;
                }
                int pct = TenSteps.Contains((target.Vid, target.Pid)) ? power[6] * 10 : power[6];
                ctx.Add(new Reading(key, name, Kinds.Guess(name, DeviceKind.Mouse), pct, power[7] == 1));
            }
            finally
            {
                foreach (var x in handles) x.Dispose();
            }
        }
    }

    static byte[]? Ask(HidHandle sender, List<HidHandle> listeners, byte cmd)
    {
        var packet = new byte[17];
        packet[0] = 0x08;
        packet[1] = cmd;
        packet[16] = (byte)(0x55 - 0x08 - cmd);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool sent = sender.Info.FeatLen == 17 ? sender.SetFeature(packet) : sender.Write(packet);
            if (!sent) return null;
            var reply = HidHandle.ReadAny(listeners, 600, (_, r) => Valid(r, cmd));
            if (reply != null) return reply.Value.Data;
            Thread.Sleep(150);
        }
        return null;
    }

    static bool Valid(byte[] r, byte cmd)
    {
        if (r.Length < 17 || r[0] is not (0x08 or 0x09) || r[1] != cmd) return false;
        int sum = 0;
        for (int i = 0; i < 17; i++) sum += r[i];
        return (sum & 0xFF) == 0x55;
    }
}

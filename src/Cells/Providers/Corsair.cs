using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// Corsair: the older Void / HS70 / HS80 headsets, and everything on the "Bragi" protocol
// (Slipstream receivers, newer headsets, wireless mice and keyboards). Compx-based Sabre mice
// are handled by the Compx provider.
sealed class Corsair : IProvider
{
    static readonly HashSet<ushort> Void =
        [0x1B27, 0x0A14, 0x0A16, 0x0A17, 0x0A1D, 0x0A1A, 0x1B2A, 0x1B23, 0x1B29, 0x0A55, 0x0A51, 0x0A52, 0x0A38, 0x0A4F, 0x0A2B, 0x0A75, 0x0A56];
    static readonly HashSet<ushort> BragiReceivers = [0x2A08, 0x2A02, 0x0A97, 0x0A64, 0x0A62, 0x0A3E, 0x0A3D, 0x2B00, 0x1BA6, 0x1B65, 0x1BDC];
    const byte Battery = 0x0F, ChargeState = 0x10, PairedPid = 0x12;

    public string Name => "Corsair";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Vendor(0x1B1C).GroupBy(h => h.Group))
        {
            var list = group.ToList();
            var first = list[0];
            if (Void.Contains(first.Pid)) VoidHeadset(ctx, list);
            else if (list.FirstOrDefault(h => (h.UsagePage == 0xFF42 || h.Interface == 4 && h.IsVendor && BragiReceivers.Contains(h.Pid))
                                              && h.OutLen is 64 or 65 && h.InLen is 64 or 65) is { } bragi)
                Bragi(ctx, bragi);
        }
    }

    static void VoidHeadset(PollContext ctx, List<HidInfo> list)
    {
        var h = list.FirstOrDefault(x => x.UsagePage is 0xFFC5 or 0xFF00 && x.OutLen > 0);
        if (h == null) return;
        ctx.Claim(h);
        var r = Query.Once(h, [0xC9, 0x64], x => x.Length >= 5);
        ctx.Note($"  {h} reply={PollContext.Hex(r, 8)}");
        var name = Curves.Clean(h.Product, "Corsair headset");
        var key = $"corsair:{h.Pid:x4}";
        bool online = r != null && r[4] is >= 1 and <= 5 and not 3;
        ctx.Add(new Reading(key, name, DeviceKind.Headset, online ? r![2] & 0x7F : -1, online && r![4] >= 4));
    }

    static void Bragi(PollContext ctx, HidInfo h)
    {
        ctx.Claim(h);
        using var dev = HidHandle.Open(h);
        if (dev == null) return;
        dev.Drain();
        bool receiver = BragiReceivers.Contains(h.Pid) || h.Product.Contains("receiver", StringComparison.OrdinalIgnoreCase)
                        || h.Product.Contains("slipstream", StringComparison.OrdinalIgnoreCase);
        bool plain = h.Pid == 0x2B00;
        byte[] targets = !receiver ? [0x08] : h.Product.Contains("slipstream", StringComparison.OrdinalIgnoreCase) || plain ? [0x09, 0x0A, 0x0B] : [0x09];

        int? Get(byte target, byte prop)
        {
            byte[] body = plain ? [target, 0x02, prop] : [0x02, target, 0x02, prop];
            byte[] request = h.OutLen == 65 ? [0x00, .. body] : body;
            var r = dev.Exchange(request, 800, x => Offset(x) is { } o && x.Length > o + 4);
            if (r == null || Offset(r) is not { } o || r[o + 2] != 0) return null;
            return r[o + 3] | r[o + 4] << 8;
        }

        foreach (var target in targets)
        {
            var raw = Get(target, Battery);
            ctx.Note($"  {h} target={target:x2} battery={raw}");
            var key = $"corsair:{h.Pid:x4}:{target:x2}";
            if (raw is not (> 0 and <= 1000))
            {
                if (target == 0x09 && receiver) ctx.Add(new Reading(key, NameFor(h, null), DeviceKind.Other, -1));
                continue;
            }
            var state = Get(target, ChargeState);
            var paired = receiver ? Get(target, PairedPid) : h.Pid;
            ctx.Add(new Reading(key, NameFor(h, paired), KindFor(h, paired), (raw.Value + 5) / 10, state is 1 or 3));
        }
    }

    // Replies are "source 02 status value(LE)", optionally behind a 02 report id.
    static int? Offset(byte[] r)
    {
        if (r.Length > 2 && r[0] == 0x02 && r[2] == 0x02) return 1;
        if (r.Length > 1 && r[1] == 0x02) return 0;
        return null;
    }

    static DeviceKind KindFor(HidInfo h, int? paired) =>
        paired is { } p && (p >> 8) == 0x0A ? DeviceKind.Headset : Kinds.Guess(h.Product, DeviceKind.Mouse);

    static string NameFor(HidInfo h, int? paired)
    {
        var name = Curves.Clean(h.Product, "Corsair device");
        if (!name.Contains("slipstream", StringComparison.OrdinalIgnoreCase) && !name.Contains("receiver", StringComparison.OrdinalIgnoreCase)) return name;
        return KindFor(h, paired) == DeviceKind.Headset ? "Corsair headset" : "Corsair wireless";
    }
}

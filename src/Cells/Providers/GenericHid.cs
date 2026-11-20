using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// Devices that follow the HID spec and report "Battery Strength" (Generic Device Controls 0x06:0x20),
// common for Bluetooth keyboards, mice and some gamepads.
sealed class GenericHid : IProvider
{
    const int CapsSize = 72;

    public string Name => "Generic HID";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Hid.GroupBy(h => h.Group))
        {
            // Pens report a stale level; UPS pages are covered by the system battery.
            if (group.Any(h => h.UsagePage is 0x0D or 0x84 or 0x85)) continue;
            foreach (var h in group)
            {
                if (ctx.IsClaimed(h)) break;
                if (Read(h) is not { } pct) continue;
                var key = h.Bluetooth ? $"bt:{h.Group}" : Curves.Key("hid", h);
                if (ctx.Has(key)) break;
                ctx.Note($"  {h} level={pct}");
                var kind = group.Any(x => x.UsagePage == 1 && x.Usage == 6) ? DeviceKind.Keyboard
                    : group.Any(x => x.UsagePage == 1 && x.Usage == 2) ? DeviceKind.Mouse
                    : group.Any(x => x.UsagePage == 1 && x.Usage is 4 or 5) ? DeviceKind.Gamepad
                    : DeviceKind.Other;
                var name = Curves.Clean(h.Product, "HID device");
                ctx.Add(new Reading(key, name, Kinds.Guess(name, kind), pct));
                break;
            }
        }
    }

    static int? Read(HidInfo h)
    {
        using var dev = HidHandle.Open(h, io: false);
        if (dev == null) return null;
        return dev.WithPreparsed<int?>(data =>
        {
            foreach (int type in new[] { HidNative.Feature, HidNative.Input })
            {
                ushort count = 64;
                var caps = new byte[count * CapsSize];
                if (HidNative.HidP_GetValueCaps(type, caps, ref count, data) != HidNative.StatusSuccess) continue;
                for (int i = 0; i < count; i++)
                {
                    var c = caps.AsSpan(i * CapsSize, CapsSize);
                    ushort page = BitConverter.ToUInt16(c[0..]);
                    bool isRange = c[12] != 0;
                    ushort usageMin = BitConverter.ToUInt16(c[56..]), usageMax = BitConverter.ToUInt16(c[58..]);
                    if (page != 0x06 || (isRange ? usageMin > 0x20 || usageMax < 0x20 : usageMin != 0x20)) continue;

                    byte reportId = c[2];
                    ushort link = BitConverter.ToUInt16(c[6..]);
                    int min = BitConverter.ToInt32(c[40..]), max = BitConverter.ToInt32(c[44..]);
                    var report = type == HidNative.Feature ? dev.GetFeature(reportId) : dev.GetInput(reportId);
                    if (report == null || max <= min) continue;
                    if (HidNative.HidP_GetUsageValue(type, 0x06, link, 0x20, out uint value, data, report, report.Length) != HidNative.StatusSuccess) continue;
                    if (value < min || value > max) continue;
                    return (int)Math.Round(100.0 * (value - min) / (max - min));
                }
            }
            return null;
        }, null);
    }
}

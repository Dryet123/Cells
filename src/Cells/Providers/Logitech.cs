using System.Text;
using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// Logitech HID++. Receivers (Unifying, Bolt, Lightspeed) host up to six devices at indexes 1-6;
// a device on a cable or Bluetooth answers at index 0xFF. HID++ 2.0 features are looked up by id
// through the root feature, HID++ 1.0 devices expose battery registers instead.
sealed class Logitech : IProvider
{
    const byte Short = 0x10, Long = 0x11, SoftwareId = 0x0A;
    const ushort DeviceInfo = 0x0003, DeviceName = 0x0005, BatteryStatus = 0x1000, BatteryVoltage = 0x1001, UnifiedBattery = 0x1004, Adc = 0x1F20;

    static readonly HashSet<ushort> Receivers = [0xC517, 0xC52B, 0xC52F, 0xC532, 0xC534, 0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC548, 0xC54D];
    static readonly HashSet<ushort> G633Family = [0x0A5C, 0x0A89, 0x0A5B, 0x0A87, 0x0AB5, 0x0AFE];
    static readonly (int, int)[] G633Curve = [(4100, 100), (3950, 80), (3850, 60), (3750, 40), (3650, 20), (3500, 10), (3300, 5), (3150, 0)];
    static readonly (int, int)[] HeadsetCurve = [(4100, 100), (3850, 50), (3780, 30), (3730, 20), (3650, 5), (3300, 0)];

    sealed class Slot
    {
        public string? Key;
        public string Name = "Logitech device";
        public DeviceKind Kind = DeviceKind.Other;
        public readonly Dictionary<ushort, byte> Features = [];
    }

    readonly Dictionary<string, Slot> slots = [];

    public string Name => "Logitech";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Vendor(0x046D).GroupBy(h => h.Group))
        {
            var shortInfo = group.FirstOrDefault(h => h.UsagePage == 0xFF00 && h.Usage == 0x0001 && h.OutLen == 7);
            var longInfo = group.FirstOrDefault(h => (h.UsagePage == 0xFF00 && h.Usage == 0x0002 || h.UsagePage == 0xFF43 && h.Usage == 0x0202) && h.OutLen == 20);
            var main = shortInfo ?? longInfo;
            if (main == null) continue;
            ctx.Claim(main);

            using var shortDev = shortInfo != null ? HidHandle.Open(shortInfo) : null;
            using var longDev = longInfo != null ? HidHandle.Open(longInfo) : null;
            if (shortDev == null && longDev == null) continue;
            var link = new Link(shortDev, longDev);
            shortDev?.Drain();
            longDev?.Drain();

            bool receiver = Receivers.Contains(main.Pid) || main.Product.Contains("Receiver", StringComparison.OrdinalIgnoreCase);
            foreach (byte index in receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : [0xFF])
                PollDevice(ctx, link, main, index, receiver);
        }
    }

    void PollDevice(PollContext ctx, Link link, HidInfo h, byte index, bool receiver)
    {
        var slotId = $"{h.Group}:{index}";
        slots.TryGetValue(slotId, out var slot);
        var ping = link.Call(index, 0x00, 1, 0, 0, 0x5A);
        bool hidpp10 = ping != null && ping[2] == 0x8F && ping[5] == 0x01;
        if (ping == null || IsError(ping) && !hidpp10)
        {
            if (slot?.Key != null) ctx.Add(new Reading(slot.Key, slot.Name, slot.Kind, -1));
            return;
        }
        if (slot == null) slots[slotId] = slot = new Slot();

        (int Pct, bool Charging)? battery;
        if (hidpp10)
        {
            if (slot.Key == null)
            {
                slot.Key = $"logi:{h.Pid:x4}:{index}";
                if (receiver && link.Raw(Short, 0xFF, 0x83, 0xB5, (byte)(0x40 + index - 1)) is { } pairing && !IsError(pairing) && pairing.Length > 6)
                    slot.Name = Encoding.ASCII.GetString(pairing, 6, Math.Min(pairing[5], pairing.Length - 6)).Trim('\0', ' ');
                slot.Kind = Kinds.Guess(slot.Name, DeviceKind.Mouse);
            }
            battery = Registers(link, index);
        }
        else
        {
            if (slot.Key == null) Identify(link, h, index, slot);
            battery = Features(link, h, index, slot);
        }
        ctx.Note($"  index {index:x2} {slot.Name} hidpp{(hidpp10 ? "1.0" : "2.0")} battery={battery}");
        ctx.Add(new Reading(slot.Key!, slot.Name, slot.Kind, battery?.Pct ?? -1, battery?.Charging ?? false));
    }

    static bool IsError(byte[] r) => r[2] is 0xFF or 0x8F;

    static byte Feature(Link link, byte index, Slot slot, ushort id)
    {
        if (slot.Features.TryGetValue(id, out var cached)) return cached;
        var r = link.Call(index, 0x00, 0, (byte)(id >> 8), (byte)id);
        if (r == null || IsError(r)) return 0;
        return slot.Features[id] = r[4];
    }

    static void Identify(Link link, HidInfo h, byte index, Slot slot)
    {
        byte info = Feature(link, index, slot, DeviceInfo);
        if (info != 0 && link.Call(index, info, 0) is { } r && !IsError(r))
            slot.Key = $"logi:{Convert.ToHexString(r, 5, 4).ToLowerInvariant()}";
        slot.Key ??= $"logi:{h.Pid:x4}:{index}";

        byte names = Feature(link, index, slot, DeviceName);
        if (names == 0)
        {
            slot.Name = Curves.Clean(h.Product, "Logitech device");
            slot.Kind = Kinds.Guess(slot.Name);
            return;
        }
        int length = link.Call(index, names, 0) is { } count && !IsError(count) ? count[4] : 0;
        var name = new StringBuilder();
        while (name.Length < length)
        {
            var chunk = link.Call(index, names, 1, (byte)name.Length);
            if (chunk == null || IsError(chunk)) break;
            int before = name.Length;
            for (int i = 4; i < chunk.Length && name.Length < length && chunk[i] != 0; i++) name.Append((char)chunk[i]);
            if (name.Length == before) break;
        }
        if (name.Length > 0) slot.Name = name.ToString().Trim();
        int type = link.Call(index, names, 2) is { } t && !IsError(t) ? t[4] : -1;
        slot.Kind = type switch
        {
            0 or 2 => DeviceKind.Keyboard,
            3 or 4 or 5 => DeviceKind.Mouse,
            8 => DeviceKind.Headset,
            11 or 12 => DeviceKind.Gamepad,
            _ => Kinds.Guess(slot.Name),
        };
    }

    static (int, bool)? Features(Link link, HidInfo h, byte index, Slot slot)
    {
        byte f = Feature(link, index, slot, UnifiedBattery);
        if (f != 0 && link.Call(index, f, 1) is { } u && !IsError(u))
        {
            int soc = u[4], levels = u[5], status = u[6];
            int pct = soc is >= 1 and <= 100 ? soc
                : (levels & 8) != 0 ? 100 : (levels & 4) != 0 ? 60 : (levels & 2) != 0 ? 20 : 5;
            return (status == 3 ? 100 : pct, status is 1 or 2 or 3);
        }
        f = Feature(link, index, slot, BatteryStatus);
        if (f != 0 && link.Call(index, f, 0) is { } s && !IsError(s) && s[4] <= 100)
            return (s[4] == 0 && s[6] == 3 ? 100 : s[4], s[6] is >= 1 and <= 4);
        f = Feature(link, index, slot, BatteryVoltage);
        if (f != 0 && link.Call(index, f, 0) is { } v && !IsError(v))
            return (Curves.Percent(v[4] << 8 | v[5], Curves.LiIon), (v[6] & 0x80) != 0);
        f = Feature(link, index, slot, Adc);
        if (f != 0 && link.Call(index, f, 0) is { } a && !IsError(a))
            return (Curves.Percent(a[4] << 8 | a[5], G633Family.Contains(h.Pid) ? G633Curve : HeadsetCurve), a[6] == 3);
        return null;
    }

    static readonly int[] Steps = [0, 5, 10, 20, 35, 50, 75, 100];

    static (int, bool)? Registers(Link link, byte index)
    {
        if (link.Raw(Short, index, 0x81, 0x0D) is { } p && !IsError(p) && p[4] is >= 1 and <= 100)
            return (p[4], p[6] is >= 0x21 and <= 0x26);
        if (link.Raw(Short, index, 0x81, 0x07) is { } l && !IsError(l) && l[4] is >= 1 and <= 7)
            return (Steps[l[4]], l[5] is >= 0x21 and <= 0x26);
        return null;
    }

    // Short (7-byte) and long (20-byte) reports live in separate collections on Windows,
    // and a reply may come back on either one.
    sealed class Link(HidHandle? shortDev, HidHandle? longDev)
    {
        public byte[]? Call(byte index, byte feature, int function, params byte[] args) =>
            Raw(Long, index, feature, (byte)(function << 4 | SoftwareId), args);

        public byte[]? Raw(byte reportId, byte index, byte sub, byte address, params byte[] args)
        {
            var dev = reportId == Long ? longDev ?? shortDev : shortDev ?? longDev;
            if (dev == null) return null;
            byte id = dev == longDev ? Long : Short;
            var request = new byte[id == Long ? 20 : 7];
            request[0] = id;
            request[1] = index;
            request[2] = sub;
            request[3] = address;
            Array.Copy(args, 0, request, 4, Math.Min(args.Length, request.Length - 4));
            if (!dev.Write(request)) return null;

            var handles = new List<HidHandle>(2);
            if (shortDev != null) handles.Add(shortDev);
            if (longDev != null) handles.Add(longDev);
            return HidHandle.ReadAny(handles, 600, (_, r) => r.Length >= 7 && r[0] is Short or Long && r[1] == index
                && (r[2] == sub && r[3] == address || r[2] is 0xFF or 0x8F && r[3] == sub && r[4] == address))?.Data;
        }
    }
}

using Cells.Core;

namespace Cells.Providers;

// Bluetooth devices whose level Windows already knows (the number shown in Settings > Bluetooth & devices):
// classic headsets report it over HFP, LE devices through the GATT battery service.
sealed class Bluetooth : IProvider
{
    static readonly DevNode.Key Battery = new(new("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);
    static readonly DevNode.Key Connected = new(new("83da6326-97a6-4088-9453-a1923f573b29"), 15);
    static readonly DevNode.Key ClassOfDevice = new(new("2bd67d8b-8beb-48d5-87e0-6cda3428040a"), 10);

    sealed class Entry
    {
        public string? Name;
        public int Level = -1;
        public bool Connected, ConnectedKnown;
        public uint ClassOfDevice;
    }

    public static IReadOnlyList<(string Key, string Name)> ConnectedDevices { get; private set; } = [];

    public string Name => "Bluetooth";

    public void Poll(PollContext ctx)
    {
        var entries = new Dictionary<Guid, Entry>();
        foreach (var id in DevNode.PresentDevices())
        {
            if (!id.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) && !id.StartsWith(@"BTHLE", StringComparison.OrdinalIgnoreCase)) continue;
            var node = DevNode.Locate(id);
            if (DevNode.Guid(node, DevNode.ContainerId) is not { } container) continue;
            if (!entries.TryGetValue(container, out var e)) entries[container] = e = new Entry();

            if (DevNode.Byte(node, Battery) is { } level && level <= 100) e.Level = Math.Max(e.Level, level);
            if (DevNode.Bool(node, Connected) is { } connected)
            {
                e.Connected |= connected;
                e.ConnectedKnown = true;
            }
            if (DevNode.UInt(node, ClassOfDevice) is { } cod) e.ClassOfDevice = cod;
            // The DEV_ node carries the name the user sees; service nodes are called things like "Hands-Free AG".
            bool main = id.Contains(@"\DEV_", StringComparison.OrdinalIgnoreCase);
            if ((main || e.Name == null) && (DevNode.String(node, DevNode.FriendlyName) ?? DevNode.String(node, DevNode.Description)) is { } name)
                e.Name = name;
        }

        var connectedList = new List<(string, string)>();
        foreach (var (container, e) in entries)
        {
            if (e.Name == null) continue;
            var key = $"bt:{container}";
            bool online = !e.ConnectedKnown || e.Connected;
            if (e.ConnectedKnown && e.Connected) connectedList.Add((key, e.Name));
            if (e.Level < 0) continue;
            ctx.Note($"  {e.Name} level={e.Level} connected={(e.ConnectedKnown ? e.Connected : "?")} cod={e.ClassOfDevice:x6}");
            ctx.Add(new Reading(key, e.Name, Kinds.Guess(e.Name, KindOf(e.ClassOfDevice)), online ? e.Level : -1));
        }
        ConnectedDevices = connectedList;
    }

    static DeviceKind KindOf(uint cod)
    {
        int major = (int)(cod >> 8) & 0x1F, minor = (int)(cod >> 2) & 0x3F;
        return major switch
        {
            1 => DeviceKind.Computer,
            2 => DeviceKind.Phone,
            4 => minor == 5 ? DeviceKind.Speaker : DeviceKind.Headset,
            5 when (minor & 0x0F) is 1 or 2 => DeviceKind.Gamepad,
            5 when (minor & 0x0F) == 5 => DeviceKind.Pen,
            5 => (minor >> 4) == 2 ? DeviceKind.Mouse : DeviceKind.Keyboard,
            _ => DeviceKind.Other,
        };
    }
}

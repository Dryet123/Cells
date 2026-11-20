using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// DualSense, DualSense Edge and DualShock 4: the battery byte rides along in every input report.
sealed class PlayStation : IProvider
{
    static readonly Dictionary<ushort, string> Models = new()
    {
        [0x05C4] = "DualShock 4", [0x09CC] = "DualShock 4", [0x0BA0] = "DualShock 4",
        [0x0CE6] = "DualSense", [0x0DF2] = "DualSense Edge",
    };

    public string Name => "PlayStation";

    public void Poll(PollContext ctx)
    {
        foreach (var h in ctx.Vendor(0x054C).Where(h => h.UsagePage == 0x01 && h.Usage == 0x05 && Models.ContainsKey(h.Pid)))
        {
            ctx.Claim(h);
            using var dev = HidHandle.Open(h);
            if (dev == null) continue;

            bool bt = h.InLen > 64;
            bool sense = h.Pid is 0x0CE6 or 0x0DF2;
            // Over Bluetooth the pad sends a reduced report until a feature report is read.
            if (bt) dev.GetFeature(0x05);
            byte id = bt ? (byte)(sense ? 0x31 : 0x11) : (byte)0x01;
            int at = sense ? (bt ? 54 : 53) : (bt ? 32 : 30);
            var report = dev.ReadUntil(600, r => r.Length > at && r[0] == id);
            ctx.Note($"  {Models[h.Pid]} bt={bt} report={PollContext.Hex(report, 60)}");
            if (report == null) continue;

            byte b = report[at];
            if (h.Pid == 0x0BA0 && b == 0) continue;
            int level = b & 0x0F;
            int pct;
            bool charging;
            if (sense)
            {
                int state = b >> 4;
                pct = state == 2 ? 100 : Math.Min(100, level * 10 + 5);
                charging = state is 1 or 2;
            }
            else
            {
                bool cable = (b & 0x10) != 0;
                pct = cable ? (level >= 11 ? 100 : level * 10) : Math.Min(100, level * 10 + 5);
                charging = cable;
            }
            ctx.Add(new Reading(Curves.Key("ps", h), Models[h.Pid], DeviceKind.Gamepad, pct, charging));
        }
    }
}

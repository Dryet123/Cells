using System.Runtime.InteropServices;
using Cells.Core;
using Windows.Gaming.Input;

namespace Cells.Providers;

// Xbox controllers through Windows.Gaming.Input (exact percent), with XInput's four coarse steps as a fallback.
sealed class XboxPads : IProvider
{
    [StructLayout(LayoutKind.Sequential)]
    struct BatteryInformation
    {
        public byte Type, Level;
    }

    [DllImport("xinput1_4.dll")] static extern int XInputGetBatteryInformation(int user, byte devType, out BatteryInformation info);

    public string Name => "Xbox";

    public XboxPads()
    {
        // The list fills in asynchronously; touching it early means it is ready by the first poll.
        try { _ = Gamepad.Gamepads.Count; }
        catch (Exception ex) { Log.Error(ex); }
    }

    public void Poll(PollContext ctx)
    {
        if (ctx.Readings.Any(r => r.Key.StartsWith("bt:") && r.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase))) return;
        int found = 0;
        try
        {
            foreach (var pad in Gamepad.Gamepads)
            {
                var report = pad.TryGetBatteryReport();
                var raw = RawGameController.FromGameController(pad);
                ctx.Note($"  {raw?.DisplayName} status={report?.Status} {report?.RemainingCapacityInMilliwattHours}/{report?.FullChargeCapacityInMilliwattHours}");
                if (report is not { RemainingCapacityInMilliwattHours: { } left, FullChargeCapacityInMilliwattHours: > 0 and var full }) continue;
                if (report.Status == Windows.System.Power.BatteryStatus.NotPresent) continue;
                found++;
                var name = string.IsNullOrWhiteSpace(raw?.DisplayName) ? "Xbox controller" : raw!.DisplayName;
                var key = $"xbox:{AppPaths.StableHash(raw?.NonRoamableId ?? name):x8}";
                ctx.Add(new Reading(key, name, DeviceKind.Gamepad, (int)Math.Round(100.0 * left / full),
                    report.Status == Windows.System.Power.BatteryStatus.Charging));
            }
        }
        catch (Exception ex) { Log.Error(ex); }
        if (found > 0) return;

        for (int user = 0; user < 4; user++)
        {
            if (XInputGetBatteryInformation(user, 0, out var info) != 0) continue;
            // 0 disconnected, 1 wired (no battery), 0xFF unknown
            if (info.Type is 0 or 1 or 0xFF) continue;
            int pct = info.Level switch { 0 => 5, 1 => 30, 2 => 65, _ => 100 };
            ctx.Add(new Reading($"xinput:{user}", $"Xbox controller {user + 1}", DeviceKind.Gamepad, pct, Detail: "Approximate"));
        }
    }
}

using System.Runtime.InteropServices;
using Cells.Core;

namespace Cells.Providers;

// Laptop battery, or a UPS that Windows treats as the system battery.
sealed class SystemBattery : IProvider
{
    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus
    {
        public byte AcLine, Flag, Percent, SaverStatus;
        public int LifeTime, FullLifeTime;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus status);

    public string Name => "System battery";

    public void Poll(PollContext ctx)
    {
        if (!GetSystemPowerStatus(out var s) || (s.Flag & 128) != 0 || s.Flag == 255 || s.Percent > 100) return;
        bool charging = (s.Flag & 8) != 0 || s.AcLine == 1 && s.Percent < 100;
        string? left = !charging && s.LifeTime > 0 ? $"{s.LifeTime / 3600}h {s.LifeTime / 60 % 60:00}m left" : null;
        ctx.Add(new Reading("system", "This PC", DeviceKind.Computer, s.Percent, charging, left));
    }
}

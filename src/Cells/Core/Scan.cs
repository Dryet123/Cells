using System.Diagnostics;
using System.Text;
using Cells.Hid;
using Cells.Providers;

namespace Cells.Core;

// Plain-text report of every HID collection and what each provider saw. Used to add support for new devices.
public static class Scan
{
    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Cells {Installer.Short(Installer.CurrentVersion)} device scan, {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        var hid = HidBus.List();
        sb.AppendLine("HID collections");
        foreach (var h in hid.OrderBy(h => h.Vid).ThenBy(h => h.Pid).ThenBy(h => h.Interface).ThenBy(h => h.UsagePage))
            sb.AppendLine("  " + h);

        var ctx = new PollContext(hid) { Tracing = true };
        foreach (var provider in DeviceMonitor.CreateProviders())
        {
            ctx.Note(provider.Name);
            var watch = Stopwatch.StartNew();
            try { provider.Poll(ctx); }
            catch (Exception ex) { ctx.Note("  error: " + ex.Message); }
            ctx.Note($"  {watch.ElapsedMilliseconds} ms");
        }
        sb.AppendLine();
        sb.AppendLine("Providers");
        foreach (var line in ctx.Trace) sb.AppendLine("  " + line);
        sb.AppendLine();
        sb.AppendLine("Readings");
        foreach (var r in ctx.Readings)
            sb.AppendLine($"  {r.Name} ({r.Kind}) {(r.Reachable ? r.Level + "%" : "not reachable")}{(r.Charging ? " charging" : "")} {r.Detail} [{r.Key}]");
        return sb.ToString();
    }

    public static string WriteReport()
    {
        File.WriteAllText(AppPaths.ScanReport, Run());
        return AppPaths.ScanReport;
    }
}

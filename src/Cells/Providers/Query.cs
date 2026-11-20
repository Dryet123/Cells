using Cells.Hid;

namespace Cells.Providers;

// Helpers for the simple "write a request, read the first matching reply" protocols.
static class Query
{
    public static HidInfo? ByUsage(IEnumerable<HidInfo> group, ushort page, ushort usage) =>
        group.FirstOrDefault(h => h.UsagePage == page && h.Usage == usage && h.OutLen > 0);

    public static HidInfo? ByInterface(IEnumerable<HidInfo> group, int number) =>
        group.Where(h => h.Interface == number && h.OutLen > 0).OrderByDescending(h => h.IsVendor).FirstOrDefault();

    public static HidInfo? FirstVendor(IEnumerable<HidInfo> group, int minOut = 2, int minIn = 2) =>
        group.FirstOrDefault(h => h.IsVendor && h.OutLen >= minOut && h.InLen >= minIn);

    public static byte[]? Once(HidInfo? h, byte[] request, Func<byte[], bool> match, int timeoutMs = 1000)
    {
        if (h == null) return null;
        using var dev = HidHandle.Open(h);
        if (dev == null) return null;
        dev.Drain();
        return dev.Exchange(request, timeoutMs, match);
    }

    public static int Scale(int value, int max) => (int)Math.Round(100.0 * Math.Clamp(value, 0, max) / max);
}

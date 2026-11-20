using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// HyperX (HP 03F0 and the older Kingston 0951 ids). Almost every model has its own request layout.
sealed class HyperX : IProvider
{
    static readonly HashSet<ushort> Report66 = [0x0C9D, 0x05B7, 0x069F, 0x0995];
    static readonly HashSet<ushort> CloudIIIS = [0x06BE, 0x02CC];
    static readonly HashSet<ushort> CloudIIHp = [0x0696, 0x018B, 0x0D93, 0x0C8C];
    static readonly HashSet<ushort> Kingston62 = [0x1718, 0x16EA, 0x16EB];
    static readonly HashSet<ushort> Alpha2 = [0x08BE, 0x0F98, 0x0DA0, 0x0D97];
    static readonly HashSet<ushort> Flight = [0x16C4, 0x1723, 0x1749, 0x0E90];
    static readonly HashSet<ushort> Stinger = [0x0A93, 0x0A8E, 0x170B, 0x1713, 0x173F];
    static readonly (int, int)[] FlightCurve = [(4000, 100), (3900, 80), (3800, 55), (3700, 30), (3640, 10), (3500, 0)];

    public string Name => "HyperX";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Hid.Where(h => h.Vid is 0x03F0 or 0x0951).GroupBy(h => h.Group))
        {
            var list = group.ToList();
            ushort pid = list[0].Pid;
            (int Pct, bool Charging)? result;
            if (Report66.Contains(pid)) result = Cloud66(list.FirstOrDefault(h => h.IsVendor && h.OutLen >= 62 && h.InLen >= 62));
            else if (CloudIIIS.Contains(pid)) result = CloudIIIs(list.FirstOrDefault(h => h.OutLen == 64 && h.InLen == 64));
            else if (CloudIIHp.Contains(pid)) result = CloudII(Query.ByUsage(list, 0xFF90, 0x0303));
            else if (Kingston62.Contains(pid)) result = Kingston(Query.ByUsage(list, 0xFF13, 0x0001));
            else if (pid == 0x098D) result = Alpha(Query.FirstVendor(list));
            else if (Alpha2.Contains(pid)) result = Alpha2Proto(pid == 0x08BE ? Query.ByUsage(list, 0xFF13, 0xFF00) : Query.FirstVendor(list, 2, 8));
            else if (Flight.Contains(pid)) result = CloudFlight(Query.FirstVendor(list));
            else if (Stinger.Contains(pid)) result = StingerCore(list.FirstOrDefault(h => h.UsagePage == 0xFF00 && h.Usage == 0x0001 && h.FeatLen >= 32));
            else continue;

            ctx.Claim(list[0]);
            var name = Curves.Clean(list[0].Product, "HyperX device");
            ctx.Note($"  {list[0]} result={result}");
            ctx.Add(new Reading($"hyperx:{list[0].Vid:x4}:{pid:x4}", name, Kinds.Guess(name, DeviceKind.Headset), result?.Pct ?? -1, result?.Charging ?? false));
        }
    }

    static (int, bool)? Cloud66(HidInfo? h)
    {
        byte[]? Ask(byte cmd) => Query.Once(h, [0x66, cmd], r => r.Length > 4 && r[0] == 0x66 && r[1] == cmd);
        if (Ask(0x82) is not { } link || link[2] == 0) return null;
        var level = Ask(0x89);
        return level != null && level[4] <= 100 ? (level[4], Ask(0x8A)?[2] == 1) : null;
    }

    static (int, bool)? CloudIIIs(HidInfo? h)
    {
        byte[]? Ask(byte cmd) => Query.Once(h, [0x0C, 0x02, 0x03, 0x01, 0x00, cmd], r => r.Length > 6 && r[0] == 0x0C && r[5] == cmd);
        if (Ask(0x02) is not { } link || link[6] != 2) return null;
        var level = Ask(0x06);
        return level != null && level[6] <= 100 ? (level[6], Ask(0x48)?[6] is 1 or 2) : null;
    }

    static (int, bool)? CloudII(HidInfo? h)
    {
        byte[]? Ask(byte cmd) => Query.Once(h, [0x06, 0xFF, 0xBB, cmd], r => r.Length > 7 && r[2] == 0xBB && r[3] == cmd);
        var level = Ask(0x02);
        return level != null && level[7] <= 100 ? (level[7], Ask(0x03)?[4] == 1) : null;
    }

    static (int, bool)? Kingston(HidInfo? h)
    {
        byte[]? Ask(byte cmd) => Query.Once(h, [0x06, 0x00, 0x02, 0x00, 0x9A, 0x00, 0x00, 0x68, 0x4A, 0x8E, 0x0A, 0x00, 0x00, 0x00, 0xBB, cmd, 0x00],
            r => r.Length > 7 && r[0] == 0x0B && Array.IndexOf(r, (byte)0xBB) is var i and >= 0 && i + 1 < r.Length && r[i + 1] == cmd);
        var level = Ask(0x02);
        return level != null && level[7] <= 100 ? (level[7], Ask(0x03)?[4] == 1) : null;
    }

    static (int, bool)? Alpha(HidInfo? h)
    {
        byte[]? Ask(byte cmd) => Query.Once(h, [0x21, 0xBB, cmd], r => r.Length > 3 && r[0] == 0x21 && r[2] == cmd);
        if (Ask(0x03) is not { } link || link[3] == 0x01) return null;
        var level = Ask(0x0B);
        return level != null && level[3] <= 100 ? (level[3], Ask(0x0C)?[3] == 1) : null;
    }

    static (int, bool)? Alpha2Proto(HidInfo? h)
    {
        byte[]? Ask(byte cmd, byte sub) => Query.Once(h, [cmd, sub], r => r.Length > 3 && r[0] == cmd + 1 && r[1] == sub);
        if (Ask(0x52, 0x01) is not { } link || link[2] == 0) return null;
        var level = Ask(0x50, 0x02);
        return level != null && level[2] <= 100 ? (level[2], level[3] is 1 or 2) : null;
    }

    static (int, bool)? CloudFlight(HidInfo? h)
    {
        var r = Query.Once(h, [0x21, 0xFF, 0x05], x => x.Length > 4 && x[0] == 0x21 && x[1] == 0xFF);
        if (r == null) return null;
        int mv = r[3] << 8 | r[4];
        return (Curves.Percent(mv, FlightCurve), mv > 0x100B);
    }

    static (int, bool)? StingerCore(HidInfo? h)
    {
        if (h == null) return null;
        using var dev = HidHandle.Open(h, io: false);
        if (dev == null) return null;
        byte[]? Ask(byte[] request, Func<byte[], bool> fresh)
        {
            if (!dev.SetFeature(request)) return null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Thread.Sleep(20);
                if (dev.GetFeature(0xFF) is { } r && fresh(r)) return r;
            }
            return null;
        }
        var link = Ask([0xFF, 0x07, 0x00, 0xFD, 0x04, 0x00, 0x00, 0x02, 0x82, 0x03, 0x05, 0x29, 0x0F, 0x00, 0x00, 0x03, 0x68, 0x10, 0x00, 0x00], r => r.Length > 11);
        if (link == null || link[11] != 1) return null;
        var r = Ask([0xFF, 0x07, 0x00, 0xFD, 0x04, 0x0C, 0xF1, 0x02, 0x01, 0x04, 0xF0, 0x0C], r => r.Length > 12 && r[1] == 0x12 && r[2] == 0x05);
        if (r == null) return null;
        return (Curves.Percent(r[11] | r[12] << 8, Curves.LiIon), r[9] is 0x05 or 0x06);
    }
}

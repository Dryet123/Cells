using Cells.Core;
using Cells.Hid;

namespace Cells.Providers;

// Razer wireless headsets. Each family has its own receiver protocol; none of them use the 90-byte
// report of Razer mice and keyboards.
sealed class RazerHeadsets : IProvider
{
    public string Name => "Razer headsets";

    public void Poll(PollContext ctx)
    {
        foreach (var group in ctx.Vendor(0x1532).GroupBy(h => h.Group))
        {
            var first = group.First();
            switch (first.Pid)
            {
                case 0x0552:
                    Airoha(ctx, group, "Barracuda X", Airoha552.Instance);
                    break;
                case 0x0550:
                    Airoha(ctx, group, "Barracuda X", Airoha550.Instance);
                    break;
                case 0x0555:
                    Airoha(ctx, group, "BlackShark V2 Pro", Airoha555.Instance);
                    break;
                case 0x0565:
                    Cached(ctx, first, "BlackShark V2 HyperSpeed", () => HyperSpeed(ctx, group));
                    break;
                case 0x0576 or 0x0577 or 0x0579 or 0x057A or 0x0A4E or 0x0A55:
                    Cached(ctx, first, first.Pid is 0x0579 or 0x057A ? "BlackShark V3" : "BlackShark V3 Pro", () => V3(group));
                    break;
                case 0x057C or 0x057D:
                    Cached(ctx, first, "BlackShark V3 X", () => V3X(group));
                    break;
                case 0x0528:
                    V2Pro2020(ctx, group);
                    break;
            }
        }
    }

    // The BlackShark receivers drop the link when polled every minute, so their answers are reused for five minutes.
    readonly Dictionary<ushort, (DateTime At, Reading Reading)> cache = [];

    void Cached(PollContext ctx, HidInfo h, string name, Func<(int Pct, bool Charging)?> read)
    {
        ctx.Claim(h);
        var key = $"razer:{h.Vid:x4}:{h.Pid:x4}";
        if (cache.TryGetValue(h.Pid, out var c) && DateTime.Now - c.At < TimeSpan.FromMinutes(5))
        {
            ctx.Add(c.Reading);
            return;
        }
        var result = read();
        var reading = new Reading(key, name, DeviceKind.Headset, result?.Pct ?? -1, result?.Charging ?? false);
        if (result != null) cache[h.Pid] = (DateTime.Now, reading);
        ctx.Note($"  {h} {result}");
        ctx.Add(reading);
    }

    // 64-byte frames closed by an XOR of bytes 0..61: 02 00 seq 00 00 00 04 00 00 dir cmd ...
    static byte[] XorFrame(byte seq, byte dir, byte cmd)
    {
        var f = new byte[64];
        f[0] = 0x02;
        f[2] = seq;
        f[6] = 0x04;
        f[9] = dir;
        f[10] = cmd;
        for (int i = 0; i < 62; i++) f[62] ^= f[i];
        return f;
    }

    int sequence;

    (int, bool)? HyperSpeed(PollContext ctx, IEnumerable<HidInfo> group)
    {
        var h = group.FirstOrDefault(x => x.UsagePage == 0xFF14 && x.Usage == 0x01 && x.OutLen >= 64)
                ?? group.FirstOrDefault(x => x.UsagePage == 0xFF00 && x.OutLen >= 64 && x.InLen >= 64);
        if (h == null) return null;
        ctx.Claim(h);
        using var dev = HidHandle.Open(h);
        if (dev == null) return null;
        dev.Drain();
        int? Get(byte cmd)
        {
            byte seq = (byte)(0x60 | (++sequence & 0x1F));
            var r = dev.Exchange(XorFrame(seq, 0x80, cmd), 650, r => r.Length > 13 && r[0] == 0x02 && r[2] == seq && r[10] == cmd);
            return r != null && r[12] >= 1 ? r[13] : null;
        }
        return Get(0x21) is { } pct && pct <= 100 ? (pct, Get(0x2A) > 0) : null;
    }

    static (int, bool)? V3(IEnumerable<HidInfo> group)
    {
        var h = group.FirstOrDefault(x => x.Interface == 5 && x.OutLen >= 64 && x.InLen >= 15);
        if (h == null) return null;
        using var dev = HidHandle.Open(h);
        if (dev == null) return null;
        dev.Write(XorFrame(0x60, 0x00, 0x02));
        dev.Write(XorFrame(0x60, 0x00, 0x2A));
        dev.Write(XorFrame(0x60, 0x80, 0x2A));
        Thread.Sleep(250);
        dev.Drain();
        var r = dev.Exchange(XorFrame(0x60, 0x80, 0x21), 600, r => r.Length > 14 && r[0] == 0x02 && r[2] == 0x60 && r[10] == 0x21);
        return r != null && r[13] <= 100 ? (r[13], r[14] != 0) : null;
    }

    // Feature report 07 in the mouse/keyboard command layout, closed by an XOR of bytes 3..60.
    static (int, bool)? V3X(IEnumerable<HidInfo> group)
    {
        var h = group.FirstOrDefault(x => x.Interface == 0 && x.FeatLen >= 64);
        if (h == null) return null;
        using var dev = HidHandle.Open(h, io: false);
        if (dev == null) return null;
        byte tid = 0x40;
        int? Get(byte cmd)
        {
            var f = new byte[h.FeatLen];
            f[0] = 0x07;
            f[2] = ++tid;
            f[6] = 0x02;
            f[7] = 0x07;
            f[8] = cmd;
            for (int i = 3; i <= 60; i++) f[62] ^= f[i];
            if (!dev.SetFeature(f)) return null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Thread.Sleep(60);
                var r = dev.GetFeature(0x07);
                if (r == null || r[1] == 0x01) continue;
                return r[1] == 0x02 && r[2] == tid && r[7] == 0x07 && r[8] == cmd ? r[10] : null;
            }
            return null;
        }
        return Get(0x80) is { } pct && pct <= 100 ? (pct, Get(0x84) == 1) : null;
    }

    static void V2Pro2020(PollContext ctx, IEnumerable<HidInfo> group)
    {
        var h = group.FirstOrDefault(x => x.Interface == 3 && x.UsagePage == 0xFF00 && x.FeatLen >= 64);
        if (h == null) return;
        ctx.Claim(h);
        using var dev = HidHandle.Open(h, io: false);
        if (dev == null || !dev.SetFeature(0xFF, 0x0A, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05)) return;
        var key = $"razer:{h.Vid:x4}:{h.Pid:x4}";
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Thread.Sleep(100);
            var r = dev.GetFeature(0xFF);
            if (r is not { Length: > 14 } || r[1] != 0x0F || r[4] != 0x12) continue;
            bool charging = r[11] == 0x09;
            int pct = charging ? Curves.Percent(r[12] << 8 | r[13], Curves.LiIon) : r[14];
            ctx.Add(new Reading(key, "BlackShark V2 Pro", DeviceKind.Headset, pct, charging));
            return;
        }
        ctx.Add(new Reading(key, "BlackShark V2 Pro", DeviceKind.Headset, -1));
    }

    // Barracuda X and BlackShark V2 Pro (2023) talk to an Airoha chip through "PA" frames:
    // 50 41 group seq body, answered by 50 49 frames with a 10-byte header.
    static void Airoha(PollContext ctx, IEnumerable<HidInfo> group, string name, AirohaLink link)
    {
        var h = group.FirstOrDefault(link.Accepts);
        if (h == null) return;
        ctx.Claim(h);
        using var dev = HidHandle.Open(h);
        if (dev == null) return;
        var key = $"razer:{h.Vid:x4}:{h.Pid:x4}";
        dev.Drain();
        link.Start(dev);
        byte seq = 0;
        byte[]? Ask(byte frameGroup, byte[] body, Func<byte[], bool> match, int timeout)
        {
            seq = (byte)(seq % 127 + 1);
            byte[] payload = [0x50, 0x41, frameGroup, seq, .. body];
            if (!link.Send(dev, payload)) return null;
            return Frame(dev.ReadUntil(timeout, r =>
            {
                bool hit = link.Stream(r) is { } s && Find(s) is { } f && match(f);
                if (!hit) ctx.Note($"    skipped {PollContext.Hex(r, 20)}");
                return hit;
            }), link);
        }

        try
        {
            var remote = Ask(0x0E, [0x02, 0xE1, 0x01], f => f.Length > 12 && f[10] == 0x0E && f[11] == (0x80 | seq), 900);
            var battery = Ask(0x08, [0x03, 0x21, 0x00, 0x00], f => f.Length > 13 && f[2] == 0x08 && f[10] == 0x21 && f[11] == 0x01, 1000);
            var cable = battery == null ? null : Ask(0x08, [0x03, 0x2A, 0x00, 0x00], f => f.Length > 13 && f[2] == 0x08 && f[10] == 0x2A && f[11] == 0x01, 800);
            ctx.Note($"  {h} remote={PollContext.Hex(remote, 16)} battery={PollContext.Hex(battery, 16)} cable={PollContext.Hex(cable, 16)}");
            bool ok = battery != null && battery[13] <= 100;
            ctx.Add(new Reading(key, name, DeviceKind.Headset, ok ? battery![13] : -1, cable?[13] == 1));
        }
        finally
        {
            Ask(0x0E, [0x02, 0xE1, 0x00], f => f.Length > 11 && f[10] == 0x0E, 300);
        }
    }

    static byte[]? Frame(byte[]? report, AirohaLink link) => report != null && link.Stream(report) is { } s ? Find(s) : null;

    static byte[]? Find(byte[] stream)
    {
        for (int i = 0; i + 10 <= stream.Length; i++)
            if (stream[i] == 0x50 && stream[i + 1] == 0x49) return stream[i..];
        return null;
    }

    abstract class AirohaLink
    {
        public abstract bool Accepts(HidInfo h);
        public virtual void Start(HidHandle dev) { }
        public abstract bool Send(HidHandle dev, byte[] payload);
        public abstract byte[]? Stream(byte[] report);
    }

    // 1532:0552 receiver: Macronix bridge, report 01, "01 80 len" framing after a 01 40 handshake.
    sealed class Airoha552 : AirohaLink
    {
        public static readonly Airoha552 Instance = new();
        public override bool Accepts(HidInfo h) => h.UsagePage == 0xFF00 && h.InLen >= 64 && h.OutLen >= 64;

        public override void Start(HidHandle dev)
        {
            if (dev.Write(0x01, 0x40)) dev.ReadUntil(500, r => r.Length > 1 && r[0] == 0x01 && r[1] == 0x40);
        }

        public override bool Send(HidHandle dev, byte[] payload) => dev.Write([0x01, 0x80, (byte)payload.Length, .. payload]);

        public override byte[]? Stream(byte[] r) =>
            r.Length > 3 && r[0] == 0x01 && r[1] == 0x80 ? r[3..Math.Min(r.Length, 3 + r[2])] : null;
    }

    // 1532:0550 receiver: report 02 with a CRC-16/XMODEM, every write acknowledged with 'O' (ok) or 'F' (busy).
    sealed class Airoha550 : AirohaLink
    {
        public static readonly Airoha550 Instance = new();
        public override bool Accepts(HidInfo h) => h.UsagePage == 0xFF00 && h.Usage == 0x0001 && h.InLen >= 64 && h.OutLen >= 64;

        public override bool Send(HidHandle dev, byte[] payload)
        {
            var report = new byte[64];
            report[0] = 0x02;
            report[1] = 0x80;
            report[2] = (byte)payload.Length;
            payload.CopyTo(report, 5);
            ushort crc = Crc16(report);
            report[3] = (byte)crc;
            report[4] = (byte)(crc >> 8);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!dev.Write(report)) return false;
                var ack = dev.GetInput(0x02);
                if (ack is { Length: > 1 } && ack[1] == 'O') return true;
                Thread.Sleep(50);
            }
            return false;
        }

        public override byte[]? Stream(byte[] r) => r.Length > 2 && r[0] == 0x02 ? r[2..Math.Min(r.Length, 2 + r[1])] : null;

        static ushort Crc16(byte[] data)
        {
            ushort crc = 0;
            foreach (byte b in data)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
            return crc;
        }
    }

    // 1532:0555 (BlackShark V2 Pro 2023): same frames as 0550 without the CRC and the acknowledgement.
    sealed class Airoha555 : AirohaLink
    {
        public static readonly Airoha555 Instance = new();
        public override bool Accepts(HidInfo h) => h.UsagePage == 0xFF00 && h.OutLen >= 64 && h.InLen >= 16;
        public override bool Send(HidHandle dev, byte[] payload) => dev.Write([0x02, 0x80, (byte)payload.Length, 0x00, 0x00, .. payload]);
        public override byte[]? Stream(byte[] r) => r.Length > 2 && r[0] == 0x02 ? r[2..Math.Min(r.Length, 2 + r[1])] : null;
    }
}

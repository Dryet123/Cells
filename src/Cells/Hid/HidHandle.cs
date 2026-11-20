using Microsoft.Win32.SafeHandles;

namespace Cells.Hid;

// Open HID collection. Reads follow hidapi: a leading 0x00 report id (unnumbered reports) is stripped,
// so offsets in protocol notes written against hidapi can be used as they are.
public sealed class HidHandle : IDisposable
{
    public HidInfo Info { get; }
    readonly SafeFileHandle handle;
    readonly FileStream? stream;
    readonly byte[] buffer;
    readonly CancellationTokenSource cts = new();
    Task<int>? pending;
    bool dead;

    HidHandle(HidInfo info, SafeFileHandle handle, FileAccess? access)
    {
        Info = info;
        this.handle = handle;
        if (access is { } mode) stream = new FileStream(handle, mode, 0, true);
        buffer = new byte[Math.Max(info.InLen, 1)];
    }

    public static HidHandle? Open(HidInfo info, bool io = true)
    {
        if (io)
        {
            foreach (var (access, mode) in new[] { (HidNative.GenericRead | HidNative.GenericWrite, FileAccess.ReadWrite), (HidNative.GenericRead, FileAccess.Read) })
            {
                var h = HidNative.Open(info.Path, access, HidNative.FlagOverlapped);
                if (!h.IsInvalid) return Create(info, h, mode);
                h.Dispose();
            }
        }
        var plain = HidNative.Open(info.Path, 0, 0);
        if (!plain.IsInvalid) return Create(info, plain, null);
        plain.Dispose();
        return null;
    }

    static HidHandle? Create(HidInfo info, SafeFileHandle h, FileAccess? access)
    {
        try { return new HidHandle(info, h, access); }
        catch (Exception)
        {
            h.Dispose();
            return null;
        }
    }

    static byte[] Pad(byte[] data, int length)
    {
        if (data.Length == length) return data;
        var padded = new byte[length];
        Array.Copy(data, padded, Math.Min(data.Length, length));
        return padded;
    }

    public bool Write(params byte[] data)
    {
        if (Info.OutLen == 0) return false;
        var report = Pad(data, Info.OutLen);
        if (stream is { CanWrite: true })
        {
            try
            {
                using var timeout = new CancellationTokenSource(1000);
                stream.WriteAsync(report, timeout.Token).AsTask().Wait();
                return true;
            }
            catch (Exception) { }
        }
        return HidNative.HidD_SetOutputReport(handle, report, report.Length);
    }

    public bool SetFeature(params byte[] data)
    {
        if (Info.FeatLen == 0) return false;
        var report = Pad(data, Info.FeatLen);
        return HidNative.HidD_SetFeature(handle, report, report.Length);
    }

    public byte[]? GetFeature(byte reportId)
    {
        if (Info.FeatLen == 0) return null;
        var report = new byte[Info.FeatLen];
        report[0] = reportId;
        return HidNative.HidD_GetFeature(handle, report, report.Length) ? report : null;
    }

    public byte[]? GetInput(byte reportId)
    {
        if (Info.InLen == 0) return null;
        var report = new byte[Info.InLen];
        report[0] = reportId;
        return HidNative.HidD_GetInputReport(handle, report, report.Length) ? report : null;
    }

    public T WithPreparsed<T>(Func<IntPtr, T> use, T fallback)
    {
        if (!HidNative.HidD_GetPreparsedData(handle, out var data)) return fallback;
        try { return use(data); }
        finally { HidNative.HidD_FreePreparsedData(data); }
    }

    public byte[]? Read(int timeoutMs) => ReadAny([this], timeoutMs, (_, _) => true)?.Data;

    public byte[]? ReadUntil(int timeoutMs, Func<byte[], bool> match) => ReadAny([this], timeoutMs, (_, r) => match(r))?.Data;

    public byte[]? Exchange(byte[] request, int timeoutMs, Func<byte[], bool> match) => Write(request) ? ReadUntil(timeoutMs, match) : null;

    // Skips reports that were already queued, so a reply can't be confused with older traffic.
    public void Drain()
    {
        for (int i = 0; i < 64 && Read(12) != null; i++) { }
    }

    Task<int>? StartRead()
    {
        if (stream is not { CanRead: true } || dead || Info.InLen == 0) return null;
        try { return pending ??= stream.ReadAsync(buffer, 0, buffer.Length, cts.Token); }
        catch (Exception)
        {
            dead = true;
            return null;
        }
    }

    byte[]? Take()
    {
        var task = pending;
        pending = null;
        if (task is not { IsCompletedSuccessfully: true })
        {
            dead = true;
            return null;
        }
        int n = task.Result;
        if (n <= 0) return null;
        return buffer[0] == 0 ? buffer[1..n] : buffer[..n];
    }

    // A pending read survives a timeout and is picked up by the next call, so no report is lost.
    public static (HidHandle From, byte[] Data)? ReadAny(IReadOnlyList<HidHandle> handles, int timeoutMs, Func<HidHandle, byte[], bool> match)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var tasks = new List<Task>();
            var owners = new List<HidHandle>();
            foreach (var h in handles)
            {
                if (h.StartRead() is not { } t) continue;
                tasks.Add(t);
                owners.Add(h);
            }
            int left = (int)(deadline - Environment.TickCount64);
            if (tasks.Count == 0 || left <= 0) return null;
            int i = Task.WaitAny([.. tasks], left);
            if (i < 0) return null;
            if (owners[i].Take() is { } data && match(owners[i], data)) return (owners[i], data);
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        try { pending?.Wait(200); }
        catch (Exception) { }
        stream?.Dispose();
        handle.Dispose();
        cts.Dispose();
    }
}

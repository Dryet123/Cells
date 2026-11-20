using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Cells.Core;

namespace Cells.Hid;

// One HID top-level collection. A physical device usually exposes several; Group ties them together.
public sealed class HidInfo
{
    public required string Path { get; init; }
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InLen { get; init; }
    public int OutLen { get; init; }
    public int FeatLen { get; init; }
    public string Product { get; init; } = "";
    public string Serial { get; init; } = "";
    public int Interface { get; init; } = -1;
    public string Group { get; init; } = "";

    public bool Bluetooth => Path.Contains("{00001124-", StringComparison.OrdinalIgnoreCase)
                             || Path.Contains("{00001812-", StringComparison.OrdinalIgnoreCase);

    public bool IsVendor => UsagePage >= 0xFF00;

    public override string ToString() =>
        $"{Vid:X4}:{Pid:X4} mi{Interface:00} {UsagePage:X4}:{Usage:X4} in{InLen} out{OutLen} feat{FeatLen} \"{Product}\"{(Bluetooth ? " bt" : "")}";
}

public static partial class HidBus
{
    static readonly Dictionary<string, HidInfo?> cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Guid RootContainer = new("00000000-0000-0000-ffff-ffffffffffff");
    static Guid hidGuid;

    public static List<HidInfo> List()
    {
        if (hidGuid == Guid.Empty) HidNative.HidD_GetHidGuid(out hidGuid);
        var paths = DevNode.Interfaces(hidGuid);
        var list = new List<HidInfo>();
        lock (cache)
        {
            foreach (var path in paths)
            {
                if (!cache.TryGetValue(path, out var info)) cache[path] = info = Describe(path);
                if (info != null) list.Add(info);
            }
            foreach (var gone in cache.Keys.Except(paths, StringComparer.OrdinalIgnoreCase).ToList()) cache.Remove(gone);
        }
        return list;
    }

    static HidInfo? Describe(string path)
    {
        try
        {
            using var h = HidNative.Open(path, 0, 0);
            if (h.IsInvalid) return null;
            var attr = new HidNative.Attributes { Size = Marshal.SizeOf<HidNative.Attributes>() };
            if (!HidNative.HidD_GetAttributes(h, ref attr)) return null;
            HidNative.Caps caps = default;
            if (HidNative.HidD_GetPreparsedData(h, out var data))
            {
                try { HidNative.HidP_GetCaps(data, out caps); }
                finally { HidNative.HidD_FreePreparsedData(data); }
            }

            var match = InterfaceRx().Match(path);
            var node = DevNode.InterfaceInstance(path) is { } id ? DevNode.Locate(id) : 0;
            var container = DevNode.Guid(node, DevNode.ContainerId);
            return new HidInfo
            {
                Path = path,
                Vid = attr.VendorId,
                Pid = attr.ProductId,
                UsagePage = caps.UsagePage,
                Usage = caps.Usage,
                InLen = caps.InputReportByteLength,
                OutLen = caps.OutputReportByteLength,
                FeatLen = caps.FeatureReportByteLength,
                Product = Text(h, HidNative.HidD_GetProductString),
                Serial = Text(h, HidNative.HidD_GetSerialNumberString),
                Interface = match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : -1,
                Group = container is { } g && g != RootContainer ? g.ToString() : StemRx().Replace(path.ToLowerInvariant(), ""),
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    static string Text(Microsoft.Win32.SafeHandles.SafeFileHandle h, Func<Microsoft.Win32.SafeHandles.SafeFileHandle, byte[], int, bool> read)
    {
        var buffer = new byte[512];
        if (!read(h, buffer, buffer.Length)) return "";
        var s = Encoding.Unicode.GetString(buffer);
        int end = s.IndexOf('\0');
        return (end >= 0 ? s[..end] : s).Trim();
    }

    [GeneratedRegex("&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfaceRx();

    [GeneratedRegex("&col[0-9a-f]+|&[0-9a-f]{4}(?=#\\{)")]
    private static partial Regex StemRx();
}

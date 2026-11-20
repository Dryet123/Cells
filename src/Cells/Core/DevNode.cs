using System.Runtime.InteropServices;
using System.Text;

namespace Cells.Core;

// Thin wrapper over cfgmgr32 for reading device properties (the same data Device Manager shows).
static class DevNode
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Key(Guid fmt, uint pid)
    {
        public Guid Fmt = fmt;
        public uint Pid = pid;
    }

    public static readonly Key FriendlyName = new(new("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    public static readonly Key Description = new(new("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    public static readonly Key HardwareIds = new(new("a45c254e-df1c-4efd-8020-67d146a850e0"), 3);
    public static readonly Key ContainerId = new(new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    public static readonly Key InstanceId = new(new("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
    public static readonly Key BusDescription = new(new("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);

    const int Ok = 0, BufferSmall = 0x1A;
    const uint TypeByte = 0x03, TypeUInt32 = 0x07, TypeGuid = 0x0D, TypeBool = 0x11, TypeString = 0x12, TypeStringList = 0x2012;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_Interface_List_SizeW(out int length, ref Guid cls, string? id, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_Interface_ListW(ref Guid cls, string? id, char[] buffer, int length, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_Interface_PropertyW(string path, ref Key key, out uint type, byte[]? buffer, ref int size, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List_SizeW(out int length, string? filter, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_ListW(string? filter, char[] buffer, int length, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint node, string id, int flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_PropertyW(uint node, ref Key key, out uint type, byte[]? buffer, ref int size, int flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out uint parent, uint node, int flags);

    public static string[] Interfaces(Guid cls)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_Interface_List_SizeW(out int length, ref cls, null, 0) != Ok) return [];
            var buffer = new char[length];
            int cr = CM_Get_Device_Interface_ListW(ref cls, null, buffer, length, 0);
            if (cr == BufferSmall) continue;
            return cr == Ok ? SplitMulti(buffer) : [];
        }
        return [];
    }

    public static string[] PresentDevices()
    {
        const int FilterPresent = 0x100;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out int length, null, FilterPresent) != Ok) return [];
            var buffer = new char[length];
            int cr = CM_Get_Device_ID_ListW(null, buffer, length, FilterPresent);
            if (cr == BufferSmall) continue;
            return cr == Ok ? SplitMulti(buffer) : [];
        }
        return [];
    }

    public static uint Locate(string instanceId) => CM_Locate_DevNodeW(out uint node, instanceId, 0) == Ok ? node : 0;

    public static uint Parent(uint node) => CM_Get_Parent(out uint parent, node, 0) == Ok ? parent : 0;

    public static string? InterfaceInstance(string path)
    {
        var key = InstanceId;
        int size = 0;
        CM_Get_Device_Interface_PropertyW(path, ref key, out _, null, ref size, 0);
        if (size <= 0) return null;
        var buffer = new byte[size];
        return CM_Get_Device_Interface_PropertyW(path, ref key, out uint type, buffer, ref size, 0) == Ok && type == TypeString
            ? Text(buffer)
            : null;
    }

    static byte[]? Raw(uint node, Key key, out uint type)
    {
        type = 0;
        if (node == 0) return null;
        int size = 0;
        CM_Get_DevNode_PropertyW(node, ref key, out _, null, ref size, 0);
        if (size <= 0) return null;
        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(node, ref key, out type, buffer, ref size, 0) == Ok ? buffer : null;
    }

    public static string? String(uint node, Key key) => Raw(node, key, out uint type) is { } b && type == TypeString ? Text(b) : null;
    public static string[] Strings(uint node, Key key) => Raw(node, key, out uint type) is { } b && type == TypeStringList ? SplitMulti(Encoding.Unicode.GetString(b).ToCharArray()) : [];
    public static Guid? Guid(uint node, Key key) => Raw(node, key, out uint type) is { Length: 16 } b && type == TypeGuid ? new Guid(b) : null;
    public static byte? Byte(uint node, Key key) => Raw(node, key, out uint type) is { Length: >= 1 } b && type == TypeByte ? b[0] : null;
    public static bool? Bool(uint node, Key key) => Raw(node, key, out uint type) is { Length: >= 1 } b && type == TypeBool ? b[0] != 0 : null;
    public static uint? UInt(uint node, Key key) => Raw(node, key, out uint type) is { Length: >= 4 } b && type == TypeUInt32 ? BitConverter.ToUInt32(b) : null;

    static string Text(byte[] b) => Encoding.Unicode.GetString(b).TrimEnd('\0');

    static string[] SplitMulti(char[] buffer) => new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
}

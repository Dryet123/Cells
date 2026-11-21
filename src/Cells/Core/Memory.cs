using System.Runtime.InteropServices;

namespace Cells.Core;

// The app sleeps between polls, so after each one it gives its pages back to Windows.
// They come back on demand at the next poll; Task Manager meanwhile shows only what is in use.
public static class Memory
{
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    public static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(new IntPtr(-1), -1, -1);
    }
}

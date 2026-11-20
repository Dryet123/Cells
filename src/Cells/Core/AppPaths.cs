namespace Cells.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Init();
    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Devices => Path.Combine(DataDir, "devices.json");
    public static string ScanReport => Path.Combine(DataDir, "scan.txt");
    public static bool IsCustom => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CELLS_DATA_DIR"));

    // One instance per data folder, so a test copy with CELLS_DATA_DIR can run next to the real one.
    public static string SyncName(string kind) => $@"Local\Cells.{StableHash(DataDir.ToLowerInvariant()):x8}.{kind}";

    // string.GetHashCode is randomized per process, so names shared between processes need their own hash.
    public static uint StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    public static void Signal(string kind)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(SyncName(kind));
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    static string Init()
    {
        var dir = Environment.GetEnvironmentVariable("CELLS_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cells");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class Log
{
    static readonly object gate = new();
    static readonly HashSet<string> seen = [];

    // Polling repeats every minute, so the same failure is written once per run.
    public static void Error(Exception ex)
    {
        try
        {
            lock (gate)
            {
                if (!seen.Add(ex.GetType().Name + ex.Message + ex.TargetSite)) return;
                File.AppendAllText(Path.Combine(AppPaths.DataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}\n\n");
            }
        }
        catch { }
    }
}

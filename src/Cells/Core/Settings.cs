using System.Text.Json;

namespace Cells.Core;

public sealed class Settings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public bool ShowWidget { get; set; } = true;
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }
    public bool WidgetLocked { get; set; }
    public double WidgetScale { get; set; } = 1.0;
    public string Theme { get; set; } = "light";
    public List<string> CardHidden { get; set; } = [];
    public bool TrayShowsLevel { get; set; }
    public string? TrayDevice { get; set; }
    public List<string> Hidden { get; set; } = [];
    public Dictionary<string, string> Names { get; set; } = [];
    public int LowAlert { get; set; } = 20;
    public bool FullAlert { get; set; }
    public int PollSeconds { get; set; } = 60;
    public bool AutostartRegistered { get; set; }
    public bool WelcomeShown { get; set; }

    public string NameOf(Device d) => Names.TryGetValue(d.Key, out var custom) && custom.Length > 0 ? custom : d.Name;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.Settings)) ?? new();
        }
        catch (Exception ex) { Log.Error(ex); }
        return new();
    }

    public void Save()
    {
        try
        {
            var tmp = AppPaths.Settings + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, AppPaths.Settings, true);
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}

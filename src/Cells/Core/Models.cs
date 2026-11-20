namespace Cells.Core;

public enum DeviceKind { Other, Mouse, Keyboard, Headset, Earbuds, Gamepad, Computer, Phone, Speaker, Pen }

// What a provider saw during one poll. Level -1 means the device is known (its receiver is plugged in)
// but didn't answer, e.g. a headset that is switched off.
public sealed record Reading(string Key, string Name, DeviceKind Kind, int Level, bool Charging = false, string? Detail = null)
{
    public bool Reachable => Level >= 0;
}

// A device as the app remembers it between polls and restarts.
public sealed class Device
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceKind Kind { get; set; }
    public int Level { get; set; } = -1;
    public bool Charging { get; set; }
    public string? Detail { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Online { get; set; }

    public bool HasLevel => Level >= 0;
}

public static class Kinds
{
    static readonly (DeviceKind Kind, string[] Words)[] Hints =
    [
        (DeviceKind.Earbuds, ["airpods", "buds", "earbud", "freebuds", "wf-", "earfun", "tws", "beats fit", "powerbeats"]),
        (DeviceKind.Headset, ["headset", "headphone", "kraken", "blackshark", "barracuda", "nari", "arctis", "cloud", "void", "virtuoso",
            "hs80", "hs70", "astro", "g733", "g935", "g933", "g633", "g533", "g535", "g435", "maxwell", "inzone", "pulse 3d", "pulse elite", "wh-", "quietcomfort", "jabra", "evolve"]),
        (DeviceKind.Gamepad, ["controller", "gamepad", "dualsense", "dualshock", "xbox", "joy-con", "wolverine", "8bitdo", "pro controller"]),
        (DeviceKind.Keyboard, ["keyboard", "keys", "blackwidow", "huntsman", "ornata", "deathstalker", "pro type", "joro", "k70", "k57", "k100", "apex", "g915", "g715", "craft"]),
        (DeviceKind.Mouse, ["mouse", "viper", "deathadder", "basilisk", "naga", "orochi", "cobra", "mamba", "lancehead", "g pro", "g305", "g502", "g703", "g903", "mx master",
            "mx anywhere", "lift", "aerox", "rival", "prime", "model o", "model d", "pulsefire", "sabre", "dark core", "harpoon", "ironclaw", "m65", "trackball"]),
        (DeviceKind.Pen, ["pen", "stylus"]),
        (DeviceKind.Speaker, ["speaker", "soundlink", "jbl", "boom"]),
        (DeviceKind.Phone, ["phone", "iphone", "galaxy", "pixel"]),
    ];

    public static DeviceKind Guess(string name, DeviceKind fallback = DeviceKind.Other)
    {
        var lower = name.ToLowerInvariant();
        foreach (var (kind, words) in Hints)
            if (words.Any(lower.Contains)) return kind;
        return fallback;
    }
}

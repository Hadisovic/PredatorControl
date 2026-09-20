using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace PredatorControlApp;

public class RgbProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("zones")]
    public string[] Zones { get; set; } = new string[4];

    [JsonPropertyName("brightness")]
    public int Brightness { get; set; } = 100;
}

public static class RgbProfileManager
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PredatorControl",
        "rgb_profiles.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly List<RgbProfile> DefaultProfiles = new()
    {
        new RgbProfile { Name = "Teal (Default)", Zones = ["#00E6B4", "#00E6B4", "#00E6B4", "#00E6B4"], Brightness = 100 },
        new RgbProfile { Name = "Cyberpunk", Zones = ["#00E5FF", "#D500F9", "#FF1744", "#FFEA00"], Brightness = 100 },
        new RgbProfile { Name = "Sunset Glow", Zones = ["#FF3D00", "#FF9100", "#FFD600", "#DD2C00"], Brightness = 100 },
        new RgbProfile { Name = "Ocean Breeze", Zones = ["#00B0FF", "#00E5FF", "#1DE9B6", "#00BFA5"], Brightness = 100 },
        new RgbProfile { Name = "Crimson Edge", Zones = ["#D50000", "#FF1744", "#FF5252", "#B71C1C"], Brightness = 100 }
    };

    public static List<RgbProfile> LoadProfiles()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<RgbProfile>>(json, JsonOptions);
                if (list != null && list.Count > 0) return list;
            }
        }
        catch { }

        SaveProfiles(DefaultProfiles);
        return new List<RgbProfile>(DefaultProfiles);
    }

    public static void SaveProfiles(List<RgbProfile> profiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(profiles, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }

    public static void AddOrUpdateProfile(string name, Color[] zones, int brightness = 100)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var profiles = LoadProfiles();
        var existing = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var hexZones = ColorsToHex(zones);

        if (existing != null)
        {
            existing.Zones = hexZones;
            existing.Brightness = brightness;
        }
        else
        {
            profiles.Add(new RgbProfile { Name = name.Trim(), Zones = hexZones, Brightness = brightness });
        }

        SaveProfiles(profiles);
        SetActiveProfile(name.Trim());
    }

    public static void DeleteProfile(string name)
    {
        var profiles = LoadProfiles();
        profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profiles.Count == 0) profiles.AddRange(DefaultProfiles);
        SaveProfiles(profiles);
    }

    public static string GetActiveProfile()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
            if (key?.GetValue("ActiveRgbProfile") is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        catch { }
        return "Teal (Default)";
    }

    public static void SetActiveProfile(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
            key.SetValue("ActiveRgbProfile", name);
        }
        catch { }
    }

    public static Color[] ParseZoneColors(string[] zones)
    {
        var colors = new Color[4];
        for (int i = 0; i < 4; i++)
        {
            if (zones != null && i < zones.Length && !string.IsNullOrWhiteSpace(zones[i]))
            {
                try { colors[i] = ColorTranslator.FromHtml(zones[i]); }
                catch { colors[i] = Color.FromArgb(0x00, 0xE6, 0xB4); }
            }
            else
            {
                colors[i] = Color.FromArgb(0x00, 0xE6, 0xB4);
            }
        }
        return colors;
    }

    public static string[] ColorsToHex(Color[] colors)
    {
        var hex = new string[4];
        for (int i = 0; i < 4; i++)
        {
            if (colors != null && i < colors.Length)
                hex[i] = $"#{colors[i].R:X2}{colors[i].G:X2}{colors[i].B:X2}";
            else
                hex[i] = "#00E6B4";
        }
        return hex;
    }
}

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

    private static readonly object FileLock = new();

    private static readonly RgbProfile[] DefaultProfiles =
    {
        new RgbProfile { Name = "Teal (Default)", Zones = ["#00E6B4", "#00E6B4", "#00E6B4", "#00E6B4"], Brightness = 100 },
        new RgbProfile { Name = "Cyberpunk", Zones = ["#00E5FF", "#D500F9", "#FF1744", "#FFEA00"], Brightness = 100 },
        new RgbProfile { Name = "Sunset Glow", Zones = ["#FF3D00", "#FF9100", "#FFD600", "#DD2C00"], Brightness = 100 },
        new RgbProfile { Name = "Ocean Breeze", Zones = ["#00B0FF", "#00E5FF", "#1DE9B6", "#00BFA5"], Brightness = 100 },
        new RgbProfile { Name = "Crimson Edge", Zones = ["#D50000", "#FF1744", "#FF5252", "#B71C1C"], Brightness = 100 }
    };

    private enum LoadStatus { Ok, Missing, Corrupt, Unreadable }

    /// <summary>Returns fresh copies (including zone arrays) so callers can never mutate the built-in defaults.</summary>
    private static List<RgbProfile> CloneDefaults() => DefaultProfiles.Select(Clone).ToList();

    private static RgbProfile Clone(RgbProfile p) => new()
    {
        Name = p.Name,
        Zones = (string[])p.Zones.Clone(),
        Brightness = p.Brightness
    };

    /// <summary>Normalizes a loaded profile; returns null if it is unusable.</summary>
    private static RgbProfile? Validate(RgbProfile? p)
    {
        if (p == null || string.IsNullOrWhiteSpace(p.Name)) return null;

        var zones = new string[4];
        for (int i = 0; i < 4; i++)
        {
            string? z = p.Zones != null && i < p.Zones.Length ? p.Zones[i] : null;
            zones[i] = IsValidHex(z) ? z!.Trim() : "#00E6B4";
        }

        return new RgbProfile
        {
            Name = p.Name.Trim(),
            Zones = zones,
            Brightness = Math.Clamp(p.Brightness, 0, 100)
        };
    }

    private static bool IsValidHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Length != 7 || s[0] != '#') return false;
        for (int i = 1; i < 7; i++)
            if (!Uri.IsHexDigit(s[i])) return false;
        return true;
    }

    private static LoadStatus TryLoad(out List<RgbProfile> profiles)
    {
        profiles = new List<RgbProfile>();
        string json;
        try
        {
            if (!File.Exists(FilePath)) return LoadStatus.Missing;
            json = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporarily locked / inaccessible: the contents may be perfectly fine, so never overwrite.
            return LoadStatus.Unreadable;
        }
        catch { return LoadStatus.Unreadable; }

        try
        {
            var raw = JsonSerializer.Deserialize<List<RgbProfile>>(json, JsonOptions);
            if (raw == null) return LoadStatus.Corrupt;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in raw)
            {
                var v = Validate(item);
                if (v != null && seen.Add(v.Name)) profiles.Add(v);
            }
            // A valid-but-empty list is treated as "nothing usable", not as damage worth preserving.
            return profiles.Count > 0 ? LoadStatus.Ok : (raw.Count == 0 ? LoadStatus.Missing : LoadStatus.Corrupt);
        }
        catch (JsonException)
        {
            return LoadStatus.Corrupt;
        }
        catch
        {
            return LoadStatus.Corrupt;
        }
    }

    /// <summary>Moves a damaged profile file aside so the user's data is not destroyed.</summary>
    private static void PreserveDamagedFile()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            string backup = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(FilePath, backup, overwrite: true);
        }
        catch { }
    }

    public static List<RgbProfile> LoadProfiles()
    {
        lock (FileLock)
        {
            var status = TryLoad(out var list);
            switch (status)
            {
                case LoadStatus.Ok:
                    return list;

                case LoadStatus.Unreadable:
                    // Don't touch the file; serve defaults for this call only.
                    return CloneDefaults();

                case LoadStatus.Corrupt:
                    PreserveDamagedFile();
                    break;
            }

            var defaults = CloneDefaults();
            WriteProfiles(defaults);
            return defaults;
        }
    }

    public static void SaveProfiles(List<RgbProfile> profiles)
    {
        lock (FileLock) WriteProfiles(profiles);
    }

    private static void WriteProfiles(List<RgbProfile> profiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(profiles, JsonOptions);

            // Write to a temp file and swap it in, so a crash mid-write can't truncate the real file.
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, null);
            else
                File.Move(tmp, FilePath);
        }
        catch { }
    }

    public static void AddOrUpdateProfile(string name, Color[] zones, int brightness = 100)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        lock (FileLock)
        {
            // If the existing file can't be read right now, don't overwrite it with defaults + one profile.
            var status = TryLoad(out var profiles);
            if (status == LoadStatus.Unreadable) return;
            if (status == LoadStatus.Corrupt) PreserveDamagedFile();
            if (status != LoadStatus.Ok) profiles = CloneDefaults();

            var existing = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            var hexZones = ColorsToHex(zones);

            if (existing != null)
            {
                existing.Zones = hexZones;
                existing.Brightness = brightness;
            }
            else
            {
                profiles.Add(new RgbProfile { Name = name, Zones = hexZones, Brightness = brightness });
            }

            WriteProfiles(profiles);
        }
        SetActiveProfile(name);
    }

    public static void DeleteProfile(string name)
    {
        List<RgbProfile> profiles;
        lock (FileLock)
        {
            var status = TryLoad(out profiles);
            if (status == LoadStatus.Unreadable) return;
            if (status == LoadStatus.Corrupt) PreserveDamagedFile();
            if (status != LoadStatus.Ok) profiles = CloneDefaults();

            profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (profiles.Count == 0) profiles.AddRange(CloneDefaults());
            WriteProfiles(profiles);
        }

        string currentActive = GetActiveProfile();
        if (string.Equals(currentActive, name, StringComparison.OrdinalIgnoreCase))
        {
            SetActiveProfile(profiles[0].Name);
        }
    }
    public static string GetActiveProfile()
    {
        return RegistryConfig.ReadString("ActiveRgbProfile") ?? "Teal (Default)";
    }

    public static void SetActiveProfile(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
            key.SetValue("ActiveRgbProfile", name);
        }
        catch { }
        try
        {
            using var hklmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\PredatorControl");
            hklmKey?.SetValue("ActiveRgbProfile", name);
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

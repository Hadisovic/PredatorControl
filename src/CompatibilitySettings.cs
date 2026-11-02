using Microsoft.Win32;

namespace PredatorControlApp;

internal static class CompatibilitySettings
{
    internal static object? Read(RegistryKey? key, string name)
    {
        if (key == null) return null;
        if (!HardwarePreferencePolicy.IsHardwareSetting(name)) return key.GetValue(name);
        string current = WmiController.GetSystemIdentity().LocalPreferenceKey;
        string? binding = key.GetValue("CompatibilityV1_" + name) as string;
        if (binding == null || binding == current) return key.GetValue(name);
        using var scoped = key.OpenSubKey(@"CompatibilityPreferences\" + current);
        return scoped?.GetValue(name);
    }

    internal static void Write(RegistryKey key, string name, object value)
    {
        if (HardwarePreferencePolicy.IsHardwareSetting(name))
        {
            string current = WmiController.GetSystemIdentity().LocalPreferenceKey;
            string previous = key.GetValue("CompatibilityV1_" + name) as string ?? "legacy";
            object? old = key.GetValue(name);
            if (old != null && previous != current)
            {
                using var archive = key.CreateSubKey(@"CompatibilityPreferences\" + previous);
                archive.SetValue(name, old);
            }
            key.SetValue("CompatibilityV1_" + name, current);
        }
        key.SetValue(name, value);
    }
}

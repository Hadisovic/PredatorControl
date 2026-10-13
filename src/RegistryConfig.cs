using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Unified registry reader for PredatorControl settings.
    /// Lookup order: HKCU\SOFTWARE\PredatorControl first, then HKLM\SOFTWARE\PredatorControl as fallback.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class RegistryConfig
    {
        private const string SubKeyPath = @"SOFTWARE\PredatorControl";

        public static object? ReadValue(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SubKeyPath);
                var value = key?.GetValue(name);
                if (value != null) return value;
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SubKeyPath);
                var value = key?.GetValue(name);
                if (value != null) return value;
            }
            catch { }

            return null;
        }

        public static int? ReadInt(string name)
        {
            var value = ReadValue(name);
            if (value is int i) return i;
            if (value != null && int.TryParse(value.ToString(), out int parsed)) return parsed;
            return null;
        }

        public static string? ReadString(string name)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SubKeyPath);
                if (key?.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(SubKeyPath);
                if (key?.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { }

            return null;
        }
    }
}

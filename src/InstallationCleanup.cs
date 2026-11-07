namespace PredatorControlApp;

internal static class InstallationCleanup
{
    private static readonly string UserRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PredatorControl");
    internal static readonly string ReportPath = Path.Combine(UserRoot, "cleanup-report.txt");

    internal static bool Run(bool uninstall)
    {
        var errors = new List<string>();
        void TryRemove(string path, Action remove)
        {
            try { remove(); }
            catch (Exception ex) { errors.Add(path + ": " + ex.Message); }
        }
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string oldRoot = Path.Combine(programFiles, "PredatorControl");
        foreach (string version in Directory.Exists(oldRoot) && SecureLaunch.IsAdminOnlyDirectory(oldRoot)
                     ? Directory.GetDirectories(oldRoot).Where(p => Version.TryParse(Path.GetFileName(p), out _))
                     : [])
        {
            TryRemove(version, () =>
            {
                if (!SecureLaunch.IsAdminOnlyDirectory(version)) throw new IOException("Unprotected legacy folder.");
                foreach (string folder in Directory.GetFileSystemEntries(version))
                    TryRemove(folder, () => RemoveLegacyBuild(folder));
                if (!Directory.EnumerateFileSystemEntries(version).Any()) Directory.Delete(version);
            });
        }
        foreach (string legacy in new[] { "PredatorControlData-v2" })
        {
            string root = Path.Combine(programFiles, legacy);
            TryRemove(root, () => RemoveProtectedData(root, legacy: true));
        }
        string oldAppData = Path.Combine(StartupInstallation.Root, "data");
        if (!oldAppData.Equals(SecureLaunch.ProtectedDataDir, StringComparison.OrdinalIgnoreCase))
            TryRemove(oldAppData, () => RemoveProtectedData(oldAppData));
        if (!oldRoot.Equals(StartupInstallation.Root, StringComparison.OrdinalIgnoreCase) &&
            SecureLaunch.IsAdminOnlyDirectory(oldRoot) && !Directory.EnumerateFileSystemEntries(oldRoot).Any())
            TryRemove(oldRoot, () => Directory.Delete(oldRoot));
        if (uninstall)
            TryRemove(SecureLaunch.ProtectedDataDir, SecureLaunch.RemoveProtectedDataDirectory);

        if (Directory.Exists(UserRoot))
        {
            foreach (string name in new[] { "WebView2", "WebView2-v3", "WebView2-Test-v3", "backup", "update", "WebView2Loader.dll" })
                TryRemove(Path.Combine(UserRoot, name), () => RemoveUserEntry(name));
            if (uninstall)
            {
                foreach (string name in new[] { "WebView2-Evergreen", "WebView2-Test-Evergreen", "game_sync.json",
                    "rgb_profiles.json", "crash.log", "schtasks.log", "last-error.txt", "last-setup-error.txt", "cleanup-report.txt" })
                    TryRemove(Path.Combine(UserRoot, name), () => RemoveUserEntry(name));
                TryRemove(UserRoot, () =>
                {
                    if (Directory.EnumerateFileSystemEntries(UserRoot).Any())
                        throw new IOException("Contains unrecognized files.");
                    SafeFile.DeleteTree(UserRoot, directory: true);
                });
            }
        }
        if (uninstall)
        {
            const string key = @"Software\PredatorControl";
            TryRemove(@"HKCU\" + key, () =>
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false));
            TryRemove(@"HKLM\" + key, () =>
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(key, throwOnMissingSubKey: false));
        }
        if (errors.Count == 0) return true;
        SafeFile.WriteText(ReportPath, string.Join(Environment.NewLine, errors));
        return false;
    }

    private static void RemoveLegacyBuild(string folder)
    {
        string hash = Path.GetFileName(folder);
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || !SecureLaunch.IsAdminOnlyDirectory(folder))
            throw new IOException("Unrecognized legacy build.");
        StartupInstallation.VerifyInstalled(Path.Combine(folder, "PredatorControl.exe"), hash);
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            string name = Path.GetFileName(entry);
            if (!SecureLaunch.IsAdminOnly(entry) ||
                !(name.Equals("PredatorControl.exe", StringComparison.OrdinalIgnoreCase) ||
                  name.Equals("ThirdPartyNotices.txt", StringComparison.OrdinalIgnoreCase) ||
                  System.Text.RegularExpressions.Regex.IsMatch(name, @"\Aunins[0-9]{3}\.(exe|dat|msg)\z",
                      System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                throw new IOException("Unrecognized legacy file: " + entry);
        }
        SafeFile.DeleteTree(folder, directory: true);
    }

    private static void RemoveProtectedData(string root, bool legacy = false)
    {
        if (!Directory.Exists(root) && !File.Exists(root)) return;
        // Earlier setups sometimes left administrator-owned data with writable ACLs.
        // Treat it as data to remove through no-follow handles, never as trusted input.
        if (!(legacy ? SecureLaunch.IsSystemOwnedDirectory(root) : SecureLaunch.IsAdminOnlyDirectory(root)))
            throw new IOException("Data ownership could not be verified.");
        string[] entries = Directory.GetFileSystemEntries(root);
        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            bool loader = name.Equals("WebView2Loader.dll", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(entry) && !Directory.Exists(entry) && SecureLaunch.IsAdminOnly(entry);
            bool knownDirectory = Directory.Exists(entry) &&
                (name is "browser" or "native" or "tasks" or "logs" or "reports" or "backup" or "update") &&
                SecureLaunch.IsSystemOwnedDirectory(entry);
            if (!loader && !knownDirectory)
                throw new IOException("Unrecognized data entry: " + entry);
        }
        foreach (string entry in entries) SafeFile.DeleteTree(entry, directory: Directory.Exists(entry));
        Directory.Delete(root);
    }

    private static void RemoveUserEntry(string name)
    {
        string path = Path.Combine(UserRoot, name);
        if (Directory.Exists(path)) SafeFile.DeleteTree(path, directory: true);
        else if (File.Exists(path)) SafeFile.DeleteTree(path, directory: false);
    }
}

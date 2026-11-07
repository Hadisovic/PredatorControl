using System.Reflection;
using System.Text.Json;

internal static class BundleChecks
{
    internal static async Task Run(Assembly assembly, Func<string, Func<Task<bool>>, Task> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var secure = assembly.GetType("PredatorControlApp.SecureLaunch")!;
        var ancestorRights = secure.GetMethod("AncestorRights", flags)!;
        await check("volume root ignores self-delete but still protects children and its ACL", () =>
        {
            const System.Security.AccessControl.FileSystemRights delete = System.Security.AccessControl.FileSystemRights.Delete;
            const System.Security.AccessControl.FileSystemRights required = System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles |
                System.Security.AccessControl.FileSystemRights.ChangePermissions | System.Security.AccessControl.FileSystemRights.TakeOwnership;
            var directory = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            var root = (System.Security.AccessControl.FileSystemRights)ancestorRights.Invoke(null, [directory.Root])!;
            var child = (System.Security.AccessControl.FileSystemRights)ancestorRights.Invoke(null, [directory])!;
            return Task.FromResult((root & (required | delete)) == required && (child & (required | delete)) == (required | delete));
        });
        await check("app includes the native loader but no bundled WebView2 runtime", () =>
        {
            string[] names = assembly.GetManifestResourceNames();
            return Task.FromResult(names.Contains("WebView2Loader.dll") &&
                !names.Any(name => name.StartsWith("Browser.", StringComparison.Ordinal)) &&
                !names.Contains("OfflineWebView2Installer.exe"));
        });
        var updater = assembly.GetType("PredatorControlApp.Updater")!;
        var pick = updater.GetMethod("PickAssetInfo", flags)!;
        await check("updater prefers portable and never selects setup as an application", () =>
        {
            using var setup = JsonDocument.Parse("""{"assets":[{"name":"PredatorControl-Setup-9.0.0.exe","browser_download_url":"https://github.com/setup.exe"}]}""");
            if (pick.Invoke(null, [setup.RootElement, true]) != null) return Task.FromResult(false);
            using var both = JsonDocument.Parse("""{"assets":[{"name":"PredatorControl-Setup-9.0.0.exe","browser_download_url":"https://github.com/setup.exe"},{"name":"PredatorControl-Portable-9.0.0.exe","browser_download_url":"https://github.com/portable.exe"}]}""");
            return Task.FromResult(pick.Invoke(null, [both.RootElement, true])!.ToString()!.Contains("portable.exe"));
        });
    }
}

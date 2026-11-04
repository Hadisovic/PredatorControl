using System.Reflection;
using System.Text.Json;

internal static class BundleChecks
{
    internal static async Task Run(Assembly assembly, Func<string, Func<Task<bool>>, Task> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var browser = assembly.GetType("PredatorControlApp.BundledBrowser")!;
        var name = browser.GetMethod("ValidateName", flags)!;
        await check("browser payload rejects traversal, alternate streams and malformed paths", () =>
        {
            foreach (string path in new[] { "../escape.exe", "/root.exe", "C:/root.exe", "folder\\file.exe", "a//b", "a/./b", "a/../b", "a:b", "a. /b", "a./b", "a /b" })
            {
                try { name.Invoke(null, [path]); return Task.FromResult(false); }
                catch (TargetInvocationException) { }
            }
            return Task.FromResult((string)name.Invoke(null, ["Locales/en-US.pak"])! == "Locales/en-US.pak");
        });
        await check("browser extraction refuses user-writable staging", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "PredatorBundle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                try { browser.GetMethod("Extract", flags)!.Invoke(null, [null, path, null, null]); return Task.FromResult(false); }
                catch (TargetInvocationException ex) { return Task.FromResult(ex.InnerException is UnauthorizedAccessException); }
            }
            finally { Directory.Delete(path); }
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

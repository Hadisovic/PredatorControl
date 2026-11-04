using System.IO.Compression;
using System.Security.Cryptography;
using PredatorControlApp;

if (Environment.UserName != "WDAGUtilityAccount") return 90;
var results = new List<string>();
void Check(string name, Action body) { body(); results.Add("PASS " + name); }
void Reject(Action body) { try { body(); } catch (InvalidDataException) { return; } catch (IOException) { return; } catch (UnauthorizedAccessException) { return; } throw new Exception("Expected rejection"); }
byte[] bytes = [1, 2, 3];
var manifest = new BundledBrowser.PayloadManifest("fixture", [new("Locales/sample.pak", 3, Convert.ToHexString(SHA256.HashData(bytes)))]);
MemoryStream Zip(params (string name, byte[] data)[] entries)
{
    var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        foreach (var item in entries) { using var file = zip.CreateEntry(item.name).Open(); file.Write(item.data); }
    stream.Position = 0;
    return stream;
}
string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PredatorBundleTest-" + Guid.NewGuid().ToString("N"));
try
{
    SecureLaunch.CreateAdminOnlyDirectory(root, usersMayRead: true);
    string NewCase() => SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(root, Guid.NewGuid().ToString("N")), usersMayRead: true);
    Check("complete extraction and repeat verification", () => {
        string path = NewCase(); using var data = Zip(("Locales/sample.pak", bytes)); using var zip = new ZipArchive(data);
        BundledBrowser.Extract(zip, path, manifest); BundledBrowser.VerifyTree(path, manifest); BundledBrowser.VerifyTree(path, manifest);
        File.WriteAllBytes(Path.Combine(path, "Locales", "sample.pak"), [4, 5, 6]);
        Reject(() => BundledBrowser.VerifyTree(path, manifest));
    });
    Check("traversal archive rejected without outside writes", () => {
        string path = NewCase(); using var data = Zip(("../escape.exe", bytes)); using var zip = new ZipArchive(data);
        Reject(() => BundledBrowser.Extract(zip, path, manifest));
        if (File.Exists(Path.Combine(root, "escape.exe"))) throw new Exception("escaped");
    });
    Check("duplicate entries and partial extraction rejected", () => {
        string path = NewCase(); using var data = Zip(("Locales/sample.pak", bytes), ("Locales/sample.pak", bytes)); using var zip = new ZipArchive(data);
        Reject(() => BundledBrowser.Extract(zip, path, manifest));
        Reject(() => BundledBrowser.VerifyTree(path, manifest));
    });
    Check("wrong file length rejected", () => {
        string path = NewCase(); using var data = Zip(("Locales/sample.pak", new byte[4])); using var zip = new ZipArchive(data);
        Reject(() => BundledBrowser.Extract(zip, path, manifest));
    });
    Check("untrusted existing storage refused without ACL repair", () => {
        string path = Path.Combine(Path.GetTempPath(), "UnsafeBundle-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try { Reject(() => SecureLaunch.CreateAdminOnlyDirectory(path)); } finally { Directory.Delete(path); }
    });
    Check("junction ancestor rejected", () => {
        string target = NewCase(), junction = Path.Combine(root, "junction");
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(junction); start.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("junction fixture failed");
        try { Reject(() => BundledBrowser.VerifyTree(junction, manifest)); } finally { Directory.Delete(junction); }
    });
    results.Add("PASS: no hardware, tasks, installation or host changes.");
    return 0;
}
catch (Exception ex) { results.Add("FAIL " + ex); return 1; }
finally
{
    File.WriteAllLines(@"C:\Output\bundle-tests.txt", results);
    // Only the unique guest-local fixture directory is removed; no production cache is touched.
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

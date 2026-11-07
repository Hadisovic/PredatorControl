using System.Diagnostics;
using PredatorControlApp;

string root = Path.Combine(Path.GetTempPath(), "PredatorCleanup-" + Guid.NewGuid().ToString("N"));
string retained = Path.Combine(root, "retained");
string cleanup = Path.Combine(root, "cleanup");
Directory.CreateDirectory(retained);
Directory.CreateDirectory(cleanup);
string marker = Path.Combine(retained, "keep.txt");
File.WriteAllText(marker, "Keep this file.");
try
{
    string junction = Path.Combine(cleanup, "junction");
    var cmd = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
    foreach (string arg in new[] { "/c", "mklink", "/J", junction, retained }) cmd.ArgumentList.Add(arg);
    using (var process = Process.Start(cmd)!)
    {
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create junction fixture.");
    }
    try
    {
        bool rejected = false;
        try { SafeFile.DeleteTree(cleanup, directory: true); }
        catch (IOException) { rejected = true; }
        if (!rejected || !File.Exists(marker)) throw new IOException("Cleanup followed the junction.");
        Console.WriteLine("PASS cleanup refused a junction and preserved its target");
    }
    finally { Directory.Delete(junction); }

    Directory.CreateDirectory(Path.Combine(cleanup, "nested"));
    File.WriteAllText(Path.Combine(cleanup, "nested", "test.txt"), "Remove this file.");
    SafeFile.DeleteTree(cleanup, directory: true);
    if (Directory.Exists(cleanup) || !File.Exists(marker)) throw new IOException("Cleanup scope failed.");
    Console.WriteLine("PASS cleanup removed only the selected tree");
}
finally
{
    if (Directory.Exists(cleanup)) SafeFile.DeleteTree(cleanup, directory: true);
    File.Delete(marker);
    Directory.Delete(retained);
    Directory.Delete(root);
}

using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace PredatorControlApp;

// The manifest and archive are embedded together by packaging/Build.ps1.
internal static class BundledBrowser
{
    internal sealed record PayloadFile(string Path, long Length, string Sha256);
    internal sealed record PayloadManifest(string Version, PayloadFile[] Files);
    private static readonly object Gate = new();
    private static string? prepared;
    internal static bool IsBundled => typeof(BundledBrowser).Assembly.GetManifestResourceInfo("Browser.manifest.json") != null;

    internal static void AcceptTerms(bool installerAlreadyAccepted = false)
    {
        if (!IsBundled) return;
        using var stream = typeof(BundledBrowser).Assembly.GetManifestResourceStream("Browser.terms.txt")
            ?? throw new IOException("Bundled runtime terms are missing.");
        using var reader = new StreamReader(stream);
        string terms = reader.ReadToEnd();
        string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(terms)));
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\PredatorControl");
        if (key.GetValue("BundledRuntimeTerms") as string == hash) return;
        if (!installerAlreadyAccepted)
        {
            using var dialog = new Form { Text = "Bundled browser terms", Width = 640, Height = 520,
                StartPosition = FormStartPosition.CenterScreen, MinimizeBox = false, MaximizeBox = false };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var agree = new Button { Text = "I agree", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Use Classic", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(agree); buttons.Controls.Add(cancel);
            dialog.Controls.Add(new TextBox { Text = terms.Replace("\n", "\r\n"), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill });
            dialog.Controls.Add(buttons); dialog.AcceptButton = agree; dialog.CancelButton = cancel;
            if (dialog.ShowDialog() != DialogResult.OK) throw new OperationCanceledException("Bundled browser terms were declined.");
        }
        key.SetValue("BundledRuntimeTerms", hash);
    }

    internal static async Task<string> PrepareWithProgressAsync(Form owner)
    {
        using var dialog = new Form { Text = "Preparing Predator Control", Width = 390, Height = 135,
            FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false, StartPosition = FormStartPosition.CenterScreen,
            ShowInTaskbar = false };
        var label = new Label { Text = "Preparing bundled browser…", Dock = DockStyle.Top, Height = 42, Padding = new Padding(12) };
        dialog.Controls.Add(new ProgressBar { Dock = DockStyle.Bottom, Height = 24, Style = ProgressBarStyle.Marquee });
        dialog.Controls.Add(label);
        dialog.Show(owner);
        try { return await Task.Run(() => EnsureReady(message => dialog.BeginInvoke(() => label.Text = message))); }
        finally { dialog.Close(); }
    }

    internal static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') ||
            name.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Invalid browser payload path.");
        return name;
    }

    internal static string EnsureReady(Action<string>? progress = null)
    {
        lock (Gate)
        {
            if (prepared != null) return prepared;
            using var manifestStream = typeof(BundledBrowser).Assembly.GetManifestResourceStream("Browser.manifest.json")
                ?? throw new IOException("This development build has no bundled browser. Publish with packaging/Build.ps1.");
            using var bytes = new MemoryStream();
            manifestStream.CopyTo(bytes);
            var manifest = JsonSerializer.Deserialize<PayloadManifest>(bytes.ToArray()) ?? throw new InvalidDataException("Missing browser manifest.");
            string id = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
            SecureLaunch.CreateAdminOnlyDirectory(SecureLaunch.ProtectedDataDir);
            string root = SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(SecureLaunch.ProtectedDataDir, "browser"), usersMayRead: true);
            // Keep the lock unreadable to ordinary users; a deny-sharing read handle must not block preparation.
            string lockRoot = SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(root, "locks"));
            using var held = AcquireLock(lockRoot);
            string destination = Path.Combine(root, id);
            progress?.Invoke("Checking bundled browser…");
            if (Directory.Exists(destination)) VerifyTree(destination, manifest);
            else
            {
                string staging = SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N")), usersMayRead: true);
                try
                {
                    using var archiveStream = typeof(BundledBrowser).Assembly.GetManifestResourceStream("Browser.zip")
                        ?? throw new IOException("Missing bundled browser archive.");
                    using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
                    Extract(archive, staging, manifest, progress);
                    VerifyTree(staging, manifest);
                    Directory.Move(staging, destination);
                }
                finally { if (Directory.Exists(staging)) DeleteProtectedTree(staging); }
            }
            prepared = destination;
            return destination;
        }
    }

    private static FileStream AcquireLock(string root)
    {
        string path = Path.Combine(root, "prepare.lock");
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (true)
        {
            if (File.Exists(path) && !SecureLaunch.IsAdminOnly(path)) throw new UnauthorizedAccessException("Browser preparation lock is not protected.");
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(200); }
        }
    }

    internal static void Extract(ZipArchive archive, string directory, PayloadManifest manifest, Action<string>? progress = null)
    {
        if (!SecureLaunch.IsAdminOnlyDirectory(directory)) throw new UnauthorizedAccessException("Browser staging directory is not protected.");
        var expected = manifest.Files.ToDictionary(file => ValidateName(file.Path), StringComparer.OrdinalIgnoreCase);
        if (archive.Entries.Count != expected.Count || expected.Count == 0) throw new InvalidDataException("Browser payload file count mismatch.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            string name = ValidateName(entry.FullName);
            if (!seen.Add(name) || !expected.TryGetValue(name, out var file) || entry.Length != file.Length ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Unexpected browser payload entry.");
            string path = Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar));
            CreateSubdirectories(directory, Path.GetDirectoryName(path)!);
            using (var input = entry.Open())
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long count = 0;
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    count = checked(count + read);
                    if (count > file.Length) throw new InvalidDataException("Browser payload exceeded its declared length.");
                    output.Write(buffer, 0, read);
                }
                if (count != file.Length) throw new InvalidDataException("Truncated browser payload.");
                output.Flush(true);
            }
            progress?.Invoke($"Preparing bundled browser… {seen.Count}/{expected.Count}");
        }
        // WebView2 v120+ renderer AppContainers need read/execute on Windows 10.
        GrantBrowserReaders(directory);
    }

    private static void CreateSubdirectories(string root, string target)
    {
        if (target.Equals(root, StringComparison.OrdinalIgnoreCase)) return;
        CreateSubdirectories(root, Path.GetDirectoryName(target)!);
        SecureLaunch.CreateAdminOnlyDirectory(target, usersMayRead: true);
    }

    private static void GrantBrowserReaders(string directory)
    {
        foreach (string child in Directory.GetDirectories(directory)) GrantBrowserReaders(child);
        var info = new DirectoryInfo(directory);
        var acl = info.GetAccessControl();
        foreach (string sid in new[] { "S-1-15-2-1", "S-1-15-2-2" })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
    }

    internal static void VerifyTree(string directory, PayloadManifest manifest)
    {
        if (!SecureLaunch.IsAdminOnlyDirectory(directory)) throw new UnauthorizedAccessException("Browser directory is not protected.");
        var expected = manifest.Files.ToDictionary(file => ValidateName(file.Path), StringComparer.OrdinalIgnoreCase);
        var actual = EnumerateProtectedFiles(directory).ToArray();
        if (actual.Length != expected.Count) throw new InvalidDataException("Browser cache contents do not match the bundled manifest.");
        foreach (string path in actual)
        {
            string name = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (!expected.TryGetValue(name, out var file) || !SecureLaunch.IsAdminOnly(path)) throw new UnauthorizedAccessException("Unexpected or unprotected browser file.");
            using var input = SafeFile.OpenRead(path);
            if (input.Length != file.Length || !Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Bundled browser verification failed. The cache was not changed.");
        }
    }

    private static IEnumerable<string> EnumerateProtectedFiles(string directory)
    {
        if (!SecureLaunch.IsAdminOnlyDirectory(directory)) throw new UnauthorizedAccessException("Unsafe browser directory.");
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Browser cache contains a link.");
            if ((attributes & FileAttributes.Directory) != 0)
            { foreach (string child in EnumerateProtectedFiles(path)) yield return child; }
            else yield return path;
        }
    }

    private static void DeleteProtectedTree(string directory)
    {
        foreach (string file in EnumerateProtectedFiles(directory).ToArray())
        {
            if (!SecureLaunch.IsAdminOnly(file)) throw new UnauthorizedAccessException("Unsafe staging file.");
            File.Delete(file);
        }
        DeleteEmptyDirectories(directory);
    }

    private static void DeleteEmptyDirectories(string directory)
    {
        if (!SecureLaunch.IsAdminOnlyDirectory(directory)) throw new UnauthorizedAccessException("Unsafe staging directory.");
        foreach (string child in Directory.GetDirectories(directory)) DeleteEmptyDirectories(child);
        Directory.Delete(directory);
    }
}

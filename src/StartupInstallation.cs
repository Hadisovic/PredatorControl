using System.Diagnostics.CodeAnalysis;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PredatorControlApp;

internal static class StartupInstallation
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtAreMappedFilesTheSame(IntPtr image, IntPtr data);

    internal static void VerifyRunningImage(FileStream source)
    {
        using var mapping = MemoryMappedFile.CreateFromFile(source, null, 0, MemoryMappedFileAccess.Read,
            HandleInheritability.None, leaveOpen: true);
        using var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        var image = GetModuleHandleW(null);
        if (image == IntPtr.Zero || NtAreMappedFilesTheSame(image, view.SafeMemoryMappedViewHandle.DangerousGetHandle()) != 0)
            throw new IOException("The source file is not the running executable. Close this copy and reopen the original download.");
        GC.KeepAlive(view);
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Installation explicitly rejects non-bundled development builds.")]
    internal static string InstallRunningImage()
    {
        if (!string.IsNullOrEmpty(typeof(StartupInstallation).Assembly.Location))
            throw new InvalidOperationException("Use the published standalone or win-x64 single-file executable to enable startup.");
        string current = Environment.ProcessPath ?? throw new IOException("Cannot identify the running executable.");
        // Native handle-relative traversal rejects reparse ancestors and hardlinks; the returned handle
        // denies write/delete sharing and stays open through image identity, hash, copy and publication.
        using var source = SafeFile.OpenRead(current);
        VerifyRunningImage(source);
        string root = SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PredatorControl"), usersMayRead: true);
        string version = typeof(StartupInstallation).Assembly.GetName().Version!.ToString(3);
        root = SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(root, version), usersMayRead: true);
        return InstallPinnedImage(source, root);
    }

    internal static string InstallPinnedImage(FileStream source, string versionRoot)
    {
        if (!SecureLaunch.IsAdminOnlyDirectory(versionRoot)) throw new UnauthorizedAccessException("Installation folder is not protected.");
        source.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        string destinationDirectory = Path.Combine(versionRoot, hash);
        string destination = Path.Combine(destinationDirectory, "PredatorControl.exe");
        if (Directory.Exists(destinationDirectory))
        {
            VerifyInstalled(destination, hash);
            return destination;
        }
        string staging = SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(versionRoot, ".staging-" + Guid.NewGuid().ToString("N")), usersMayRead: true);
        string staged = Path.Combine(staging, "PredatorControl.exe");
        try
        {
            source.Position = 0;
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            VerifyInstalled(staged, hash);
            try { Directory.Move(staging, destinationDirectory); }
            catch (IOException) when (Directory.Exists(destinationDirectory)) { VerifyInstalled(destination, hash); }
            VerifyInstalled(destination, hash);
            return destination;
        }
        finally
        {
            // Only our unique staging file/directory is eligible for cleanup; never recurse.
            if (File.Exists(staged)) File.Delete(staged);
            if (Directory.Exists(staging)) Directory.Delete(staging);
        }
    }

    internal static void VerifyInstalled(string path, string hash)
    {
        if (!SecureLaunch.IsAdminOnly(path)) throw new UnauthorizedAccessException("Installed executable is not protected.");
        using var file = SafeFile.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Installed executable hash does not match the source.");
    }

    internal static string ShortcutFailureReason { get; private set; } = "";

    internal static bool CreateShortcut(string executable)
    {
        ShortcutFailureReason = "";
        object? shell = null, shortcut = null;
        string? temporary = null;
        try
        {
            if (!SecureLaunch.IsAdminOnly(executable)) throw new UnauthorizedAccessException("Shortcut target is not protected.");
            string directory = SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Predator Control"), usersMayRead: true);
            string final = Path.Combine(directory, "Predator Control.lnk");
            if (File.Exists(final) && !SecureLaunch.IsAdminOnly(final)) throw new UnauthorizedAccessException("Existing shortcut is not protected.");
            temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".lnk");
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(temporary);
            ((dynamic)shortcut).TargetPath = executable;
            ((dynamic)shortcut).WorkingDirectory = Path.GetDirectoryName(executable);
            ((dynamic)shortcut).IconLocation = executable + ",0";
            ((dynamic)shortcut).Save();
            if (!SecureLaunch.IsAdminOnly(temporary)) throw new UnauthorizedAccessException("New shortcut is not protected.");
            File.Move(temporary, final, overwrite: true);
            return SecureLaunch.IsAdminOnly(final);
        }
        catch (Exception ex) { ShortcutFailureReason = ex.Message; return false; }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
            if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

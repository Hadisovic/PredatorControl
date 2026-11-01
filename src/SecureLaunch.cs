using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;

namespace PredatorControlApp;

/// <summary>
/// Guards every elevated launch: absolute System32 paths for built-in tools, and an admin-only
/// location for any binary that a SYSTEM/elevated scheduled task will execute.
/// </summary>
internal static class SecureLaunch
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    /// <summary>Resolves a built-in tool (sc.exe, schtasks.exe, ...) to its System32 path so it cannot be hijacked by search order.</summary>
    internal static string System32(string exe) => Path.IsPathRooted(exe) ? exe : Path.Combine(Environment.SystemDirectory, exe);

    internal static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PredatorControl");
    internal static readonly string InstalledExe = Path.Combine(InstallDir, "PredatorControl.exe");
    internal static readonly string ProgramDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PredatorControl");

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier CreatorOwner = new(WellKnownSidType.CreatorOwnerSid, null);
    private static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    private const FileSystemRights ObjectWrite = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership |
        (FileSystemRights)0x10000000 | (FileSystemRights)0x40000000; // GenericAll | GenericWrite
    private const FileSystemRights AncestorWrite = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private static bool IsTrusted(SecurityIdentifier? sid) =>
        sid != null && (sid.Equals(LocalSystem) || sid.Equals(Admins) || sid.Equals(TrustedInstaller) || sid.Equals(CreatorOwner));

    private static bool HasUntrustedWriter(FileSystemSecurity security, FileSystemRights mask)
    {
        if (!IsTrusted(security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)) return true;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue; // applies to children only
            if ((rule.FileSystemRights & mask) != 0 && !IsTrusted(rule.IdentityReference as SecurityIdentifier)) return true;
        }
        return false;
    }

    /// <summary>
    /// True only when the file, its folder and every ancestor cannot be modified, replaced or renamed by
    /// non-administrators. Reparse points anywhere on the chain are rejected.
    /// </summary>
    internal static bool IsAdminOnly(string path)
    {
        try
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (HasUntrustedWriter(file.GetAccessControl(), ObjectWrite)) return false;
            bool first = true;
            for (var dir = file.Directory; dir != null; dir = dir.Parent)
            {
                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (HasUntrustedWriter(dir.GetAccessControl(), first ? ObjectWrite : AncestorWrite)) return false;
                first = false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Creates (or re-secures) a directory writable only by SYSTEM and Administrators; children inherit.</summary>
    internal static string CreateAdminOnlyDirectory(string path, bool usersMayRead = false)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (usersMayRead)
            security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        try { security.SetOwner(Admins); } catch { }

        var info = new DirectoryInfo(path);
        if (info.Exists) info.SetAccessControl(security);
        else info.Create(security);
        return path;
    }

    /// <summary>
    /// Returns the path a scheduled task may safely execute. If this process already lives somewhere admin-only
    /// it is used directly; otherwise it is copied into %ProgramFiles%\PredatorControl and verified. Returns
    /// null (and a reason) when no admin-only location can be established, so no task is registered.
    /// </summary>
    internal static string? PrepareTaskBinary(out string reason)
    {
        reason = "";
        string current = Environment.ProcessPath ?? Application.ExecutablePath;
        if (IsAdminOnly(current)) return current;
        try
        {
            CreateAdminOnlyDirectory(InstallDir, usersMayRead: true);
            if (!SameFile(current, InstalledExe))
            {
                string temp = InstalledExe + ".new";
                File.Copy(current, temp, true);
                try { File.Move(temp, InstalledExe, true); }
                catch (IOException)
                {
                    // A running instance holds the old image: Windows still allows renaming it out of the way.
                    string old = InstalledExe + ".old-" + Guid.NewGuid().ToString("N");
                    File.Move(InstalledExe, old);
                    File.Move(temp, InstalledExe, true);
                    try { File.Delete(old); } catch { }
                }
            }
            if (IsAdminOnly(InstalledExe)) return InstalledExe;
            reason = "The install location is writable by non-administrators.";
        }
        catch (Exception ex) { reason = ex.Message; }
        return null;
    }

    /// <summary>Keeps the Program Files copy used by startup tasks in step with a newer copy the user launches.</summary>
    internal static bool InstalledCopyIsOlderThanCurrent()
    {
        try
        {
            string current = Environment.ProcessPath ?? "";
            if (!File.Exists(InstalledExe) || string.Equals(Path.GetFullPath(current), InstalledExe, OIC)) return false;
            return Version.TryParse(FileVersionInfo.GetVersionInfo(current).FileVersion, out var cur) &&
                   Version.TryParse(FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion, out var installed) && cur > installed;
        }
        catch { return false; }
    }

    private static bool SameFile(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (!fb.Exists || fa.Length != fb.Length) return false;
            using var sa = fa.OpenRead(); using var sb = fb.OpenRead();
            return SHA256.HashData(sa).AsSpan().SequenceEqual(SHA256.HashData(sb));
        }
        catch { return false; }
    }

    internal static bool IsElevated()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }
}

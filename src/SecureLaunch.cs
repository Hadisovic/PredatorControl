using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace PredatorControlApp;

/// <summary>
/// Guards every elevated launch: absolute System32 paths for built-in tools, and an admin-only
/// location for any binary that a SYSTEM/elevated scheduled task will execute.
/// </summary>
internal static class SecureLaunch
{
    /// <summary>Resolves a built-in tool (sc.exe, schtasks.exe, ...) to its System32 path so it cannot be hijacked by search order.</summary>
    internal static string System32(string exe) => Path.IsPathRooted(exe) ? exe : Path.Combine(Environment.SystemDirectory, exe);

    // Shared, administrator-protected native data lives separately from the app folder.
    // Existing folders are never trusted or repaired in place unless their ACLs pass validation.
    internal static string ProtectedDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PredatorControlData");

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

    // A volume root cannot itself be deleted or renamed. Rights to delete its children
    // or change its ACL still matter, including on Windows Sandbox's system volume.
    private static FileSystemRights AncestorRights(DirectoryInfo directory) =>
        directory.Parent == null ? AncestorWrite & ~FileSystemRights.Delete : AncestorWrite;

    private static bool IsTrusted(SecurityIdentifier? sid) =>
        sid != null && (sid.Equals(LocalSystem) || sid.Equals(Admins) || sid.Equals(TrustedInstaller) || sid.Equals(CreatorOwner));

    internal static bool IsSystemOwnedDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        return dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) == 0 &&
            IsTrusted(dir.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier);
    }

    private static bool HasUntrustedWriter(FileSystemSecurity security, FileSystemRights mask)
    {
        if (new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl == null) return true;
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
                if (HasUntrustedWriter(dir.GetAccessControl(), first ? ObjectWrite : AncestorRights(dir))) return false;
                first = false;
            }
            return true;
        }
        catch { return false; }
    }

    internal static bool IsAdminOnlyDirectory(string path)
    {
        try
        {
            bool first = true;
            for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
            {
                if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (HasUntrustedWriter(dir.GetAccessControl(), first ? ObjectWrite : AncestorRights(dir))) return false;
                first = false;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Creates a protected directory; never follows or repairs an untrusted existing path.</summary>
    internal static string CreateAdminOnlyDirectory(string path, bool usersMayRead = false, SecurityIdentifier? reader = null)
    {
        var info = new DirectoryInfo(Path.GetFullPath(path));
        try
        {
            if ((File.GetAttributes(info.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("A protected directory cannot be a reparse point.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        // Validate ancestors before creating anything or changing an ACL.
        for (var parent = info.Parent; parent != null; parent = parent.Parent)
            if (!parent.Exists || (parent.Attributes & FileAttributes.ReparsePoint) != 0 ||
                HasUntrustedWriter(parent.GetAccessControl(), AncestorRights(parent)))
                throw new UnauthorizedAccessException("The directory has an untrusted ancestor: " + parent.FullName);
        if (info.Exists)
        {
            if (!IsAdminOnlyDirectory(info.FullName))
                throw new UnauthorizedAccessException("The existing directory is not protected: " + info.FullName);
            return info.FullName;
        }
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (usersMayRead)
            security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (reader != null)
            security.AddAccessRule(new FileSystemAccessRule(reader, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.SetOwner(Admins);
        info.Create(security);
        if (!IsAdminOnlyDirectory(info.FullName))
            throw new UnauthorizedAccessException("Could not establish a protected directory.");
        return info.FullName;
    }

    /// <summary>
    /// Reconciles the fixed Program Files data directory during elevated setup. A legacy
    /// directory with a weak ACL is treated as disposable data: remove it using no-follow
    /// handles, then create a fresh administrator-only directory. Nothing from that path is
    /// loaded or trusted before it has been secured.
    /// </summary>
    internal static string PrepareProtectedDataDirectory()
    {
        string path = Path.GetFullPath(ProtectedDataDir);
        string parent = Path.GetDirectoryName(path) ?? throw new IOException("The protected data directory has no parent.");
        if (!IsAdminOnlyDirectory(parent))
            throw new UnauthorizedAccessException("The protected data directory has an untrusted Program Files ancestor.");

        FileAttributes? attributes = null;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }

        if (attributes.HasValue)
        {
            bool isDirectory = (attributes.Value & FileAttributes.Directory) != 0;
            bool isProtected = isDirectory ? IsAdminOnlyDirectory(path) : IsAdminOnly(path);
            if (!isDirectory || !isProtected)
            {
                if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The protected data path is a reparse point and cannot be safely replaced.");
                SafeFile.DeleteTree(path, isDirectory);
            }
        }

        return CreateAdminOnlyDirectory(path);
    }

    /// <summary>Removes only the fixed Predator Control Program Files data root using no-follow handles.</summary>
    internal static void RemoveProtectedDataDirectory()
    {
        string path = Path.GetFullPath(ProtectedDataDir);
        string parent = Path.GetDirectoryName(path) ?? throw new IOException("The protected data directory has no parent.");
        if (!IsAdminOnlyDirectory(parent))
            throw new UnauthorizedAccessException("The protected data directory has an untrusted Program Files ancestor.");
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The protected data path is a reparse point and cannot be safely removed.");
        SafeFile.DeleteTree(path, (attributes & FileAttributes.Directory) != 0);
    }

    // Call only inside a protected directory. Hold the returned read handle throughout native loading.
    internal static FileStream WriteLockedResource(string path, byte[] bytes)
    {
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            output.Write(bytes);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    internal static bool VerifyHashAndRewind(Stream stream, byte[] expectedHash)
    {
        if (!stream.CanSeek) return false;
        stream.Position = 0;
        byte[] actualHash = SHA256.HashData(stream);
        stream.Position = 0;
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    /// <summary>Only the running copy in a protected location may become a startup task.</summary>
    internal static string? PrepareTaskBinary(out string reason)
    {
        string current = Environment.ProcessPath ?? Application.ExecutablePath;
        if (IsAdminOnly(current))
        {
            reason = "";
            return current;
        }
        reason = "Open the app from an administrator-protected folder before enabling Start with Windows.";
        return null;
    }

    internal static bool IsElevated()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }
}

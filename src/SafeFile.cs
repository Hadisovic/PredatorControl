using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PredatorControlApp;

// Resolve components without reparsing, then create and rename relative to the verified directory handle.
// Path checks followed by a second pathname-based write are insufficient in a user-writable directory.
internal static class SafeFile
{
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory, ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { public IntPtr Status, Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle file, uint access, ref ObjectAttributes attributes,
        out IoStatus status, IntPtr allocationSize, uint fileAttributes, FileShare share, uint disposition,
        uint options, IntPtr extendedAttributes, uint extendedLength);
    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle file, out IoStatus status, byte[] data, uint length, int kind);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, byte[] data, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    private static SafeFileHandle Open(string name, SafeFileHandle? parent, bool directory, bool create, bool write = false, bool delete = false)
    {
        IntPtr text = Marshal.StringToHGlobalUni(name);
        IntPtr unicode = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            var value = new UnicodeString { Length = checked((ushort)(name.Length * 2)),
                MaximumLength = checked((ushort)(name.Length * 2 + 2)), Buffer = text };
            Marshal.StructureToPtr(value, unicode, false);
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), ObjectName = unicode,
                RootDirectory = parent?.DangerousGetHandle() ?? IntPtr.Zero, Attributes = 0x40 | 0x1000 }; // CASE_INSENSITIVE | DONT_REPARSE
            uint access = directory ? 0x001000A0U : write ? 0x40110080U : 0x80100000U;
            if (delete) access |= 0x10000U;
            uint disposition = create ? directory ? 3U : 2U : 1U; // OPEN_IF directories, CREATE new files, OPEN existing
            int status = NtCreateFile(out var handle, access, ref attributes, out _, IntPtr.Zero, 0x80,
                directory && !delete ? FileShare.ReadWrite | FileShare.Delete : FileShare.Read, disposition,
                0x20U | 0x00200000U | (directory ? 1U : 0x40U), IntPtr.Zero, 0); // synchronous, OPEN_REPARSE_POINT, DIRECTORY/NON_DIRECTORY
            GC.KeepAlive(parent);
            if (status < 0)
            {
                handle.Dispose();
                throw new IOException("The data path could not be opened safely.", new Win32Exception((int)RtlNtStatusToDosError(status)));
            }
            if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                handle.Dispose();
                throw new IOException("Reparse points are not supported for elevated data operations.");
            }
            return handle;
        }
        finally { Marshal.FreeHGlobal(unicode); Marshal.FreeHGlobal(text); }
    }

    private static SafeFileHandle OpenDirectory(string fullPath, bool create)
    {
        var names = new Stack<string>();
        var directory = new DirectoryInfo(Path.GetDirectoryName(fullPath)!);
        for (var dir = directory; dir.Parent != null; dir = dir.Parent) names.Push(dir.Name);
        string root = directory.Root.FullName;
        if (root.Length != 3 || root[1] != ':') throw new IOException("Choose a local drive for elevated data operations.");
        var parent = Open(@"\??\" + root, null, directory: true, create: false);
        try
        {
            foreach (string name in names)
            {
                var child = Open(name, parent, directory: true, create);
                parent.Dispose();
                parent = child;
            }
            return parent;
        }
        catch { parent.Dispose(); throw; }
    }

    internal static FileStream OpenRead(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using var parent = OpenDirectory(fullPath, create: false);
        var handle = Open(Path.GetFileName(fullPath), parent, directory: false, create: false);
        try
        {
            if (!GetFileInformationByHandle(handle, out var info) || info.Links != 1)
                throw new IOException("The data file must have one link.");
            return new FileStream(handle, FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static void DeleteTree(string path, bool directory)
    {
        string fullPath = Path.GetFullPath(path);
        using var parent = OpenDirectory(fullPath, create: false);
        DeleteEntry(fullPath, parent, directory);
    }

    private static void DeleteEntry(string path, SafeFileHandle parent, bool directory)
    {
        using var entry = Open(Path.GetFileName(path), parent, directory, create: false, delete: true);
        if (directory)
        {
            foreach (string child in Directory.GetFileSystemEntries(path))
                DeleteEntry(child, entry, Directory.Exists(child));
        }
        else if (!GetFileInformationByHandle(entry, out var info) || info.Links != 1)
            throw new IOException("Cannot remove a file with multiple links: " + path);
        if (!SetFileInformationByHandle(entry, 4, [1], 1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not remove " + path);
    }

    internal static string ReadText(string path, int limit = 1024 * 1024)
    {
        using var file = OpenRead(path);
        if (file.Length > limit) throw new IOException("The data file is too large.");
        using var reader = new StreamReader(file);
        char[] text = new char[limit + 1];
        int count = reader.ReadBlock(text, 0, text.Length);
        if (count > limit) throw new IOException("The data file is too large.");
        return new string(text, 0, count);
    }

    internal static void WriteText(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        using var parent = OpenDirectory(fullPath, create: true);
        WriteTextCore(parent, Path.GetFileName(fullPath), text);
    }

    private static void WriteTextCore(SafeFileHandle parent, string filename, string text)
    {
        if (string.IsNullOrEmpty(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || filename is "." or "..")
            throw new IOException("Invalid data file name.");
        using var file = Open(Guid.NewGuid().ToString("N") + ".tmp", parent, directory: false, create: true, write: true);
        using var stream = new FileStream(file, FileAccess.Write);
        bool renamed = false;
        try
        {
            stream.Write(new UTF8Encoding(false).GetBytes(text));
            stream.Flush(flushToDisk: true);
            byte[] name = Encoding.Unicode.GetBytes(filename);
            int nameOffset = IntPtr.Size == 8 ? 20 : 12;
            byte[] rename = new byte[nameOffset + name.Length];
            rename[0] = 1; // Replace the destination entry, never its link target.
            if (IntPtr.Size == 8) BinaryPrimitives.WriteInt64LittleEndian(rename.AsSpan(8), parent.DangerousGetHandle().ToInt64());
            else BinaryPrimitives.WriteInt32LittleEndian(rename.AsSpan(4), parent.DangerousGetHandle().ToInt32());
            BinaryPrimitives.WriteInt32LittleEndian(rename.AsSpan(nameOffset - 4), name.Length);
            name.CopyTo(rename, nameOffset);
            int status = NtSetInformationFile(file, out _, rename, (uint)rename.Length, 10); // FileRenameInformation; supports RootDirectory.
            if (status < 0) throw new Win32Exception((int)RtlNtStatusToDosError(status));
            GC.KeepAlive(parent);
            renamed = true;
        }
        finally { if (!renamed) SetFileInformationByHandle(file, 4, [1], 1); } // Delete this handle's file only.
    }
}

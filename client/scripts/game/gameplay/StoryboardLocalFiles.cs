using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DynamiteUniverse.Game;

/// <summary>Mounted local attachments are bounded, link-free files, not arbitrary filesystem streams.</summary>
internal static class StoryboardLocalFiles
{
    internal static bool Within(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !Path.IsPathRooted(relative) &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static void CheckDirectoryChain(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Storyboard requires an absolute local directory");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            CheckAttributes(directory.FullName, directory: true);
    }

    internal static string SafePath(string directory, string relative, string extension)
    {
        var segments = ValidatePath(relative, extension);
        var path = directory;
        for (var i = 0; i < segments.Length; ++i)
        {
            path = Path.Combine(path, segments[i]);
            CheckAttributes(path, directory: i < segments.Length - 1);
        }
        return path;
    }

    internal static string[] ValidatePath(string relative, string extension)
    {
        var segments = relative.Split('/');
        if (relative.Length is 0 or > 1024 || !relative.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
            segments.Any(segment => segment.Length is 0 or > 255 || segment is "." or ".." ||
                segment[0] == ' ' || segment[^1] is '.' or ' ' || IsDeviceName(segment) ||
                segment.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format ||
                    c is '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')))
            throw new InvalidDataException($"Unsafe Storyboard {extension} path '{relative}'");
        return segments;
    }

    private static bool IsDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var name = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ').ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
            name[3] is >= '0' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3';
    }

    private static void CheckAttributes(string path, bool directory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 ||
            ((attributes & FileAttributes.Directory) != 0) != directory)
            throw new InvalidDataException($"Unexpected path type or link in Storyboard attachment: '{path}'");
    }

    internal static FileStream Open(string path, int maxBytes, out int length)
    {
        CheckAttributes(path, directory: false);
        // Normal Unix FileStream opens FIFOs in blocking mode. An independent write process
        // must never be needed just to see whether a local attachment is a regular file.
        FileStream stream;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
        {
            var darwin = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();
            var nonBlockingNoFollowCloseOnExec = darwin ? 0x01000104 : 0x000A0800;
            var fd = UnixOpen(path, nonBlockingNoFollowCloseOnExec);
            if (fd < 0) throw new IOException($"Cannot open Storyboard attachment '{path}'", new Win32Exception(Marshal.GetLastWin32Error()));
            var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
            try { stream = new FileStream(handle, FileAccess.Read); }
            catch { handle.Dispose(); throw; }
        }
        else stream = File.OpenRead(path);

        try
        {
            if (!stream.CanSeek) throw new InvalidDataException($"Storyboard attachment is not a regular file: '{path}'");
            var size = stream.Length;
            if (size <= 0 || size > maxBytes)
                throw new InvalidDataException($"Invalid Storyboard attachment size: '{path}' (maximum {maxBytes} bytes)");
            length = (int)size;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    internal static byte[] Read(string path, int maxBytes)
    {
        using var stream = Open(path, maxBytes, out var length);
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException($"Storyboard attachment changed while reading: '{path}'");
        return bytes;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int UnixOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}

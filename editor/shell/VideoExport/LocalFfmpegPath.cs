namespace DynamiteUniverse.Editor.Shell.VideoExport;

/// <summary>Bounds FFmpeg discovery to explicit absolute local paths; follows only local links.</summary>
internal static class LocalFfmpegPath
{
    internal static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string? ResolveFile(string? path)
    {
        if (!IsExecutable(path)) return null;
        var resolved = Resolve(path, directory: false);
        return IsExecutable(resolved) ? resolved : null;
    }

    private static bool IsExecutable(string? path) =>
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    internal static string? ResolveDirectory(string? path) => Resolve(path, directory: true);

    internal static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        // Do not hand UNC/device paths to filesystem APIs (including extended UNC syntax).
        if (OperatingSystem.IsWindows() && (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':')) return null;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (OperatingSystem.IsWindows() && full.AsSpan(2).Contains(':')) return null; // NTFS streams
            return full;
        }
        catch (Exception error) when (IsPathError(error)) { return null; }
    }

    private static string? Resolve(string? path, bool directory)
    {
        try
        {
            var current = Normalize(path);
            for (var redirects = 0; current is not null && redirects <= 16; redirects++)
            {
                var root = Path.GetPathRoot(current)!;
                if (OperatingSystem.IsWindows() && new DriveInfo(root).DriveType is not
                    (DriveType.Fixed or DriveType.Removable or DriveType.Ram)) return null;
                if (current == root) return directory ? root : null;

                // Inspect each node before accessing its children. A local-looking path on a fixed
                // drive can still lead to a network share through a parent directory symlink.
                var segments = current[root.Length..].Split(Path.DirectorySeparatorChar);
                var parent = root;
                var followedLink = false;
                for (var i = 0; i < segments.Length; i++)
                {
                    var last = i == segments.Length - 1;
                    var next = Path.Combine(parent, segments[i]);
                    var attributes = File.GetAttributes(next);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        FileSystemInfo node = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(next) : new FileInfo(next);
                        if (node.LinkTarget is not { Length: > 0 } target) return null;
                        if (!Path.IsPathRooted(target)) target = Path.Combine(parent, target);
                        if (OperatingSystem.IsWindows() && target.StartsWith(@"\\?\", StringComparison.Ordinal)) target = target[4..];
                        var destination = Normalize(target);
                        if (destination is null) return null;
                        current = Path.Combine(destination, string.Join(Path.DirectorySeparatorChar, segments, i + 1, segments.Length - i - 1));
                        followedLink = true;
                        break;
                    }
                    if (((attributes & FileAttributes.Directory) != 0) != (!last || directory)) return null;
                    parent = next;
                }
                if (!followedLink) return parent;
            }
        }
        catch (Exception error) when (IsPathError(error)) { }
        return null;
    }

    internal static bool IsPathError(Exception error) => error is IOException or ArgumentException or
        NotSupportedException or UnauthorizedAccessException or System.Security.SecurityException;
}

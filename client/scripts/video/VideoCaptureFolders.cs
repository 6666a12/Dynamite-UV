namespace DynamiteUniverse.Video;

/// <summary>Validates isolated capture branches without rejecting a shared mounted output directory.</summary>
internal static class VideoCaptureFolders
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static void Validate(string user, string output)
    {
        var userChain = Ancestry(user).ToArray();
        var outputChain = Ancestry(output).ToArray();
        var common = userChain.Select(item => item.FullName)
            .Intersect(outputChain.Select(item => item.FullName), PathComparer).FirstOrDefault();
        if (PathComparer.Equals(user, common) || PathComparer.Equals(output, common))
            throw new InvalidDataException("capture output and isolated user directories must be separate");

        // A reparse point above both branches does not break their separation. Below their
        // common ancestor (or on different roots), it can alias one branch into the other.
        foreach (var directory in userChain.TakeWhile(d => !PathComparer.Equals(common, d.FullName))
            .Concat(outputChain.TakeWhile(d => !PathComparer.Equals(common, d.FullName))))
            RequirePlainDirectory(directory);

        // On startup Godot may already have created engine-output directories, but authored
        // input is restricted to a single chart package. Do not accept linked engine caches.
        string[] allowed = ["charts", "logs", "shader_cache", "vulkan"];
        var root = userChain[0];
        if (root.EnumerateFiles().Any() || root.EnumerateDirectories().Any(d => !allowed.Contains(d.Name, PathComparer)))
            throw new InvalidDataException("isolated user directory contains unexpected authored data");
        foreach (var directory in root.EnumerateDirectories()) RequirePlainDirectory(directory);
        var charts = new DirectoryInfo(Path.Combine(user, "charts"));
        RequirePlainDirectory(charts);
        var packs = charts.GetDirectories();
        if (packs.Length != 1 || charts.EnumerateFiles().Any())
            throw new InvalidDataException("isolated user charts/ must contain exactly one snapshot package");
        RequirePlainDirectory(packs[0]);
    }

    private static IEnumerable<DirectoryInfo> Ancestry(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            yield return directory;
    }

    private static void RequirePlainDirectory(DirectoryInfo directory)
    {
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("capture paths must use existing directories, not links or junctions within the isolated branches");
    }
}

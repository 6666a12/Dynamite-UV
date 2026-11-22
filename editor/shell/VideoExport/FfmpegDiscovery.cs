namespace DynamiteUniverse.Editor.Shell.VideoExport;

/// <summary>Ordered, bounded FFmpeg discovery from Windows install locations and absolute PATH entries.</summary>
internal static class FfmpegDiscovery
{
    private const int PathEntryLimit = 256;
    private const int PackageDirectoryLimit = 64;
    private const string FileName = "ffmpeg.exe";

    internal sealed record Locations(
        string? Override, IReadOnlyList<string?> Paths, string CurrentDirectory, string ApplicationDirectory,
        string LocalAppData, string UserProfile, string ProgramData, string WindowsDirectory,
        IReadOnlyList<string> ProgramFiles, string? Scoop, string? ScoopGlobal, string? ChocolateyInstall)
    {
        internal static Locations Read()
        {
            var windows = OperatingSystem.IsWindows();
            static string Folder(Environment.SpecialFolder id) => Environment.GetFolderPath(id, Environment.SpecialFolderOption.DoNotVerify);
            static string? PathFrom(EnvironmentVariableTarget target)
            {
                try { return Environment.GetEnvironmentVariable("PATH", target); }
                catch (Exception error) when (LocalFfmpegPath.IsPathError(error)) { return null; }
            }
            return new(Environment.GetEnvironmentVariable("DYNA_FFMPEG"),
                windows ? [PathFrom(EnvironmentVariableTarget.Process), PathFrom(EnvironmentVariableTarget.Machine),
                    PathFrom(EnvironmentVariableTarget.User)] : [PathFrom(EnvironmentVariableTarget.Process)],
                Directory.GetCurrentDirectory(), AppContext.BaseDirectory, Folder(Environment.SpecialFolder.LocalApplicationData),
                Folder(Environment.SpecialFolder.UserProfile), Folder(Environment.SpecialFolder.CommonApplicationData),
                Folder(Environment.SpecialFolder.Windows),
                [Environment.GetEnvironmentVariable("ProgramW6432") ?? "", Folder(Environment.SpecialFolder.ProgramFiles),
                    Folder(Environment.SpecialFolder.ProgramFilesX86)], Environment.GetEnvironmentVariable("SCOOP"),
                Environment.GetEnvironmentVariable("SCOOP_GLOBAL"), Environment.GetEnvironmentVariable("ChocolateyInstall"));
        }
    }

    internal static IEnumerable<string> Find(string? remembered, Func<Locations> read, CancellationToken token)
    {
        var seen = new HashSet<string>(LocalFfmpegPath.Comparer);
        var visited = new HashSet<string>(LocalFfmpegPath.Comparer);
        string? Resolve(string? candidate)
        {
            token.ThrowIfCancellationRequested();
            var path = LocalFfmpegPath.Normalize(candidate);
            return path is not null && visited.Add(path) ? LocalFfmpegPath.ResolveFile(path) : null;
        }

        if (Resolve(remembered) is { } saved && seen.Add(saved)) yield return saved;
        var env = read();
        if (Resolve(env.Override) is { } development && seen.Add(development)) yield return development;

        var current = LocalFfmpegPath.Normalize(env.CurrentDirectory);
        var resolvedCurrent = LocalFfmpegPath.ResolveDirectory(current);
        foreach (var list in env.Paths)
        {
            foreach (var entry in (list ?? "").Split(Path.PathSeparator, PathEntryLimit + 1).Take(PathEntryLimit))
            {
                var directory = LocalFfmpegPath.Normalize(Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"')));
                if (directory is null || LocalFfmpegPath.Comparer.Equals(current, directory)) continue;
                var path = Path.Combine(directory, FileName);
                if (Resolve(path) is { } executable && !LocalFfmpegPath.Comparer.Equals(resolvedCurrent, Path.GetDirectoryName(executable)) &&
                    seen.Add(executable)) yield return executable;
            }
        }

        foreach (var candidate in InstallPaths(env, token))
        {
            if (Resolve(candidate) is { } executable && seen.Add(executable)) yield return executable;
        }
    }

    private static IEnumerable<string?> InstallPaths(Locations env, CancellationToken token)
    {
        var winget = new[] { Combine(env.LocalAppData, "Microsoft", "WinGet") }
            .Concat(env.ProgramFiles.Select(root => Combine(root, "WinGet"))).OfType<string>().Distinct(LocalFfmpegPath.Comparer);
        foreach (var root in winget)
        {
            token.ThrowIfCancellationRequested();
            yield return Combine(root, "Links", FileName);
            var packages = Combine(root, "Packages");
            foreach (var pattern in new[] { "Gyan.FFmpeg_*", "Gyan.FFmpeg.Essentials_*", "BtbN.FFmpeg.*_*" })
            {
                token.ThrowIfCancellationRequested();
                foreach (var package in Subdirectories(packages, pattern))
                    foreach (var executable in DistributionPaths(package)) yield return executable;
            }
        }

        foreach (var root in new[] { env.Scoop, Combine(env.UserProfile, "scoop"), env.ScoopGlobal, Combine(env.ProgramData, "scoop") })
            yield return Combine(root, "apps", "ffmpeg", "current", "bin", FileName);

        foreach (var root in new[] { env.ChocolateyInstall, Combine(env.ProgramData, "chocolatey") }.OfType<string>().Distinct(LocalFfmpegPath.Comparer))
        {
            foreach (var package in new[] { "ffmpeg", "ffmpeg-full", "ffmpeg-shared" })
            {
                token.ThrowIfCancellationRequested();
                foreach (var executable in DistributionPaths(Combine(root, "lib", package, "tools"))) yield return executable;
            }
            yield return Combine(root, "bin", FileName);
        }

        var portable = env.ProgramFiles.Select(root => Combine(root, "ffmpeg"))
            .Append(Combine(env.LocalAppData, "Programs", "ffmpeg"))
            .Append(Combine(env.UserProfile, "ffmpeg"))
            .Append(Combine(Path.GetPathRoot(env.WindowsDirectory), "ffmpeg"))
            .Append(Combine(env.ApplicationDirectory, "tools", "ffmpeg"));
        foreach (var root in portable.OfType<string>().Distinct(LocalFfmpegPath.Comparer))
        {
            token.ThrowIfCancellationRequested();
            foreach (var executable in DistributionPaths(root)) yield return executable;
        }
        yield return Combine(env.ApplicationDirectory, FileName);
    }

    private static IEnumerable<string?> DistributionPaths(string? root)
    {
        yield return Combine(root, "bin", FileName);
        yield return Combine(root, FileName);
        foreach (var wrapper in Subdirectories(root, "ffmpeg-*"))
        {
            yield return Combine(wrapper, "bin", FileName);
            yield return Combine(wrapper, FileName);
        }
    }

    private static string[] Subdirectories(string? root, string pattern)
    {
        if (LocalFfmpegPath.ResolveDirectory(root) is not { } directory) return [];
        try
        {
            var children = Directory.EnumerateDirectories(directory, pattern).Take(PackageDirectoryLimit + 1).ToArray();
            // A noisy scope is ignored rather than taking a filesystem-order-dependent subset.
            return children.Length <= PackageDirectoryLimit ? children.Order(LocalFfmpegPath.Comparer).ToArray() : [];
        }
        catch (Exception error) when (LocalFfmpegPath.IsPathError(error)) { return []; }
    }

    private static string? Combine(string? root, params string[] parts) => LocalFfmpegPath.Normalize(root) is { } absolute
        ? Path.Combine([absolute, .. parts]) : null;
}

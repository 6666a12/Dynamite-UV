using DynamiteUniverse.Editor.Shell.VideoExport;

namespace DynamiteUniverse.Editor.Shell;

internal static class VerificationFfmpegDiscovery
{
    internal static async Task Run(string root, Action<string, bool> check)
    {
        CheckLocations(Path.Combine(root, "ffmpeg discovery"), check);
        await CheckProbes(check);
    }

    private static void CheckLocations(string root, Action<string, bool> check)
    {
        string At(params string[] parts) => Path.Combine([root, .. parts]);
        string Create(params string[] parts)
        {
            var path = At(parts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "discovery fixture only; never execute this file");
            return path;
        }
        var saved = Create("saved", "chosen.exe");
        var development = Create("development", "test.exe");
        var current = Create("cwd", "ffmpeg.exe");
        var inherited = Create("inherited PATH", "ffmpeg.exe");
        var machine = Create("machine PATH", "ffmpeg.exe");
        var user = Create("user PATH", "ffmpeg.exe");
        var wingetLink = Create("local", "Microsoft", "WinGet", "Links", "ffmpeg.exe");
        var wingetZ = Create("local", "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg_z", "ffmpeg-8", "bin", "ffmpeg.exe");
        var wingetA = Create("local", "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg_a", "ffmpeg-7", "bin", "ffmpeg.exe");
        var wingetSystem = Create("Program Files", "WinGet", "Links", "ffmpeg.exe");
        var scoop = Create("custom Scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe");
        var scoopGlobal = Create("ProgramData", "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe");
        var chocolatey = Create("ProgramData", "chocolatey", "lib", "ffmpeg", "tools", "ffmpeg-7", "bin", "ffmpeg.exe");
        var shim = Create("ProgramData", "chocolatey", "bin", "ffmpeg.exe");
        var programFiles = Create("Program Files", "ffmpeg", "bin", "ffmpeg.exe");
        var portable = Create("profile", "ffmpeg", "bin", "ffmpeg.exe");
        var adjacent = Create("trusted shell installation", "ffmpeg.exe");
        _ = Create("local", "Microsoft", "WinGet", "Packages", "unrelated_pkg", "ffmpeg-7", "bin", "ffmpeg.exe");
        _ = Create("Program Files", "Unrelated Suite", "ffmpeg.exe");
        _ = Create("profile", "Downloads", "ffmpeg.exe");

        string DirectoryOf(string path) => Path.GetDirectoryName(path)!;
        var processPath = string.Join(Path.PathSeparator, "", ".", "relative", DirectoryOf(current), $"\"{DirectoryOf(inherited)}\"", DirectoryOf(inherited));
        var env = new FfmpegDiscovery.Locations(development, [processPath, DirectoryOf(machine), DirectoryOf(user)],
            DirectoryOf(current), DirectoryOf(adjacent), At("local"), At("profile"), At("ProgramData"), "",
            [At("Program Files")], At("custom Scoop"), null, null);
        var expected = new[] { saved, development, inherited, machine, user, wingetLink, wingetA, wingetZ, wingetSystem,
            scoop, scoopGlobal, chocolatey, shim, programFiles, portable, adjacent };
        check("FFmpeg search is deterministic, refreshes PATH, and considers only known package/portable layouts",
            FfmpegDiscovery.Find(saved, () => env, default).SequenceEqual(expected));
        check("A missing remembered FFmpeg falls through without implicitly checking CWD or incomplete/network paths",
            FfmpegDiscovery.Find(At("removed", "ffmpeg.exe"), () => env, default).SequenceEqual(expected.Skip(1)) &&
            LocalFfmpegPath.ResolveFile("ffmpeg.exe") is null &&
            LocalFfmpegPath.ResolveFile(@"C:ffmpeg.exe") is null &&
            LocalFfmpegPath.ResolveFile(@"\\dynamaker-never-look-this-up.invalid\ffmpeg\ffmpeg.exe") is null &&
            LocalFfmpegPath.ResolveFile(@"\\?\UNC\dynamaker-never-look-this-up.invalid\ffmpeg\ffmpeg.exe") is null);
        check("An existing remembered FFmpeg is resolved without reading any fallback location", FfmpegDiscovery
            .Find(saved, () => throw new InvalidOperationException("already selected"), default).First() == saved);
        CheckLocalLink(At("alias", "ffmpeg.exe"), machine, env, check);
    }

    private static void CheckLocalLink(string link, string target, FfmpegDiscovery.Locations env, Action<string, bool> check)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try { File.CreateSymbolicLink(link, target); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) when (OperatingSystem.IsWindows()) { return; } // Developer mode or privilege may be absent.
        var candidates = FfmpegDiscovery.Find(link, () => env, default).ToArray();
        check("FFmpeg allows a local package symlink and does not probe the same resolved binary again through PATH",
            candidates[0] == target && candidates.Count(item => LocalFfmpegPath.Comparer.Equals(item, target)) == 1);
    }

    private static async Task CheckProbes(Action<string, bool> check)
    {
        var calls = new List<string>();
        IEnumerable<string> Candidates(CancellationToken _) => ["old", "missing-encoders", "good", "unused"];
        Task<VideoExportCapabilities> Inspect(string candidate, CancellationToken _)
        {
            calls.Add(candidate);
            if (candidate == "old") throw new IOException("old failure");
            return Task.FromResult<VideoExportCapabilities>(candidate == "good" ? new(candidate, ["h264"]) : new(candidate, [], "incompatible"));
        }
        var budget = TimeSpan.FromSeconds(2);
        var good = await FfmpegProbe.TryCandidates(Candidates, Inspect, budget, budget, 4, default);
        check("FFmpeg skips an unstartable and an incompatible auto candidate and stops at the first compatible candidate",
            good.FfmpegPath == "good" && calls.SequenceEqual(new[] { "old", "missing-encoders", "good" }));
        calls.Clear();
        var limited = await FfmpegProbe.TryCandidates(Candidates, Inspect, budget, budget, 2, default);
        check("FFmpeg limits the number of probes and retains the first actionable failure if no candidate worked",
            limited.FfmpegPath == "old" && limited.Error == "old failure" && calls.Count == 2);

        var shortBudget = TimeSpan.FromMilliseconds(30);
        async Task<VideoExportCapabilities> HangingThenGood(string candidate, CancellationToken token)
        {
            if (candidate == "old") await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return new(candidate, ["h264"]);
        }
        var recovered = await FfmpegProbe.TryCandidates(_ => ["old", "good"], HangingThenGood, budget, shortBudget, 2, default);
        check("FFmpeg can recover from a timed-out auto candidate", recovered.FfmpegPath == "good");
        var expired = await FfmpegProbe.TryCandidates(_ => ["old", "good"], HangingThenGood, shortBudget, budget, 2, default);
        check("FFmpeg stops at the overall time budget", expired.Codecs.Count == 0 && expired.Error?.Contains("timed out") == true);

        using var canceled = new CancellationTokenSource();
        var canceledAndStopped = false;
        try
        {
            await FfmpegProbe.TryCandidates(Candidates, (_, token) =>
            {
                canceled.Cancel();
                return Task.FromCanceled<VideoExportCapabilities>(token);
            }, budget, budget, 4, canceled.Token);
        }
        catch (OperationCanceledException) { canceledAndStopped = true; }
        check("FFmpeg probing propagates caller cancellation rather than falling back", canceledAndStopped);
        var explicitMissing = await FfmpegProbe.Explicit("relative/ffmpeg.exe", default);
        check("An invalid explicit FFmpeg pick reports a failure without automatic fallback", explicitMissing is { FfmpegPath: null, Error: not null });
    }
}

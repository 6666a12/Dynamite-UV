namespace DynamiteUniverse.Editor.Shell.VideoExport;

/// <summary>Tests candidates without installing or invoking any command shell or package manager.</summary>
internal static class FfmpegProbe
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AutomaticAttempt = TimeSpan.FromSeconds(3);
    private const int MaxAutomaticAttempts = 8;

    internal static Task<VideoExportCapabilities> Auto(string? remembered, CancellationToken token) => Task.Run(() =>
        TryCandidates(t => FfmpegDiscovery.Find(remembered, FfmpegDiscovery.Locations.Read, t), Inspect,
            Budget, AutomaticAttempt, MaxAutomaticAttempts, token), token);

    internal static Task<VideoExportCapabilities> Explicit(string? requested, CancellationToken token) => Task.Run(() =>
        TryCandidates(_ => LocalFfmpegPath.ResolveFile(requested) is { } path ? [path] : [], Inspect,
            Budget, Budget, 1, token, "Choose an existing local FFmpeg executable (.exe)."), token);

    internal static async Task<VideoExportCapabilities> TryCandidates(Func<CancellationToken, IEnumerable<string>> discover,
        Func<string, CancellationToken, Task<VideoExportCapabilities>> inspect, TimeSpan budget,
        TimeSpan attemptBudget, int maxAttempts, CancellationToken token, string? notFound = null)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(token);
        total.CancelAfter(budget);
        VideoExportCapabilities? firstFailure = null;
        try
        {
            // Take limits process launches, not solely the number of filesystem lookups.
            foreach (var executable in discover(total.Token).Take(maxAttempts))
            {
                total.Token.ThrowIfCancellationRequested();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                attempt.CancelAfter(attemptBudget);
                VideoExportCapabilities result;
                try
                {
                    result = await inspect(executable, attempt.Token).ConfigureAwait(false);
                    attempt.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (!total.IsCancellationRequested)
                { result = new(executable, [], "FFmpeg probe timed out."); }
                catch (Exception error) when (error is IOException or TimeoutException or System.ComponentModel.Win32Exception)
                { result = new(executable, [], error.Message); }
                if (result.Codecs.Count != 0) return result;
                firstFailure ??= result;
            }
            total.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new(firstFailure?.FfmpegPath, [], "FFmpeg discovery or probing timed out. Locate a compatible FFmpeg executable manually.");
        }
        return firstFailure ?? new(null, [], notFound);
    }

    private static async Task<VideoExportCapabilities> Inspect(string executable, CancellationToken token)
    {
        var result = await VideoExportProcess.Run(executable, ["-hide_banner", "-encoders"], null, null, token).ConfigureAwait(false);
        if (result.ExitCode != 0) return new(executable, [], result.Diagnostic);
        var codecs = VideoExportEncoding.Available(result.Output);
        if (codecs.Length == 0) return new(executable, [], "FFmpeg does not provide AAC and a supported video encoder.");
        bool passthrough = false, timeBase = false;
        var help = await VideoExportProcess.Run(executable, ["-hide_banner", "-h", "full"], null, line =>
        {
            passthrough |= VideoExportEncoding.AdvertisesOption(line, "-fps_mode");
            timeBase |= VideoExportEncoding.AdvertisesOption(line, "-enc_time_base");
        }, token).ConfigureAwait(false);
        return help.ExitCode == 0 && passthrough && timeBase
            ? new(executable, codecs)
            : new(executable, [], "Select a newer FFmpeg build with -fps_mode and -enc_time_base support.");
    }
}

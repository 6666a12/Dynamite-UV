using System.Globalization;
using System.Text.Json;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

/// <summary>The disposable Godot recording job and transactional MP4 publisher.</summary>
internal sealed class VideoExportService
{
    internal const int Fps = 60;
    internal const string PreferenceFile = "video-export.json";
    internal sealed record Preferences(string FfmpegPath);

    public async Task<VideoExportCapabilities> ProbeAsync(string? configuredPath, CancellationToken token)
    {
        var executable = VideoExportEncoding.FindFfmpeg(configuredPath);
        if (executable is null) return new(null, []);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var result = await VideoExportProcess.Run(executable, ["-hide_banner", "-encoders"], null, null, timeout.Token);
            if (result.ExitCode != 0) return new(executable, [], result.Diagnostic);
            var codecs = VideoExportEncoding.Available(result.Output);
            if (codecs.Length == 0) return new(executable, []);
            bool passthrough = false, timeBase = false;
            var help = await VideoExportProcess.Run(executable, ["-hide_banner", "-h", "full"], null, line =>
            {
                passthrough |= VideoExportEncoding.AdvertisesOption(line, "-fps_mode");
                timeBase |= VideoExportEncoding.AdvertisesOption(line, "-enc_time_base");
            }, timeout.Token);
            return help.ExitCode == 0 && passthrough && timeBase
                ? new(executable, codecs)
                : new(executable, [], "Select a newer FFmpeg build with -fps_mode and -enc_time_base support.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(executable, [], "FFmpeg probe timed out."); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        { return new(executable, [], error.Message); }
    }

    public async Task ExportAsync(EditorDocumentSession session, VideoExportOptions options,
        IProgress<VideoExportProgress> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var worker = FindCaptureExecutable() ?? throw new FileNotFoundException(
            "DynaMakerCapture.exe not found. Install a UV candidate that includes the video capture component.");
        var encoding = VideoExportEncoding.Resolve(options);
        var destination = Path.GetFullPath(options.OutputPath);
        if (!Path.IsPathFullyQualified(options.OutputPath) || !Path.GetExtension(destination).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose an absolute MP4 output path.");
        if (Path.Exists(destination)) throw new IOException("The output already exists. Choose a different name.");
        var parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        var resultHold = ToFrames(options.ResultHoldSeconds);
        var requestedPreview = ToFrames(options.PreviewSeconds);
        // Keep the capture and MP4 staging on the chosen drive, not the system TEMP volume.
        var storage = VideoExportStorage.ForDirectory(parent);
        storage.CheckStarting();
        var temp = Directory.CreateDirectory(Path.Combine(parent, ".uv-video-" + Guid.NewGuid().ToString("N")));
        Exception? failure = null;
        try
        {
            // The worker checks its actual OS.GetUserDataDir() against this private root.
            var appData = Path.Combine(temp.FullName, "appdata");
            var user = Path.Combine(appData, "Godot", "app_userdata", "Dynamite Universe");
            var charts = Directory.CreateDirectory(Path.Combine(user, "charts")).FullName;
            var capture = Directory.CreateDirectory(Path.Combine(temp.FullName, "capture")).FullName;
            var marker = Path.Combine(capture, "capture.uvstream");
            var reportPath = Path.Combine(capture, "capture-result.json");
            progress.Report(new(VideoExportPhase.Prepare));
            // Start the snapshot on the document owner's thread, before the first await.
            var snapshot = await session.WriteExportSnapshotAsync(Path.Combine(charts, "snapshot"), token, storage.CheckSnapshotWrite);
            var preview = Math.Min(requestedPreview, ToFrames(Math.Min(60, snapshot.PreviewSeconds)));
            storage.CheckWriting();
            var partial = Path.Combine(temp.FullName, "output.mp4");
            var capabilities = await ProbeAsync(options.FfmpegPath, token).ConfigureAwait(false);
            if (capabilities.FfmpegPath is not { } ffmpeg || !capabilities.Codecs.Contains(options.Codec))
                throw new IOException(capabilities.Error ?? $"FFmpeg does not provide {encoding.Encoder} and AAC.");
            progress.Report(new(VideoExportPhase.Render));
            var stream = await new VideoExportStream(progress).Run(worker, ffmpeg, encoding, marker, partial,
                new Dictionary<string, string>
                {
                    ["APPDATA"] = appData, ["DU_CAPTURE_USER"] = user, ["DU_CAPTURE_OUT"] = capture,
                    ["DU_CAPTURE_CHART_ID"] = snapshot.ChartId,
                    ["DU_CAPTURE_PREVIEW_FRAMES"] = preview.ToString(CultureInfo.InvariantCulture),
                    ["DU_CAPTURE_RESULT_HOLD_FRAMES"] = resultHold.ToString(CultureInfo.InvariantCulture),
                }, storage, token).ConfigureAwait(false);
            storage.CheckWriting();
            if (!File.Exists(partial) || !File.Exists(reportPath))
                throw new IOException("Capture did not produce an MP4 and a scene report.");
            var log = Path.Combine(user, "logs", "godot.log");
            if (File.Exists(log) && File.ReadLines(log).FirstOrDefault(line =>
                    line.StartsWith("ERROR:", StringComparison.Ordinal) || line.StartsWith("SCRIPT ERROR:", StringComparison.Ordinal)) is { } fault)
                throw new InvalidDataException(fault);
            if (File.Exists(Path.Combine(user, "scores.json")) || File.Exists(Path.Combine(user, "settings.json")))
                throw new InvalidDataException("The recording wrote scores or settings unexpectedly.");
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, token).ConfigureAwait(false));
            var (trim, frames) = VideoExportReport.Validate(report.RootElement, snapshot.ChartId, preview, resultHold);
            if (stream.FirstSourceFrame != trim || stream.Frames != frames ||
                stream.AudioSamplesPerChannel != (long)frames * VideoExportStream.AudioFramesPerVideoFrame ||
                stream.MaxSlotsUsed is < 1 or > VideoExportStream.Slots)
                throw new InvalidDataException($"The scene and native stream disagree on the clip boundaries: scene {trim}+{frames}, stream {stream}.");
            RemoveScratch(new DirectoryInfo(appData));
            RemoveScratch(new DirectoryInfo(capture));
            progress.Report(new(VideoExportPhase.Validate));
            var decoded = await VideoExportProcess.Run(ffmpeg,
                ["-nostdin", "-v", "error", "-xerror", "-i", partial, "-map", "0:v:0", "-map", "0:a:0",
                 "-fps_mode", "passthrough", "-abort_on", "empty_output_stream", "-progress", "pipe:1", "-f", "null", "-"],
                null, null, token).ConfigureAwait(false);
            if (decoded.ExitCode != 0 || !VideoExportStream.DecodedFrameCountMatches(decoded.Output, frames))
                throw new InvalidDataException($"MP4 validation failed. {decoded.Diagnostic}");
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination, overwrite: false);
            progress.Report(new(VideoExportPhase.Validate, 1, destination));
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            try { RemoveScratch(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                var detail = $"Could not remove export scratch directory {temp.FullName}: {cleanup.Message}";
                if (failure is null) throw new IOException($"MP4 saved to {destination}. {detail}", cleanup);
                throw new IOException($"{failure.Message} {detail}", new AggregateException(failure, cleanup));
            }
        }
    }

    private static int ToFrames(double seconds) => double.IsFinite(seconds) && seconds is >= 0 and <= 60
        ? (int)Math.Round(seconds * Fps, MidpointRounding.AwayFromZero)
        : throw new ArgumentOutOfRangeException(nameof(seconds));


    private static string? FindCaptureExecutable()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "capture", "DynaMakerCapture.exe"));
        return VideoExportProcess.IsDevelopmentOverride("DYNA_VIDEO_CAPTURE", out var dev) ? dev : File.Exists(path) ? path : null;
    }

    internal static void RemoveScratch(DirectoryInfo directory)
    {
        directory.Refresh();
        if (!directory.Exists) return;
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete(); // Delete the link itself, not the target.
            return;
        }
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child) RemoveScratch(child);
            else
            {
                if ((entry.Attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == FileAttributes.ReadOnly)
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                entry.Delete();
            }
        }
        directory.Delete();
    }
}

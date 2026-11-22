using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using DynamiteUniverse.Editor.Shell.VideoExport;

namespace DynamiteUniverse.Editor.Shell;

internal static class VerificationVideoExportBoundaries
{
    internal static async Task Run(string root, IWorkbenchLocalizer text, Action<string, bool> check)
    {
        CheckReports(check);
        CheckPause(text, check);
        const string fpsMode = "   -fps_mode[:<stream_spec>]  set framerate mode";
        check("Video FFmpeg probe matches a real advertised option, not prose or a prefix",
            VideoExportEncoding.AdvertisesOption(fpsMode, "-fps_mode") &&
            !VideoExportEncoding.AdvertisesOption(" -vsync  superseded by -fps_mode", "-fps_mode") &&
            !VideoExportEncoding.AdvertisesOption(" -fps_model", "-fps_mode"));
        if (OperatingSystem.IsWindows())
        {
            VideoExportStorage.ForDirectory(root).CheckStarting();
            var scratch = Directory.CreateDirectory(Path.Combine(root, "owned"));
            var staged = Path.Combine(scratch.FullName, "read-only.wav");
            File.WriteAllText(staged, "synthetic");
            File.SetAttributes(staged, File.GetAttributes(staged) | FileAttributes.ReadOnly);
            VideoExportService.RemoveScratch(scratch);
            check("Video scratch cleanup removes an owned read-only file", !Directory.Exists(scratch.FullName));
            await CheckProcessTree(check);
        }
    }

    private static void CheckReports(Action<string, bool> check)
    {
        const string report = """
            {"version":2,"pass":true,"autoResult":true,"chartId":"test", "previewFrames":2,
             "previewAvailableFrames":1,"recordedPreviewFrames":1,"resultHoldFrames":0,
             "trimFrame":80,"totalFrames":601,
             "frames":{"preview-playing":92,"preview-ended":93,"start":93,"handoff":109,"result-revealed":600,"result-held":600}}
            """;
        (int, int) Parse(string json, int preview = 2)
        {
            using var doc = JsonDocument.Parse(json);
            return VideoExportReport.Validate(doc.RootElement, "test", preview, 0);
        }
        bool Rejects(string json)
        {
            try { _ = Parse(json); return false; }
            catch (InvalidDataException) { return true; }
        }
        check("A worker can clamp a 2-frame audition to one decoded frame with exact scene marks", Parse(report) == (80, 521));
        check("Recorder report rejects extra audition/reveal frames and old protocol",
            Rejects(report.Replace("ended\":93", "ended\":94", StringComparison.Ordinal)) &&
            Rejects(report.Replace("revealed\":600", "revealed\":599", StringComparison.Ordinal)) &&
            Rejects(report.Replace("version\":2", "version\":1", StringComparison.Ordinal)));
        var silent = report.Replace("previewFrames\":2", "previewFrames\":0", StringComparison.Ordinal)
            .Replace("Frames\":1", "Frames\":0", StringComparison.Ordinal)
            .Replace("trimFrame\":80", "trimFrame\":109", StringComparison.Ordinal)
            .Replace("\"preview-playing\":92,\"preview-ended\":93,", "", StringComparison.Ordinal);
        check("A zero-frame audition trims directly to handoff", Parse(silent, 0) == (109, 492));
    }

    private static void CheckPause(IWorkbenchLocalizer text, Action<string, bool> check)
    {
        var clock = new WorkbenchPreviewClock(new TimelineView(), new TimingView(), new TextBlock(), text);
        var requests = new List<(bool, long)>();
        bool Send(bool playing, long id) { requests.Add((playing, id)); return true; }
        clock.Toggle(Send); // Play sent, stage has not acknowledged.
        clock.Pause(Send);
        clock.Pause(Send); // Pause sent, no resume.
        check("Video explicit pause supersedes pending Play and never flips a pending Pause",
            requests.SequenceEqual(new[] { (true, 1L), (false, 2L) }));
    }

    private static async Task CheckProcessTree(Action<string, bool> check)
    {
        var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var command = "$p = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -PassThru -NoNewWindow " +
            "-ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30'; [Console]::WriteLine($p.Id)";
        var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var result = await VideoExportProcess.Run(shell, ["-NoProfile", "-Command", command], null, null, timeout.Token);
        var hasId = int.TryParse(result.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid);
        check("A root exit closes inherited output handles and kills its still-running child",
            result.ExitCode == 0 && watch.Elapsed < TimeSpan.FromSeconds(8) && hasId && ProcessExited(pid));
    }

    private static bool ProcessExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.WaitForExit(1000); }
        catch (ArgumentException) { return true; }
    }
}

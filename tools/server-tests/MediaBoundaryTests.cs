using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;
using DynamiteUniverse.Server.Hosting;
using DynamiteUniverse.Shared.Audio;

internal static class MediaBoundaryTests
{
    private static object? Invoke(string type, string method, params object[] args)
    {
        try
        {
            return typeof(ServerApplication).Assembly.GetType("DynamiteUniverse.Server.Catalog." + type)!
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    internal static async Task Run(Action<bool, string> check)
    {
        using var alreadyCancelled = new CancellationTokenSource(); alreadyCancelled.Cancel();
        try { Invoke("PackageInspector", "Inspect", "missing.zip", "unused", alreadyCancelled.Token); check(false, "Cancelled inspection rejected"); }
        catch (OperationCanceledException) { check(true, "Cancelled inspection stops before opening archive"); }
        var root = Path.Combine(Path.GetTempPath(), "du-media-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cancel = new CancellationTokenSource();
        try
        {
            var marker = Path.Combine(root, "started");
            var worker = Task.Run(() => Invoke("CatalogMediaValidator", "Run", "dotnet",
                new[] { typeof(MediaBoundaryTests).Assembly.Location, "--media-cancellation-worker", marker }, 4096, cancel.Token));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(marker)) { if (worker.IsCompleted) await worker; await Task.Delay(20, timeout.Token); }
            var pid = int.Parse(await File.ReadAllTextAsync(marker, timeout.Token));
            var watch = Stopwatch.StartNew(); cancel.Cancel();
            try { await worker.WaitAsync(TimeSpan.FromSeconds(5)); check(false, "Media cancellation propagated"); }
            catch (OperationCanceledException) { check(watch.Elapsed.TotalSeconds < 5, "Media subprocess observes request cancellation promptly"); }
            try { using var child = Process.GetProcessById(pid); check(child.WaitForExit(3000), "Cancelled media subprocess is terminated"); }
            catch (ArgumentException) { check(true, "Cancelled media subprocess is terminated"); }
        }
        finally { cancel.Cancel(); Directory.Delete(root, true); }
    }

    // Runs with the PostgreSQL/media integration suite, whose prerequisite is FFmpeg.
    internal static async Task Compatibility(CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "du-media-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
            foreach (var seconds in new[] { 16, 480 })
            {
                var audio = Path.Combine(root, seconds + ".flac");
                var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo", "-t", seconds.ToString(), "-c:a", "flac", "-sample_fmt", "s16", audio })
                    start.ArgumentList.Add(arg);
                using (var process = Process.Start(start)!)
                {
                    try { await process.WaitForExitAsync(ct); if (process.ExitCode != 0) throw new Exception("FLAC fixture generation failed"); }
                    finally { if (!process.HasExited) process.Kill(true); }
                }
                var archive = Path.Combine(root, seconds + ".zip");
                using (var input = new ZipArchive(new MemoryStream(LocalMockPackage.Create(repo!.FullName))))
                using (var output = ZipFile.Open(archive, ZipArchiveMode.Create))
                {
                    foreach (var entry in input.Entries.Where(e => e.FullName != "audio.wav"))
                    {
                        using var from = entry.Open(); using var to = output.CreateEntry(entry.FullName).Open();
                        if (entry.FullName == "meta.json")
                        {
                            var meta = JsonNode.Parse(from)!; meta["audio"] = "audio.flac";
                            using var writer = new StreamWriter(to); writer.Write(meta.ToJsonString());
                        }
                        else from.CopyTo(to);
                    }
                    output.CreateEntryFromFile(audio, "audio.flac");
                }
                var stage = Path.Combine(root, "stage-" + seconds); Directory.CreateDirectory(stage);
                var rejected = false;
                try { Invoke("PackageInspector", "Inspect", archive, stage, ct); }
                catch (InvalidDataException ex) when (ex.Message == "FLAC exceeds game playback budget") { rejected = true; }
                if (rejected != (seconds == 480)) throw new Exception("Incorrect FLAC publication budget");
                if (seconds == 16) _ = FlacDecoder.Decode(await File.ReadAllBytesAsync(audio, ct), FlacDecoderOptions.GamePlayback);
                Console.WriteLine("PASS FLAC publication budget: " + seconds + " seconds " + (rejected ? "rejected" : "accepted and playable"));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}

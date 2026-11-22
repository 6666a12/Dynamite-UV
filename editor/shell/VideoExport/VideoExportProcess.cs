using System.Diagnostics;
using System.Text;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

internal static class VideoExportProcess
{
    internal sealed record Result(int ExitCode, string Output, string Diagnostic);
    private const int OutputLimit = 256 * 1024;

    public static async Task<Result> Run(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environment, Action<string>? onLine,
        CancellationToken token, Action? monitor = null)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
            },
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var (key, value) in environment) process.StartInfo.Environment[key] = value;
        using var job = VideoExportJob.Create();
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var drainTimeout = TimeSpan.FromSeconds(5);
        if (!process.Start()) throw new IOException($"Could not start {executable}.");
        Task<string[]>? readers = null;
        try
        {
            VideoExportJob.Attach(job, process);
            var stdout = Read(process.StandardOutput, onLine, readCancellation.Token);
            var stderr = Read(process.StandardError, null, readCancellation.Token);
            readers = Task.WhenAll(stdout, stderr);
            var exited = process.WaitForExitAsync();
            while (!exited.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                monitor?.Invoke();
                if (stdout.IsFaulted) await stdout.ConfigureAwait(false);
                if (stderr.IsFaulted) await stderr.ConfigureAwait(false);
                await Task.WhenAny(exited, Task.Delay(250, token)).ConfigureAwait(false);
            }
            await exited.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // The root is done. Close the job before draining any inherited write handles.
            job?.Dispose();
            var result = await readers.WaitAsync(drainTimeout, token).ConfigureAwait(false);
            var diagnostic = string.IsNullOrWhiteSpace(result[1]) ? result[0] : result[1];
            return new(process.ExitCode, result[0], diagnostic.Length <= 2000 ? diagnostic.Trim() : diagnostic[^2000..].Trim());
        }
        finally
        {
            readCancellation.Cancel();
            try
            {
                if (!process.HasExited)
                {
                    // Also cover a process that could not be assigned to the job.
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                }
            }
            finally
            {
                job?.Dispose();
                try { await process.WaitForExitAsync().WaitAsync(drainTimeout).ConfigureAwait(false); }
                finally { if (readers is not null) await ObserveReaders(readers, drainTimeout).ConfigureAwait(false); }
            }
        }
    }

    private static async Task ObserveReaders(Task<string[]> readers, TimeSpan timeout)
    {
        try { await readers.WaitAsync(timeout).ConfigureAwait(false); }
        catch (Exception error) { Debug.WriteLine(error); }
    }

    private static async Task<string> Read(StreamReader reader, Action<string>? onLine, CancellationToken token)
    {
        var text = new StringBuilder();
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            onLine?.Invoke(line);
            text.AppendLine(line.Length > OutputLimit ? line[^OutputLimit..] : line);
            if (text.Length > OutputLimit) text.Remove(0, text.Length - OutputLimit);
        }
        return text.ToString();
    }

    public static bool IsDevelopmentOverride(string name, out string? path)
    {
        path = Environment.GetEnvironmentVariable(name);
        return path is not null && Path.IsPathFullyQualified(path) && File.Exists(path);
    }
}

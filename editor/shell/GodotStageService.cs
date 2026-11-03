using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DynamiteUniverse.Editor.Shell;

public sealed record StageMessage(string Event, JsonElement Data);

public sealed class GodotStageService : IDisposable
{
    private readonly object _writeLock = new();
    private Process? _process;
    private bool _disposed;

    public event Action<StageMessage>? MessageReceived;
    public event Action<string>? RuntimeError;
    public nint NativeHandle { get; private set; }
    public int ProcessId => _process?.Id ?? 0;
    public bool Running => _process is { HasExited: false };

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GodotStageService));
        if (Running) return;
        var editorRoot = FindEditorRoot();
        var engine = Environment.GetEnvironmentVariable("DYNAMAKER_GODOT") ?? Path.GetFullPath(Path.Combine(
            editorRoot, "../../godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe"));
        if (!File.Exists(engine)) throw new FileNotFoundException("Godot 4.7.1 Mono executable not found", engine);
        var start = new ProcessStartInfo(engine)
        {
            WorkingDirectory = editorRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("--path");
        start.ArgumentList.Add(editorRoot);
        _process = Process.Start(start) ?? throw new InvalidOperationException("Unable to launch Godot");
        _process.EnableRaisingEvents = true;
        _process.OutputDataReceived += (_, e) => ParseLine(e.Data);
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) RuntimeError?.Invoke(e.Data);
        };
        _process.Exited += (_, _) =>
        {
            NativeHandle = 0;
            MessageReceived?.Invoke(new StageMessage("exited", JsonDocument.Parse("{}").RootElement.Clone()));
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Send(object command)
    {
        if (!Running) return;
        lock (_writeLock)
        {
            _process!.StandardInput.WriteLine(JsonSerializer.Serialize(command));
            _process.StandardInput.Flush();
        }
    }

    public void Load(string chartJson, string title, string artist, string? audioPath, double bar) =>
        Send(new { command = "load", chart = chartJson, title, artist, audioPath, bar });
    public void TogglePlayback(bool playing) => Send(new { command = "play", playing });
    public void Seek(double bar) => Send(new { command = "seek", bar });
    public void SetPlayerSpeed(double multiplier) => Send(new { command = "playerSpeed", multiplier });
    public void SetPlaybackRate(double multiplier) => Send(new { command = "playbackRate", multiplier });
    public void SetAuthoring(string tool, string type, string track, double width, int grid) =>
        Send(new { command = "authoring", tool, type, track, width, grid });
    public void Capture(string path) => Send(new { command = "capture", path });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeHandle = 0;
        if (_process is null) return;
        try
        {
            Send(new { command = "quit" });
            if (!_process.WaitForExit(1500)) _process.Kill(true);
            _process.WaitForExit();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException) { }
        _process.Dispose();
    }

    private void ParseLine(string? line)
    {
        if (line is null || !line.StartsWith("DYNA_STAGE ", StringComparison.Ordinal)) return;
        try
        {
            using var json = JsonDocument.Parse(line["DYNA_STAGE ".Length..]);
            var root = json.RootElement;
            var name = root.GetProperty("event").GetString() ?? "unknown";
            if (name == "ready" && root.TryGetProperty("hwnd", out var hwnd))
                NativeHandle = (nint)hwnd.GetInt64();
            MessageReceived?.Invoke(new StageMessage(name, root.Clone()));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            RuntimeError?.Invoke("Invalid stage event: " + ex.Message);
        }
    }

    private static string FindEditorRoot()
    {
        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DynaMakerUv.Editor.csproj")) &&
                    File.Exists(Path.Combine(directory.FullName, "project.godot")))
                    return directory.FullName;
                directory = directory.Parent!;
            }
        }
        throw new DirectoryNotFoundException("The DynaMaker UV Godot editor project was not found");
    }
}

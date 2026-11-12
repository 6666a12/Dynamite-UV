using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DynamiteUniverse.Editor.Shell;

public sealed record StageMessage(string Event, JsonElement Data);
public sealed record StageAudioSnapshot(double Second, double AudioSecond, bool Playing,
    bool Paused, bool PanSuspended, int Loads, int Restarts);

public sealed class GodotStageService : IDisposable
{
    private readonly object _writeLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long,
        TaskCompletionSource<StageAudioSnapshot>> _audioSnapshots = new();
    private long _nextRequestId;
    private Process? _process;
    private bool _disposed;

    public event Action<StageMessage>? MessageReceived;
    public event Action<string>? RuntimeError;
    public nint NativeHandle { get; private set; }
    public int ProcessId => _process?.Id ?? 0;
    public bool Running => _process is { HasExited: false };

    public void Start(nint parentHandle)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GodotStageService));
        if (Running) return;
        var start = StageLaunch.Create(AppContext.BaseDirectory, Directory.GetCurrentDirectory(),
            Environment.GetEnvironmentVariable("DYNAMAKER_GODOT"));
        start.ArgumentList.Add("--resolution");
        start.ArgumentList.Add("1x1");
        if (parentHandle != 0)
        {
            start.ArgumentList.Add("--wid");
            start.ArgumentList.Add(parentHandle.ToString());
        }
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

    public void Load(string chartJson, string title, string artist, string? audioPath, double bar,
        long revision, IReadOnlyList<string> selection, string difficulty = "", int? level = null) =>
        Send(new { command = "load", chart = chartJson, title, artist, audioPath, bar, revision, selection, difficulty, level });
    public void Update(string chartJson, string title, string artist, string? audioPath,
        long revision, IReadOnlyList<string> selection, string difficulty = "", int? level = null) =>
        Send(new { command = "update", chart = chartJson, title, artist, audioPath, revision, selection, difficulty, level });
    public void TogglePlayback(bool playing) => Send(new { command = "play", playing });
    public void Seek(double bar) => Send(new { command = "seek", bar });
    public void SetPlayerSpeed(double multiplier) => Send(new { command = "playerSpeed", multiplier });
    public void SetPlaybackRate(double multiplier) => Send(new { command = "playbackRate", multiplier });
    public void SetAuthoring(string tool, string type, string track, double width, int grid, bool positionSnap = true) =>
        Send(new { command = "authoring", tool, type, track, width, grid, positionSnap });
    public void Capture(string path) => Send(new { command = "capture", path });

    public async Task<StageAudioSnapshot> AudioSnapshotAsync()
    {
        if (!Running) throw new InvalidOperationException("The stage is not running");
        var id = Interlocked.Increment(ref _nextRequestId);
        var source = new TaskCompletionSource<StageAudioSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        _audioSnapshots[id] = source;
        try
        {
            Send(new { command = "audioSnapshot", requestId = id });
            return await source.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { _audioSnapshots.TryRemove(id, out _); }
    }

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
            if (name == "audioSnapshot" && _audioSnapshots.TryRemove(
                    root.GetProperty("requestId").GetInt64(), out var source))
                source.TrySetResult(new StageAudioSnapshot(root.GetProperty("second").GetDouble(),
                    root.GetProperty("audioSecond").GetDouble(), root.GetProperty("audioPlaying").GetBoolean(),
                    root.GetProperty("audioPaused").GetBoolean(), root.GetProperty("panSuspended").GetBoolean(),
                    root.GetProperty("audioLoads").GetInt32(), root.GetProperty("audioRestarts").GetInt32()));
            MessageReceived?.Invoke(new StageMessage(name, root.Clone()));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            RuntimeError?.Invoke("Invalid stage event: " + ex.Message);
        }
    }

}

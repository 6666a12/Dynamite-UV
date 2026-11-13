using System.Diagnostics;
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
    public int ProcessId { get { lock (_writeLock) return _process?.Id ?? 0; } }
    public bool Running { get { lock (_writeLock) return !_disposed && _process is { HasExited: false }; } }

    public void Start(nint parentHandle)
    {
        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is { HasExited: false }) return;
            _process?.Dispose();
            _process = null;
            NativeHandle = 0;
            FailSnapshots(new IOException("The stage restarted"));
            var start = StageLaunch.Create(AppContext.BaseDirectory, Directory.GetCurrentDirectory(),
                Environment.GetEnvironmentVariable("DYNAMAKER_GODOT"));
            start.ArgumentList.Add("--resolution");
            start.ArgumentList.Add("1x1");
            if (parentHandle != 0)
            {
                start.ArgumentList.Add("--wid");
                start.ArgumentList.Add(parentHandle.ToString());
            }
            var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to launch Godot");
            _process = process;
            process.OutputDataReceived += (_, e) =>
            {
                lock (_writeLock) { if (!_disposed && ReferenceEquals(process, _process)) ParseLine(e.Data); }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                lock (_writeLock)
                    if (!_disposed && ReferenceEquals(process, _process) && !string.IsNullOrWhiteSpace(e.Data))
                        RuntimeError?.Invoke(e.Data);
            };
            process.Exited += (_, _) =>
            {
                lock (_writeLock)
                {
                    if (_disposed || !ReferenceEquals(process, _process)) return;
                    NativeHandle = 0;
                    FailSnapshots(new IOException("The stage exited"));
                    MessageReceived?.Invoke(new StageMessage("exited", JsonSerializer.SerializeToElement(new { })));
                }
            };
            process.EnableRaisingEvents = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
    }

    public void Send(object command) => TrySend(command);

    private bool TrySend(object command)
    {
        var line = JsonSerializer.Serialize(command);
        string? error = null;
        lock (_writeLock)
        {
            if (_disposed || _process is not { HasExited: false }) return false;
            try { Write(_process, line); return true; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                error = ex.Message;
                FailSnapshots(ex);
            }
        }
        RuntimeError?.Invoke("Stage write failed: " + error);
        return false;
    }

    private static void Write(Process process, string line)
    {
        process.StandardInput.WriteLine(line);
        process.StandardInput.Flush();
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
        var id = Interlocked.Increment(ref _nextRequestId);
        var source = new TaskCompletionSource<StageAudioSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        _audioSnapshots[id] = source;
        try
        {
            if (!TrySend(new { command = "audioSnapshot", requestId = id }))
                throw new IOException("The stage is not available");
            return await source.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { _audioSnapshots.TryRemove(id, out _); }
    }

    public void Dispose()
    {
        Process? process;
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
            NativeHandle = 0;
            process = _process;
            _process = null;
            FailSnapshots(new ObjectDisposedException(nameof(GodotStageService)));
            if (process is null) return;
            try { if (!process.HasExited) Write(process, "{\"command\":\"quit\"}"); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException) { }
        }
        // Never hold the event/write lock while waiting for redirected readers to finish.
        try
        {
            if (!process.WaitForExit(1500))
            {
                process.Kill(true);
                process.WaitForExit(1500);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        { RuntimeError?.Invoke("Stage shutdown failed: " + ex.Message); }
        finally { process.Dispose(); }
    }

    private void FailSnapshots(Exception error)
    {
        foreach (var id in _audioSnapshots.Keys)
            if (_audioSnapshots.TryRemove(id, out var source)) source.TrySetException(error);
    }

    internal void ParseLine(string? line)
    {
        if (line is null || !line.StartsWith("DYNA_STAGE ", StringComparison.Ordinal)) return;
        long? request = null;
        try
        {
            using var json = JsonDocument.Parse(line["DYNA_STAGE ".Length..]);
            var root = json.RootElement;
            var name = root.GetProperty("event").GetString() ?? "unknown";
            if (name == "ready" && root.TryGetProperty("hwnd", out var hwnd))
                NativeHandle = (nint)hwnd.GetInt64();
            if (name == "audioSnapshot")
            {
                request = root.GetProperty("requestId").GetInt64();
                var snapshot = new StageAudioSnapshot(root.GetProperty("second").GetDouble(),
                    root.GetProperty("audioSecond").GetDouble(), root.GetProperty("audioPlaying").GetBoolean(),
                    root.GetProperty("audioPaused").GetBoolean(), root.GetProperty("panSuspended").GetBoolean(),
                    root.GetProperty("audioLoads").GetInt32(), root.GetProperty("audioRestarts").GetInt32());
                if (!double.IsFinite(snapshot.Second) || !double.IsFinite(snapshot.AudioSecond))
                    throw new FormatException("Non-finite audio position");
                if (_audioSnapshots.TryRemove(request.Value, out var source)) source.TrySetResult(snapshot);
            }
            MessageReceived?.Invoke(new StageMessage(name, root.Clone()));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            if (request is { } id && _audioSnapshots.TryRemove(id, out var source)) source.TrySetException(ex);
            RuntimeError?.Invoke("Invalid stage event: " + ex.Message);
        }
    }
}

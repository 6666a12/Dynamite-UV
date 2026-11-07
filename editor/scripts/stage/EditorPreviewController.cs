using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor;

public partial class EditorPreviewController : Control
{
    private readonly EditorStageIpc _ipc = new();
    private readonly HashSet<int> _selection = new();
    private EditorStagePreview _stage = null!;
    private AudioStreamPlayer _audio = null!;
    private V2RuntimeAdapterResult? _adapted;
    private bool _playing;
    private double _second;
    private double _bar;
    private double _playerSpeed = 1.0;
    private double _playbackRate = 1.0;
    private double _tickAccumulator;
    private string? _audioPath;
    private long _revision = -1;
    private int _audioLoads;
    private int _audioRestarts;

    public override void _EnterTree()
    {
        if (DisplayServer.GetName() != "headless") GetWindow().Visible = false;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _stage = new EditorStagePreview { Name = "StagePreview" };
        AddChild(_stage);
        _stage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _stage.NotePicked += OnNotePicked;
        _stage.NotePressed += OnNotePressed;
        _stage.PathNotePicked += OnPathNotePicked;
        _stage.PlaceRequested += _ipc.SendPlacement;
        _stage.PathAddRequested += _ipc.SendPathAdd;
        _stage.PathMoveRequested += _ipc.SendPathMove;
        _stage.NoteMoveRequested += _ipc.SendNoteMove;
        _stage.NoteResizeRequested += _ipc.SendNoteResize;
        _stage.PanStarted += OnPanStarted;
        _stage.PanTimeDelta += OnPanTimeDelta;
        _stage.PanEnded += OnPanEnded;
        _stage.WheelSteps += steps =>
        {
            if (_adapted is null || _stage.HasActiveDrag) return;
            var speed = VisualScrollMath.SpeedPixelsPerSecond(GameplayStageGeometry.BaseFallSpeedPx,
                _adapted.Metadata.ScrollMap.RuntimeSpeedAt(_bar), _playerSpeed);
            if (speed > 0) Seek(_adapted.Metadata.BpmTimeline.RuntimeBarAt(_second + steps * 100d / speed));
        };
        _audio = new AudioStreamPlayer { Name = "PreviewAudio" };
        AddChild(_audio);
        if (DisplayServer.GetName() != "headless")
        {
            GetWindow().Position = Vector2I.Zero;
            GetWindow().Visible = false;
        }
        _ipc.Start();
        _ipc.SendReady(NativeWindowHandle(), GetWindow().Size);
        SendTick();
    }

    public override void _Process(double delta)
    {
        _ipc.Drain(HandleCommand);
        if (_ipc.Ended) GetTree().Quit();
        if (_playing && _adapted is not null)
        {
            if (!_panAudioSuspended) _second += delta * _playbackRate;
            if (_audio.Stream is { } boundedStream) _second = Math.Min(_second, boundedStream.GetLength());
            if (!_panAudioSuspended && _second >= 0 && _audio.Stream is not null &&
                !_audio.Playing && !_audio.StreamPaused)
                RestartAudio();
            _bar = _adapted.Metadata.BpmTimeline.RuntimeBarAt(_second);
            if (!_panAudioSuspended) _stage.AdvancePlayback(_second, delta);
            if (_audio.Stream is { } stream && _second >= stream.GetLength())
            {
                _second = stream.GetLength();
                SetPlaying(false);
            }
        }
        _tickAccumulator += delta;
        if (_tickAccumulator < .25) return;
        _tickAccumulator %= .25;
        SendTick();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) GetTree().Quit();
    }

    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true }) GetWindow().GrabFocus();
        if (input is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
            _stage.HandlePointerInput(input, viewportCoordinates: true);
        if (input is InputEventMouseMotion && _stage.HasActiveDrag)
            _stage.HandlePointerInput(input, viewportCoordinates: true);
        if (input is InputEventKey { Pressed: true, Echo: false } key)
            _ipc.SendKey(key.Keycode, key.CtrlPressed, key.ShiftPressed, key.AltPressed);
    }

    public override void _ExitTree() => _ipc.Dispose();

    private void HandleCommand(string command, System.Text.Json.JsonElement root)
    {
        switch (command)
        {
            case "load": Load(root, false); break;
            case "update": Load(root, true); break;
            case "play": SetPlaying(root.GetProperty("playing").GetBoolean()); break;
            case "seek": Seek(root.GetProperty("bar").GetDouble()); break;
            case "playerSpeed": SetPlayerSpeed(root.GetProperty("multiplier").GetDouble()); break;
            case "playbackRate": SetPlaybackRate(root.GetProperty("multiplier").GetDouble()); break;
            case "authoring":
                _stage.SetAuthoring(root.GetProperty("tool").GetString()!,
                    Enum.Parse<V2NoteType>(root.GetProperty("type").GetString()!),
                    Enum.Parse<EditorTrack>(root.GetProperty("track").GetString()!),
                    root.GetProperty("width").GetDouble(), root.GetProperty("grid").GetInt32(),
                    !root.TryGetProperty("positionSnap", out var positionSnap) || positionSnap.GetBoolean());
                break;
            case "audioSnapshot":
                _ipc.SendAudioSnapshot(root.GetProperty("requestId").GetInt64(), _second,
                    _audio.GetPlaybackPosition(), _audio.Playing, _audio.StreamPaused,
                    _panAudioSuspended, _audioLoads, _audioRestarts);
                break;
            case "capture": Capture(root.GetProperty("path").GetString()!); break;
            case "quit": GetTree().Quit(); break;
            default: _ipc.SendError($"unknown command {command}"); break;
        }
    }

    private void Load(System.Text.Json.JsonElement root, bool incremental)
    {
        var revision = root.GetProperty("revision").GetInt64();
        if (revision <= _revision) return;
        var chart = V2JsonDecoder.DecodeChart(root.GetProperty("chart").GetString()!, "editor-preview",
            requireMainJudgement: false);
        var adapted = V2RuntimeAdapter.Adapt(chart, root.GetProperty("title").GetString(),
            requireMainJudgement: false);
        var audioPath = root.TryGetProperty("audioPath", out var audioElement) &&
            audioElement.ValueKind == System.Text.Json.JsonValueKind.String ? audioElement.GetString() : null;
        if (!incremental || audioPath != _audioPath)
        {
            var stream = EditorAudioLoader.Load(audioPath);
            _audio.Stop();
            _audio.Stream = stream;
            _audioPath = audioPath;
            _audioLoads++;
            if (incremental && _playing && _second >= 0 && stream is not null) RestartAudio();
        }
        if (!incremental)
        {
            SetPlaying(false);
            _second = 0;
        }
        _adapted = adapted;
        _bar = adapted.Metadata.BpmTimeline.RuntimeBarAt(_second);
        _stage.SetChart(adapted, incremental, _second);
        _stage.SetPlayerSpeed(_playerSpeed);
        _stage.SuspendPlayback(_panAudioSuspended);
        ApplySelection(root, adapted);
        if (!incremental && root.TryGetProperty("bar", out var bar)) Seek(bar.GetDouble());
        _revision = revision;
        SendInput($"{(incremental ? "updated" : "loaded")} chart={chart.ChartId} notes={adapted.RuntimeChart.AllNotes.Count()} audio={_audio.Stream is not null} revision={revision} playing={_playing} selection={_selection.Count}{AudioTelemetry()}");
        SendTick();
    }

    private void ApplySelection(System.Text.Json.JsonElement root, V2RuntimeAdapterResult adapted)
    {
        _selection.Clear();
        if (root.TryGetProperty("selection", out var selection) &&
            selection.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var runtimeBySource = adapted.SourceIdsByRuntimeId.ToDictionary(item => item.Value, item => item.Key);
            foreach (var source in selection.EnumerateArray().Select(item => item.GetString()))
                if (source is not null && runtimeBySource.TryGetValue(source, out var runtime)) _selection.Add(runtime);
        }
        _stage.SetSelection(_selection);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing && _adapted is not null;
        _stage.SetPlaying(_playing);
        _stage.SuspendPlayback(_panAudioSuspended);
        _audio.PitchScale = (float)_playbackRate;
        // A playing PAN suspends the preview clock and the audio for the drag: playback
        // commands must not unpause or restart the stream until the pan ends.
        _audio.StreamPaused = !_playing || _panAudioSuspended;
        if (_playing && !_panAudioSuspended && _second >= 0 && _audio.Stream is not null &&
            !_audio.Playing)
            RestartAudio();
        if (!_playing) _audio.Stop();
        SendTick();
    }

    private void Seek(double bar)
    {
        if (_adapted is null) return;
        _bar = Math.Max(0, bar);
        _second = _adapted.Metadata.BpmTimeline.RuntimeSecondsAt(_bar);
        if (_audio.Stream is { } stream) _second = Math.Min(_second, stream.GetLength());
        _bar = _adapted.Metadata.BpmTimeline.RuntimeBarAt(_second);
        _stage.CurrentBar = _bar;
        _stage.SuspendPlayback(_panAudioSuspended);
        if (_playing && !_panAudioSuspended && _audio.Stream is not null && _second >= 0)
        {
            _audio.Stop();
            _audio.StreamPaused = false;
            RestartAudio();
        }
        SendTick();
    }

    private void SetPlayerSpeed(double multiplier)
    {
        _playerSpeed = Math.Clamp(multiplier, .1, 2.0);
        _stage.SetPlayerSpeed(_playerSpeed);
        SendInput($"playerSpeed={_playerSpeed:0.0}{AudioTelemetry()}");
        SendTick();
    }

    private void SetPlaybackRate(double multiplier)
    {
        _playbackRate = Math.Clamp(multiplier, .2, 2.0);
        _audio.PitchScale = (float)_playbackRate;
        SendInput($"playbackRate={_playbackRate:0.0}{AudioTelemetry()}");
        SendTick();
    }

    private void OnNotePicked(int id, bool additive) => SelectNote(id, additive, false);
    private void OnNotePressed(int id, bool additive) => SelectNote(id, additive, false);
    private void OnPathNotePicked(int id) => SelectNote(id, false, true);

    // PAN seeks the preview clock only; it never touches the document. Grab-the-world
    // metaphor: dragging down pulls later content into view. While playing, audio is paused
    // for the duration of the drag and restarts at the panned position on release.
    private bool _panAudioSuspended;

    private void OnPanStarted()
    {
        // Suspend for the whole gesture, even if it starts paused: Space/seek/load must not
        // make audio run while PAN owns the preview clock.
        _panAudioSuspended = true;
        _stage.SuspendPlayback(true);
        _audio.StreamPaused = true;
    }

    private void OnPanTimeDelta(double designDy)
    {
        if (_adapted is null) return;
        var metadata = _adapted.Metadata;
        var speed = VisualScrollMath.SpeedPixelsPerSecond(GameplayStageGeometry.BaseFallSpeedPx,
            metadata.ScrollMap.RuntimeSpeedAt(_bar), _playerSpeed);
        if (speed <= 0) return;
        var second = _second + designDy / speed;
        if (_audio.Stream is { } stream) second = Math.Min(second, stream.GetLength());
        second = Math.Max(0, second);
        if (Math.Abs(second - _second) < 1e-9) return;
        _second = second;
        _bar = metadata.BpmTimeline.RuntimeBarAt(second);
        _stage.CurrentBar = _bar;
        _stage.SuspendPlayback(_panAudioSuspended);
    }

    private void OnPanEnded()
    {
        if (!_panAudioSuspended) return;
        _panAudioSuspended = false;
        _stage.SuspendPlayback(false);
        if (_playing && _audio.Stream is not null && _second >= 0)
        {
            _audio.Stop();
            _audio.StreamPaused = false;
            RestartAudio();
        }
    }

    private void RestartAudio()
    {
        if (!_playing || _panAudioSuspended || _audio.Stream is null || _second < 0) return;
        _audio.StreamPaused = false;
        _audio.Play((float)_second);
        _audioRestarts++;
    }

    // Observed audio telemetry for shell verification: GetStreamPlayback reports the real
    // player position and the counters expose loads/restarts, so a test can tell continuous
    // playback apart from the stage clock advancing on frame delta alone.
    private string AudioTelemetry()
    {
        var second = !_audio.Playing ? -1d :
            _audio.GetStreamPlayback()?.GetPlaybackPosition() ?? -1d;
        return $" audioSecond={second.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
               $" audioRestarts={_audioRestarts} audioLoads={_audioLoads}";
    }

    private void SelectNote(int id, bool additive, bool path)
    {
        if (additive)
        {
            if (!_selection.Remove(id)) _selection.Add(id);
        }
        else
        {
            _selection.Clear();
            _selection.Add(id);
        }
        if (_selection.Count == 0) _selection.Add(id);
        _stage.SetSelection(_selection);
        if (_adapted is null) return;
        var sourceIds = _selection.Select(runtime => _adapted.SourceIdsByRuntimeId[runtime]).ToArray();
        var noteIds = _selection.Select(runtime =>
        {
            var selected = _adapted.RuntimeChart.AllNotes.Single(item => item.Id == runtime);
            return selected.V2Metadata?.ParentSourceId ?? _adapted.SourceIdsByRuntimeId[runtime];
        }).Distinct().ToArray();
        var primary = _adapted.SourceIdsByRuntimeId[id];
        var note = _adapted.RuntimeChart.AllNotes.Single(item => item.Id == id);
        _ipc.SendSelection(sourceIds, primary, note.V2Metadata?.ParentSourceId ?? primary, noteIds,
            path && note.V2Metadata?.IsPathNode == true ? primary : null);
    }

    private void SendInput(string detail) => _ipc.SendInput(detail);

    private void SendTick()
    {
        if (_adapted is not null) _bar = _adapted.Metadata.BpmTimeline.RuntimeBarAt(_second);
        _ipc.SendTick(_second, _bar, _playing, _playerSpeed, _playbackRate);
    }

    private async void Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        if (error != Error.Ok) _ipc.SendError($"capture failed: {error}");
    }

    private static long NativeWindowHandle() => DisplayServer.GetName() == "headless" ? 0L :
        Convert.ToInt64(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
}

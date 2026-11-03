using Godot;
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
        _stage.PathNotePicked += OnPathNotePicked;
        _stage.PlaceRequested += _ipc.SendPlacement;
        _stage.PathAddRequested += _ipc.SendPathAdd;
        _stage.PathMoveRequested += _ipc.SendPathMove;
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
            _second += delta * _playbackRate;
            if (_second >= 0 && _audio.Stream is not null && !_audio.Playing && !_audio.StreamPaused)
                _audio.Play((float)_second);
            _bar = _adapted.Metadata.BpmTimeline.RuntimeBarAt(_second);
            _stage.CurrentBar = _bar;
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
        if (input is InputEventKey { Pressed: true, Echo: false } key)
            _ipc.SendKey(key.Keycode, key.CtrlPressed, key.ShiftPressed, key.AltPressed);
    }

    public override void _ExitTree() => _ipc.Dispose();

    private void HandleCommand(string command, System.Text.Json.JsonElement root)
    {
        switch (command)
        {
            case "load": Load(root); break;
            case "play": SetPlaying(root.GetProperty("playing").GetBoolean()); break;
            case "seek": Seek(root.GetProperty("bar").GetDouble()); break;
            case "playerSpeed": SetPlayerSpeed(root.GetProperty("multiplier").GetDouble()); break;
            case "playbackRate": SetPlaybackRate(root.GetProperty("multiplier").GetDouble()); break;
            case "authoring":
                _stage.SetAuthoring(root.GetProperty("tool").GetString()!,
                    Enum.Parse<V2NoteType>(root.GetProperty("type").GetString()!),
                    Enum.Parse<EditorTrack>(root.GetProperty("track").GetString()!),
                    root.GetProperty("width").GetDouble(), root.GetProperty("grid").GetInt32());
                break;
            case "capture": Capture(root.GetProperty("path").GetString()!); break;
            case "quit": GetTree().Quit(); break;
            default: _ipc.SendError($"unknown command {command}"); break;
        }
    }

    private void Load(System.Text.Json.JsonElement root)
    {
        var chart = V2JsonDecoder.DecodeChart(root.GetProperty("chart").GetString()!, "editor-preview",
            requireMainJudgement: false);
        var adapted = V2RuntimeAdapter.Adapt(chart, root.GetProperty("title").GetString(),
            requireMainJudgement: false);
        var audioPath = root.TryGetProperty("audioPath", out var audioElement) &&
            audioElement.ValueKind == System.Text.Json.JsonValueKind.String ? audioElement.GetString() : null;
        var stream = EditorAudioLoader.Load(audioPath);
        SetPlaying(false);
        _audio.Stop();
        _audio.Stream = stream;
        _adapted = adapted;
        _selection.Clear();
        _stage.SetChart(adapted);
        _stage.SetPlayerSpeed(_playerSpeed);
        _second = 0;
        _bar = adapted.Metadata.BpmTimeline.RuntimeBarAt(0);
        _stage.CurrentBar = _bar;
        if (root.TryGetProperty("bar", out var bar)) Seek(bar.GetDouble());
        SendInput($"loaded chart={chart.ChartId} notes={adapted.RuntimeChart.AllNotes.Count()} audio={stream is not null}");
        SendTick();
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing && _adapted is not null;
        _audio.PitchScale = (float)_playbackRate;
        _audio.StreamPaused = !_playing;
        if (_playing && _second >= 0 && _audio.Stream is not null && !_audio.Playing)
            _audio.Play((float)_second);
        if (!_playing) _audio.Stop();
        SendTick();
    }

    private void Seek(double bar)
    {
        if (_adapted is null) return;
        _bar = Math.Max(0, bar);
        _second = _adapted.Metadata.BpmTimeline.RuntimeSecondsAt(_bar);
        _stage.CurrentBar = _bar;
        if (_playing && _audio.Stream is not null && _second >= 0)
        {
            _audio.Stop();
            _audio.Play((float)_second);
        }
        SendTick();
    }

    private void SetPlayerSpeed(double multiplier)
    {
        _playerSpeed = Math.Clamp(multiplier, .1, 2.0);
        _stage.SetPlayerSpeed(_playerSpeed);
        SendInput($"playerSpeed={_playerSpeed:0.0}");
        SendTick();
    }

    private void SetPlaybackRate(double multiplier)
    {
        _playbackRate = Math.Clamp(multiplier, .2, 2.0);
        _audio.PitchScale = (float)_playbackRate;
        SendInput($"playbackRate={_playbackRate:0.0}");
        SendTick();
    }

    private void OnNotePicked(int id, bool additive) => SelectNote(id, additive, false);
    private void OnPathNotePicked(int id) => SelectNote(id, false, true);

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
        var primary = _adapted.SourceIdsByRuntimeId[id];
        var note = _adapted.RuntimeChart.AllNotes.Single(item => item.Id == id);
        _ipc.SendSelection(sourceIds, primary, note.V2Metadata?.ParentSourceId ?? primary,
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

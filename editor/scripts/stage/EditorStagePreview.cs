using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

public partial class EditorStagePreview : Control
{
    private readonly GameplayStageRenderer _renderer;
    private readonly EditorPlacementOverlay _placementOverlay;
    private readonly Node2D _playbackRoot;
    private readonly EditorAutoPreview _autoPreview;
    private readonly Node2D _hudRoot;
    private EditorPreviewHud? _hud;
    private bool _gamePreview;
    public event Action? PauseRequested;
    public event Action? TransportRequested;
    private V2RuntimeAdapterResult? _adapted;
    private bool _playing;
    private bool _playbackVisible;
    private bool _scrubbing;
    private readonly Node2D _stageRoot;
    private readonly Control _selectionOverlay;
    private readonly EditorStageAuthoring _authoring = new();
    private readonly EditorMarqueeSelection _marquee = new();
    private EditorTrack _selectionTrack = EditorTrack.Center;
    private EditorMarqueeSelection.Candidate[] _marqueeCandidates = Array.Empty<EditorMarqueeSelection.Candidate>();
    internal IReadOnlySet<int> MarqueePreview => _marquee.Preview;
    public event Action<IReadOnlySet<int>>? NotesBoxSelected;
    private IReadOnlyList<Note> _notes = Array.Empty<Note>();
    private IReadOnlySet<int> _selection = new HashSet<int>();
    private V2RuntimeChartMetadata? _metadata;
    private Rect2 _stageRect;
    private float _stageScale = 1f;
    private float _panOffsetX;
    private float _panOffsetY;
    private double _currentBar;
    private double _currentTime;
    private double _playerSpeed = 1.0;

    public event Action<int, bool>? NotePicked;
    public event Action<int, bool>? NotePressed;
    public event Action<int>? PathNotePicked;
    public event Action<StagePlacementRequest>? PlaceRequested;
    public event Action<StagePathAddRequest>? PathAddRequested;
    public event Action<StagePathMoveRequest>? PathMoveRequested;
    public event Action<StageNoteMoveRequest>? NoteMoveRequested;
    public event Action<StageNoteResizeRequest>? NoteResizeRequested;
    public event Action? PanStarted;
    public event Action<double>? PanTimeDelta;
    public event Action? PanEnded;
    public event Action<double>? WheelSteps;
    public bool WheelReversed { get; set; }
    public event Action<Vector2>? ContextRequested;

    public void CancelGesture()
    {
        _marquee.Cancel();
        _authoring.Cancel();
        RefreshPlacementPreview();
        _selectionOverlay.QueueRedraw();
    }

    public void SetEffectLevel(HitEffectLevel level)
    {
        if (_hud is not null) _hud.Motion = level switch
        {
            HitEffectLevel.Off => DynamiteUniverse.Ui.UiMotionMode.Off,
            HitEffectLevel.Reduced => DynamiteUniverse.Ui.UiMotionMode.Reduced,
            _ => DynamiteUniverse.Ui.UiMotionMode.Full,
        };
        _autoPreview.SetEffectLevel(level);
        if (_playbackVisible && _adapted is not null)
        {
            _autoPreview.Reset(_adapted, _currentTime, _playerSpeed);
            _autoPreview.SetPaused(!_playing && !_scrubbing);
        }
    }

    public bool HasActiveDrag => _authoring.HasActiveDrag || _marquee.Active;

    public EditorStagePreview()
    {
        ClipContents = true;
        _authoring.PathNotePicked += id => PathNotePicked?.Invoke(id);
        _authoring.PlaceRequested += request => PlaceRequested?.Invoke(request);
        _authoring.PathAddRequested += request => PathAddRequested?.Invoke(request);
        _authoring.PathMoveRequested += request => PathMoveRequested?.Invoke(request);
        _authoring.NotePressed += (id, additive) => NotePressed?.Invoke(id, additive);
        _authoring.NoteMoveRequested += request => NoteMoveRequested?.Invoke(request);
        _authoring.NoteResizeRequested += request => NoteResizeRequested?.Invoke(request);
        _authoring.PanStarted += () => PanStarted?.Invoke();
        _authoring.PanDelta += OnPanDelta;
        _authoring.PanEnded += () => PanEnded?.Invoke();
        MouseFilter = MouseFilterEnum.Stop;
        _stageRoot = new Node2D { Name = "GameplayStage" };
        AddChild(_stageRoot);
        _renderer = new GameplayStageRenderer
        {
            Position = new Vector2(0f, 40f),
            ShowControlNodes = true,
        };
        _stageRoot.AddChild(_renderer);
        _renderer.BuildCommunityStageChrome(40f, gameplayHud: true);
        _renderer.SetNotes(_notes);
        _playbackRoot = new Node2D { Name = "AutoPlayback", Position = new Vector2(0, 40), Visible = false };
        _stageRoot.AddChild(_playbackRoot);
        _autoPreview = new(_playbackRoot);
        _placementOverlay = new EditorPlacementOverlay
        {
            Position = new Vector2(0f, 40f), Size = new Vector2(1920f, 1080f),
            MouseFilter = MouseFilterEnum.Ignore, ZIndex = 5,
        };
        _stageRoot.AddChild(_placementOverlay);
        _selectionOverlay = new Control { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 10 };
        AddChild(_selectionOverlay);
        _selectionOverlay.Draw += DrawSelection;
        _hudRoot = new Node2D { Name = "EditorHud", ZIndex = 15 };
        AddChild(_hudRoot);
        Resized += LayoutStage;
        GuiInput += input => HandlePointerInput(input);
    }

    public override void _Ready()
    {
        _hud = new EditorPreviewHud(_hudRoot, () => PauseRequested?.Invoke(), () => TransportRequested?.Invoke());
        LayoutStage();
    }

    public void SetHudChart(V2RuntimeAdapterResult chart, string title, string difficulty, int? level, double audioLength) =>
        _hud?.SetChart(chart, title, difficulty, level, audioLength);

    public void SetGamePreview(bool enabled)
    {
        if (_gamePreview == enabled) return;
        CancelGesture();
        _gamePreview = enabled;
        _hud?.SetGamePreview(enabled);
        _selectionOverlay.Visible = _placementOverlay.Visible = !enabled;
        if (enabled && _adapted is not null)
        {
            _autoPreview.Reset(_adapted, _currentTime, _playerSpeed);
            SetPlaybackVisible(true);
            _autoPreview.SetPaused(!_playing);
        }
        else if (!_playing) SetPlaybackVisible(false);
        LayoutStage();
    }

    public double CurrentBar
    {
        get => _currentBar;
        set
        {
            _scrubbing = false;
            _currentBar = value;
            _currentTime = _metadata?.BpmTimeline.RuntimeSecondsAt(value) ?? value * 1.6;
            // Assignments are discontinuities (seek/PAN/edit), never playback ticks.
            if ((_playing || _gamePreview) && _adapted is not null) _autoPreview.Reset(_adapted, _currentTime, _playerSpeed);
            SetPlaybackVisible(_playing || _gamePreview);
            if (_gamePreview) _autoPreview.SetPaused(!_playing);
            _renderer.SetPreviewBar(value);
            _renderer.SetPreviewTime(_currentTime);
            RefreshPlacementPreview();
            _selectionOverlay.QueueRedraw();
        }
    }

    private bool _objectSnap;
    public void SetObjectSnap(bool enabled) { _objectSnap = enabled; RefreshMagnetViewport(); }
    private void RefreshMagnetViewport() => _authoring.SetMagnet(_objectSnap, _stageScale,
        new Rect2(-_panOffsetX, -40 - _panOffsetY, 1920, 1080));

    public void SetAuthoring(string tool, V2NoteType type, EditorTrack track, double width, int gridDivisor,
        bool positionSnap = true)
    {
        _marquee.Cancel();
        _selectionTrack = track;
        _authoring.Configure(tool, type, track, width, gridDivisor, positionSnap);
        RefreshPlacementPreview();
    }

    public void SetChart(V2RuntimeAdapterResult adapted, bool preserveCurrentBar = false, double? second = null)
    {
        _marquee.Cancel();
        _authoring.Cancel();
        _adapted = adapted;
        _metadata = adapted.Metadata;
        _renderer.NoteVisibilityFilter = note => note.V2Metadata is not { } source ||
            adapted.RuntimeIdsBySourceId.ContainsKey(source.SourceId);
        _notes = adapted.RuntimeChart.AllNotes.ToArray();
        _renderer.SetPreviewDistanceProvider((note, time) => (float)DistancePixels(note, time));
        _renderer.SetNotes(_notes);
        _selection = new HashSet<int>();
        if (!preserveCurrentBar)
        {
            _panOffsetX = 0;
            _panOffsetY = 0;
            LayoutStage();
        }
        CurrentBar = second is { } time ? adapted.Metadata.BpmTimeline.RuntimeBarAt(time)
            : preserveCurrentBar ? _currentBar : 0;
    }

    public void SetPlayerSpeed(double multiplier)
    {
        _playerSpeed = multiplier;
        if (_playbackVisible) _autoPreview.SetSpeed(multiplier);
        _renderer.SetPreviewTime(_currentTime);
        RefreshPlacementPreview();
        _selectionOverlay.QueueRedraw();
    }

    public void SetPlaying(bool playing)
    {
        if (playing && !_playbackVisible && _adapted is not null)
        {
            _autoPreview.Reset(_adapted, _currentTime, _playerSpeed);
            SetPlaybackVisible(true);
        }
        _scrubbing = false;
        _playing = playing;
        // Freeze the Auto frame on pause; an explicit seek restores the editable static view.
        _autoPreview.SetPaused(!playing);
    }

    public void AdvancePlayback(double second, double delta)
    {
        if (!_playing || _adapted is null) return;
        _currentTime = second;
        _currentBar = _adapted.Metadata.BpmTimeline.RuntimeBarAt(second);
        _autoPreview.Advance(second, delta);
        RefreshPlacementPreview();
        _selectionOverlay.QueueRedraw();
    }

    public void ScrubTo(double second)
    {
        if (_adapted is null || second == _currentTime) return;
        if (!_playbackVisible || second < _currentTime)
            _autoPreview.Reset(_adapted, Math.Min(second, _currentTime), _playerSpeed);
        if (second > _currentTime) _autoPreview.ScrubForward(second);
        _currentTime = second;
        _currentBar = _adapted.Metadata.BpmTimeline.RuntimeBarAt(second);
        _scrubbing = !_playing;
        SetPlaybackVisible(true);
        _autoPreview.SetPaused(false);
        RefreshPlacementPreview();
        _selectionOverlay.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_hud is not null)
        {
            _hud.Playing = _playing;
            _hud.Update(_currentTime, delta, _renderer);
        }
        // Wheel navigation moves chart time discretely; effects still decay in real time.
        if (!_scrubbing || _playbackRoot.ProcessMode == ProcessModeEnum.Disabled) return;
        _autoPreview.Advance(_currentTime, delta);
        _selectionOverlay.QueueRedraw();
    }

    public void SuspendPlayback(bool suspended) => _autoPreview.SetPaused(suspended || (!_playing && !_scrubbing));

    private void SetPlaybackVisible(bool visible)
    {
        _playbackVisible = visible;
        _playbackRoot.Visible = visible;
        _renderer.NoteLayer.Visible = !visible;
    }

    public void SetSelection(IReadOnlySet<int> selection)
    {
        _selection = selection;
        _selectionOverlay.QueueRedraw();
    }

    private void RefreshPlacementPreview()
    {
        _placementOverlay.SetPlacement(_authoring.PlacementPreview, _metadata, _currentTime, _playerSpeed, _notes);
        if (_marquee.Active) UpdateMarquee(_marquee.Pointer);
    }

    private double DistancePixels(Note note, double time) => _metadata is null ? 0 :
        VisualScrollMath.DistancePixels(note.Second, time, GameplayStageGeometry.BaseFallSpeedPx,
            _metadata.ScrollMap.RuntimeSpeedAt(note.BarTime), _playerSpeed,
            note.V2Metadata?.ScrollSpeedMultiplier ?? 1.0);

    private void LayoutStage()
    {
        var scale = Mathf.Min(Size.X / 1920f, Size.Y / 1080f);
        _stageScale = scale;
        RefreshMagnetViewport();
        var stageSize = new Vector2(1920f, 1080f) * scale;
        _stageRect = new Rect2((Size - stageSize) * .5f, stageSize);
        _hudRoot.Position = _stageRect.Position;
        _hudRoot.Scale = Vector2.One * scale;
        _stageRoot.Position = _stageRect.Position + (_gamePreview ? Vector2.Zero : new Vector2(_panOffsetX * scale, _panOffsetY * scale));
        _stageRoot.Scale = Vector2.One * scale;
        _selectionOverlay.Size = Size;
        _selectionOverlay.QueueRedraw();
    }

    private void OnPanDelta(Vector2 designDelta)
    {
        _panOffsetX += designDelta.X;
        if (Input.IsKeyPressed(Key.Shift)) _panOffsetY += designDelta.Y;
        LayoutStage();
        if (!Input.IsKeyPressed(Key.Shift)) PanTimeDelta?.Invoke(designDelta.Y);
    }

    internal Rect2? SelectionBounds(Note note)
    {
        // Selection IDs belong to the document; only the frame follows the live visual body.
        // Paused Auto retains its frame, while paused seek returns to editable static notes.
        if (_playbackVisible) return _autoPreview.SelectionBounds(note.Id);
        var remaining = DistancePixels(note, _currentTime);
        return remaining < -100f || remaining > GameplayStageGeometry.PreviewTravelPx(note.Track) + 100f
            ? null : GameplayVisualMapper.BoundsAt(note, (float)remaining, grow: 10f);
    }

    private void DrawSelection()
    {
        DrawDragGhost();
        DrawPathGhost();
        if (_authoring.MagnetGuide is { } guide)
        {
            var offset = _stageRect.Position + new Vector2(_panOffsetX, 40 + _panOffsetY) * _stageScale;
            var from = offset + guide.From * _stageScale;
            var to = offset + guide.To * _stageScale;
            _selectionOverlay.DrawLine(from, to, new Color("35e0ff"), 1.5f, true);
            _selectionOverlay.DrawCircle(to, 4, new Color("35e0ff"), false, 1.5f);
            _selectionOverlay.DrawString(ThemeDB.FallbackFont, from + new Vector2(10, -10),
                guide.Feature == 0 ? "CENTER" : guide.Feature < 0 ? "START EDGE" : "END EDGE", fontSize: 14, modulate: new Color("35e0ff"));
        }
        if (_marquee.Active && _marquee.Moved)
        {
            var start = MarqueeScreenPoint(_marquee.Start);
            var end = MarqueeScreenPoint(_marquee.End);
            var rect = new Rect2(start, end - start).Abs().Intersection(new Rect2(Vector2.Zero, Size));
            if (rect.HasArea())
            {
                _selectionOverlay.DrawRect(rect, new Color(.21f, .88f, 1f, .10f));
                _selectionOverlay.DrawRect(rect, new Color("35e0ff"), false, 1.5f);
            }
            _selectionOverlay.DrawString(ThemeDB.FallbackFont, _marquee.Pointer + new Vector2(12, -12),
                $"{_marquee.Preview.Count}", fontSize: 18, modulate: new Color("35e0ff"));
        }
        var selected = _marquee.Active && _marquee.Moved ? _marquee.Preview : _selection;
        foreach (var note in _notes)
        {
            if (!selected.Contains(note.Id)) continue;
            if (SelectionBounds(note) is not { } rect) continue;
            rect.Position += new Vector2(0f, 40f);
            var scale = _stageRect.Size.X / 1920f;
            rect = new Rect2(_stageRect.Position + new Vector2(_panOffsetX * scale, _panOffsetY * scale) + rect.Position * scale,
                rect.Size * scale);
            var color = note.Type is NoteType.HoldNode or NoteType.MixerNode
                ? new Color("ff4d8f") : new Color("35e0ff");
            _selectionOverlay.DrawRect(rect, new Color(color, .12f));
            _selectionOverlay.DrawRect(rect, color, false, 2f);
            const float corner = 10f;
            _selectionOverlay.DrawLine(rect.Position, rect.Position + new Vector2(corner, 0f), Colors.White, 2f, true);
            _selectionOverlay.DrawLine(rect.Position, rect.Position + new Vector2(0f, corner), Colors.White, 2f, true);
        }
    }

    private void DrawDragGhost()
    {
        if (_metadata is not { } metadata ||
            !_authoring.TryGetSelectPreview(metadata, _currentTime, _playerSpeed,
                out var runtimeId, out var groupRuntimeIds, out var request, out var resize))
            return;
        var members = _notes.Where(note => groupRuntimeIds.Contains(note.Id)).ToArray();
        // Only parent heads change position. MovePathNoteCommand shifts node times, not their
        // centers/widths, so only heads constrain the shared center delta.
        var heads = members.Where(note => note.V2Metadata?.IsPathNode != true).ToArray();
        var move = (Bar: ExactBarTime.Zero, Center: 0d);
        if (request is not null)
        {
            var anchor = heads.FirstOrDefault(note => note.V2Metadata?.ParentSourceId == request.NoteId);
            if (anchor is null) return;
            var delta = ExactTime(request.EndBar) - ExactTime(request.StartBar);
            delta = EditorStagePlacement.ResolveMoveDelta(anchor.V2Metadata!.ExactTime, delta,
                _authoring.GridDivisor, request.Snap);
            move = ClampedMove(heads, delta, EditorStagePlacement.ResolvePositionDelta(
                anchor.Position + anchor.Width / 2d, request.EndCenter - request.StartCenter, request.PositionStep));
        }
        var color = new Color("ffd166");
        foreach (var note in members)
        {
            if (resize is not null && note.Id != runtimeId) continue;
            var center = note.Position + note.Width * .5;
            var width = note.Width;
            if (resize is not null)
            {
                var span = EditorStagePlacement.ResizeSnappedFromEdge(center, width,
                    resize.Edge, resize.MoveStartEdge, resize.WidthStep);
                center = span.Center;
                width = span.Width;
            }
            else if (note.V2Metadata?.IsPathNode != true) center = EditorSpatialMath.Add(center, move.Center);
            var bar = (note.V2Metadata!.ExactTime + move.Bar).ToDouble();
            if (GhostRect(note, metadata, bar, center, width) is not { } rect) continue;
            rect = new Rect2(_stageRect.Position + new Vector2(_panOffsetX * _stageScale, _panOffsetY * _stageScale) +
                rect.Position * _stageScale, rect.Size * _stageScale);
            _selectionOverlay.DrawRect(rect, new Color(color, .18f));
            _selectionOverlay.DrawRect(rect, color, false, 2f);
        }
    }

    private void DrawPathGhost()
    {
        if (_metadata is not { } metadata ||
            _authoring.PathPreview(metadata, _currentTime, _playerSpeed) is not { } request) return;
        var note = _notes.FirstOrDefault(item => item.V2Metadata?.SourceId == (request.NodeId ?? request.NoteId));
        if (note?.V2Metadata is not { } source) return;
        var delta = EditorStagePlacement.ResolveMoveDelta(source.ExactTime,
            ExactTime(request.EndBar) - ExactTime(request.StartBar), _authoring.GridDivisor, request.Snap);
        var originalCenter = note.Position + note.Width / 2d;
        var center = EditorSpatialMath.Add(originalCenter, EditorStagePlacement.ResolvePositionDelta(originalCenter,
            request.EndCenter - request.StartCenter, request.PositionStep));
        if (GhostRect(note, metadata, (source.ExactTime + delta).ToDouble(), center, note.Width) is not { } rect) return;
        rect = new Rect2(_stageRect.Position + new Vector2(_panOffsetX * _stageScale, _panOffsetY * _stageScale) +
            rect.Position * _stageScale, rect.Size * _stageScale);
        _selectionOverlay.DrawRect(rect, new Color("ffd166"), false, 2f);
    }

    private static ExactBarTime ExactTime(double bar) => ExactBarTime.FromFraction(
        (long)Math.Round(Math.Max(0, bar) * 1_000_000d), 1_000_000);

    private static (ExactBarTime Bar, double Center) ClampedMove(IReadOnlyList<Note> heads,
        ExactBarTime delta, double centerDelta)
    {
        var earliest = heads.Min(note => note.V2Metadata!.ExactTime);
        if (earliest + delta < ExactBarTime.Zero) delta = ExactBarTime.Zero - earliest;
        return (delta, centerDelta);
    }

    private Rect2? GhostRect(Note note, V2RuntimeChartMetadata metadata,
        double bar, double center, double width)
    {
        var second = metadata.BpmTimeline.RuntimeSecondsAt(bar);
        var remaining = VisualScrollMath.DistancePixels(second, _currentTime,
            GameplayStageGeometry.BaseFallSpeedPx, metadata.ScrollMap.RuntimeSpeedAt(bar), _playerSpeed,
            note.V2Metadata?.ScrollSpeedMultiplier ?? 1d);
        if (remaining < -100f || remaining > GameplayStageGeometry.PreviewTravelPx(note.Track) + 100f)
            return null;
        var ghost = new Note
        {
            Id = note.Id,
            SubNoteId = note.SubNoteId,
            Type = note.Type,
            Track = note.Track,
            BarTime = bar,
            Position = center - width * .5,
            Width = width,
            Second = second,
            BakedSecond = second,
        };
        var rect = GameplayVisualMapper.BoundsAt(ghost, (float)remaining, grow: 10f);
        rect.Position += new Vector2(0f, 40f);
        return rect;
    }

    private EditorMarqueeSelection.Point MarqueePoint(Vector2 pointer)
    {
        var point = (pointer - _stageRect.Position) / _stageScale - new Vector2(_panOffsetX, 40 + _panOffsetY);
        var time = EditorStagePlacement.BarTimeAt(_marquee.Active ? _marquee.Track : _selectionTrack,
            point.X, point.Y, _metadata!, _currentTime, _playerSpeed, _authoring.GridDivisor, snap: false);
        var center = EditorStagePlacement.CenterAt(_marquee.Active ? _marquee.Track : _selectionTrack, point.X, point.Y);
        return new(time, center);
    }

    private void UpdateMarquee(Vector2 pointer) => _marquee.Update(pointer, MarqueePoint(pointer), _marqueeCandidates);

    private Vector2 MarqueeScreenPoint(EditorMarqueeSelection.Point point)
    {
        var bar = point.Time.ToDouble();
        var remaining = VisualScrollMath.DistancePixels(_metadata!.BpmTimeline.RuntimeSecondsAt(bar), _currentTime,
            GameplayStageGeometry.BaseFallSpeedPx, _metadata.ScrollMap.RuntimeSpeedAt(bar), _playerSpeed);
        var design = GameplayStageGeometry.PositionAt(EditorStagePlacement.ToRuntimeTrack(_marquee.Track), point.Center, (float)remaining);
        return _stageRect.Position + (new Vector2(design.X, design.Y) + new Vector2(_panOffsetX, 40 + _panOffsetY)) * _stageScale;
    }

    private void BeginMarquee(Vector2 pointer, bool additive)
    {
        // Source heads retain exact time/center even after Auto has recycled all visual bodies.
        var source = _selectionTrack switch
        {
            EditorTrack.Left => _metadata!.SourceChart.NotesLeft,
            EditorTrack.Right => _metadata!.SourceChart.NotesRight,
            _ => _metadata!.SourceChart.NotesCenter,
        };
        _marqueeCandidates = source.Select(note => new EditorMarqueeSelection.Candidate(
            _metadata!.RuntimeIdsBySourceId[note.Id], note.Time, note.Center)).ToArray();
        var start = MarqueePoint(pointer);
        _marquee.Begin(_selectionTrack, pointer, start, _selection, additive);
    }

    public void HandlePointerInput(InputEvent input, bool viewportCoordinates = false)
    {
        if (_metadata is null || _gamePreview) return;
        if (input is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            WheelSteps?.Invoke((wheel.ButtonIndex == MouseButton.WheelDown ? 1d : -1d) * wheel.Factor *
                (WheelReversed ? -1d : 1d));
            AcceptEvent();
            return;
        }
        Vector2? localPosition = input switch
        {
            InputEventMouseButton { ButtonIndex: MouseButton.Left } button => viewportCoordinates
                ? GetGlobalTransformWithCanvas().AffineInverse() * button.Position
                : button.Position,
            InputEventMouseMotion motion => viewportCoordinates
                ? GetGlobalTransformWithCanvas().AffineInverse() * motion.Position
                : motion.Position,
            InputEventMouseButton { ButtonIndex: MouseButton.Right } button => viewportCoordinates
                ? GetGlobalTransformWithCanvas().AffineInverse() * button.Position
                : button.Position,
            _ => null,
        };
        if (localPosition is not { } position) return;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            if (HasActiveDrag) CancelGesture();
            else
            {
                var hitPoint = (position - _stageRect.Position) / _stageScale - new Vector2(_panOffsetX, 40 + _panOffsetY);
                if (!_notes.Any(note => SelectionBounds(note)?.HasPoint(hitPoint) == true))
                {
                    CancelGesture();
                    ContextRequested?.Invoke(position);
                }
            }
            AcceptEvent();
            return;
        }
        if (_marquee.Active && (input is InputEventMouseMotion ||
            input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }))
        {
            UpdateMarquee(position);
            if (input is InputEventMouseButton)
            {
                var selected = _marquee.Finish();
                SetSelection(selected);
                NotesBoxSelected?.Invoke(selected);
            }
            _selectionOverlay.QueueRedraw();
            AcceptEvent();
            return;
        }
        var designPoint = (position - _stageRect.Position) / (_stageRect.Size.X / 1920f);
        // PAN uses an unshifted drag reference so changing the offset does not feed back into
        // the next delta; selection and resizing after a pan use the shifted chart coordinates.
        designPoint -= new Vector2(_authoring.Tool == "PAN" ? 0f : _panOffsetX,
            40f + (_authoring.Tool == "PAN" ? 0f : _panOffsetY));
        if (_authoring.Tool == "SELECT" && input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click &&
            _authoring.HitNote(designPoint, _notes, _metadata, _currentTime, _playerSpeed) < 0)
        {
            BeginMarquee(position, click.ShiftPressed || Input.IsKeyPressed(Key.Shift));
            AcceptEvent();
            return;
        }
        if (_authoring.HandleInput(input, designPoint, _notes, _metadata, _currentTime,
                _playerSpeed, _selection))
        {
            RefreshPlacementPreview();
            _selectionOverlay.QueueRedraw();
            AcceptEvent();
            return;
        }
        if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return;
        var best = _authoring.HitNote(designPoint, _notes, _metadata, _currentTime, _playerSpeed);
        if (best >= 0) NotePicked?.Invoke(best, Input.IsKeyPressed(Key.Shift));
        AcceptEvent();
    }
}

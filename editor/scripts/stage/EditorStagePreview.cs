using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

public partial class EditorStagePreview : Control
{
    private readonly GameplayStageRenderer _renderer;
    private readonly Node2D _stageRoot;
    private readonly Control _selectionOverlay;
    private readonly EditorStageAuthoring _authoring = new();
    private IReadOnlyList<Note> _notes = Array.Empty<Note>();
    private IReadOnlySet<int> _selection = new HashSet<int>();
    private V2RuntimeChartMetadata? _metadata;
    private Rect2 _stageRect;
    private double _currentBar;
    private double _currentTime;
    private double _playerSpeed = 1.0;

    public event Action<int, bool>? NotePicked;
    public event Action<int>? PathNotePicked;
    public event Action<StagePlacementRequest>? PlaceRequested;
    public event Action<StagePathAddRequest>? PathAddRequested;
    public event Action<StagePathMoveRequest>? PathMoveRequested;

    public EditorStagePreview()
    {
        ClipContents = true;
        _authoring.PathNotePicked += id => PathNotePicked?.Invoke(id);
        _authoring.PlaceRequested += request => PlaceRequested?.Invoke(request);
        _authoring.PathAddRequested += request => PathAddRequested?.Invoke(request);
        _authoring.PathMoveRequested += request => PathMoveRequested?.Invoke(request);
        MouseFilter = MouseFilterEnum.Stop;
        _stageRoot = new Node2D { Name = "GameplayStage" };
        AddChild(_stageRoot);
        _renderer = new GameplayStageRenderer
        {
            Position = new Vector2(0f, 40f),
            ShowControlNodes = true,
        };
        _stageRoot.AddChild(_renderer);
        _renderer.BuildCommunityStageChrome(40f);
        _renderer.SetNotes(_notes);
        _selectionOverlay = new Control { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 10 };
        AddChild(_selectionOverlay);
        _selectionOverlay.Draw += DrawSelection;
        Resized += LayoutStage;
        GuiInput += input => HandlePointerInput(input);
    }

    public override void _Ready() => LayoutStage();

    public double CurrentBar
    {
        get => _currentBar;
        set
        {
            _currentBar = value;
            _currentTime = _metadata?.BpmTimeline.RuntimeSecondsAt(value) ?? value * 1.6;
            _renderer.SetPreviewBar(value);
            _renderer.SetPreviewTime(_currentTime);
            _selectionOverlay.QueueRedraw();
        }
    }

    public void SetAuthoring(string tool, V2NoteType type, EditorTrack track, double width, int gridDivisor) =>
        _authoring.Configure(tool, type, track, width, gridDivisor);

    public void SetChart(V2RuntimeAdapterResult adapted)
    {
        _authoring.Cancel();
        _metadata = adapted.Metadata;
        _notes = adapted.RuntimeChart.AllNotes.ToArray();
        _renderer.SetPreviewDistanceProvider((note, time) => (float)DistancePixels(note, time));
        _renderer.SetNotes(_notes);
        _selection = new HashSet<int>();
        CurrentBar = 0;
    }

    public void SetPlayerSpeed(double multiplier)
    {
        _playerSpeed = multiplier;
        _renderer.SetPreviewTime(_currentTime);
        _selectionOverlay.QueueRedraw();
    }

    public void SetSelection(IReadOnlySet<int> selection)
    {
        _selection = selection;
        _selectionOverlay.QueueRedraw();
    }

    private double DistancePixels(Note note, double time) => _metadata is null ? 0 :
        VisualScrollMath.DistancePixels(note.Second, time, GameplayStageGeometry.BaseFallSpeedPx,
            _metadata.ScrollMap.RuntimeSpeedAt(note.BarTime), _playerSpeed,
            note.V2Metadata?.ScrollSpeedMultiplier ?? 1.0);

    private void LayoutStage()
    {
        var scale = Mathf.Min(Size.X / 1920f, Size.Y / 1080f);
        var stageSize = new Vector2(1920f, 1080f) * scale;
        _stageRect = new Rect2((Size - stageSize) * .5f, stageSize);
        _stageRoot.Position = _stageRect.Position;
        _stageRoot.Scale = Vector2.One * scale;
        _selectionOverlay.Size = Size;
        _selectionOverlay.QueueRedraw();
    }

    private void DrawSelection()
    {
        foreach (var note in _notes)
        {
            if (!_selection.Contains(note.Id)) continue;
            var remaining = DistancePixels(note, _currentTime);
            if (remaining < -100f || remaining > GameplayStageGeometry.PreviewTravelPx(note.Track) + 100f)
                continue;
            var point = GameplayVisualMapper.PositionAt(note, (float)remaining) + new Vector2(0f, 40f);
            var raw = GameplayVisualMapper.SizeFor(note);
            var visualSize = note.Track == Track.Center ? raw : new Vector2(raw.Y, raw.X);
            var rect = new Rect2(point - visualSize * .5f, visualSize).Grow(10f);
            var scale = _stageRect.Size.X / 1920f;
            rect = new Rect2(_stageRect.Position + rect.Position * scale, rect.Size * scale);
            var color = note.Type is NoteType.HoldNode or NoteType.MixerNode
                ? new Color("ff4d8f") : new Color("35e0ff");
            _selectionOverlay.DrawRect(rect, new Color(color, .12f));
            _selectionOverlay.DrawRect(rect, color, false, 2f);
            const float corner = 10f;
            _selectionOverlay.DrawLine(rect.Position, rect.Position + new Vector2(corner, 0f), Colors.White, 2f, true);
            _selectionOverlay.DrawLine(rect.Position, rect.Position + new Vector2(0f, corner), Colors.White, 2f, true);
        }
    }

    public void HandlePointerInput(InputEvent input, bool viewportCoordinates = false)
    {
        if (_metadata is null || input is not InputEventMouseButton
            { ButtonIndex: MouseButton.Left } click)
            return;
        var localPosition = viewportCoordinates
            ? GetGlobalTransformWithCanvas().AffineInverse() * click.Position
            : click.Position;
        var designPoint = (localPosition - _stageRect.Position) / (_stageRect.Size.X / 1920f);
        designPoint -= new Vector2(0f, 40f);
        if (_authoring.HandleInput(input, designPoint, _notes, _metadata, _currentTime, _playerSpeed))
        {
            AcceptEvent();
            return;
        }
        if (!click.Pressed) return;
        var best = _authoring.HitNote(designPoint, _notes, _metadata, _currentTime, _playerSpeed);
        if (best >= 0) NotePicked?.Invoke(best, Input.IsKeyPressed(Key.Shift));
        AcceptEvent();
    }
}

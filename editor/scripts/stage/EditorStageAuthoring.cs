using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

public sealed record StagePlacementRequest(int Type, int Track, double Bar, double Center,
    double Width, double TailBar, bool Snap);
public sealed record StagePathAddRequest(string NoteId, double Bar);
public sealed record StagePathMoveRequest(string NoteId, string? NodeId, double StartBar,
    double EndBar, double StartCenter, double EndCenter, bool Snap = true, double PositionStep = 0);
/// <summary>SELECT-tool whole-note move. Bars/centres travel as gesture deltas so the Shell can
/// re-apply them onto the authoritative document note exactly like <c>pathMove</c>.</summary>
public sealed record StageNoteMoveRequest(string NoteId, double StartBar, double EndBar,
    double StartCenter, double EndCenter, bool Snap = true, double PositionStep = 0);
/// <summary>SELECT-tool edge resize. <paramref name="Edge"/> is the target chart coordinate of the
/// grabbed edge (magnetic-snapped on the stage); the Shell resolves the span with
/// <see cref="EditorStagePlacement.ResizeFromEdge"/> so the opposite edge stays fixed.</summary>
public sealed record StageNoteResizeRequest(string NoteId, bool MoveStartEdge, double Edge, double WidthStep = .05d);

public sealed class EditorStageAuthoring
{
    private const float ClickThresholdPx = 6f;
    private const float EdgeHandlePx = 14f;
    private const float MotionDedupePxSq = 1e-3f;
    private string _tool = "SELECT";
    private V2NoteType _type = V2NoteType.Tap;
    private EditorTrack _track = EditorTrack.Center;
    private double _width = 1;
    private int _gridDivisor = EditorDocument.DefaultGridDivisor;
    private StagePlacementDraft? _placement;
    private EditorPlacementVisual? _placementPreview;
    private PathDrag? _pathDrag;
    private SelectDrag? _selectDrag;
    private PanDrag? _panDrag;
    private Vector2 _lastMotionPoint = new(-1e9f, -1e9f);

    public event Action<StagePlacementRequest>? PlaceRequested;
    public event Action<StagePathAddRequest>? PathAddRequested;
    public event Action<StagePathMoveRequest>? PathMoveRequested;
    public event Action<int>? PathNotePicked;
    public event Action<int, bool>? NotePressed;
    public event Action<StageNoteMoveRequest>? NoteMoveRequested;
    public event Action<StageNoteResizeRequest>? NoteResizeRequested;
    public event Action? PanStarted;
    public event Action<Vector2>? PanDelta;
    public event Action? PanEnded;

    public string Tool => _tool;
    public int GridDivisor => _gridDivisor;
    public EditorPlacementVisual? PlacementPreview => _placementPreview;

    /// <summary>True while any pointer gesture is mid-drag; the root controller uses this to
    /// forward viewport mouse-motion events as a fallback when the embedded control misses them.</summary>
    public bool HasActiveDrag => _placement is not null || _pathDrag is not null
        || _selectDrag is not null || _panDrag is not null;

    private bool _positionSnap = true;
    public double PositionStep => !_positionSnap ? 0 : Input.IsKeyPressed(Key.X) ? .01 : .1;
    public double WidthStep => !_positionSnap ? 0 : Input.IsKeyPressed(Key.X) ? .01 : .05;

    public void Configure(string tool, V2NoteType type, EditorTrack track, double width, int gridDivisor,
        bool positionSnap = true)
    {
        _positionSnap = positionSnap;
        if (tool != _tool || track != _track || type != _type) Cancel();
        _tool = tool;
        _type = type;
        _track = track;
        _width = Math.Max(width, .1);
        _gridDivisor = Math.Max(1, gridDivisor);
    }

    public void Cancel()
    {
        _placement = null;
        _placementPreview = null;
        _pathDrag = null;
        _selectDrag = null;
        if (_panDrag is not null)
        {
            _panDrag = null;
            PanEnded?.Invoke();
        }
    }

    public bool HandleInput(InputEvent input, Vector2 point, IReadOnlyList<Note> notes,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed,
        IReadOnlySet<int>? selection = null)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when _tool == "PLACE":
                Cancel();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click when click.Pressed:
                return Press(click, point, notes, metadata, currentTime, playerSpeed, selection);
            case InputEventMouseButton { ButtonIndex: MouseButton.Left }:
                return Release(point, metadata, currentTime, playerSpeed);
            case InputEventMouseMotion:
                return Motion(point, metadata, currentTime, playerSpeed);
            default:
                return false;
        }
    }

    public bool TryGetSelectPreview(V2RuntimeChartMetadata metadata, double currentTime,
        double playerSpeed, out int runtimeId, out IReadOnlySet<int> groupRuntimeIds,
        out StageNoteMoveRequest? move, out StageNoteResizeRequest? resize)
    {
        runtimeId = 0;
        groupRuntimeIds = new HashSet<int>();
        move = null;
        resize = null;
        if (_selectDrag is not { } drag) return false;
        runtimeId = drag.RuntimeId;
        groupRuntimeIds = drag.GroupRuntimeIds;
        if (drag.Resize) resize = ResizeRequest(drag, drag.Current);
        else move = MoveRequest(drag, drag.Current, metadata, currentTime, playerSpeed);
        return true;
    }

    public int HitNote(Vector2 point, IReadOnlyList<Note> notes, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed)
    {
        var best = -1;
        var bestDistance = float.MaxValue;
        foreach (var note in notes)
        {
            // Curve tessellation points are rendering data, not authored/editable nodes.
            if (note.V2Metadata is { } source && !metadata.RuntimeIdsBySourceId.ContainsKey(source.SourceId))
                continue;
            var remaining = DistancePixels(note, metadata, currentTime, playerSpeed);
            if (remaining < -60f || remaining > GameplayStageGeometry.PreviewTravelPx(note.Track) + 60f)
                continue;
            var notePoint = GameplayVisualMapper.PositionAt(note, (float)remaining);
            var rect = GameplayVisualMapper.BoundsAt(note, (float)remaining, grow: 16f);
            if (!rect.HasPoint(point)) continue;
            var distance = notePoint.DistanceSquaredTo(point);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = note.Id;
        }
        return best;
    }

    private bool Press(InputEventMouseButton click, Vector2 point, IReadOnlyList<Note> notes,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed,
        IReadOnlySet<int>? selection)
    {
        if (_tool == "PAN")
        {
            _panDrag = new PanDrag(point);
            PanStarted?.Invoke();
            _lastMotionPoint = point;
            return true;
        }
        if (_tool == "SELECT")
        {
            var hitId = HitNote(point, notes, metadata, currentTime, playerSpeed);
            if (hitId < 0) return false;
            var shift = Input.IsKeyPressed(Key.Shift);
            var alreadySelected = selection?.Contains(hitId) == true;
            // Shift-press on an already-selected note keeps the toggle-off click behaviour.
            if (shift && alreadySelected) return false;
            var hitNote = notes.Single(item => item.Id == hitId);
            if (!shift && hitNote.V2Metadata?.IsPathNode == true)
            {
                BeginPathDrag(hitNote, point, metadata, currentTime, playerSpeed);
                return true;
            }
            if (!alreadySelected) NotePressed?.Invoke(hitId, shift);
            var track = ToEditorTrack(hitNote.Track);
            var selectedRuntime = alreadySelected && selection is not null
                ? new HashSet<int>(selection)
                : shift ? new HashSet<int>(selection ?? new HashSet<int>()) : new HashSet<int>();
            selectedRuntime.Add(hitId);
            var parentIds = notes.Where(note => selectedRuntime.Contains(note.Id))
                .Select(note => note.V2Metadata?.ParentSourceId).Where(id => id is not null).ToHashSet();
            var groupRuntimeIds = notes.Where(note => selectedRuntime.Contains(note.Id) ||
                parentIds.Contains(note.V2Metadata?.ParentSourceId)).Select(note => note.Id).ToHashSet();
            var moveStartEdge = false;
            var resize = hitNote.V2Metadata?.IsPathNode != true &&
                TryEdgeResize(hitNote, point, metadata, currentTime, playerSpeed, out moveStartEdge);
            var startEdge = 0d;
            var edgeVisualScale = 1d;
            if (resize)
            {
                var noteCenter = hitNote.Position + hitNote.Width * .5;
                startEdge = moveStartEdge ? noteCenter - hitNote.Width / 2d : noteCenter + hitNote.Width / 2d;
                // With the opposite logical edge fixed, the rendered grabbed edge moves by
                // half the center translation plus half the visual width change. Use that
                // derivative, not absolute rendered coordinates (nor visual width alone).
                var unitSize = GameplayVisualMapper.SizeFor(hitNote.Type, hitNote.Track, 1d);
                var positionUnit = hitNote.Track == Track.Center
                    ? GameplayStageGeometry.CenterUnitPx : GameplayStageGeometry.SideUnitPx;
                var widthUnit = hitNote.Track == Track.Center ? unitSize.X : unitSize.Y;
                edgeVisualScale = (positionUnit + widthUnit) * .5d;
            }
            _selectDrag = new SelectDrag(hitId, hitNote, point,
                BarAt(hitNote.Track, point, metadata, currentTime, playerSpeed,
                    hitNote.V2Metadata?.ScrollSpeedMultiplier ?? 1),
                EditorStagePlacement.CenterAt(track, point.X, point.Y), resize, moveStartEdge,
                groupRuntimeIds, startEdge, edgeVisualScale);
            _lastMotionPoint = point;
            return true;
        }
        if (_tool == "PLACE")
        {
            if (_placement is { TailStage: true })
            {
                UpdatePlacement(point, metadata, currentTime, playerSpeed);
                CommitPlacement();
            }
            else if (_placement is null)
            {
                UpdatePlacement(point, metadata, currentTime, playerSpeed);
                if (_placementPreview is { } preview)
                    _placement = new StagePlacementDraft(preview.Time, preview.Center,
                        preview.Width, EditorStagePlacement.CenterAt(_track, point.X, point.Y));
            }
            return true;
        }
        if (_tool != "PATH") return false;
        if (click.DoubleClick)
        {
            _pathDrag = null;
            var parent = FindPathBody(point, notes, metadata, currentTime, playerSpeed);
            if (parent is not null)
            {
                var parentNote = notes.First(note => note.V2Metadata?.ParentSourceId == parent);
                PathAddRequested?.Invoke(new StagePathAddRequest(parent,
                    SnappedBarAt(TrackOf(parent, notes), point, metadata, currentTime, playerSpeed,
                        parentNote.V2Metadata?.ScrollSpeedMultiplier ?? 1).ToDouble()));
            }
            return true;
        }
        var id = HitNote(point, notes, metadata, currentTime, playerSpeed);
        if (id < 0) return true;
        var note = notes.Single(item => item.Id == id);
        if (note.V2Metadata?.SourceType is not (V2NoteType.Hold or V2NoteType.Mixer)) return true;
        BeginPathDrag(note, point, metadata, currentTime, playerSpeed);
        return true;
    }

    private void BeginPathDrag(Note note, Vector2 point, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed)
    {
        PathNotePicked?.Invoke(note.Id);
        _pathDrag = new PathDrag(note.Id, note, point,
            BarAt(note.Track, point, metadata, currentTime, playerSpeed,
                note.V2Metadata?.ScrollSpeedMultiplier ?? 1),
            EditorStagePlacement.CenterAt(ToEditorTrack(note.Track), point.X, point.Y));
    }

    private bool Motion(Vector2 point, V2RuntimeChartMetadata metadata, double currentTime,
        double playerSpeed)
    {
        if (_tool == "PLACE")
        {
            UpdatePlacement(point, metadata, currentTime, playerSpeed);
            return true;
        }
        if (_pathDrag is { } path)
        {
            _pathDrag = path with { Current = point };
            return true;
        }
        if (_selectDrag is null && _panDrag is null) return false;
        // Both the control GuiInput and the root controller fallback may deliver the same motion
        // event; identical design points are the same event and must apply only once.
        if ((point - _lastMotionPoint).LengthSquared() < MotionDedupePxSq) return true;
        _lastMotionPoint = point;
        if (_selectDrag is not null)
        {
            _selectDrag.Current = point;
            return true;
        }
        var delta = point - _panDrag!.Last;
        _panDrag.Last = point;
        PanDelta?.Invoke(delta);
        return true;
    }

    private bool Release(Vector2 point, V2RuntimeChartMetadata metadata, double currentTime,
        double playerSpeed)
    {
        if (_placement is not null)
        {
            if (_placement.TailStage) return true;
            // Apply the release point to the width draft before committing: motion events can
            // be dropped between the last motion and the release, and Hold/Mixer must freeze the
            // head width before the tail stage begins.
            UpdatePlacement(point, metadata, currentTime, playerSpeed);
            if (_type is V2NoteType.Hold or V2NoteType.Mixer)
            {
                _placement.BeginTail(_gridDivisor);
                UpdatePlacement(point, metadata, currentTime, playerSpeed);
            }
            else
            {
                CommitPlacement();
                UpdatePlacement(point, metadata, currentTime, playerSpeed);
            }
            return true;
        }
        if (_pathDrag is not { } drag) return ReleaseSelectOrPan(point, metadata, currentTime, playerSpeed);
        _pathDrag = null;
        if (drag.Start.DistanceTo(point) <= ClickThresholdPx)
        {
            PathNotePicked?.Invoke(drag.RuntimeId);
            return true;
        }
        PathMoveRequested?.Invoke(PathMoveRequest(drag, point, metadata, currentTime, playerSpeed));
        return true;
    }

    public StagePathMoveRequest? PathPreview(V2RuntimeChartMetadata metadata, double currentTime,
        double playerSpeed) => _pathDrag is { } drag
        ? PathMoveRequest(drag, drag.Current, metadata, currentTime, playerSpeed) : null;

    private StagePathMoveRequest PathMoveRequest(PathDrag drag, Vector2 point,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed) =>
        new(drag.Note.V2Metadata!.ParentSourceId,
            drag.Note.V2Metadata.IsPathNode ? drag.Note.V2Metadata.SourceId : null, drag.StartBar,
            BarAt(drag.Note.Track, point, metadata, currentTime, playerSpeed,
                drag.Note.V2Metadata.ScrollSpeedMultiplier), drag.StartCenter,
            EditorStagePlacement.CenterAt(ToEditorTrack(drag.Note.Track), point.X, point.Y),
            !Input.IsKeyPressed(Key.Z), PositionStep);

    private void UpdatePlacement(Vector2 point, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed)
    {
        // Same predicate as SnappedBarAt below: Z disables snapping regardless of Ctrl, so the
        // ghost time, its label and the Snap request flag always agree.
        var snap = !Input.IsKeyPressed(Key.Z);
        if (_placement is null)
        {
            var time = SnappedBarAt(_track, point, metadata, currentTime, playerSpeed);
            var width = Math.Max(EditorStagePlacement.QuantizePosition(_width, WidthStep), .1);
            var center = EditorStagePlacement.QuantizePosition(
                EditorStagePlacement.CenterAt(_track, point.X, point.Y), PositionStep);
            var tail = _type is V2NoteType.Hold or V2NoteType.Mixer
                ? time + ExactBarTime.FromFraction(1, _gridDivisor) : time;
            _placementPreview = new EditorPlacementVisual((int)_type, (int)_track, time,
                tail, center, width, false, snap);
            return;
        }

        if (_placement.TailStage)
            _placement.UpdateTail(SnappedBarAt(_track, point, metadata, currentTime, playerSpeed),
                _gridDivisor);
        else
            _placement.UpdateWidth(EditorStagePlacement.CenterAt(_track, point.X, point.Y), WidthStep);
        _placementPreview = new EditorPlacementVisual((int)_type, (int)_track,
            _placement.Time, _placement.TailTime, _placement.Center,
            _placement.Width, _placement.TailStage, snap);
    }

    private void CommitPlacement()
    {
        if (_placementPreview is not { } preview) return;
        _width = preview.Width;
        PlaceRequested?.Invoke(new StagePlacementRequest(preview.Type, preview.Track, preview.Bar,
            preview.Center, preview.Width, preview.TailBar, preview.Snapped));
        _placement = null;
        _placementPreview = null;
    }

    private bool ReleaseSelectOrPan(Vector2 point, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed)
    {
        if (_selectDrag is { } select)
        {
            _selectDrag = null;
            if (select.Start.DistanceTo(point) > ClickThresholdPx)
                ReleaseSelectDrag(select, point, metadata, currentTime, playerSpeed);
            return true;
        }
        if (_panDrag is not { } pan) return false;
        _panDrag = null;
        // Apply the tail from the last motion to the release point so panning still lands fully
        // when motion events were dropped by the embedded window; the state clear keeps the
        // controller-forwarded duplicate release from re-applying it.
        if ((point - pan.Last).LengthSquared() >= MotionDedupePxSq)
            PanDelta?.Invoke(point - pan.Last);
        PanEnded?.Invoke();
        return true;
    }

    private void ReleaseSelectDrag(SelectDrag drag, Vector2 point,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed)
    {
        var note = drag.Note;
        var sourceId = note.V2Metadata?.ParentSourceId;
        if (sourceId is null) return;
        if (drag.Resize) NoteResizeRequested?.Invoke(ResizeRequest(drag, point));
        else NoteMoveRequested?.Invoke(MoveRequest(drag, point, metadata, currentTime, playerSpeed));
    }

    private StageNoteResizeRequest ResizeRequest(SelectDrag drag, Vector2 point)
    {
        // Side-chart coordinates increase upwards, opposite to screen Y.
        var pointerDelta = drag.Note.Track == Track.Center
            ? point.X - drag.Start.X : drag.Start.Y - point.Y;
        var edge = drag.StartEdge + pointerDelta / drag.EdgeVisualScale;
        return new StageNoteResizeRequest(drag.Note.V2Metadata!.ParentSourceId, drag.MoveStartEdge, edge, WidthStep);
    }

    private StageNoteMoveRequest MoveRequest(SelectDrag drag, Vector2 point,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed) =>
        new(drag.Note.V2Metadata!.ParentSourceId, drag.StartBar,
            BarAt(drag.Note.Track, point, metadata, currentTime, playerSpeed,
                drag.Note.V2Metadata.ScrollSpeedMultiplier), drag.StartCenter,
            EditorStagePlacement.CenterAt(ToEditorTrack(drag.Note.Track), point.X, point.Y),
            !Input.IsKeyPressed(Key.Z), PositionStep);

    private bool TryEdgeResize(Note note, Vector2 point, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed, out bool moveStartEdge)
    {
        moveStartEdge = false;
        var remaining = DistancePixels(note, metadata, currentTime, playerSpeed);
        if (remaining < -60f || remaining > GameplayStageGeometry.PreviewTravelPx(note.Track) + 60f)
            return false;
        var notePoint = GameplayVisualMapper.PositionAt(note, (float)remaining);
        var size = GameplayVisualMapper.SizeFor(note);
        float startEdge;
        float endEdge;
        float axis;
        if (note.Track == Track.Center)
        {
            startEdge = notePoint.X - size.X * .5f;
            endEdge = notePoint.X + size.X * .5f;
            axis = point.X;
        }
        else
        {
            // Chart centre grows from the bottom edge toward the top edge on the side tracks.
            startEdge = notePoint.Y + size.Y * .5f;
            endEdge = notePoint.Y - size.Y * .5f;
            axis = point.Y;
        }
        var nearStart = Math.Abs(axis - startEdge);
        var nearEnd = Math.Abs(axis - endEdge);
        if (nearStart > EdgeHandlePx && nearEnd > EdgeHandlePx) return false;
        moveStartEdge = nearStart <= nearEnd;
        return true;
    }

    private string? FindPathBody(Vector2 point, IReadOnlyList<Note> notes,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed)
    {
        string? best = null;
        var bestDistance = 30f;
        foreach (var group in notes.Where(note => note.V2Metadata?.SourceType
                is V2NoteType.Hold or V2NoteType.Mixer).GroupBy(note => note.V2Metadata!.ParentSourceId))
        {
            var path = group.OrderBy(note => note.Second).ToArray();
            for (var index = 1; index < path.Length; index++)
            {
                var from = VisualPoint(path[index - 1], metadata, currentTime, playerSpeed);
                var to = VisualPoint(path[index], metadata, currentTime, playerSpeed);
                var distance = DistanceToSegment(point, from, to);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = group.Key;
            }
        }
        return best;
    }

    private static Vector2 VisualPoint(Note note, V2RuntimeChartMetadata metadata, double currentTime,
        double playerSpeed) => GameplayVisualMapper.PositionAt(note,
        (float)DistancePixels(note, metadata, currentTime, playerSpeed));

    private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        var segment = to - from;
        if (segment.LengthSquared() < .001f) return point.DistanceTo(from);
        var weight = Mathf.Clamp((point - from).Dot(segment) / segment.LengthSquared(), 0f, 1f);
        return point.DistanceTo(from + segment * weight);
    }

    private static double DistancePixels(Note note, V2RuntimeChartMetadata metadata, double currentTime,
        double playerSpeed) => VisualScrollMath.DistancePixels(note.Second, currentTime,
            GameplayStageGeometry.BaseFallSpeedPx, metadata.ScrollMap.RuntimeSpeedAt(note.BarTime), playerSpeed,
            note.V2Metadata?.ScrollSpeedMultiplier ?? 1);

    private ExactBarTime SnappedBarAt(EditorTrack track, Vector2 point, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed, double noteSpeedMultiplier = 1) =>
        EditorStagePlacement.BarTimeAt(track, point.X, point.Y, metadata, currentTime, playerSpeed,
            _gridDivisor, !Input.IsKeyPressed(Key.Z), noteSpeedMultiplier);

    private static double BarAt(Track track, Vector2 point, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed, double noteSpeedMultiplier = 1) =>
        EditorStagePlacement.BarTimeAt(ToEditorTrack(track), point.X, point.Y, metadata,
            currentTime, playerSpeed, 1, false, noteSpeedMultiplier).ToDouble();

    private static EditorTrack TrackOf(string parentId, IReadOnlyList<Note> notes) =>
        ToEditorTrack(notes.First(note => note.V2Metadata?.ParentSourceId == parentId).Track);

    private static EditorTrack ToEditorTrack(Track track) =>
        EditorStagePlacement.ToEditorTrack(track);

    private sealed record PathDrag(int RuntimeId, Note Note, Vector2 Start, double StartBar,
        double StartCenter)
    {
        public Vector2 Current { get; init; } = Start;
    }

    private sealed class SelectDrag
    {
        public SelectDrag(int runtimeId, Note note, Vector2 start, double startBar,
            double startCenter, bool resize, bool moveStartEdge,
            IReadOnlySet<int> groupRuntimeIds, double startEdge, double edgeVisualScale)
        {
            RuntimeId = runtimeId;
            Note = note;
            Start = start;
            Current = start;
            StartBar = startBar;
            StartCenter = startCenter;
            Resize = resize;
            MoveStartEdge = moveStartEdge;
            GroupRuntimeIds = groupRuntimeIds;
            StartEdge = startEdge;
            EdgeVisualScale = edgeVisualScale;
        }

        public int RuntimeId { get; }
        public Note Note { get; }
        public Vector2 Start { get; }
        public Vector2 Current { get; set; }
        public double StartBar { get; }
        public double StartCenter { get; }
        public bool Resize { get; }
        public bool MoveStartEdge { get; }
        public IReadOnlySet<int> GroupRuntimeIds { get; }
        public double StartEdge { get; }
        public double EdgeVisualScale { get; }
    }

    private sealed class PanDrag
    {
        public PanDrag(Vector2 start)
        {
            Start = start;
            Last = start;
        }

        public Vector2 Start { get; }
        public Vector2 Last { get; set; }
    }
}

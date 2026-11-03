using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

public sealed record StagePlacementRequest(int Type, int Track, double Bar, double Center,
    double Width, double TailBar);
public sealed record StagePathAddRequest(string NoteId, double Bar);
public sealed record StagePathMoveRequest(string NoteId, string? NodeId, double StartBar,
    double EndBar, double StartCenter, double EndCenter);

public sealed class EditorStageAuthoring
{
    private string _tool = "SELECT";
    private V2NoteType _type = V2NoteType.Tap;
    private EditorTrack _track = EditorTrack.Center;
    private double _width = 1;
    private int _gridDivisor = EditorDocument.DefaultGridDivisor;
    private Vector2? _placementStart;
    private PathDrag? _pathDrag;

    public event Action<StagePlacementRequest>? PlaceRequested;
    public event Action<StagePathAddRequest>? PathAddRequested;
    public event Action<StagePathMoveRequest>? PathMoveRequested;
    public event Action<int>? PathNotePicked;

    public string Tool => _tool;

    public void Configure(string tool, V2NoteType type, EditorTrack track, double width, int gridDivisor)
    {
        if (tool != _tool) Cancel();
        _tool = tool;
        _type = type;
        _track = track;
        _width = Math.Clamp(width, .1, 5);
        _gridDivisor = Math.Max(1, gridDivisor);
    }

    public void Cancel()
    {
        _placementStart = null;
        _pathDrag = null;
    }

    public bool HandleInput(InputEvent input, Vector2 point, IReadOnlyList<Note> notes,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed)
    {
        if (_tool == "SELECT" || _tool == "PAN") return false;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (click.Pressed) return Press(click, point, notes, metadata, currentTime, playerSpeed);
            return Release(point, metadata, currentTime, playerSpeed);
        }
        return false;
    }

    public int HitNote(Vector2 point, IReadOnlyList<Note> notes, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed)
    {
        var best = -1;
        var bestDistance = float.MaxValue;
        foreach (var note in notes)
        {
            var remaining = DistancePixels(note, metadata, currentTime, playerSpeed);
            if (remaining < -60f || remaining > GameplayStageGeometry.PreviewTravelPx(note.Track) + 60f)
                continue;
            var notePoint = GameplayVisualMapper.PositionAt(note, (float)remaining);
            var raw = GameplayVisualMapper.SizeFor(note);
            var visualSize = note.Track == Track.Center ? raw : new Vector2(raw.Y, raw.X);
            var rect = new Rect2(notePoint - visualSize * .5f, visualSize).Grow(16f);
            if (!rect.HasPoint(point)) continue;
            var distance = notePoint.DistanceSquaredTo(point);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = note.Id;
        }
        return best;
    }

    private bool Press(InputEventMouseButton click, Vector2 point, IReadOnlyList<Note> notes,
        V2RuntimeChartMetadata metadata, double currentTime, double playerSpeed)
    {
        if (_tool == "PLACE")
        {
            _placementStart = point;
            return true;
        }
        if (_tool != "PATH") return false;
        if (click.DoubleClick)
        {
            _pathDrag = null;
            var parent = FindPathBody(point, notes, metadata, currentTime, playerSpeed);
            if (parent is not null)
                PathAddRequested?.Invoke(new StagePathAddRequest(parent,
                    SnappedBarAt(TrackOf(parent, notes), point, metadata, currentTime, playerSpeed).ToDouble()));
            return true;
        }
        var id = HitNote(point, notes, metadata, currentTime, playerSpeed);
        if (id < 0) return true;
        var note = notes.Single(item => item.Id == id);
        if (note.V2Metadata?.SourceType is not (V2NoteType.Hold or V2NoteType.Mixer)) return true;
        _pathDrag = new PathDrag(id, note, point,
            BarAt(note.Track, point, metadata, currentTime, playerSpeed),
            EditorStagePlacement.CenterAt(ToEditorTrack(note.Track), point.X, point.Y));
        return true;
    }

    private bool Release(Vector2 point, V2RuntimeChartMetadata metadata, double currentTime,
        double playerSpeed)
    {
        if (_placementStart is { } start)
        {
            _placementStart = null;
            var bar = SnappedBarAt(_track, start, metadata, currentTime, playerSpeed);
            var width = _width;
            if (start.DistanceTo(point) > 6f)
                width += EditorStagePlacement.WidthFromDrag(_track, start.X, start.Y, point.X, point.Y);
            width = Math.Clamp(EditorStagePlacement.SnapWidth(width), .1, 5);
            var center = EditorStagePlacement.SnapCenter(
                EditorStagePlacement.CenterAt(_track, start.X, start.Y), false);
            center = Math.Clamp(center, width / 2d, 5 - width / 2d);
            var tail = _type is V2NoteType.Hold or V2NoteType.Mixer ? bar + ExactBarTime.One : bar;
            PlaceRequested?.Invoke(new StagePlacementRequest((int)_type, (int)_track,
                bar.ToDouble(), center, width, tail.ToDouble()));
            return true;
        }
        if (_pathDrag is not { } drag) return false;
        _pathDrag = null;
        if (drag.Start.DistanceTo(point) <= 6f)
        {
            PathNotePicked?.Invoke(drag.RuntimeId);
            return true;
        }
        var note = drag.Note;
        var track = ToEditorTrack(note.Track);
        PathMoveRequested?.Invoke(new StagePathMoveRequest(note.V2Metadata!.ParentSourceId,
            note.V2Metadata.IsPathNode ? note.V2Metadata.SourceId : null, drag.StartBar,
            BarAt(note.Track, point, metadata, currentTime, playerSpeed), drag.StartCenter,
            EditorStagePlacement.CenterAt(track, point.X, point.Y)));
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
        double currentTime, double playerSpeed)
    {
        var bar = BarAt(ToRuntimeTrack(track), point, metadata, currentTime, playerSpeed);
        var exact = ExactBarTime.FromFraction((long)Math.Round(bar * 1_000_000d), 1_000_000);
        return EditorGeometry.Snap(exact, _gridDivisor);
    }

    private static double BarAt(Track track, Vector2 point, V2RuntimeChartMetadata metadata,
        double currentTime, double playerSpeed)
    {
        var remaining = GameplayStageGeometry.StageDistancePx(track, point.X, point.Y);
        var second = currentTime;
        for (var iteration = 0; iteration < 6; iteration++)
        {
            var bar = metadata.BpmTimeline.RuntimeBarAt(second);
            var speed = VisualScrollMath.SpeedPixelsPerSecond(GameplayStageGeometry.BaseFallSpeedPx,
                metadata.ScrollMap.RuntimeSpeedAt(bar), playerSpeed);
            second = currentTime + remaining / speed;
        }
        return Math.Max(0, metadata.BpmTimeline.RuntimeBarAt(second));
    }

    private static EditorTrack TrackOf(string parentId, IReadOnlyList<Note> notes) =>
        ToEditorTrack(notes.First(note => note.V2Metadata?.ParentSourceId == parentId).Track);

    private static EditorTrack ToEditorTrack(Track track) => track switch
    {
        Track.Left => EditorTrack.Left,
        Track.Right => EditorTrack.Right,
        _ => EditorTrack.Center,
    };

    private static Track ToRuntimeTrack(EditorTrack track) => track switch
    {
        EditorTrack.Left => Track.Left,
        EditorTrack.Right => Track.Right,
        _ => Track.Center,
    };

    private sealed record PathDrag(int RuntimeId, Note Note, Vector2 Start, double StartBar,
        double StartCenter);
}

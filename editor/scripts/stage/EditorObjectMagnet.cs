using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

/// <summary>Projects authored neighbours into viewport pixels; owns magnet latch and guide, not document edits.</summary>
internal sealed class EditorObjectMagnet
{
    private readonly ObjectMagnet _magnet = new();
    private readonly List<ObjectMagnet.Candidate> _candidates = new();
    private readonly Dictionary<(int, int), Vector2> _targets = new();
    private readonly HashSet<string> _excludedParents = new();
    public bool Enabled { get; set; }
    public float Scale { get; set; } = 1;
    public Rect2 VisibleDesignRect { get; set; } = new(0, 0, 1920, 1080);
    public (Vector2 From, Vector2 To, int Feature)? Guide { get; private set; }
    public void Reset() { _magnet.Reset(); Guide = null; }

    // Feature: -1 = start edge, 0 = centre, +1 = end edge. Resize only offers its grabbed edge.
    public double? Resolve(Note moving, double center, double width, double remaining,
        IReadOnlyList<Note> notes, V2RuntimeChartMetadata metadata, double time, double speed,
        IReadOnlySet<int>? excluded = null, string? excludedParent = null, int? edge = null,
        bool resize = false, bool symmetricWidth = false)
    {
        Guide = null;
        if (!Enabled || Input.IsKeyPressed(Key.Alt) || Scale <= 0) { Reset(); return null; }
        _candidates.Clear(); _targets.Clear(); _excludedParents.Clear();
        if (excluded is not null)
            foreach (var note in notes)
                if (excluded.Contains(note.Id) && note.V2Metadata is { } excludedSource)
                    _excludedParents.Add(excludedSource.ParentSourceId);
        var middle = GameplayVisualMapper.PositionAt(moving, (float)remaining);
        var original = moving.Position + moving.Width / 2;
        var unit = moving.Track == Track.Center ? GameplayStageGeometry.CenterUnitPx : GameplayStageGeometry.SideUnitPx;
        middle += moving.Track == Track.Center ? new Vector2((float)((center - original) * unit), 0)
            : new Vector2(0, (float)(-(center - original) * unit));
        var size = GameplayVisualMapper.SizeFor(moving.Type, moving.Track, width);
        var half = (moving.Track == Track.Center ? size.X : size.Y) / 2;
        var widthSize = GameplayVisualMapper.SizeFor(moving.Type, moving.Track, 1);
        var widthUnit = moving.Track == Track.Center ? widthSize.X : widthSize.Y;
        float Axis(Vector2 p) => moving.Track == Track.Center ? p.X : -p.Y;
        Vector2 FeaturePoint(Vector2 p, double offset) => p + (moving.Track == Track.Center ? new Vector2((float)offset, 0) : new Vector2(0, (float)-offset));
        foreach (var note in notes)
        {
            if (note.Track != moving.Track || excluded?.Contains(note.Id) == true ||
                excludedParent is not null && note.V2Metadata?.ParentSourceId == excludedParent ||
                note.V2Metadata is not { } source || _excludedParents.Contains(source.ParentSourceId) || !metadata.RuntimeIdsBySourceId.ContainsKey(source.SourceId) ||
                note.Type == NoteType.BarLine) continue;
            var distance = VisualScrollMath.DistancePixels(note.Second, time, GameplayStageGeometry.BaseFallSpeedPx,
                metadata.ScrollMap.RuntimeSpeedAt(note.BarTime), speed, source.ScrollSpeedMultiplier);
            if (distance < -60 || distance > GameplayStageGeometry.PreviewTravelPx(note.Track) + 60) continue;
            var target = GameplayVisualMapper.PositionAt(note, (float)distance);
            if (!VisibleDesignRect.HasPoint(target)) continue;
            var cross = moving.Track == Track.Center ? Math.Abs(target.Y - middle.Y) : Math.Abs(target.X - middle.X);
            if (cross * Scale > 96) continue;
            var targetSize = GameplayVisualMapper.SizeFor(note);
            var targetHalf = (note.Track == Track.Center ? targetSize.X : targetSize.Y) / 2;
            for (var feature = -1; feature <= 1; feature++)
            {
                if (edge is { } only && feature != only || symmetricWidth && feature == 0) continue;
                var point = FeaturePoint(target, feature * targetHalf);
                var correction = Axis(point) - (Axis(middle) + feature * half);
                var value = symmetricWidth ? width + correction * 2 / (feature * widthUnit)
                    : resize ? center + feature * width / 2 + correction / ((unit + widthUnit) / 2)
                    : center + correction / unit;
                if (!double.IsFinite(value) || (symmetricWidth && value < .1)) continue;
                _candidates.Add(new(note.Id, feature, Axis(middle) * Scale + correction * Scale, value));
                _targets[(note.Id, feature)] = point;
            }
        }
        var chosen = _magnet.Resolve(Axis(middle) * Scale, _candidates);
        if (chosen is not { } match) return null;
        var to = _targets[(match.Id, match.Feature)];
        var from = moving.Track == Track.Center ? new Vector2(to.X, middle.Y) : new Vector2(middle.X, to.Y);
        Guide = (from, to, match.Feature);
        return match.Value;
    }
}

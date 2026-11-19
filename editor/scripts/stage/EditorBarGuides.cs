using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using G = DynamiteUniverse.Shared.Chart.GameplayStageGeometry;

namespace DynamiteUniverse.Editor;

/// <summary>Editor-only timing overlay in the same stage-local space as notes. No input, notes, or persistence.</summary>
internal partial class EditorBarGuides : Node2D
{
    internal readonly record struct Mark(Track Track, long Tick, Vector2 From, Vector2 To, float Width, float Alpha,
        string? Label, Vector2 Baseline);
    private readonly List<EditorBarGuideGrid.Sample> _samples = new();
    private readonly List<Mark> _marks = new();
    private readonly HashSet<int> _bins = new();
    private readonly List<Rect2> _labels = new();
    private V2RuntimeChartMetadata? _metadata;
    private EditorBarGuideGrid? _grid;
    private double _now, _speed;
    private (int Left, int Center, int Right) _levels;
    private Rect2 _clip;
    private const int MaxPerTrack = 192;
    private const float LabelSize = 18;

    public EditorBarGuides() { Name = "BarGuides"; ZIndex = -1; }

    internal void SetLevels(int left, int center, int right)
    {
        if (left is < 0 or > 2 || center is < 0 or > 2 || right is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(left), "barGuides levels must be integers from 0 to 2.");
        if (_levels == (left, center, right)) return;
        _levels = (left, center, right);
        QueueRedraw();
    }

    internal void SetViewport(Rect2 clip) { if (_clip != clip) { _clip = clip; QueueRedraw(); } }

    internal void SetClock(V2RuntimeChartMetadata? metadata, double now, double speed)
    {
        if (metadata == _metadata && now == _now && speed == _speed) return;
        if (!ReferenceEquals(metadata, _metadata)) { _metadata = metadata; _grid = null; }
        _now = now; _speed = speed;
        if (_levels != default && Visible) QueueRedraw();
    }

    internal IReadOnlyList<Mark> BuildFrame()
    {
        _marks.Clear();
        if (!Visible || _levels == default || _metadata is null || !_clip.HasArea()) return _marks;
        _grid ??= new(_metadata);
        _grid.Collect(_now, _speed, G.TravelPx, _samples);
        AddTrack(Track.Center, _levels.Center);
        AddTrack(Track.Left, _levels.Left);
        AddTrack(Track.Right, _levels.Right);
        return _marks;
    }

    private void AddTrack(Track track, int level)
    {
        if (level == 0) return;
        var center = track == Track.Center;
        var font = ThemeDB.FallbackFont;
        var density = center ? 1f : G.SideDistanceScale;
        var step = density * G.TravelPx / (MaxPerTrack - 1);
        _bins.Clear(); _labels.Clear();
        foreach (var s in _samples)
        {
            var p = G.PositionAt(track, 0, s.Distance);
            // Fixed judgement-to-spawn corridor, intersected with the actual panned viewport.
            var from = center ? new Vector2(Math.Max(G.LeftLineX, _clip.Position.X), p.Y)
                : new Vector2(p.X, Math.Max(G.CenterLineY - G.TravelPx, _clip.Position.Y));
            var to = center ? new Vector2(Math.Min(G.RightLineX, _clip.End.X), p.Y)
                : new Vector2(p.X, Math.Min(G.CenterLineY, _clip.End.Y));
            if (center ? to.X <= from.X || p.Y < _clip.Position.Y || p.Y >= _clip.End.Y
                       : to.Y <= from.Y || p.X < _clip.Position.X || p.X >= _clip.End.X) continue;
            if (!_bins.Add((int)MathF.Floor(s.Distance * density / step))) continue;
            var width = s.Weight == 2 ? 3f : s.Weight == 1 ? 2f : 1f;
            var opacity = s.Weight == 2 ? .60f : s.Weight == 1 ? .40f : .24f;
            var alpha = opacity * (level == 2 ? 1f : .5f);
            string? text = null;
            var baseline = Vector2.Zero;
            // Consider whole bars first; finer labels need real projected room (especially across BPM/Scroll events).
            var span = 1L << Math.Min(5, s.Rank);
            if (s.Tick % 32 == 0 || Math.Abs(_grid!.Distance(s.Tick + span, _now, _speed) - s.Distance) * density >= (center ? 24 : 70))
            {
                var candidate = s.Label;
                var size = font.GetStringSize(candidate, fontSize: (int)LabelSize);
                var corner = center ? new Vector2(G.RightLineX + 8, p.Y - size.Y / 2)
                    : new Vector2(p.X - size.X / 2, G.CenterLineY + 5);
                var bounds = new Rect2(corner, size);
                if (_clip.Encloses(bounds) && !_labels.Any(r => r.Intersects(bounds)))
                {
                    _labels.Add(bounds.Grow(3)); text = candidate;
                    baseline = corner + Vector2.Down * font.GetAscent((int)LabelSize);
                }
            }
            _marks.Add(new(track, s.Tick, from, to, width, alpha, text, baseline));
        }
    }

    public override void _Draw()
    {
        foreach (var mark in BuildFrame())
        {
            DrawLine(mark.From, mark.To, new Color(.76f, .84f, .92f, mark.Alpha), mark.Width, true);
            if (mark.Label is not { } text) continue;
            var ink = mark.Tick % 32 == 0 ? Colors.White : new Color("c2ceda");
            var opacity = Math.Min(1f, mark.Alpha * 2.5f);
            DrawStringOutline(ThemeDB.FallbackFont, mark.Baseline, text, fontSize: (int)LabelSize, size: 3, modulate: new Color(0, 0, 0, opacity));
            DrawString(ThemeDB.FallbackFont, mark.Baseline, text, fontSize: (int)LabelSize, modulate: new Color(ink, opacity));
        }
    }
}

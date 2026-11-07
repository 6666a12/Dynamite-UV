using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>Owns only preview playback visuals. Audio, authoring and document identity stay outside.</summary>
internal sealed class EditorAutoPreview
{
    private readonly Node2D _root;
    private readonly PreviewAppearance _appearance = new();
    private EditorAutoTimeline _timeline = null!;
    private GameplayNoteRenderer _renderer = null!;
    private V2RuntimeAdapterResult _chart = null!;
    private readonly HashSet<int> _finished = new();
    public IReadOnlyList<NoteView> ActiveViews => _renderer.ActiveViews;

    public EditorAutoPreview(Node2D root) => _root = root;

    public void Reset(V2RuntimeAdapterResult chart, double second, double speed)
    {
        foreach (var child in _root.GetChildren()) child.Free();
        _chart = chart;
        _appearance.FallSpeedMultiplier = speed;
        _timeline = new(chart.RuntimeChart);
        _timeline.Reset(second);
        _finished.Clear();
        var notes = chart.RuntimeChart.AllNotes.OrderBy(note => note.Second).ToArray();
        var plan = _timeline.Plan;
        _renderer = new(_root, chart.RuntimeChart, _appearance, chart.SyncAccentRuntimeIds,
            notes, notes.ToDictionary(note => note.Id), notes.ToDictionary(note => note.Id, note => note.Second),
            plan.Sustains, VisualPoolPlanning.Analyze(plan.Units, notes, plan.Sustains.Values), ReadSustain);
        // Past ordinary hits must not respawn when scroll speed changes after a seek/edit.
        _renderer.SpawnVisibilityFilter = note => note.Second > second ||
            plan.Sustains.TryGetValue(note.Id, out var path) && _timeline.IsActive(path);
        var bar = chart.Metadata.BpmTimeline.RuntimeBarAt(second);
        _renderer.SpawnNotes(second, bar);
        _renderer.UpdateViews(second, bar, 0);
        foreach (var path in plan.Sustains.Values)
        {
            if (path.EndTime <= second) _finished.Add(path.HeadId);
            if (_timeline.IsActive(path) && path.Kind == SustainKind.Hold)
                _renderer.ResolveNoteView(path.HeadId, ScoreCategory.HoldStart, JudgeGrade.Perfect, emitEffect: false);
        }
        UpdateSustains();
    }

    public void Advance(double second, double delta)
    {
        var bar = _chart.Metadata.BpmTimeline.RuntimeBarAt(second);
        _renderer.SpawnNotes(second, bar);
        _renderer.UpdateViews(second, bar, delta);
        _timeline.Advance(second, Resolve);
        UpdateSustains();
    }

    public Rect2? SelectionBounds(int noteId)
    {
        if (_renderer.FindBody(noteId) is not { Visible: true, IsResidual: false } view) return null;
        return new Rect2(view.Position - view.BodySize * .5f, view.BodySize).Grow(10);
    }

    public void SetPaused(bool paused)
    {
        _root.ProcessMode = paused ? Node.ProcessModeEnum.Disabled : Node.ProcessModeEnum.Inherit;
        // GPU simulation is render-server owned; disabling managed _Process alone is insufficient.
        foreach (var effect in _root.GetChildren())
            foreach (var child in effect.GetChildren())
                if (child is GpuParticles2D particles) particles.SpeedScale = paused ? 0 : 1;
    }

    public void SetSpeed(double speed)
    {
        _appearance.FallSpeedMultiplier = speed;
        _renderer.SetFallSpeed(speed);
        var bar = _chart.Metadata.BpmTimeline.RuntimeBarAt(_timeline.Second);
        _renderer.SpawnNotes(_timeline.Second, bar);
        _renderer.UpdateViews(_timeline.Second, bar, 0);
    }

    private void Resolve(JudgeUnit unit)
    {
        // Community Hold/Mixer intermediate ticks score silently; only heads and Hold tails burst.
        if (unit.Kind == UnitKind.HoldPoint) return;
        if (unit.Kind == UnitKind.HoldEnd && unit.SustainHeadId is { } head)
        {
            _renderer.FinishHoldViews(_timeline.Plan.Sustains[head], failed: false);
            _finished.Add(head);
        }
        _renderer.ResolveNoteView(unit.NoteId, unit.Category, JudgeGrade.Perfect,
            emitEffect: unit.Kind != UnitKind.Mine);
    }

    private void UpdateSustains()
    {
        foreach (var path in _timeline.Plan.Sustains.Values)
        {
            if (_timeline.IsActive(path))
            {
                var bounds = path.BoundsAtRuntime(_timeline.Second);
                if (path.Kind == SustainKind.Hold)
                    _renderer.UpdateHoldHeadContactView(path, true, bounds);
                else
                    _renderer.UpdateMixerHeadView(path, true, bounds.Center, bounds);
                _renderer.UpdateSustainEffect(path, true, bounds, bounds.Center);
            }
            else if (_timeline.Second >= path.EndTime && _finished.Add(path.HeadId))
            {
                if (path.Kind == SustainKind.Mixer) _renderer.ReleaseMixerHeadView(path.HeadId);
                else _renderer.FinishHoldViews(path, failed: false);
            }
        }
    }

    private SustainVisualSnapshot? ReadSustain(int id) =>
        _timeline.Plan.Sustains.TryGetValue(id, out var path)
            ? new(path, path.StartTime <= _timeline.Second, _timeline.IsActive(path), false,
                path.EndTime <= _timeline.Second, Math.Clamp(_timeline.Second, path.StartTime, path.EndTime))
            : null;

    private sealed class PreviewAppearance : IGameplayVisualSettings
    {
        public int TimingOffsetMs => 0;
        public double FallSpeedMultiplier { get; set; } = 1;
        public bool MirrorEnabled => false;
        public float NoteBrightnessScale => 1.3f;
        public float FlashIntensityScale => 1;
        public int HitResidualMs => 300;
        public float HitResidualSeconds => .3f;
        public HitEffectLevel HitEffectLevel => HitEffectLevel.Full;
        public UiMotionMode MotionMode => UiMotionMode.Full;
    }
}

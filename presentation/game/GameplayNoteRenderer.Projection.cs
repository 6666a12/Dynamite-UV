using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;
using static DynamiteUniverse.Game.GameplayRenderGeometry;

namespace DynamiteUniverse.Game;

public sealed partial class GameplayNoteRenderer
{

    public double CurrentBarAt(double second) =>
        _chart.V2Metadata?.BpmTimeline != null
            ? _chart.V2Metadata!.BpmTimeline.RuntimeBarAt(ChartAudioSecond(second))
            : _chart.Sections.Count > 0 ? _chart.SecondsToBarTime(second) : second;

    private double ChartAudioSecond(double songClockSecond) =>
        songClockSecond - _settings.TimingOffsetMs / 1000.0;

    private readonly record struct ProjectionInputs(double Time, double Bar, int TimingOffsetMs,
        double Second, double Scroll, bool V2);
    private ProjectionInputs? _lastProjectionInputs;

    private ProjectionInputs ReadProjectionInputs(double t, double currentBar)
    {
        var timingOffset = _settings.TimingOffsetMs;
        if (_retainVisualInputs && _lastProjectionInputs is { } last &&
            last.Time == t && last.Bar == currentBar && last.TimingOffsetMs == timingOffset)
            return last;

        // Keep the original v2 audio-time conversion: some callers supply a legacy/preview bar.
        // The settings key also handles offset changes at the same time during previews.
        var metadata = _chart.V2Metadata;
        var inputs = new ProjectionInputs(t, currentBar, timingOffset, t, 0, false);
        if (metadata?.ScrollMap is { } scroll && metadata.BpmTimeline is { } bpm)
        {
            var second = t - timingOffset / 1000.0;
            inputs = inputs with { Second = second, Scroll = scroll.RuntimeSpeedAt(bpm.RuntimeBarAt(second)), V2 = true };
        }
        else
            inputs = inputs with { Scroll = _dropSpeedMap.SpeedAt(currentBar) };
        if (_retainVisualInputs)
            _lastProjectionInputs = inputs;
        return inputs;
    }

    private float VisualDistanceFor(Note n, double t, double currentBar)
    {
        var projection = ReadProjectionInputs(t, currentBar);
        if (projection.V2)
        {
            var noteSecond = _runtimeNoteSeconds.TryGetValue(n.Id, out var cached)
                ? cached : n.Second;
            return (float)VisualScrollMath.DistancePixels(
                noteSecond, projection.Second, BaseFallSpeedPx, projection.Scroll, _fallSpeedMultiplier,
                n.V2Metadata?.ScrollSpeedMultiplier ?? 1.0);
        }
        return (float)VisualScrollMath.DistancePixels(
            n.Second, t, BaseFallSpeedPx, projection.Scroll,
            _fallSpeedMultiplier);
    }

    private Vector2 PositionFor(Note n, double t, double currentBar)
    {
        // 生成时尚未到线；保留钳制只防止跳转启动时把新视图放到线外。
        var rem = Mathf.Max(0f, VisualDistanceFor(n, t, currentBar));
        return _geometry.PositionAt(n, rem);
    }

    private Vector2 PositionForActiveView(Note n, double t, double currentBar)
    {
        var rem = VisualDistanceFor(n, t, currentBar);
        if (!MovesPastJudgeLineContinuously(n.Type))
            rem = Mathf.Max(0f, rem);
        return _geometry.PositionAt(n, rem);
    }

    private static bool MovesPastJudgeLineContinuously(NoteType type) =>
        UsesOrdinaryMissFallthrough(type);

    private double VisualScrollAt(double t, double currentBar) => ReadProjectionInputs(t, currentBar).Scroll;

    private float MissFallSpeedPx(Note note, double t, double currentBar)
    {
        var speed = (float)VisualScrollMath.FallthroughSpeedPixelsPerSecond(
            BaseFallSpeedPx, VisualScrollAt(t, currentBar), _fallSpeedMultiplier,
            note.V2Metadata?.ScrollSpeedMultiplier ?? 1.0);
        if (note.Track != Track.Center)
            speed *= SideDistScale;
        return Mathf.Max(60f, speed);
    }
}

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

    private float VisualDistanceFor(Note n, double t, double currentBar)
    {
        if (_chart.V2Metadata?.ScrollMap != null && _chart.V2Metadata?.BpmTimeline != null)
        {
            var currentSecond = ChartAudioSecond(t);
            var currentBarRuntime = _chart.V2Metadata!.BpmTimeline.RuntimeBarAt(currentSecond);
            var noteSecond = _runtimeNoteSeconds.TryGetValue(n.Id, out var cached)
                ? cached : n.Second;
            return (float)VisualScrollMath.DistancePixels(
                noteSecond, currentSecond, BaseFallSpeedPx,
                _chart.V2Metadata!.ScrollMap.RuntimeSpeedAt(currentBarRuntime), _fallSpeedMultiplier,
                n.V2Metadata?.ScrollSpeedMultiplier ?? 1.0);
        }
        return (float)VisualScrollMath.DistancePixels(
            n.Second, t, BaseFallSpeedPx, _dropSpeedMap.SpeedAt(currentBar),
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

    private double VisualScrollAt(double t, double currentBar)
    {
        if (_chart.V2Metadata?.ScrollMap != null && _chart.V2Metadata?.BpmTimeline != null)
        {
            var currentBarRuntime = _chart.V2Metadata!.BpmTimeline.RuntimeBarAt(ChartAudioSecond(t));
            return _chart.V2Metadata!.ScrollMap.RuntimeSpeedAt(currentBarRuntime);
        }
        return _dropSpeedMap.SpeedAt(currentBar);
    }

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

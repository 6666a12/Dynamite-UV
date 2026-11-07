using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

[Flags]
public enum EditorSpatialRisk
{
    None = 0, RecommendedOverflow = 1, Invisible = 2, Unreachable = 4, NumericRange = 8,
}

public sealed record EditorSpatialReport(EditorSpatialRisk Risks, bool PathSampled, int CheckedPoints);

/// <summary>
/// Advisory geometry at the judgement rail in the community game's ideal 1920x1080 content.
/// Includes normal and mirrored play. Not a device, occlusion, timing or complete path-playability proof.
/// Never changes chart data or validation. Path samples can reveal risk, not certify safety.
/// </summary>
public static class EditorSpatialAnalysis
{
    // Mirrors the current community game's input policy, not an original-game compatibility claim.
    public const double StageOffsetY = 40;
    public const double HalfTouchWidth = .2;

    public static EditorSpatialRisk Point(EditorTrack track, V2NoteType type, double center, double width)
    {
        var risk = EditorSpatialRisk.None;
        if (!double.IsFinite(center) || !double.IsFinite(width) || width <= 0)
            return EditorSpatialRisk.NumericRange;
        var left = center - width / 2;
        var right = center + width / 2;
        if (left < 0 || right > 5) risk |= EditorSpatialRisk.RecommendedOverflow;
        foreach (var mirrored in new[] { false, true })
        {
            var (displayTrack, displayCenter) = GameplayStageGeometry.MirroredDisplay(
                EditorStagePlacement.ToRuntimeTrack(track), center, mirrored);
            var central = displayTrack == Track.Center;
            var unit = central ? GameplayStageGeometry.CenterUnitPx : GameplayStageGeometry.SideUnitPx;
            var origin = central ? GameplayStageGeometry.CenterX0 : GameplayStageGeometry.SideY0 + StageOffsetY;
            var extent = central ? GameplayStageGeometry.DesignWidth : GameplayStageGeometry.DesignHeight;
            var pixelCenter = central ? origin + displayCenter * unit : origin - displayCenter * unit;
            var visualUnit = central ? unit : GameplayStageGeometry.SideNoteLengthUnitPx;
            if (type == V2NoteType.BarLine && !central) visualUnit = GameplayStageGeometry.SidePositionUnitPx;
            var pixels = Math.Max(12, width * visualUnit * GameplayStageGeometry.NoteVisualScale);
            if (!double.IsFinite(pixelCenter) || !double.IsFinite(pixels) ||
                Math.Abs(pixelCenter) > float.MaxValue || pixels > float.MaxValue)
            {
                risk |= EditorSpatialRisk.NumericRange;
                continue;
            }
            if (pixelCenter + pixels / 2 <= 0 || pixelCenter - pixels / 2 >= extent)
                risk |= EditorSpatialRisk.Invisible;
            if (type == V2NoteType.BarLine) continue; // No player input required.
            var low = central ? -origin / unit : (origin - extent) / unit;
            var high = central ? (extent - origin) / unit : origin / unit;
            // Mixer receiver/body is stricter than its scored head; report the stricter risk.
            var expansion = type is V2NoteType.Mixer or V2NoteType.Mine ? 0 : HalfTouchWidth;
            if (displayCenter + width / 2 + expansion < low || displayCenter - width / 2 - expansion > high)
                risk |= EditorSpatialRisk.Unreachable;
        }
        return risk;
    }

    public static EditorSpatialReport Analyze(EditableNote note)
    {
        var risk = Point(note.Track, note.Type, note.Center, note.Width);
        var count = 1;
        foreach (var node in note.Nodes)
        {
            risk |= Point(note.Track, note.Type, node.Center, node.Width);
            count++;
        }
        if (note.Nodes.Count == 0) return new(risk, false, count);
        // Bound the work for huge charts. Always check authored points above, sample the first
        // 4096 intermediate points. No clean bill of health is inferred from a clear sample set.
        var shape = EditorPathProjection.ShapeOf(note);
        var path = new V2PathNote
        {
            Id = note.Id, Type = note.Type, Time = note.Time, Center = note.Center, Width = note.Width,
            CurveToNext = note.CurveToNext,
            Nodes = note.Nodes.Select(n => new V2PathNode { Id = n.Id, Time = n.Time,
                Center = n.Center, Width = n.Width, CurveToNext = n.CurveToNext, Judge = n.Judge,
                JudgeWasExplicit = n.Judge.HasValue }).ToArray(),
        };
        try
        {
            var evaluator = new V2PathEvaluator(path);
            var start = shape.Head.Time;
            var budget = 4096;
            foreach (var node in shape.Nodes)
            {
                for (var i = 1; i < 32 && budget > 0; i++, budget--)
                {
                    var sample = evaluator.Evaluate(start + (node.Time - start) * ExactBarTime.FromFraction(i, 32));
                    risk |= Point(note.Track, note.Type, sample.Center, sample.Width);
                    count++;
                }
                start = node.Time;
                if (budget == 0) break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ArithmeticException or InvalidOperationException)
        {
            risk |= EditorSpatialRisk.NumericRange;
        }
        return new(risk, true, count);
    }
}

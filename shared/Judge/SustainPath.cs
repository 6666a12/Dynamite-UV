using DuxShared.Chart;

namespace DuxShared.Judge;

public enum SustainKind
{
    Hold,
    Mixer,
}

/// <summary>A Hold/Mixer chain with time-interpolated chart-coordinate bounds.</summary>
public sealed class SustainPath
{
    public required SustainKind Kind { get; init; }
    public required int HeadId { get; init; }
    public required Track Track { get; init; }
    public required IReadOnlyList<Note> Nodes { get; init; }
    /// <summary>Exact v2 path evaluator when this sustain originated from a v2 chart.</summary>
    public DuxShared.Chart.V2.V2PathEvaluator? V2Evaluator { get; init; }
    /// <summary>Exact v2 BPM map used to translate runtime seconds back to path BarTime.</summary>
    public DuxShared.Chart.V2.V2BpmTimeline? V2BpmTimeline { get; init; }

    public Note Head => Nodes[0];
    public Note End => Nodes[^1];
    public double StartTime => Head.Second;
    public double EndTime => End.Second;

    public NoteBounds BoundsAt(double time)
    {
        if (V2Evaluator is not null && V2BpmTimeline is not null)
        {
            var sample = V2Evaluator.Evaluate(V2BpmTimeline.ToBarTime(time));
            return new NoteBounds(sample.Left, sample.Right);
        }
        if (Nodes.Count == 1 || time <= Nodes[0].Second)
            return InputJudgeRules.Bounds(Nodes[0].Position, Nodes[0].Width);
        if (time >= Nodes[^1].Second)
            return InputJudgeRules.Bounds(Nodes[^1].Position, Nodes[^1].Width);

        for (var i = 1; i < Nodes.Count; i++)
        {
            var right = Nodes[i];
            if (time > right.Second)
                continue;

            var left = Nodes[i - 1];
            var duration = right.Second - left.Second;
            var ratio = duration <= 1e-9 ? 1.0 : (time - left.Second) / duration;
            var leftEdge = left.Position + (right.Position - left.Position) * ratio;
            var right0 = left.Position + left.Width;
            var right1 = right.Position + right.Width;
            var rightEdge = right0 + (right1 - right0) * ratio;
            return new NoteBounds(Math.Min(leftEdge, rightEdge), Math.Max(leftEdge, rightEdge));
        }

        return InputJudgeRules.Bounds(Nodes[^1].Position, Nodes[^1].Width);
    }
}

namespace DynamiteUniverse.Shared.Judge;

/// <summary>Logical receiver width is the chart width; skin inset/glow is decorative.</summary>
public static class MixerContactGeometry
{
    private const double ContactTolerance = 1e-10;
    public static bool CanCatch(NoteBounds body, double receiver) =>
        double.IsFinite(receiver) && receiver >= body.Left - ContactTolerance && receiver <= body.Right + ContactTolerance;

    /// <summary>All possible contact transitions, including path discontinuities.</summary>
    public static void AddBoundaries(SustainPath path, double from, double to,
        double receiver, List<double> output) => AddBoundaries(path, from, to, receiver, output, []);

    internal static void AddBoundaries(SustainPath path, double from, double to,
        double receiver, List<double> output, List<double> bars)
    {
        if (path.V2Evaluator is { } evaluator && path.V2BpmTimeline is { } bpm)
        {
            bars.Clear();
            evaluator.AddReceiverBoundaries(bpm.RuntimeBarAt(from), bpm.RuntimeBarAt(to), receiver, bars, ContactTolerance);
            foreach (var bar in bars)
            {
                var second = bpm.RuntimeSecondsAt(bar);
                if (second > from && second < to) output.Add(second);
            }
            return;
        }
        for (var i = 1; i < path.Nodes.Count; i++)
        {
            var a = path.Nodes[i - 1];
            var b = path.Nodes[i];
            if (b.Second <= from || a.Second >= to) continue;
            if (b.Second > from && b.Second < to) output.Add(b.Second);
            Root(a.Position, b.Position);
            Root(a.Position + a.Width, b.Position + b.Width);

            void Root(double left, double right)
            {
                if (left == right) return;
                var u = (receiver - left) / (right - left);
                var time = a.Second + (b.Second - a.Second) * u;
                if (u > 0 && u < 1 && time > from && time < to) output.Add(time);
            }
        }
    }
}

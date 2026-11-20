namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>Affine image of the compiled progress polyline. The image of each piece is an
/// interval, so its extrema occur at knots even for an overshooting or reversing expression.</summary>
public static class CustomCurveGeometry
{
    public static (double Minimum, double Maximum) Range(double from, double to, CustomCurve curve)
    {
        var a = Interpolate(from, to, curve.Minimum);
        var b = Interpolate(from, to, curve.Maximum);
        return (Math.Min(a, b), Math.Max(a, b));
    }

    internal static double Interpolate(double from, double to, double p)
    {
        if (p == 0 || from == to) return from;
        if (p == 1) return to;
        var delta = to - from;
        return double.IsFinite(delta) ? Math.FusedMultiplyAdd(delta, p, from)
            : Math.FusedMultiplyAdd(from, 1 - p, to * p);
    }
}

/// <summary>One occurrence of a compiled curve at a specific exact bar interval. Normalize in
/// local coordinates; subtracting two large rounded bar timestamps first can erase a short span.</summary>
internal sealed class CustomCurveInterval(ExactBarTime start, ExactBarTime end, CustomCurve curve)
{
    internal CustomCurve Curve => curve;
    private readonly double _start = start.ToDouble();
    private readonly double _remainder = (start - ExactBarTime.FromDouble(start.ToDouble())).ToDouble();
    private readonly double _length = (end - start).ToDouble();
    // Ordinary song positions use allocation-free local arithmetic. Where the origin ULP
    // becomes appreciable relative to the span, form the final intersection in rationals.
    private readonly bool _precisionSensitive = Math.BitIncrement(start.ToDouble()) - start.ToDouble() >
        (end - start).ToDouble() * 1e-9;

    internal static void MarkRounded(ExactBarTime time, ref HashSet<double>? ambiguous)
    {
        var bar = time.ToDouble();
        if (ExactBarTime.FromDouble(bar) != time) (ambiguous ??= []).Add(bar);
    }

    internal double Normalized(double bar) => Math.Clamp(_precisionSensitive && double.IsFinite(bar)
        ? ((ExactBarTime.FromDouble(bar) - start) / (end - start)).ToDouble()
        : ((bar - _start) - _remainder) / _length, 0, 1);

    private double Bar(double u) => _precisionSensitive
        ? (start + (end - start) * ExactBarTime.FromDouble(u)).ToDouble()
        : _start + Math.FusedMultiplyAdd(u, _length, _remainder);

    // Only visit the pieces overlapping this replay window, not every knot on every frame.
    internal void AddCrossings(double from, double to, double receiver, V2PathSample head,
        V2PathSample tail, List<double> output, double tolerance)
    {
        var knots = curve.Knots;
        var lower = Normalized(from);
        var upper = Normalized(to);
        var lo = 0;
        var hi = knots.Count - 1;
        while (lo + 1 < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (knots[mid] < lower) lo = mid; else hi = mid;
        }
        var left = Sample(knots[lo]);
        for (var i = lo; i + 1 < knots.Count && knots[i] <= upper; i++)
        {
            var right = Sample(knots[i + 1]);
            Edge(knots[i], knots[i + 1], left.Left - tolerance, right.Left - tolerance, receiver);
            Edge(knots[i], knots[i + 1], left.Right + tolerance, right.Right + tolerance, receiver);
            left = right;
        }

        V2PathSample Sample(double u)
        {
            var p = curve.Evaluate(u);
            return new(CustomCurveGeometry.Interpolate(head.Center, tail.Center, p),
                CustomCurveGeometry.Interpolate(head.Width, tail.Width, p));
        }

        void Edge(double a, double b, double y0, double y1, double target)
        {
            // Touches at knots, including the ends of a plateau, matter even without a sign change.
            if (target == y0) Add(a);
            if (target == y1) Add(b);
            if (!(y0 < target && target < y1 || y1 < target && target < y0)) return;
            var d = y1 - y0;
            var f = double.IsFinite(d) ? (target - y0) / d :
                (target * .5 - y0 * .5) / (y1 * .5 - y0 * .5);
            Add(Math.FusedMultiplyAdd(b - a, f, a));
        }

        void Add(double u)
        {
            var time = Bar(u);
            if (time > from && time < to) output.Add(time);
        }
    }
}

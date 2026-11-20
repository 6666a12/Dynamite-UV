namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>A validated author expression and its authoritative, immutable piecewise-linear progress.</summary>
public sealed class CustomCurve
{
    public const double ProgressTolerance = 0.0001;
    public const double EndpointTolerance = 1e-9;
    public const int MaximumSegments = 2048;
    private static long _compilations;
    internal static long CompilationCount => Interlocked.Read(ref _compilations);
    private const int CacheLimit = 64;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Lazy<CustomCurve>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> CacheOrder = new();
    private readonly double[] _times, _values;
    public IReadOnlyList<double> Knots { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public string Canonical { get; }

    private CustomCurve(CurveTerm term)
    {
        Canonical = term.Canonical;
        var times = new List<double> { 0 };
        var values = new List<double> { 0 };
        var budget = 0;
        CurveInterval At(double t)
        {
            var value = term.At(CurveInterval.Point(t), false).Value;
            if (!value.Known) throw new FormatException(FormattableString.Invariant($"Formula is undefined or cannot be certified at t={t:R}."));
            return value;
        }
        var zero = At(0); var one = At(1);
        static double EndpointError(CurveInterval v, double anchor) =>
            (v - CurveInterval.Point(anchor)).Magnitude;
        if (EndpointError(zero, 0) > EndpointTolerance || EndpointError(one, 1) > EndpointTolerance)
            throw new FormatException("Formula endpoints must satisfy f(0)=0 and f(1)=1 (tolerance 1e-9).");
        Subdivide(0, 1, 0, 1, zero, one, 0);
        _times = times.ToArray(); _values = values.ToArray();
        Knots = Array.AsReadOnly(_times);
        Minimum = _values.Min(); Maximum = _values.Max();

        void Subdivide(double a, double b, double left, double right, CurveInterval av, CurveInterval bv, int depth)
        {
            if ((budget += term.Cost) > 250_000 || times.Count > MaximumSegments)
                throw new FormatException("Formula exceeds the certified curve complexity budget.");
            var jet = term.At(new(a, b), true);
            if (jet.Value.Known)
            {
                var chord = new CurveInterval(Math.Min(left, right), Math.Max(left, right));
                // Bound |f - chord| directly, even for a vertical endpoint tangent.
                var error = (jet.Value - chord).Magnitude;
                if (jet.Slope.Known)
                {
                    // Chord interpolation error = h*u*(1-u)*(left secant - right secant).
                    // All secants are in Slope, including across abs/min/max corners.
                    var spread = CurveInterval.Point(jet.Slope.Hi) - CurveInterval.Point(jet.Slope.Lo);
                    var endpoint = Math.Max(EndpointError(av, left), EndpointError(bv, right));
                    var derivativeError = CurveInterval.Point(endpoint) + spread * CurveInterval.Point((b - a) * .25);
                    error = Math.Min(error, derivativeError.Hi);
                }
                // Dyadic segment endpoints make (t-a)/(b-a) exact for binary64 t in the segment.
                // Reserve round-off for the final subtract/multiply/add, including subnormals.
                var roundOff = CurveInterval.Point(Math.Max(1, chord.Magnitude)) * CurveInterval.Point(2e-15);
                if ((CurveInterval.Point(error) + roundOff).Hi <= ProgressTolerance)
                {
                    times.Add(b); values.Add(right); return;
                }
            }
            if (depth >= 48) throw new FormatException(FormattableString.Invariant(
                $"Cannot certify the domain or precision on t=[{a:R}, {b:R}]."));
            var middle = (a + b) * .5;
            var mv = At(middle); var mid = mv.Midpoint;
            Subdivide(a, middle, left, mid, av, mv, depth + 1);
            Subdivide(middle, b, mid, right, mv, bv, depth + 1);
        }
    }

    /// <summary>Compile the restricted math grammar; malformed, unprovable or over-budget expressions fail closed.</summary>
    public static CustomCurve Compile(string expr)
    {
        if (expr is null || expr.Length is 0 or > 1024) throw new FormatException("Formula length must be 1 to 1024 characters.");
        Lazy<CustomCurve> entry;
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(expr, out entry!))
            {
                if (Cache.Count >= CacheLimit) Cache.Remove(CacheOrder.Dequeue());
                entry = new(() =>
                {
                    Interlocked.Increment(ref _compilations);
                    return new CustomCurve(CurveParser.Parse(expr));
                }, LazyThreadSafetyMode.ExecutionAndPublication);
                Cache.Add(expr, entry); CacheOrder.Enqueue(expr);
            }
        }
        return entry.Value;
    }

    /// <summary>Sample the actual polyline. Finite out-of-range arguments use the nearest endpoint.</summary>
    public double Evaluate(double t)
    {
        if (!double.IsFinite(t)) throw new ArgumentOutOfRangeException(nameof(t));
        if (t <= 0) return 0; if (t >= 1) return 1;
        var i = Array.BinarySearch(_times, t);
        if (i >= 0) return _values[i];
        i = ~i;
        var u = (t - _times[i - 1]) / (_times[i] - _times[i - 1]);
        var a = _values[i - 1]; var b = _values[i];
        return Math.Clamp(a + (b - a) * u, Math.Min(a, b), Math.Max(a, b));
    }
}

using System.Numerics;

namespace DuxShared.Chart.V2;

/// <summary>An exact-time BPM map with exact rational segment accumulation.</summary>
public sealed class V2BpmTimeline
{
    private readonly Segment[] _segments;

    private sealed record Segment(
        ExactBarTime Time, double Bpm, ExactBarTime ExactBpm, ExactBarTime StartSecond);

    public V2BpmTimeline(IReadOnlyList<V2BpmEvent> events, double audioOffsetSec)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            throw new ArgumentException("A BPM timeline requires at least one event.", nameof(events));
        if (!double.IsFinite(audioOffsetSec))
            throw new ArgumentOutOfRangeException(nameof(audioOffsetSec));

        _segments = new Segment[events.Count];
        var startSecond = ExactBarTime.FromDouble(audioOffsetSec);
        for (var i = 0; i < events.Count; i++)
        {
            var current = events[i];
            if (!double.IsFinite(current.Bpm) || current.Bpm <= 0.0)
                throw new ArgumentException("BPM values must be finite and positive.", nameof(events));
            if (i > 0)
            {
                var previous = _segments[i - 1];
                if (current.Time <= previous.Time)
                    throw new ArgumentException("BPM event times must be strictly increasing.", nameof(events));
                startSecond += (current.Time - previous.Time) * new BigInteger(240) /
                    previous.ExactBpm;
            }
            _segments[i] = new Segment(current.Time, current.Bpm,
                ExactBarTime.FromDouble(current.Bpm), startSecond);
        }
    }

    /// <summary>Converts exact BarTime to audio seconds, rounding only the final result to binary64.</summary>
    public double ToSeconds(ExactBarTime time) => ToExactSeconds(time).ToDouble();

    /// <summary>Returns the exact rational audio-second value implied by binary64 BPM and offset data.</summary>
    public ExactBarTime ToExactSeconds(ExactBarTime time)
    {
        var segment = _segments[FindBarSegment(time)];
        return segment.StartSecond +
            (time - segment.Time) * new BigInteger(240) / segment.ExactBpm;
    }

    /// <summary>Converts a finite binary64 audio second to an exact rational BarTime.</summary>
    public ExactBarTime ToBarTime(double second)
    {
        if (!double.IsFinite(second))
            throw new ArgumentOutOfRangeException(nameof(second));
        var exactSecond = ExactBarTime.FromDouble(second);
        var segment = _segments[FindSecondSegment(exactSecond)];
        return segment.Time +
            (exactSecond - segment.StartSecond) * segment.ExactBpm / new BigInteger(240);
    }

    public double BpmAt(ExactBarTime time) => _segments[FindBarSegment(time)].Bpm;

    public double BpmAtSeconds(double second)
    {
        if (!double.IsFinite(second))
            throw new ArgumentOutOfRangeException(nameof(second));
        return _segments[FindSecondSegment(ExactBarTime.FromDouble(second))].Bpm;
    }

    private int FindBarSegment(ExactBarTime time)
    {
        var lo = 0;
        var hi = _segments.Length;
        while (lo + 1 < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_segments[mid].Time <= time)
                lo = mid;
            else
                hi = mid;
        }
        return lo;
    }

    private int FindSecondSegment(ExactBarTime second)
    {
        var lo = 0;
        var hi = _segments.Length;
        while (lo + 1 < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_segments[mid].StartSecond <= second)
                lo = mid;
            else
                hi = mid;
        }
        return lo;
    }
}

/// <summary>Frozen curve equations shared by scroll and path evaluation.</summary>
public static class V2Curves
{
    public static double Progress(V2PathCurve curve, double u) => curve switch
    {
        V2PathCurve.Linear => u,
        V2PathCurve.Hold => u >= 1.0 ? 1.0 : 0.0,
        V2PathCurve.EaseInQuad => u * u,
        V2PathCurve.EaseOutQuad => 1.0 - (1.0 - u) * (1.0 - u),
        V2PathCurve.EaseInOutCubic => u < 0.5
            ? 4.0 * u * u * u
            : 1.0 - Math.Pow(-2.0 * u + 2.0, 3.0) / 2.0,
        _ => throw new ArgumentException("Smooth paths require the PCHIP evaluator.", nameof(curve)),
    };

    public static double Progress(V2ScrollCurve curve, double u) => curve switch
    {
        V2ScrollCurve.Linear => u,
        V2ScrollCurve.Hold => u >= 1.0 ? 1.0 : 0.0,
        V2ScrollCurve.EaseInQuad => u * u,
        V2ScrollCurve.EaseOutQuad => 1.0 - (1.0 - u) * (1.0 - u),
        V2ScrollCurve.EaseInOutCubic => u < 0.5
            ? 4.0 * u * u * u
            : 1.0 - Math.Pow(-2.0 * u + 2.0, 3.0) / 2.0,
        _ => throw new ArgumentOutOfRangeException(nameof(curve)),
    };
}

/// <summary>Evaluates the v2 non-integrated visual scroll-speed model.</summary>
public sealed class V2ScrollMap
{
    private static readonly V2ScrollEvent Implicit = new()
    {
        Time = ExactBarTime.Zero,
        Value = 1.0,
    };

    private readonly IReadOnlyList<V2ScrollEvent> _events;

    public V2ScrollMap(IReadOnlyList<V2ScrollEvent>? events)
    {
        _events = events is null || events.Count == 0 ? [Implicit] : events;
    }

    public double SpeedAt(ExactBarTime time)
    {
        if (time <= _events[0].Time)
            return _events[0].Value;
        if (time >= _events[^1].Time)
            return _events[^1].Value;

        var lo = 0;
        var hi = _events.Count - 1;
        while (lo + 1 < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_events[mid].Time < time)
                lo = mid;
            else
                hi = mid;
        }
        if (time == _events[hi].Time)
            return _events[hi].Value;

        var left = _events[lo];
        var right = _events[hi];
        var u = ((time - left.Time) / (right.Time - left.Time)).ToDouble();
        var p = V2Curves.Progress(left.CurveToNext ?? V2ScrollCurve.Linear, u);
        return left.Value + (right.Value - left.Value) * p;
    }

    public double VisualDistanceSeconds(
        double noteSecond, double currentSecond, ExactBarTime currentTime, double playerSpeed) =>
        (noteSecond - currentSecond) * SpeedAt(currentTime) * playerSpeed;
}

/// <summary>A center/width path sample. Overscan is intentionally retained.</summary>
public readonly record struct V2PathSample(double Center, double Width)
{
    public double Left => Center - Width / 2.0;
    public double Right => Center + Width / 2.0;
}

/// <summary>
/// Evaluates linear, hold, easing, and the frozen pchip-v1 smooth path using exact BarTime interval
/// selection and binary64 curve arithmetic.
/// </summary>
public sealed class V2PathEvaluator
{
    private readonly Point[] _points;
    private readonly V2PathCurve[] _curves;

    private readonly record struct Point(ExactBarTime Time, double Center, double Width);

    public V2PathEvaluator(V2PathNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _points = new Point[note.Nodes.Count + 1];
        _curves = new V2PathCurve[note.Nodes.Count];
        _points[0] = new Point(note.Time, note.Center, note.Width);
        _curves[0] = note.CurveToNext ?? V2PathCurve.Linear;
        for (var i = 0; i < note.Nodes.Count; i++)
        {
            var node = note.Nodes[i];
            _points[i + 1] = new Point(node.Time, node.Center, node.Width);
            if (i + 1 < _points.Length - 1)
                _curves[i + 1] = node.CurveToNext ?? V2PathCurve.Linear;
        }
    }

    public ExactBarTime StartTime => _points[0].Time;
    public ExactBarTime EndTime => _points[^1].Time;

    public V2PathSample Evaluate(ExactBarTime time)
    {
        if (time <= _points[0].Time)
            return Sample(_points[0]);
        if (time >= _points[^1].Time)
            return Sample(_points[^1]);

        var interval = FindInterval(time);
        if (time == _points[interval + 1].Time)
            return Sample(_points[interval + 1]);
        var u = ((time - _points[interval].Time) /
                 (_points[interval + 1].Time - _points[interval].Time)).ToDouble();
        if (_curves[interval] != V2PathCurve.Smooth)
        {
            var p = V2Curves.Progress(_curves[interval], u);
            return new V2PathSample(
                Lerp(_points[interval].Center, _points[interval + 1].Center, p),
                Lerp(_points[interval].Width, _points[interval + 1].Width, p));
        }

        var (runStart, runEnd) = SmoothRun(interval);
        var centerSlopes = PchipSlopes(runStart, runEnd, static point => point.Center);
        var widthSlopes = PchipSlopes(runStart, runEnd, static point => point.Width);
        var local = interval - runStart;
        var h = (_points[interval + 1].Time - _points[interval].Time).ToDouble();
        return new V2PathSample(
            Hermite(_points[interval].Center, _points[interval + 1].Center,
                centerSlopes[local], centerSlopes[local + 1], h, u),
            Hermite(_points[interval].Width, _points[interval + 1].Width,
                widthSlopes[local], widthSlopes[local + 1], h, u));
    }

    /// <summary>
    /// Checks finite center and strictly positive width over every complete interval. Smooth width
    /// cubics are evaluated at both endpoints and every derivative root inside the interval.
    /// </summary>
    public bool IsFiniteAndPositive(out int failingInterval, out string reason)
    {
        for (var i = 0; i < _points.Length; i++)
        {
            if (!double.IsFinite(_points[i].Center))
            {
                failingInterval = Math.Max(0, i - 1);
                reason = "path center is nonfinite";
                return false;
            }
            if (!double.IsFinite(_points[i].Width) || _points[i].Width <= 0.0)
            {
                failingInterval = Math.Max(0, i - 1);
                reason = "path width is nonfinite or not positive";
                return false;
            }
        }

        for (var interval = 0; interval < _curves.Length; interval++)
        {
            if (_curves[interval] != V2PathCurve.Smooth)
                continue;
            var (runStart, runEnd) = SmoothRun(interval);
            var centerSlopes = PchipSlopes(runStart, runEnd, static point => point.Center);
            var widthSlopes = PchipSlopes(runStart, runEnd, static point => point.Width);
            if (centerSlopes.Any(slope => !double.IsFinite(slope)) ||
                widthSlopes.Any(slope => !double.IsFinite(slope)))
            {
                failingInterval = interval;
                reason = "pchip-v1 slope is nonfinite";
                return false;
            }

            var local = interval - runStart;
            var h = (_points[interval + 1].Time - _points[interval].Time).ToDouble();
            var centerPolynomial = HermitePolynomial(_points[interval].Center,
                _points[interval + 1].Center, centerSlopes[local], centerSlopes[local + 1], h);
            var widthPolynomial = HermitePolynomial(_points[interval].Width,
                _points[interval + 1].Width, widthSlopes[local], widthSlopes[local + 1], h);
            if (!centerPolynomial.IsFinite || !widthPolynomial.IsFinite)
            {
                failingInterval = interval;
                reason = "pchip-v1 polynomial is nonfinite";
                return false;
            }
            foreach (var u in widthPolynomial.DerivativeRoots())
            {
                if (u > 0.0 && u < 1.0 && widthPolynomial.Evaluate(u) <= 0.0)
                {
                    failingInterval = interval;
                    reason = "pchip-v1 width is not positive inside the interval";
                    return false;
                }
            }
        }

        failingInterval = -1;
        reason = string.Empty;
        return true;
    }

    private int FindInterval(ExactBarTime time)
    {
        var lo = 0;
        var hi = _points.Length - 1;
        while (lo + 1 < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_points[mid].Time < time)
                lo = mid;
            else
                hi = mid;
        }
        return lo;
    }

    private (int Start, int End) SmoothRun(int interval)
    {
        var start = interval;
        while (start > 0 && _curves[start - 1] == V2PathCurve.Smooth)
            start--;
        var end = interval;
        while (end + 1 < _curves.Length && _curves[end + 1] == V2PathCurve.Smooth)
            end++;
        return (start, end);
    }

    private double[] PchipSlopes(int runStart, int runEnd, Func<Point, double> selector)
    {
        var pointCount = runEnd - runStart + 2;
        var h = new double[pointCount - 1];
        var d = new double[pointCount - 1];
        for (var i = 0; i < h.Length; i++)
        {
            var point = runStart + i;
            h[i] = (_points[point + 1].Time - _points[point].Time).ToDouble();
            d[i] = (selector(_points[point + 1]) - selector(_points[point])) / h[i];
        }
        if (pointCount == 2)
            return [d[0], d[0]];

        var m = new double[pointCount];
        m[0] = EndpointSlope(h[0], h[1], d[0], d[1]);
        for (var i = 1; i < pointCount - 1; i++)
        {
            if (d[i - 1] == 0.0 || d[i] == 0.0 || Math.Sign(d[i - 1]) != Math.Sign(d[i]))
            {
                m[i] = 0.0;
                continue;
            }
            var w1 = 2.0 * h[i] + h[i - 1];
            var w2 = h[i] + 2.0 * h[i - 1];
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
        }
        m[^1] = EndpointSlope(h[^1], h[^2], d[^1], d[^2]);
        return m;
    }

    private static double EndpointSlope(double h0, double h1, double d0, double d1)
    {
        var slope = ((2.0 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
        if (Math.Sign(slope) != Math.Sign(d0))
            return 0.0;
        if (Math.Sign(d0) != Math.Sign(d1) && Math.Abs(slope) > Math.Abs(3.0 * d0))
            return 3.0 * d0;
        return slope;
    }

    private static double Hermite(double y0, double y1, double m0, double m1,
        double h, double u)
    {
        var u2 = u * u;
        var u3 = u2 * u;
        return (2.0 * u3 - 3.0 * u2 + 1.0) * y0 +
               (u3 - 2.0 * u2 + u) * h * m0 +
               (-2.0 * u3 + 3.0 * u2) * y1 +
               (u3 - u2) * h * m1;
    }

    private static Cubic HermitePolynomial(double y0, double y1, double m0, double m1,
        double h) => new(
        2.0 * y0 - 2.0 * y1 + h * (m0 + m1),
        -3.0 * y0 + 3.0 * y1 - h * (2.0 * m0 + m1),
        h * m0,
        y0);

    private static double Lerp(double left, double right, double progress) =>
        left + (right - left) * progress;

    private static V2PathSample Sample(Point point) => new(point.Center, point.Width);

    private readonly record struct Cubic(double A, double B, double C, double D)
    {
        public bool IsFinite =>
            double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) && double.IsFinite(D);

        public double Evaluate(double u) => ((A * u + B) * u + C) * u + D;

        public IEnumerable<double> DerivativeRoots()
        {
            const double epsilon = 1e-15;
            if (Math.Abs(A) <= epsilon)
            {
                if (Math.Abs(B) > epsilon)
                    yield return -C / (2.0 * B);
                yield break;
            }
            var discriminant = 4.0 * B * B - 12.0 * A * C;
            if (discriminant < 0.0)
                yield break;
            var root = Math.Sqrt(discriminant);
            yield return (-2.0 * B - root) / (6.0 * A);
            if (root > 0.0)
                yield return (-2.0 * B + root) / (6.0 * A);
        }
    }
}

/// <summary>Exact Mixer head-relative eighth-bar judgement grid.</summary>
public static class V2MixerTicks
{
    public static BigInteger TickCount(V2PathNote mixer)
    {
        if (mixer.Type != V2NoteType.Mixer)
            throw new ArgumentException("A Mixer path is required.", nameof(mixer));
        var duration = mixer.EndTime - mixer.Time;
        if (duration < ExactBarTime.Zero)
            throw new ArgumentException("Mixer tail precedes its head.", nameof(mixer));
        return BigInteger.Divide(duration.ImproperNumerator * 8, duration.Denominator);
    }

    /// <summary>Enumerates k=1..floor(8*(E-H)); the head at k=0 is not returned.</summary>
    public static IEnumerable<ExactBarTime> Enumerate(V2PathNote mixer)
    {
        var count = TickCount(mixer);
        for (var k = BigInteger.One; k <= count; k++)
            yield return mixer.Time + ExactBarTime.FromFraction(k, 8);
    }
}

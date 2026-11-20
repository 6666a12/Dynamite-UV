namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>Fixed range reductions and explicit Taylor remainder bounds; compilation does not depend on platform libm trig/pow/log.</summary>
internal static class CurveElementary
{
    private static CurveInterval P(double x) => CurveInterval.Point(x);
    internal static readonly CurveInterval Pi = CurveInterval.Enclose(Math.PI), E = CurveInterval.Enclose(Math.E);
    private static readonly CurveInterval HalfPi = Pi * P(.5);
    private static readonly CurveInterval Ln2 = CurveInterval.Enclose(0.6931471805599453);
    private static readonly CurveInterval Tiny = new(-1e-18, 1e-18);

    internal static CurveInterval Trig(CurveInterval x, bool cosine)
    {
        if (!x.Known || x.Magnitude > 16384) return CurveInterval.Bad;
        var mid = x.Midpoint;
        var radius = (x - P(mid)).Magnitude;
        if (radius >= 2) return new(-1, 1);
        var q = (int)Math.Round(mid / (Math.PI / 2));
        var r = P(mid) - P(q) * HalfPi;
        if (r.Magnitude > .8) return CurveInterval.Bad;
        var quadrant = (q + (cosine ? 1 : 0)) & 3;
        var even = (quadrant & 1) != 0;
        // sin through degree 21, cos through degree 20. For |r|<=.8 the next term is <1e-18.
        var term = even ? CurveInterval.One : r;
        var s = term; var minusSquare = -r.Square();
        for (var i = 1; i <= 10; i++)
        {
            var degree = 2*i + (even ? 0 : 1);
            term = term * minusSquare / P(degree*(degree-1)); s += term;
        }
        s += Tiny;
        if (quadrant >= 2) s = -s;
        // Both sine and cosine are globally 1-Lipschitz.
        s += new CurveInterval(-radius, radius);
        return new(Math.Max(-1,s.Lo), Math.Min(1,s.Hi));
    }

    internal static CurveInterval Exp(CurveInterval x)
    {
        if (!x.Known || x.Lo < -700 || x.Hi > 700) return CurveInterval.Bad;
        static CurveInterval At(double value)
        {
            if (value == 0) return CurveInterval.One;
            var k = (int)Math.Round(value / Ln2.Midpoint);
            var r = P(value) - P(k) * Ln2;
            if (r.Magnitude > .36) return CurveInterval.Bad;
            var term = CurveInterval.One; var sum = term;
            for (var i = 1; i <= 16; i++) { term = term * r / P(i); sum += term; }
            // exp(.36)*.36^17/17! < 1e-18.
            return (sum + Tiny) * P(Math.ScaleB(1,k));
        }
        return new(At(x.Lo).Lo, At(x.Hi).Hi);
    }

    internal static CurveInterval Log(CurveInterval x)
    {
        if (!x.Known || x.Lo <= 0) return CurveInterval.Bad;
        static CurveInterval At(double value)
        {
            if (value == 1) return CurveInterval.Zero;
            var k = Math.ILogB(value); var m = P(Math.ScaleB(value, -k));
            var z = (m - P(1)) / (m + P(1)); var z2 = z.Square();
            var term = z; var sum = z;
            for (var i = 3; i <= 39; i += 2) { term *= z2; sum += term / P(i); }
            // Exact reduced z is in [0,1/3]. Tail <= 2*(1/3)^41/(41*(1-1/9)).
            return P(k)*Ln2 + P(2)*sum + Tiny;
        }
        return new(At(x.Lo).Lo, At(x.Hi).Hi);
    }

    internal static CurveInterval Power(CurveInterval x, CurveInterval y)
    {
        if (!x.Known || !y.Known) return CurveInterval.Bad;
        if (y.Lo == y.Hi && y.Lo == Math.Truncate(y.Lo) && Math.Abs(y.Lo) <= 128)
            return x.IntegerPower((int)y.Lo);
        if (x.Lo < 0 || x.Lo == 0 && y.Lo <= 0) return CurveInterval.Bad;
        if (x.Lo > 0) return Exp(Log(x)*y);
        if (x.Hi == 0) return CurveInterval.Zero;
        // For positive exponents over a base interval [0,b], maximum is at b.
        var top = Exp(Log(P(x.Hi))*y);
        return top.Known ? new(0,top.Hi) : CurveInterval.Bad;
    }
}

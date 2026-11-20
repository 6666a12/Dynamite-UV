namespace DynamiteUniverse.Shared.Chart.V2;

/// <summary>Closed binary64 enclosure. Unknown never proves a domain or an error bound.</summary>
internal readonly record struct CurveInterval(double Lo, double Hi)
{
    internal static readonly CurveInterval Bad = new(double.NaN, double.NaN);
    internal static readonly CurveInterval Zero = Point(0), One = Point(1);
    internal bool Known => double.IsFinite(Lo) && double.IsFinite(Hi) && Lo <= Hi;
    internal double Magnitude => Known ? Math.Max(Math.Abs(Lo), Math.Abs(Hi)) : double.PositiveInfinity;
    internal double Midpoint => Math.Clamp(Lo * .5 + Hi * .5, Lo, Hi);
    internal static CurveInterval Point(double x) => new(x, x);
    internal static CurveInterval Enclose(double x) => new(Math.BitDecrement(x), Math.BitIncrement(x));
    internal static CurveInterval Hull(CurveInterval a, CurveInterval b) => new(Math.Min(a.Lo,b.Lo), Math.Max(a.Hi,b.Hi));
    internal CurveInterval Nonnegative() => new(Math.Max(0, Lo), Hi);
    internal CurveInterval Abs() => Lo >= 0 ? this : Hi <= 0 ? -this : new(0, Magnitude);
    public static CurveInterval operator -(CurveInterval a) => new(-a.Hi, -a.Lo);
    public static CurveInterval operator -(CurveInterval a, CurveInterval b) => a + -b;
    public static CurveInterval operator +(CurveInterval a, CurveInterval b)
    {
        if (!a.Known || !b.Known) return Bad;
        if (a == Zero) return b; if (b == Zero) return a;
        // For finite binary64 operands an exact sum rounding to zero really is zero.
        var l = a.Lo + b.Lo; var h = a.Hi + b.Hi;
        return new(l == 0 ? 0 : Math.BitDecrement(l), h == 0 ? 0 : Math.BitIncrement(h));
    }
    public static CurveInterval operator *(CurveInterval a, CurveInterval b)
    {
        if (!a.Known || !b.Known) return Bad;
        if (a == Zero || b == Zero) return Zero;
        if (a == One) return b; if (b == One) return a;
        var x = a.Lo*b.Lo; var y = a.Lo*b.Hi; var z = a.Hi*b.Lo; var w = a.Hi*b.Hi;
        var l = Math.BitDecrement(Math.Min(Math.Min(x,y),Math.Min(z,w)));
        var h = Math.BitIncrement(Math.Max(Math.Max(x,y),Math.Max(z,w)));
        if (a.Lo >= 0 && b.Lo >= 0 || a.Hi <= 0 && b.Hi <= 0) l = Math.Max(0,l);
        if (a.Lo >= 0 && b.Hi <= 0 || a.Hi <= 0 && b.Lo >= 0) h = Math.Min(0,h);
        return new(l,h);
    }
    public static CurveInterval operator /(CurveInterval a, CurveInterval b)
    {
        if (!a.Known || !b.Known || b.Lo <= 0 && b.Hi >= 0) return Bad;
        if (a == Zero) return Zero; if (b == One) return a;
        return a * new CurveInterval(Math.BitDecrement(1 / b.Hi), Math.BitIncrement(1 / b.Lo));
    }
    internal CurveInterval Square()
    {
        var x = Abs();
        return x * x;
    }
    internal CurveInterval IntegerPower(int n)
    {
        if (!Known || n < 0 && Lo <= 0 && Hi >= 0 || n == 0 && Lo <= 0 && Hi >= 0) return Bad;
        if (n == 0) return One;
        if (n < 0) return One / IntegerPower(-n);
        if (n == 1) return this;
        var half = IntegerPower(n / 2).Square();
        return n % 2 == 0 ? half : half * this;
    }
    internal CurveInterval Sqrt() => !Known || Lo < 0 ? Bad :
        new(Lo == 0 ? 0 : Math.BitDecrement(Math.Sqrt(Lo)), Math.BitIncrement(Math.Sqrt(Hi)));
}

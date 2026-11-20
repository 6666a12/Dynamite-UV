using System.Globalization;

namespace DynamiteUniverse.Shared.Chart.V2;

internal readonly record struct CurveJet(CurveInterval Value, CurveInterval Slope);

/// <summary>Interprets the closed grammar over intervals. Slopes enclose derivatives and one-sided secants at corners.</summary>
internal sealed class CurveTerm
{
    private readonly string _op;
    private readonly CurveTerm? _left, _right;
    private readonly CurveInterval _number;
    private readonly CurveJet? _constant;
    internal string Canonical { get; }
    internal int Cost { get; }
    internal CurveTerm(string op, CurveTerm? left = null, CurveTerm? right = null, double number = 0)
    {
        _op = op; _left = left; _right = right; _number = CurveInterval.Point(number);
        Cost = 1 + (left?.Cost ?? 0) + (right?.Cost ?? 0);
        Canonical = op switch {
            "number" => number.ToString("R", CultureInfo.InvariantCulture),
            "neg" => "(-"+left!.Canonical+")",
            "+" or "-" or "*" or "/" or "^" => "("+left!.Canonical+op+right!.Canonical+")",
            _ when left is null => op,
            _ => op+"("+left.Canonical+(right is null ? "" : ","+right.Canonical)+")",
        };
        if (op != "t" && (left is null || left._constant.HasValue) && (right is null || right._constant.HasValue))
            _constant = new(At(CurveInterval.Zero,false).Value, CurveInterval.Zero);
    }
    internal CurveJet At(CurveInterval t, bool slopes)
    {
        if (_constant is { } c) return c;
        var z = CurveInterval.Zero; var one = CurveInterval.One; var bad = CurveInterval.Bad;
        if (_op == "t") return new(t, one);
        if (_op == "number") return new(_number,z);
        if (_op == "pi") return new(CurveElementary.Pi,z);
        if (_op == "e") return new(CurveElementary.E,z);
        var a = _left!.At(t,slopes); var b = _right?.At(t,slopes) ?? new CurveJet(z,z);
        var x = a.Value; var y = b.Value; var dx = a.Slope; var dy = b.Slope;
        if (!x.Known || !y.Known) return new(bad,bad);
        CurveInterval v; CurveInterval d = bad;
        switch (_op)
        {
            case "neg": v = -x; if (slopes) d = -dx; break;
            case "+": v = x+y; if (slopes) d = dx+dy; break;
            case "-": v = x-y; if (slopes) d = dx-dy; break;
            case "*": v = x*y; if (slopes) d = dx*y+x*dy; break;
            case "/": v = x/y; if (slopes) d = (dx-v*dy)/y; break;
            case "^":
                v = CurveElementary.Power(x,y);
                if (slopes && v.Known)
                {
                    if (dy == z && y == one) d = dx;
                    else if (dy == z && y == z) d = z;
                    else if (dy == z && y.Lo == y.Hi)
                    {
                        // y - 1 may not be representable. Keep the enclosure, with exact
                        // singleton for integer powers (including negative bases).
                        var exponent = y.Lo == Math.Truncate(y.Lo) && Math.Abs(y.Lo) <= 128
                            ? CurveInterval.Point(y.Lo-1) : y-one;
                        d = y*CurveElementary.Power(x,exponent)*dx;
                    }
                    else if (x.Lo > 0) d = v*(dy*CurveElementary.Log(x) + y*dx/x);
                }
                break;
            case "sqrt": v = x.Sqrt(); if (slopes) d = dx / (CurveInterval.Point(2)*v); break;
            case "exp": v = CurveElementary.Exp(x); if (slopes) d = v*dx; break;
            case "log": v = CurveElementary.Log(x); if (slopes) d = dx/x; break;
            case "sin": v = CurveElementary.Trig(x,false); if (slopes) d = CurveElementary.Trig(x,true)*dx; break;
            case "cos": v = CurveElementary.Trig(x,true); if (slopes) d = -CurveElementary.Trig(x,false)*dx; break;
            case "abs": v = x.Abs(); if (slopes) d = x.Lo >= 0 ? dx : x.Hi <= 0 ? -dx : CurveInterval.Hull(dx,-dx); break;
            case "min": case "max":
                var min = _op == "min";
                v = min ? new(Math.Min(x.Lo,y.Lo),Math.Min(x.Hi,y.Hi)) : new(Math.Max(x.Lo,y.Lo),Math.Max(x.Hi,y.Hi));
                if (slopes) d = x.Hi < y.Lo ? (min ? dx : dy) : x.Lo > y.Hi ? (min ? dy : dx) : CurveInterval.Hull(dx,dy);
                break;
            default: throw new InvalidOperationException();
        }
        return new(v,d);
    }
}

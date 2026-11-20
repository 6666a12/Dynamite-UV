using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using DynamiteUniverse.Shared.Chart.V2;

namespace CoreTests;

internal static class CustomCurvePrecisionTests
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Reject(string formula)
        {
            try { CustomCurve.Compile(formula); }
            catch (FormatException) { return; }
            throw new InvalidOperationException("Expected rejected curve: " + formula);
        }
        static CustomCurve Compare(string formula, Func<double,double> reference)
        {
            var c = CustomCurve.Compile(formula); var times = c.Knots;
            Check(times.Count >= 2 && times.Count <= CustomCurve.MaximumSegments+1 && times[0] == 0 && times[^1] == 1,
                "Invalid knot count/anchors: " + formula);
            Check(c.Evaluate(0) == 0 && c.Evaluate(1) == 1, "Endpoint not anchored: " + formula);
            Check(c.Minimum == times.Min(c.Evaluate) && c.Maximum == times.Max(c.Evaluate), "Incorrect actual extrema: " + formula);
            for (var i=1; i<times.Count; i++)
            {
                var a=times[i-1]; var b=times[i]; Check(a<b,"Unsorted or repeated knot");
                for (var j=0; j<=8; j++)
                {
                    var t=a+(b-a)*j/8; var y=c.Evaluate(t);
                    Check(Math.Abs(y-reference(t)) <= CustomCurve.ProgressTolerance+1e-13,
                        FormattableString.Invariant($"Tolerance exceeded for {formula} at {t:R}: {y:R}"));
                }
            }
            return c;
        }
        var line=Compare("t",t=>t); Check(line.Knots.Count==2,"A linear primitive needs only endpoints");
        Compare("t*t*(3-2*t)", t=>t*t*(3-2*t));
        Compare("sqrt(t)",Math.Sqrt); Compare("1-(1-t)^0.5",t=>1-Math.Sqrt(1-t));
        Compare("(exp(t)-1)/(e-1)",t=>(Math.Exp(t)-1)/(Math.E-1));
        Compare("log(1+(e-1)*t)",t=>Math.Log(1+(Math.E-1)*t));
        Compare("max(0,min(1,2*t-0.5))",t=>Math.Clamp(2*t-.5,0,1));
        Compare("t^(1+t)",t=>Math.Pow(t,1+t));
        Compare("-((1-t)^2)+1",t=>1-(1-t)*(1-t));
        Compare("t^2^3",t=>Math.Pow(t,8));
        Compare("t + sin(pi*t)*.01",t=>t+.01*Math.Sin(Math.PI*t));
        var elastic=Compare("1-(1-t)^1.5*cos(3*pi*t)",t=>1-Math.Pow(1-t,1.5)*Math.Cos(3*Math.PI*t));
        Check(elastic.Maximum>1.1,"Overshoot was clipped");
        // Wire-order pairs of big-endian binary64 (t,progress), including the exact anchors.
        // This pins the actual playable shape, not merely a textual formula or a loose error bound.
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            Span<byte> pair = stackalloc byte[16];
            foreach (var t in elastic.Knots)
            {
                BinaryPrimitives.WriteDoubleBigEndian(pair,t);
                BinaryPrimitives.WriteDoubleBigEndian(pair[8..],elastic.Evaluate(t));
                hash.AppendData(pair);
            }
            Check(elastic.Knots.Count == 425 && Convert.ToHexString(hash.GetHashAndReset()) ==
                "D6143E27442EBC8FAE464317B17500B20EDF40C960AEDE0A3209F35E08033E8D", "v2.1 bounce knot vector changed");
            Check(BitConverter.DoubleToInt64Bits(elastic.Evaluate(.73123456789)) == 0x3FEC577A09F2C348,
                "v2.1 bounce between-knot evaluation changed");
        }
        var alias=Compare("t+0.01*sin(64*pi*t)",t=>t+.01*Math.Sin(64*Math.PI*t));
        Check(alias.Knots.Count>64,"Dyadic midpoint alias was silently accepted");
        var peak=Compare("t+max(0,1-50000*abs(t-.37291))",t=>t+Math.Max(0,1-50000*Math.Abs(t-.37291)));
        Check(peak.Evaluate(.37291)>1.3728,"Missed narrow off-grid peak");
        Compare("t+0.0000000001",t=>t+1e-10);
        foreach (var expr in new[] { "1+t", "t/0", "t+0/(t-.123456789)", "t+0/((t-.123456789)^2)",
            "t+0*log((t-.32123)^2)", "t+0*sqrt((t-.32123)^2-1e-16)", "t+0*((t-.3)^.5)",
            "t+0*exp(1000*t)", "1e999*t", "t+1e-999", "t+bogus(1)", "min(t)", "t,t", "t()", "t+", "t ; t",
            new string('(',80)+"t"+new string(')',80), "t+sin(1000000*t)" }) Reject(expr);
        var first=CustomCurve.Compile("t^.5"); var spaced=CustomCurve.Compile(" ( t ^ 0.500e+0 ) ");
        Check(first.Canonical == spaced.Canonical && first.Knots.SequenceEqual(spaced.Knots),"Whitespace or literal spelling changed semantics");
        Check(ReferenceEquals(first,CustomCurve.Compile("t^.5")),"Repeated compilation was not cached");
        var culture=CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR"); Check(CustomCurve.Compile("(t ^ 0.50)").Canonical==first.Canonical,"Culture changed canonical expression"); }
        finally { CultureInfo.CurrentCulture=culture; }
        Console.WriteLine("PASS certified custom curve progress, grammar, domain, precision and limits");
    }
}

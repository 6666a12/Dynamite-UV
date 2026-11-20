using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace CoreTests;

internal static class ExactRoundingTests
{
    internal static void Run()
    {
        static void Bits(double actual, double expected, string name)
        {
            if (BitConverter.DoubleToInt64Bits(actual) != BitConverter.DoubleToInt64Bits(expected))
                throw new InvalidOperationException($"{name}: got {actual:R}, expected {expected:R}");
        }
        static double R(BigInteger n, BigInteger d) => ExactBarTime.FromFraction(n, d).ToDouble();
        var p53 = BigInteger.One << 53;
        var next = Math.BitIncrement(1.0);
        Bits(R(2*p53+3, 2*p53), next, "3/4 ulp rounds up");
        Bits(R(p53+1, p53), 1.0, "tie to even low mantissa");
        Bits(R(p53+3, p53), Math.BitIncrement(next), "tie to even high mantissa");
        Bits(R(-p53-3, p53), -Math.BitIncrement(next), "negative tie to even");
        Bits(ExactBarTime.FromJsonComponents(3, 3, (BigInteger.One << 52)+1).ToDouble(), Math.BitIncrement(3.0),
            "legal mixed BarTime just below a midpoint");
        var tinyDen = BigInteger.One << 1075;
        Bits(R(1,tinyDen), 0.0, "half of minimum subnormal");
        Bits(R(-1,tinyDen), -0.0, "negative underflow keeps sign");
        Bits(R(3,tinyDen), 2*double.Epsilon, "subnormal tie to even");
        var overflowMidpoint = ((BigInteger.One<<54)-1) << 970;
        Bits(R(overflowMidpoint-1,1), double.MaxValue, "last rational just below overflow midpoint");
        Bits(R(overflowMidpoint,1), double.PositiveInfinity, "overflow midpoint rounds up");
        // Check every exponent band, both signs and both ends of the fraction word. No tolerance.
        for (int e=0; e<2047; ++e)
        foreach (ulong fraction in new ulong[] { 0, 1, 0x8abcd12345678UL, 0xfffffffffffffUL })
        {
            var x = BitConverter.Int64BitsToDouble(((long)e << 52) | (long)fraction);
            Bits(ExactBarTime.FromDouble(x).ToDouble(), x, "positive binary64 rational round trip");
            if (x != 0) Bits(ExactBarTime.FromDouble(-x).ToDouble(), -x, "negative binary64 rational round trip");
        }
        Console.WriteLine("PASS exact BarTime rounding: ties, subnormals, max finite and 16,375 signed binary64 round trips");
    }
}

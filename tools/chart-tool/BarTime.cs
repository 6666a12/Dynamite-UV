using System.Numerics;
using System.Text.Json;

namespace ChartTool.IsolatedV2;

internal sealed class RationalBarTime : IComparable<RationalBarTime>, IEquatable<RationalBarTime>
{
    private RationalBarTime(BigInteger numerator, BigInteger denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
        Bar = numerator / denominator;
        FractionNumerator = numerator % denominator;
    }

    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public BigInteger Bar { get; }
    public BigInteger FractionNumerator { get; }

    public static RationalBarTime Zero { get; } = new(BigInteger.Zero, BigInteger.One);

    public static bool TryFromBinary64(double value, out RationalBarTime? result,
        out string reason)
    {
        result = null;
        reason = string.Empty;
        if (!double.IsFinite(value))
        {
            reason = "BarTime must be finite";
            return false;
        }
        if (value < 0)
        {
            reason = "BarTime must be non-negative";
            return false;
        }
        if (value == 0)
        {
            result = Zero;
            return true;
        }

        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponentBits = (int)((bits >> 52) & 0x7ffUL);
        var fractionBits = bits & 0x000f_ffff_ffff_ffffUL;
        BigInteger numerator;
        BigInteger denominator;
        if (exponentBits == 0)
        {
            numerator = fractionBits;
            denominator = BigInteger.One << 1074;
        }
        else
        {
            numerator = fractionBits | (1UL << 52);
            var exponent = exponentBits - 1023 - 52;
            if (exponent >= 0)
            {
                numerator <<= exponent;
                denominator = BigInteger.One;
            }
            else
            {
                denominator = BigInteger.One << -exponent;
            }
        }

        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= divisor;
        denominator /= divisor;
        var candidate = new RationalBarTime(numerator, denominator);
        if (candidate.Bar > TextRules.JsonSafeInteger ||
            candidate.FractionNumerator > TextRules.JsonSafeInteger ||
            candidate.Denominator > TextRules.JsonSafeInteger)
        {
            reason = "exact IEEE 754 binary64 value cannot be represented by a normalized v2 " +
                $"BarTime whose mixed-object integers are JSON-safe (bar={candidate.Bar}, " +
                $"numerator={candidate.FractionNumerator}, denominator={candidate.Denominator})";
            return false;
        }

        result = candidate;
        return true;
    }

    public double ToDouble() => (double)Numerator / (double)Denominator;

    public int CompareTo(RationalBarTime? other)
    {
        if (other is null)
            return 1;
        return (Numerator * other.Denominator).CompareTo(
            other.Numerator * Denominator);
    }

    public bool Equals(RationalBarTime? other) =>
        other is not null && Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is RationalBarTime other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("bar", (long)Bar);
        writer.WriteNumber("numerator", (long)FractionNumerator);
        writer.WriteNumber("denominator", (long)Denominator);
        writer.WriteEndObject();
    }

    public override string ToString() =>
        $"{Bar}+{FractionNumerator}/{Denominator}";
}

internal sealed class RationalBarTimeComparer : IComparer<RationalBarTime>
{
    public static RationalBarTimeComparer Instance { get; } = new();
    public int Compare(RationalBarTime? x, RationalBarTime? y) =>
        ReferenceEquals(x, y) ? 0 : x is null ? -1 : x.CompareTo(y);
}

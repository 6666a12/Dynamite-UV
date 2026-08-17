using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuxShared.Chart.V2;

/// <summary>
/// An exact rational chart-bar position. Values use a normalized floor-based mixed form, so the
/// denominator is positive and <c>0 &lt;= Numerator &lt; Denominator</c> even for internal negative
/// results. Serialized v2 BarTime values are the non-negative, JSON-safe subset.
/// </summary>
[JsonConverter(typeof(ExactBarTimeJsonConverter))]
public readonly struct ExactBarTime : IComparable<ExactBarTime>, IEquatable<ExactBarTime>
{
    /// <summary>Largest integer that JSON can carry exactly under the v2 contract.</summary>
    public static readonly BigInteger MaxSafeInteger = new(9_007_199_254_740_991L);

    public static ExactBarTime Zero { get; } = new(BigInteger.Zero, BigInteger.Zero, BigInteger.One);
    public static ExactBarTime One { get; } = new(BigInteger.One, BigInteger.Zero, BigInteger.One);

    public BigInteger Bar { get; }
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    /// <summary>The normalized improper numerator, <c>Bar * Denominator + Numerator</c>.</summary>
    public BigInteger ImproperNumerator => Bar * Denominator + Numerator;

    public bool IsZero => Bar.IsZero && Numerator.IsZero;
    public bool IsNonNegative => Bar.Sign >= 0;

    /// <summary>
    /// Creates and normalizes a mixed rational. The numerator may be improper or negative for
    /// arithmetic; use <see cref="FromJsonComponents"/> when accepting serialized chart data.
    /// </summary>
    public ExactBarTime(BigInteger bar, BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new DivideByZeroException("ExactBarTime denominator cannot be zero.");

        var improper = bar * denominator + numerator;
        if (denominator.Sign < 0)
        {
            denominator = BigInteger.Negate(denominator);
            improper = BigInteger.Negate(improper);
        }

        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(improper), denominator);
        if (gcd > BigInteger.One)
        {
            improper /= gcd;
            denominator /= gcd;
        }

        var whole = BigInteger.DivRem(improper, denominator, out var remainder);
        if (remainder.Sign < 0)
        {
            whole -= BigInteger.One;
            remainder += denominator;
        }

        Bar = whole;
        Numerator = remainder;
        Denominator = denominator;
    }

    /// <summary>Creates a normalized rational from an improper numerator and denominator.</summary>
    public static ExactBarTime FromFraction(BigInteger numerator, BigInteger denominator) =>
        new(BigInteger.Zero, numerator, denominator);

    /// <summary>
    /// Accepts only the canonical, non-negative mixed representation permitted in v2 JSON.
    /// </summary>
    public static ExactBarTime FromJsonComponents(
        BigInteger bar, BigInteger numerator, BigInteger denominator)
    {
        if (bar.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(bar), "bar must be non-negative.");
        if (numerator.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(numerator), "numerator must be non-negative.");
        if (denominator.Sign <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator), "denominator must be positive.");
        if (bar > MaxSafeInteger || numerator > MaxSafeInteger || denominator > MaxSafeInteger)
            throw new ArgumentOutOfRangeException(nameof(bar), "BarTime components must be JSON-safe integers.");
        if (numerator >= denominator)
            throw new ArgumentException("numerator must be smaller than denominator.", nameof(numerator));
        if (BigInteger.GreatestCommonDivisor(numerator, denominator) != BigInteger.One)
            throw new ArgumentException("BarTime fraction must be reduced.", nameof(numerator));
        if (numerator.IsZero && denominator != BigInteger.One)
            throw new ArgumentException("Whole bars must use denominator 1.", nameof(denominator));

        return new ExactBarTime(bar, numerator, denominator);
    }

    /// <summary>Returns whether this value can be emitted directly as canonical v2 BarTime JSON.</summary>
    public bool IsJsonSafeCanonical =>
        Bar.Sign >= 0 && Bar <= MaxSafeInteger &&
        Numerator <= MaxSafeInteger && Denominator <= MaxSafeInteger;

    /// <summary>Converts an IEEE 754 binary64 value to its exact rational value.</summary>
    public static ExactBarTime FromDouble(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "A finite binary64 value is required.");
        if (value == 0.0)
            return Zero; // Canonicalizes negative zero.

        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponentBits = (int)((bits >> 52) & 0x7ffL);
        var fractionBits = (ulong)bits & 0x000f_ffff_ffff_ffffUL;

        BigInteger significand;
        int binaryExponent;
        if (exponentBits == 0)
        {
            significand = fractionBits;
            binaryExponent = -1022 - 52;
        }
        else
        {
            significand = (BigInteger.One << 52) + fractionBits;
            binaryExponent = exponentBits - 1023 - 52;
        }

        if (negative)
            significand = BigInteger.Negate(significand);
        return binaryExponent >= 0
            ? FromFraction(significand << binaryExponent, BigInteger.One)
            : FromFraction(significand, BigInteger.One << -binaryExponent);
    }

    /// <summary>Rounds this exact rational to IEEE 754 binary64.</summary>
    public double ToDouble()
    {
        var improper = ImproperNumerator;
        if (improper.IsZero)
            return 0.0;

        var sign = improper.Sign;
        var numerator = BigInteger.Abs(improper);
        var denominator = Denominator;
        var maxBits = Math.Max(numerator.GetBitLength(), denominator.GetBitLength());
        if (maxBits > 1022)
        {
            var shift = checked((int)(maxBits - 1022));
            numerator >>= shift;
            denominator >>= shift;
            if (numerator.IsZero)
                return sign < 0 ? -0.0 : 0.0;
            if (denominator.IsZero)
                return sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
        }

        var result = (double)numerator / (double)denominator;
        return sign < 0 ? -result : result;
    }

    public int CompareTo(ExactBarTime other) =>
        (ImproperNumerator * other.Denominator)
            .CompareTo(other.ImproperNumerator * Denominator);

    public bool Equals(ExactBarTime other) =>
        Bar == other.Bar && Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals(object? obj) => obj is ExactBarTime other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Bar, Numerator, Denominator);

    public override string ToString()
    {
        if (Numerator.IsZero)
            return Bar.ToString(CultureInfo.InvariantCulture);
        if (Bar.Sign >= 0)
            return string.Create(CultureInfo.InvariantCulture, $"{Bar}+{Numerator}/{Denominator}");
        return string.Create(CultureInfo.InvariantCulture,
            $"{ImproperNumerator}/{Denominator}");
    }

    public static ExactBarTime operator +(ExactBarTime left, ExactBarTime right) =>
        FromFraction(
            left.ImproperNumerator * right.Denominator +
            right.ImproperNumerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static ExactBarTime operator -(ExactBarTime left, ExactBarTime right) =>
        FromFraction(
            left.ImproperNumerator * right.Denominator -
            right.ImproperNumerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static ExactBarTime operator -(ExactBarTime value) =>
        FromFraction(BigInteger.Negate(value.ImproperNumerator), value.Denominator);

    public static ExactBarTime operator *(ExactBarTime left, ExactBarTime right) =>
        FromFraction(left.ImproperNumerator * right.ImproperNumerator,
            left.Denominator * right.Denominator);

    public static ExactBarTime operator /(ExactBarTime left, ExactBarTime right)
    {
        if (right.ImproperNumerator.IsZero)
            throw new DivideByZeroException();
        return FromFraction(left.ImproperNumerator * right.Denominator,
            left.Denominator * right.ImproperNumerator);
    }

    public static ExactBarTime operator *(ExactBarTime value, BigInteger multiplier) =>
        FromFraction(value.ImproperNumerator * multiplier, value.Denominator);

    public static ExactBarTime operator *(BigInteger multiplier, ExactBarTime value) =>
        value * multiplier;

    public static ExactBarTime operator /(ExactBarTime value, BigInteger divisor)
    {
        if (divisor.IsZero)
            throw new DivideByZeroException();
        return FromFraction(value.ImproperNumerator, value.Denominator * divisor);
    }

    public static bool operator ==(ExactBarTime left, ExactBarTime right) => left.Equals(right);
    public static bool operator !=(ExactBarTime left, ExactBarTime right) => !left.Equals(right);
    public static bool operator <(ExactBarTime left, ExactBarTime right) => left.CompareTo(right) < 0;
    public static bool operator <=(ExactBarTime left, ExactBarTime right) => left.CompareTo(right) <= 0;
    public static bool operator >(ExactBarTime left, ExactBarTime right) => left.CompareTo(right) > 0;
    public static bool operator >=(ExactBarTime left, ExactBarTime right) => left.CompareTo(right) >= 0;
}

/// <summary>System.Text.Json converter for the canonical v2 BarTime object.</summary>
public sealed class ExactBarTimeJsonConverter : JsonConverter<ExactBarTime>
{
    public override ExactBarTime Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("BarTime must be an object.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        BigInteger? bar = null;
        BigInteger? numerator = null;
        BigInteger? denominator = null;
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new JsonException($"Duplicate BarTime property '{property.Name}'.");
            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetInt64(out var integer))
                throw new JsonException($"BarTime property '{property.Name}' must be an integer.");
            switch (property.Name)
            {
                case "bar": bar = integer; break;
                case "numerator": numerator = integer; break;
                case "denominator": denominator = integer; break;
                default: throw new JsonException($"Unknown BarTime property '{property.Name}'.");
            }
        }

        if (bar is null || numerator is null || denominator is null)
            throw new JsonException("BarTime requires bar, numerator, and denominator.");
        try
        {
            return ExactBarTime.FromJsonComponents(bar.Value, numerator.Value, denominator.Value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, ExactBarTime value,
        JsonSerializerOptions options)
    {
        if (!value.IsJsonSafeCanonical)
            throw new JsonException("ExactBarTime is outside the canonical v2 JSON range.");
        writer.WriteStartObject();
        writer.WriteNumber("bar", (long)value.Bar);
        writer.WriteNumber("numerator", (long)value.Numerator);
        writer.WriteNumber("denominator", (long)value.Denominator);
        writer.WriteEndObject();
    }
}

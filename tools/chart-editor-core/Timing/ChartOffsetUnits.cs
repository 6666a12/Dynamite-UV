using System.Globalization;
using System.Numerics;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Editor offsets are authored in decimal milliseconds; v2 stores a binary64 second.
/// Shift decimal units before converting to binary, not after. Never snap a small authored value to zero.</summary>
public static class ChartOffsetUnits
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // The shortest decimal spelling round-trips to the exact original chart double.
    private static (BigInteger Coefficient, int Power) Decompose(double seconds)
    {
        var text = seconds.ToString("R", Inv);
        var e = text.IndexOf('E');
        var power = e < 0 ? 0 : int.Parse(text[(e + 1)..], Inv);
        var mantissa = e < 0 ? text : text[..e];
        var dot = mantissa.IndexOf('.');
        if (dot >= 0) power -= mantissa.Length - dot - 1;
        return (BigInteger.Parse(mantissa.Replace(".", ""), Inv), power);
    }

    internal static double AddMilliseconds(double origin, long milliseconds)
    {
        if (milliseconds == 0) return origin;
        var (coefficient, exponent) = Decompose(origin);
        var common = Math.Min(-3, exponent);
        var sum = coefficient * BigInteger.Pow(10, exponent - common) + milliseconds * BigInteger.Pow(10, -3 - common);
        return sum.IsZero ? 0d : double.Parse($"{sum.ToString(Inv)}E{common.ToString(Inv)}", NumberStyles.Float, Inv);
    }

    public static string FormatMilliseconds(double seconds, bool signed = false)
    {
        if (seconds == 0) return "0";
        var (coefficient, exponent) = Decompose(seconds);
        var digits = BigInteger.Abs(coefficient).ToString(Inv);
        var point = digits.Length + exponent + 3;
        digits = digits.TrimEnd('0');
        string number;
        if (point is < -5 or > 18)
            number = (digits.Length == 1 ? digits : digits.Insert(1, ".")) + "E" + (point - 1).ToString(Inv);
        else if (point <= 0) number = "0." + new string('0', -point) + digits;
        else if (point >= digits.Length) number = digits.PadRight(point, '0');
        else number = digits.Insert(point, ".");
        return (seconds < 0 ? "-" : signed ? "+" : "") + number;
    }

    public static bool TryParseMilliseconds(string? raw, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim().AsSpan();
        var e = text.IndexOfAny('e', 'E');
        var mantissa = e < 0 ? text : text[..e];
        var exponent = 0;
        if (e >= 0 && !int.TryParse(text[(e + 1)..], NumberStyles.AllowLeadingSign, Inv, out exponent)) return false;
        var shifted = string.Concat(mantissa, "E", ((long)exponent - 3).ToString(Inv));
        if (!double.TryParse(shifted, NumberStyles.Float, Inv, out var value) || !double.IsFinite(value)) return false;
        // A nonzero input outside the format's range must not silently overwrite a chart with zero.
        if (value == 0 && mantissa.IndexOfAnyInRange('1', '9') >= 0) return false;
        seconds = value == 0 ? 0 : value;
        return true;
    }
}

/// <summary>Transient per-difficulty edit state (not a change to the file format). Keep the origin
/// across key releases: even a tiny authored residual lost in an intermediate double is restored on return.</summary>
internal readonly record struct ChartOffsetPosition(double Origin, long Milliseconds, double Seconds)
{
    public static ChartOffsetPosition FromSeconds(double seconds) => new(seconds, 0, seconds);
    public ChartOffsetPosition Move(long delta)
    {
        var ticks = checked(Milliseconds + delta);
        return new(Origin, ticks, ChartOffsetUnits.AddMilliseconds(Origin, ticks));
    }
}

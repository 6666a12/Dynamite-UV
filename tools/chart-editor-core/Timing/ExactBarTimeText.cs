using System.Globalization;
using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Exact, culture-independent text conversion for non-negative editor BarTime values.</summary>
public static class ExactBarTimeText
{
    public static ExactBarTime Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim();
        if (value.Length == 0)
            throw new FormatException("BarTime text must not be empty.");
        if (value.Any(character => character is < '0' or > '9' && character is not ('+' or '/' or '.')))
            throw new FormatException("BarTime must be an integer, fraction, mixed fraction, or finite decimal.");

        ExactBarTime result;
        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            if (plus == 0 || plus == value.Length - 1 || value.LastIndexOf('+') != plus)
                throw new FormatException("Mixed BarTime must use bar+n/d.");
            var bar = ParseDigits(value.AsSpan(0, plus), "bar");
            var fraction = ParseFraction(value.AsSpan(plus + 1), requireProper: true);
            result = ExactBarTime.FromFraction(
                bar * fraction.Denominator + fraction.Numerator, fraction.Denominator);
        }
        else if (value.Contains('/'))
        {
            var fraction = ParseFraction(value.AsSpan(), requireProper: false);
            result = ExactBarTime.FromFraction(fraction.Numerator, fraction.Denominator);
        }
        else if (value.Contains('.'))
        {
            var dot = value.IndexOf('.');
            if (dot != value.LastIndexOf('.'))
                throw new FormatException("Decimal BarTime may contain only one decimal point.");
            var wholeText = value.AsSpan(0, dot);
            var fractionalText = value.AsSpan(dot + 1);
            if (wholeText.Length == 0 && fractionalText.Length == 0)
                throw new FormatException("Decimal BarTime must contain at least one digit.");
            var whole = wholeText.Length == 0 ? BigInteger.Zero : ParseDigits(wholeText, "whole part");
            var fractional = fractionalText.Length == 0
                ? BigInteger.Zero
                : ParseDigits(fractionalText, "fractional part");
            var denominator = BigInteger.Pow(10, fractionalText.Length);
            result = ExactBarTime.FromFraction(whole * denominator + fractional, denominator);
        }
        else
        {
            result = ExactBarTime.FromFraction(ParseDigits(value.AsSpan(), "bar"), BigInteger.One);
        }

        if (!result.IsNonNegative)
            throw new FormatException("BarTime must be non-negative.");
        if (!result.IsJsonSafeCanonical)
            throw new FormatException("BarTime exceeds the canonical v2 JSON-safe range.");
        return result;
    }

    public static bool TryParse(string? text, out ExactBarTime value)
    {
        if (text is not null)
        {
            try
            {
                value = Parse(text);
                return true;
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
            {
                // Return the canonical zero value on ordinary user-input failures.
            }
        }
        value = ExactBarTime.Zero;
        return false;
    }

    public static string Format(ExactBarTime value)
    {
        if (!value.IsNonNegative)
            throw new ArgumentOutOfRangeException(nameof(value), "BarTime text only supports non-negative values.");
        if (!value.IsJsonSafeCanonical)
            throw new ArgumentOutOfRangeException(nameof(value),
                "BarTime text only supports canonical v2 JSON-safe values.");
        if (value.Numerator.IsZero)
            return value.Bar.ToString(CultureInfo.InvariantCulture);
        if (value.Bar.IsZero)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{value.Numerator}/{value.Denominator}");
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"{value.Bar}+{value.Numerator}/{value.Denominator}");
    }

    private static (BigInteger Numerator, BigInteger Denominator) ParseFraction(
        ReadOnlySpan<char> text, bool requireProper)
    {
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash == text.Length - 1 || text[(slash + 1)..].Contains('/'))
            throw new FormatException("Fractional BarTime must use n/d.");
        var numerator = ParseDigits(text[..slash], "numerator");
        var denominator = ParseDigits(text[(slash + 1)..], "denominator");
        if (denominator.IsZero)
            throw new FormatException("BarTime denominator must be positive.");
        if (requireProper && numerator >= denominator)
            throw new FormatException("The fractional part of bar+n/d must be smaller than one bar.");
        return (numerator, denominator);
    }

    private static BigInteger ParseDigits(ReadOnlySpan<char> text, string part)
    {
        if (text.Length == 0 || text.ContainsAnyExceptInRange('0', '9'))
            throw new FormatException($"BarTime {part} must contain decimal digits only.");
        return BigInteger.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}

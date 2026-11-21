using System.Globalization;
using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

internal static class OsuTimingImport
{
    internal sealed record RedPoint(ExactBarTime Milliseconds, double Bpm, int Meter, int Line);

    private const int MaxNumberCharacters = 64;
    private static readonly ExactBarTime MaxAbsoluteMilliseconds = ExactBarTime.FromFraction(1_000_000_000, 1);
    private static readonly ExactBarTime MaxRawErrorMilliseconds = ExactBarTime.FromFraction(1, 100_000);
    private static readonly ExactBarTime SnapWithinBar = ExactBarTime.FromFraction(1, 1000);
    // Bound intermediate denominators across thousands of different BPM values. This grid incurs
    // at most 0.5e-18 ms per segment, far below the output and runtime precision checks.
    private static readonly BigInteger AnchorGrid = BigInteger.Pow(10, 18);

    internal static ImportedTiming Build(List<RedPoint> reds, string? filename, bool snap)
    {
        if (reds.Count == 0) throw new InvalidDataException("The osu! file contains no red timing points.");
        reds.Sort((left, right) => left.Milliseconds.CompareTo(right.Milliseconds));
        for (int i = 0; i < reds.Count; i++)
        {
            var red = reds[i];
            if (Abs(red.Milliseconds) > MaxAbsoluteMilliseconds)
                throw new InvalidDataException($"osu! red line {red.Line} is outside the supported audio time range.");
            if (i > 0 && red.Milliseconds == reds[i - 1].Milliseconds)
                throw new InvalidDataException($"Duplicate osu! red lines {reds[i - 1].Line} and {red.Line}.");
        }

        var first = reds[0];
        var firstMilliseconds = first.Milliseconds.ToDouble();
        var offset = (first.Milliseconds / 1000).ToDouble();
        var previousAnchor = ExactBarTime.FromDouble(offset) * 1000;
        var previousBar = ExactBarTime.Zero;
        var previousBarLength = GetBarLength(first);
        var points = new List<ImportedBpmPoint>(reds.Count) { new(previousBar, first.Bpm, firstMilliseconds, 0) };
        int snappedCount = 0;
        double maxSnap = 0;
        for (int i = 1; i < reds.Count; i++)
        {
            var red = reds[i];
            // Invert against the previous *output* segment's offset, bar and serialized BPM.
            // Using the previous source offset here would carry earlier snap errors forward.
            var projected = previousBar + (red.Milliseconds - previousAnchor) / previousBarLength;
            var bar = ToSerializablePosition(projected);
            var nearestBeat = snap ? RoundToGrid(projected, 4) : projected;
            bool wasSnapped = false;
            if (snap && nearestBeat != projected && nearestBeat > previousBar && nearestBeat.IsJsonSafeCanonical &&
                Abs(nearestBeat - projected) < SnapWithinBar)
            {
                var snappedAt = previousAnchor + (nearestBeat - previousBar) * previousBarLength;
                if (Abs(snappedAt - red.Milliseconds) <= ExactBarTime.One &&
                    (i + 1 == reds.Count || snappedAt < reds[i + 1].Milliseconds))
                {
                    bar = nearestBeat;
                    wasSnapped = true;
                }
            }

            var mapped = previousAnchor + (bar - previousBar) * previousBarLength;
            var error = mapped - red.Milliseconds;
            if (!bar.IsJsonSafeCanonical || bar <= previousBar || bar.ToDouble() <= previousBar.ToDouble() ||
                (mapped / 1000).ToDouble() <= (previousAnchor / 1000).ToDouble() ||
                Abs(error) > (wasSnapped ? ExactBarTime.One : MaxRawErrorMilliseconds))
                throw new InvalidDataException($"osu! red line {red.Line} cannot be represented as a distinct, accurate UV BPM point.");

            double movement = wasSnapped ? error.ToDouble() : 0;
            if (wasSnapped)
            {
                snappedCount++;
                maxSnap = Math.Max(maxSnap, Math.Abs(movement));
            }
            points.Add(new(bar, red.Bpm, red.Milliseconds.ToDouble(), movement));
            previousAnchor = RoundToGrid(mapped, AnchorGrid);
            previousBar = bar;
            previousBarLength = GetBarLength(red);
        }
        return new(points.AsReadOnly(), offset, filename, snappedCount, maxSnap,
            reds.Any(red => red.Meter != 4), false);
    }

    private static ExactBarTime GetBarLength(RedPoint red) =>
        ExactBarTime.FromFraction(240000, 1) / ExactBarTime.FromDouble(red.Bpm);

    private static ExactBarTime Abs(ExactBarTime value) => value.IsNonNegative ? value : -value;

    private static ExactBarTime ToSerializablePosition(ExactBarTime value)
    {
        var limit = ExactBarTime.MaxSafeInteger;
        if (value.IsJsonSafeCanonical || !value.IsNonNegative || value.Bar > limit) return value;

        // Find the closest fraction under the v2 denominator limit using continued fractions.
        // Operate on the fractional part so the whole bar doesn't consume numerator precision.
        var n = value.Numerator;
        var d = value.Denominator;
        BigInteger p0 = 0, q0 = 1, p1 = 1, q1 = 0;
        while (!d.IsZero)
        {
            var a = BigInteger.DivRem(n, d, out var remainder);
            var q2 = q0 + a * q1;
            if (q2 > limit) break;
            (p0, p1) = (p1, p0 + a * p1);
            (q0, q1) = (q1, q2);
            (n, d) = (d, remainder);
        }
        var k = (limit - q0) / q1;
        var edge = new ExactBarTime(value.Bar, p0 + k * p1, q0 + k * q1);
        var convergent = new ExactBarTime(value.Bar, p1, q1);
        if (!edge.IsJsonSafeCanonical) return convergent;
        if (!convergent.IsJsonSafeCanonical) return edge;
        return Abs(edge - value) < Abs(convergent - value) ? edge : convergent;
    }

    private static ExactBarTime RoundToGrid(ExactBarTime value, BigInteger denominator)
    {
        var scaled = value * denominator;
        int half = (scaled.Numerator * 2).CompareTo(scaled.Denominator);
        var rounded = half > 0 || half == 0 && !scaled.Bar.IsEven ? scaled.Bar + 1 : scaled.Bar;
        return ExactBarTime.FromFraction(rounded, denominator);
    }

    internal static bool TryDecimal(ReadOnlySpan<char> text, out ExactBarTime value)
    {
        value = ExactBarTime.Zero;
        if (text.Length is 0 or > MaxNumberCharacters) return false;
        bool negative = text[0] == '-';
        if (negative || text[0] == '+') text = text[1..];
        BigInteger digits = BigInteger.Zero;
        bool seenDigit = false;
        bool afterPoint = false;
        int fractionalDigits = 0;
        int position = 0;
        for (; position < text.Length; position++)
        {
            char c = text[position];
            if (c is >= '0' and <= '9')
            {
                seenDigit = true;
                digits = digits * 10 + (c - '0');
                if (afterPoint) fractionalDigits++;
            }
            else if (c == '.' && !afterPoint) afterPoint = true;
            else break;
        }
        if (!seenDigit) return false;
        int exponent = 0;
        if (position < text.Length && (text[position] is not ('e' or 'E') ||
            !int.TryParse(text[(position + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                out exponent) || exponent is < -MaxNumberCharacters or > MaxNumberCharacters)) return false;
        int power = exponent - fractionalDigits;
        if (negative) digits = -digits;
        value = power >= 0 ? ExactBarTime.FromFraction(digits * BigInteger.Pow(10, power), 1)
            : ExactBarTime.FromFraction(digits, BigInteger.Pow(10, -power));
        return true;
    }
}

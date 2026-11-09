using System.Globalization;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// Decimal interaction arithmetic, not a chart-wide normalization policy. Round-trip text
/// preserves authored double precision; values outside decimal's lossless range use double.
/// </summary>
public static class EditorSpatialMath
{
    private static bool TryDecimal(double value, out decimal result) =>
        decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out result) && (double)result == value;

    public static double Add(double value, double delta)
    {
        if (delta == 0) return value;
        if (TryDecimal(value, out var a) && TryDecimal(delta, out var b))
        {
            try { return (double)(a + b); }
            catch (OverflowException) { }
        }
        return value + delta;
    }

    public static double Subtract(double value, double other) => Add(value, -other);

    public static double Quantize(double value, double step)
    {
        if (!(step > 0)) return value;
        if (TryDecimal(value, out var a) && TryDecimal(step, out var b) && b > 0)
        {
            try { return (double)(decimal.Floor(a / b + .5m) * b); }
            catch (OverflowException) { }
        }
        return Math.Floor(value / step + .5d) * step;
    }
}

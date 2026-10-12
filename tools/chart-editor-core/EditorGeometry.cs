using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Editor mouse-model constants that are not stage geometry. The 1920×1080
/// layout itself lives in shared <see cref="DynamiteUniverse.Shared.Chart.GameplayStageGeometry"/>;
/// this type only owns the magnetic grid steps.</summary>
public static class EditorGeometry
{
    /// <summary>Magnetic chart-coordinate step for note centres (DynaMaker's 0.1 unit grid).</summary>
    public const double CenterSnap = .1d;

    /// <summary>Fine centre step while the free-edit modifier is held.</summary>
    public const double FineCenterSnap = .01d;

    /// <summary>Note widths snap to 1/20 of a chart unit.</summary>
    public const double WidthSnapDivisor = 20d;

    public static ExactBarTime Snap(ExactBarTime time, int divisor)
    {
        if (divisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(divisor));
        var numerator = time.ImproperNumerator * divisor;
        var rounded = System.Numerics.BigInteger.Divide(numerator * 2 + time.Denominator,
            time.Denominator * 2);
        return ExactBarTime.FromFraction(rounded, divisor);
    }
}

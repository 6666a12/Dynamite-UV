using DuxShared.Chart.V2;

namespace DuxCommunity.ChartEditor.Core;

/// <summary>Fixed 1920×1080 editor geometry, matching the approved gameplay layout.</summary>
public static class EditorGeometry
{
    public const double DesignWidth = 1920;
    public const double DesignHeight = 1080;
    public const double CenterLineY = 861;
    public const double CenterX0 = 280;
    public const double CenterUnit = 273.2;
    public const double NoteVisualScale = .95;
    public const double LeftLineX = 184;
    public const double RightLineX = 1736;
    public const double SideY0 = 840;
    public const double SideUnit = 115;
    public const double SideVisualUnit = 102;

    public static double CenterLeft(double center, double width) =>
        CenterX0 + (center - width / 2.0) * CenterUnit;

    public static double CenterWidth(double width) => width * CenterUnit * NoteVisualScale;

    public static double SideCenterY(double center) => SideY0 - SideUnit * center;

    public static double SideLength(double width) => width * SideVisualUnit * NoteVisualScale;

    public static (double Scale, double OffsetX, double OffsetY) Fit(double hostWidth, double hostHeight)
    {
        var scale = Math.Min(hostWidth / DesignWidth, hostHeight / DesignHeight);
        return (scale, (hostWidth - DesignWidth * scale) / 2.0,
            (hostHeight - DesignHeight * scale) / 2.0);
    }

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

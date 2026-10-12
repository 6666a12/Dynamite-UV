using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>右键菜单步进器调整的选中属性。</summary>
public enum EditorStepProperty
{
    Time,
    Center,
    Width,
}

/// <summary>DynaMaker 右键菜单步进器的步长与钳制规则（设计稿 Editor — Context Menu 的
/// TIME/CENTER/WIDTH 行）：CENTER 0.10（细步沿用画布空间吸附的 0.01）、WIDTH 0.05、
/// TIME 走当前网格。放在 core 里是为了让步进几何能和命令层一起回归，而不是只活在画布的
/// 鼠标模型里。</summary>
public static class EditorPropertySteps
{
    /// <summary>中心坐标步长，与画布的空间吸附共用同一个常量。</summary>
    public const double CenterStep = EditorGeometry.CenterSnap;

    /// <summary>按住细步修饰键时的中心坐标步长。</summary>
    public const double FineCenterStep = EditorGeometry.FineCenterSnap;

    /// <summary>宽度步长 = 画布宽度吸附的粒度（1/20 chart unit = 0.05）。</summary>
    public const double WidthStep = 1d / EditorGeometry.WidthSnapDivisor;

    /// <summary>细步宽度步长。</summary>
    public const double FineWidthStep = .01d;

    /// <summary>v2 与画布共用的最小宽度。</summary>
    public const double MinimumWidth = .1d;

    /// <summary>按当前网格前进/后退一格；落在负时间时钳到 bar 0。</summary>
    public static ExactBarTime StepTime(ExactBarTime time, int direction, int gridDivisor)
    {
        if (gridDivisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(gridDivisor));
        var stepped = time + ExactBarTime.FromFraction(direction, gridDivisor);
        return stepped.IsNonNegative ? stepped : ExactBarTime.Zero;
    }

    /// <summary>步进中心坐标，钳制在 [width/2, 5 - width/2]，保证对象整体留在坐标域内。</summary>
    public static double StepCenter(double center, double width, int direction, bool fine)
    {
        var increment = fine ? FineCenterStep : CenterStep;
        var half = Math.Clamp(width, MinimumWidth, EditorStagePlacement.MaximumCenter) / 2d;
        return Round(Math.Clamp(center + direction * increment, half,
            EditorStagePlacement.MaximumCenter - half));
    }

    /// <summary>步进宽度，下限 0.1，上限是不越过坐标域的 2·min(center, 5 - center)。</summary>
    public static double StepWidth(double width, double center, int direction, bool fine)
    {
        var increment = fine ? FineWidthStep : WidthStep;
        var clampedCenter = Math.Clamp(center, 0d, EditorStagePlacement.MaximumCenter);
        var maximum = 2d * Math.Min(clampedCenter, EditorStagePlacement.MaximumCenter - clampedCenter);
        return Round(Math.Clamp(width + direction * increment, MinimumWidth,
            Math.Max(MinimumWidth, maximum)));
    }

    /// <summary>0.1/0.05 这类步长在 double 上会累积尾差，回写前先归一到 1e-6。</summary>
    private static double Round(double value) => Math.Round(value, 6);
}

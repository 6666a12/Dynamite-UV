using System.Globalization;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>放置手势导引的纯计算部分（设计稿 §4 第 9、10 条）：线上读数的文本，以及
/// 已放对象锚点与幽灵/搬运对象的对齐关系判定。绘制与手势状态机留在画布层。</summary>
public enum EditorAlignRelation
{
    /// <summary>位置与宽度都不同（不画提示方块）。</summary>
    None,

    /// <summary>同位置（蓝）。</summary>
    SamePosition,

    /// <summary>同宽度（红）。</summary>
    SameWidth,

    /// <summary>两者皆同（品红）。</summary>
    Both,
}

public static class EditorPlacementGuide
{
    /// <summary>
    /// 对齐提示的颜色编码：比较最终位置与宽度，容差仅用于浮点误差，不代表对象磁吸。
    /// 位置指对象在轨内的中心坐标，与放置读数、chip 读数用的是同一个值。
    /// </summary>
    public static EditorAlignRelation Classify(double position, double width,
        double otherPosition, double otherWidth, double tolerance)
    {
        var samePosition = Math.Abs(position - otherPosition) <= Math.Abs(tolerance);
        var sameWidth = Math.Abs(width - otherWidth) <= Math.Abs(tolerance);
        return (samePosition, sameWidth) switch
        {
            (true, true) => EditorAlignRelation.Both,
            (true, false) => EditorAlignRelation.SamePosition,
            (false, true) => EditorAlignRelation.SameWidth,
            _ => EditorAlignRelation.None,
        };
    }

    /// <summary>放置读数里的位置/宽度文本：吸附后的真值，最多两位小数（吸附步长最小 0.01）。</summary>
    public static string ValueText(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// 放置读数里的时间文本：吸附态用 v2 的分数式 BarTime（设计稿的 <c>91+8/32</c>），
    /// 时间自由态（按住 Z 或 SNAP OFF）显示未吸附的原始小数小节。
    /// </summary>
    public static string TimeText(double seconds, IReadOnlyList<EditableBpm> bpms,
        int gridDivisor, bool snap)
    {
        var bar = EditorTime.SecondsToBar(Math.Max(0d, seconds), bpms);
        return snap
            ? ExactBarTimeText.Format(EditorGeometry.Snap(bar, gridDivisor))
            : bar.ToDouble().ToString("0.####", CultureInfo.InvariantCulture);
    }
}

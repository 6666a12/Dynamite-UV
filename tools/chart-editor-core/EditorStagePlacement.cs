using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Authoring-point conversion for the fixed gameplay stage.
/// Keeping it in the editor core makes left/right placement independently testable.
/// The stage itself is mapped by shared <see cref="GameplayStageGeometry"/>; this type only
/// converts between stage points, chart coordinates and mouse-model snapping.
/// </summary>
public static class EditorStagePlacement
{
    /// <summary>Chart-coordinate limit: every track keeps centre and width inside
    /// [0, <see cref="MaximumCenter"/>].</summary>
    public const double MaximumCenter = 5d;

    public readonly record struct ResizedSpan(double Center, double Width);

    public static ExactBarTime TimeAt(EditorTrack track, double stageX, double stageY,
        double previewBar, double visibleBars, int gridDivisor, bool snap)
    {
        var bar = GameplayStageGeometry.PreviewBarAtStagePoint(ToRuntimeTrack(track),
            (float)stageX, (float)stageY, previewBar, visibleBars);
        var time = ExactBarTime.FromFraction((long)Math.Round(bar * 1000d), 1000);
        return snap ? EditorGeometry.Snap(time, gridDivisor) : time;
    }

    /// <summary>
    /// 作者期舞台的时间轴**不是**上面的 8 小节预览投影：画布按实机落速滚动，所以时间
    /// 由剩余像素除以落速得到（<paramref name="fallSpeedPx"/> 传
    /// <c>GameplayVisualMapper.BaseFallSpeedPx</c>）。两种投影不可互换，保留两个入口是
    /// 为了让这一点显式，而不是让画布再算一遍距离。
    /// </summary>
    public static double TimeAtRemainingPx(double remainingPx, double currentSecond,
        double fallSpeedPx) => currentSecond + remainingPx / fallSpeedPx;

    /// <summary>Inverse of <see cref="TimeAtRemainingPx"/>.</summary>
    public static double RemainingPxAtTime(double second, double currentSecond,
        double fallSpeedPx) => (second - currentSecond) * fallSpeedPx;

    public static double CenterAt(EditorTrack track, double stageX, double stageY) =>
        GameplayStageGeometry.CenterAtStagePoint(ToRuntimeTrack(track),
            (float)stageX, (float)stageY);

    public static double WidthFromDrag(EditorTrack track, double startStageX, double startStageY,
        double endStageX, double endStageY) =>
        GameplayStageGeometry.WidthAtStageDrag(ToRuntimeTrack(track), (float)startStageX,
            (float)startStageY, (float)endStageX, (float)endStageY);

    /// <summary>Magnetic centre snap of the authoring mouse model: one
    /// <see cref="EditorGeometry.CenterSnap"/> step, or the fine step while the modifier is held.
    /// Rounds positive half-steps up, like the DynaMaker Math.round it replaces.</summary>
    public static double SnapCenter(double center, bool fine,
        double maximumCenter = MaximumCenter)
    {
        var increment = fine ? EditorGeometry.FineCenterSnap : EditorGeometry.CenterSnap;
        var value = Math.Clamp(center, 0d, maximumCenter);
        return Math.Clamp(Math.Floor(value / increment + .5d) * increment, 0d, maximumCenter);
    }

    /// <summary>Note widths snap to 1/<see cref="EditorGeometry.WidthSnapDivisor"/> of a chart unit.</summary>
    public static double SnapWidth(double width) =>
        Math.Floor(width * EditorGeometry.WidthSnapDivisor + .5d) /
        EditorGeometry.WidthSnapDivisor;

    /// <summary>Moves one geometry edge while keeping the opposite edge fixed.
    /// Center coordinates increase from the left/bottom edge toward the right/top edge on every track.</summary>
    public static ResizedSpan ResizeFromEdge(double center, double width, double requestedEdge,
        bool moveStartEdge, double minimumWidth = .25d, double maximumCenter = MaximumCenter)
    {
        var opposite = moveStartEdge ? center + width / 2d : center - width / 2d;
        var edge = Math.Clamp(requestedEdge, 0d, maximumCenter);
        if (moveStartEdge)
            edge = Math.Min(edge, opposite - minimumWidth);
        else
            edge = Math.Max(edge, opposite + minimumWidth);

        edge = Math.Clamp(edge, 0d, maximumCenter);
        var nextWidth = Math.Clamp(Math.Abs(opposite - edge), minimumWidth, maximumCenter);
        var nextCenter = Math.Clamp((opposite + edge) / 2d, nextWidth / 2d, maximumCenter - nextWidth / 2d);
        return new ResizedSpan(nextCenter, nextWidth);
    }

    private static Track ToRuntimeTrack(EditorTrack track) => track switch
    {
        EditorTrack.Left => Track.Left,
        EditorTrack.Right => Track.Right,
        _ => Track.Center,
    };
}

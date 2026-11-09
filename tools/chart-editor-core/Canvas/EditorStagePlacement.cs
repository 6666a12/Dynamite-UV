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
    /// <summary>Recommended chart-coordinate upper bound, not an editing or format limit.</summary>
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

    /// <summary>The single editor-track ↔ runtime-track conversion entry point, shared by the
    /// native stage and the shell so both sides never invent their own mapping.</summary>
    public static Track ToRuntimeTrack(EditorTrack track) => track switch
    {
        EditorTrack.Left => Track.Left,
        EditorTrack.Right => Track.Right,
        _ => Track.Center,
    };

    /// <summary>Inverse of <see cref="ToRuntimeTrack"/>; unknown runtime tracks map to
    /// <see cref="EditorTrack.Center"/>, mirroring the native stage's defensive default.</summary>
    public static EditorTrack ToEditorTrack(Track track) => track switch
    {
        Track.Left => EditorTrack.Left,
        Track.Right => EditorTrack.Right,
        _ => EditorTrack.Center,
    };

    /// <summary>
    /// 从舞台点取得**逻辑剩余像素**（正 = 尚未判定），是
    /// <see cref="GameplayStageGeometry.PositionAt"/> 的逆：中轨沿用
    /// <see cref="GameplayStageGeometry.StageDistancePx"/>；侧轨的舞台坐标被
    /// <see cref="GameplayStageGeometry.SideDistanceScale"/> 投影过，必须除回去才回到
    /// PositionAt 的 <c>remainingPx</c> 度量。native 舞台反查一律从这里取距离，不允许
    /// 再直接用 StageDistancePx（那会漏掉逆尺度，把侧轨时间算慢 1/scale 倍）。
    /// </summary>
    public static double LogicalRemainingPx(EditorTrack track, double stageX, double stageY)
    {
        var runtime = ToRuntimeTrack(track);
        var distance = GameplayStageGeometry.StageDistancePx(runtime, (float)stageX, (float)stageY);
        return runtime == Track.Center
            ? distance
            : distance / GameplayStageGeometry.SideDistanceScale;
    }

    /// <summary>
    /// 权威的舞台时间反查：舞台点 → <see cref="ExactBarTime"/>。对
    /// <see cref="VisualScrollMath.DistancePixels"/> 的正向距离方程做有界二分求根；
    /// Scroll 速度随 BarTime 变化，不能用固定次数的定点迭代代替。落速包含
    /// <paramref name="noteSpeedMultiplier"/>。对于多个解或不连续曲线，取当前时间向外
    /// 扩展所得 bracket 内二分逼近的跳变/根。
    /// <paramref name="snap"/> 为 false 时保留百万分之一小节的精确
    /// double→fraction 边界，为 true 时按 <paramref name="gridDivisor"/> 走
    /// <see cref="EditorGeometry.Snap"/>；负时间钳到 0。不经过任何 UI 显示秒或文本。
    /// </summary>
    public static ExactBarTime BarTimeAt(EditorTrack track, double stageX, double stageY,
        V2RuntimeChartMetadata metadata, double currentSecond, double playerSpeed,
        int gridDivisor, bool snap, double noteSpeedMultiplier = 1d)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var remaining = LogicalRemainingPx(track, stageX, stageY);
        if (!double.IsFinite(remaining) || !double.IsFinite(currentSecond) ||
            !double.IsFinite(playerSpeed) || playerSpeed <= 0d ||
            !double.IsFinite(noteSpeedMultiplier) || noteSpeedMultiplier <= 0d)
            throw new ArgumentOutOfRangeException(nameof(stageX), "Stage time inputs must be finite and speeds positive.");

        var currentBar = metadata.BpmTimeline.RuntimeBarAt(currentSecond);
        double Residual(double bar)
        {
            var second = metadata.BpmTimeline.RuntimeSecondsAt(bar);
            var speed = metadata.ScrollMap.RuntimeSpeedAt(bar);
            return VisualScrollMath.DistancePixels(second, currentSecond,
                GameplayStageGeometry.BaseFallSpeedPx, speed, playerSpeed, noteSpeedMultiplier) - remaining;
        }

        var resolved = currentBar;
        if (remaining != 0d)
        {
            var lower = currentBar;
            var upper = currentBar;
            var step = 1d;
            var bracketed = false;
            for (var iteration = 0; iteration < 64; iteration++)
            {
                if (remaining > 0d)
                {
                    upper = currentBar + step;
                    bracketed = Residual(upper) >= 0d;
                }
                else
                {
                    lower = currentBar - step;
                    bracketed = Residual(lower) <= 0d;
                }
                if (bracketed) break;
                step *= 2d;
            }
            if (!bracketed)
                throw new InvalidOperationException("Could not bracket the stage time inverse.");

            for (var iteration = 0; iteration < 80; iteration++)
            {
                var middle = lower + (upper - lower) / 2d;
                if (Residual(middle) >= 0d) upper = middle;
                else lower = middle;
            }
            resolved = lower + (upper - lower) / 2d;
        }

        var exact = ExactBarTime.FromFraction(
            (long)Math.Round(Math.Max(0d, resolved) * 1_000_000d), 1_000_000);
        return snap ? EditorGeometry.Snap(exact, gridDivisor) : exact;
    }

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
        return Math.Clamp(QuantizePosition(value, increment), 0d, maximumCenter);
    }

    /// <summary>Note widths snap to 1/<see cref="EditorGeometry.WidthSnapDivisor"/> of a chart unit.</summary>
    public static double SnapWidth(double width) =>
        QuantizePosition(width, 1d / EditorGeometry.WidthSnapDivisor);

    /// <summary>Snap the grabbed anchor once; all selected Notes keep their relative phases.</summary>
    public static ExactBarTime ResolveMoveDelta(ExactBarTime anchor, ExactBarTime delta,
        int gridDivisor, bool snap)
    {
        if (delta == ExactBarTime.Zero || !snap) return delta;
        var target = anchor + delta;
        if (target < ExactBarTime.Zero) target = ExactBarTime.Zero;
        return EditorGeometry.Snap(target, gridDivisor) - anchor;
    }

    /// <summary>Spatial quantization is an interaction preference, never a file restriction.</summary>
    public static double QuantizePosition(double value, double step) => EditorSpatialMath.Quantize(value, step);

    /// <summary>Snap one grabbed anchor, preserving off-grid geometry on time-only moves.</summary>
    public static double ResolvePositionDelta(double anchor, double delta, double step) =>
        delta == 0d ? 0d : step <= 0 ? delta :
            EditorSpatialMath.Subtract(QuantizePosition(EditorSpatialMath.Add(anchor, delta), step), anchor);

    /// <summary>Width snapping must retain the opposite logical edge, including at stage bounds.</summary>
    public static ResizedSpan ResizeSnappedFromEdge(double center, double width,
        double requestedEdge, bool moveStartEdge, double widthStep = .05d)
    {
        var span = ResizeFromEdge(center, width, requestedEdge, moveStartEdge);
        var opposite = EditorSpatialMath.Add(center, (moveStartEdge ? 1 : -1) * width / 2d);
        var snappedWidth = QuantizePosition(span.Width, widthStep);
        // Existing strict-v2 Notes may be narrower than the normal resize minimum.
        if (snappedWidth <= 0d) return new ResizedSpan(center, width);
        return new ResizedSpan(EditorSpatialMath.Add(opposite, (moveStartEdge ? -1 : 1) * snappedWidth / 2d),
            snappedWidth);
    }

    /// <summary>Moves one geometry edge while keeping the opposite edge fixed.
    /// Center coordinates increase from the left/bottom edge toward the right/top edge on every track.</summary>
    public static ResizedSpan ResizeFromEdge(double center, double width, double requestedEdge,
        bool moveStartEdge, double minimumWidth = .25d, double maximumCenter = MaximumCenter)
    {
        var opposite = EditorSpatialMath.Add(center, (moveStartEdge ? 1 : -1) * width / 2d);
        var edge = requestedEdge;
        if (moveStartEdge)
            edge = Math.Min(edge, opposite - minimumWidth);
        else
            edge = Math.Max(edge, opposite + minimumWidth);

        var nextWidth = Math.Abs(EditorSpatialMath.Subtract(opposite, edge));
        var nextCenter = EditorSpatialMath.Add(opposite, EditorSpatialMath.Subtract(edge, opposite) / 2d);
        return new ResizedSpan(nextCenter, nextWidth);
    }
}

using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>路径上的一个控制点（头或 node），坐标是谱面**中心**坐标（<c>center</c>，不是左缘）。
/// 画布的 authoring 投影存左缘，喂进这里之前由调用方换算。</summary>
public readonly record struct EditorPathPoint(
    ExactBarTime Time, double Center, double Width, V2PathCurve? CurveToNext);

/// <summary>Hold/Mixer 路径在谱面坐标下的快照：头 + 控制节点（可以为空，空时只有头一个点）。</summary>
public sealed record EditorPathShape(EditorPathPoint Head, IReadOnlyList<EditorPathPoint> Nodes);

/// <summary>路径求值出的一个点：<see cref="IsControlPoint"/> 区分真实控制节点与曲线细分采样。</summary>
public readonly record struct EditorPathSample(
    ExactBarTime Time, double Center, double Width, bool IsControlPoint);

/// <summary>
/// 编辑器画布用的路径求值/投影，真源是 shared <see cref="V2PathEvaluator"/>：本类型只负责
/// 「按曲线类型把路径展开成一串可逐段渲染的直线段」和「路径上某时刻的 center/width 采样」，
/// 不做任何判定或写包语义。细分判据与实机 <c>V2RuntimeAdapter</c> 相同（0.5px 弦高误差，
/// 2 的幂次数减半逼近），只有细分上限更小（64 vs 256），避免编辑期每次重建生成过多节点。
/// </summary>
public static class EditorPathProjection
{
    public const int MaximumSubdivisions = 64;
    public const double SubdivisionTolerancePx = .5d;

    /// <summary>文档里的 Hold/Mixer 路径 → 求值输入。画布插入节点与测试共用这一处换算，
    /// 保证「采样值 = 写回值」的判据只有一份。</summary>
    public static EditorPathShape ShapeOf(EditableNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new EditorPathShape(
            new EditorPathPoint(note.Time, note.Center, note.Width, note.CurveToNext),
            note.Nodes.Select(node => new EditorPathPoint(node.Time, node.Center, node.Width,
                node.CurveToNext)).ToArray());
    }

    /// <summary>路径在 <paramref name="time"/> 处求值出的 center/width（超出两端则取端点值）。
    /// 双击插入路径节点用这一处采样，因此插入点几何与路径求值结果逐位相同、形状零跳变。</summary>
    public static (double Center, double Width) Sample(EditorPathShape shape, ExactBarTime time)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.Nodes.Count == 0)
            return (shape.Head.Center, shape.Head.Width);
        var sample = new V2PathEvaluator(ToPathNote(shape)).Evaluate(time);
        return (sample.Center, sample.Width);
    }

    /// <summary>把整条路径展开成时间递增的点串（头 + 每段插值细分 + 每个控制节点）。
    /// <paramref name="positionScalePx"/> 是 1 个谱面单位在屏幕上的像素数，
    /// <paramref name="widthScalePx"/> 是宽度误差的像素系数（与实机取同一组值）。</summary>
    public static IReadOnlyList<EditorPathSample> Flatten(EditorPathShape shape,
        double positionScalePx, double widthScalePx)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var points = new EditorPathPoint[shape.Nodes.Count + 1];
        points[0] = shape.Head;
        for (var index = 0; index < shape.Nodes.Count; index++)
            points[index + 1] = shape.Nodes[index];

        var result = new List<EditorPathSample>(points.Length)
        {
            new(shape.Head.Time, shape.Head.Center, shape.Head.Width, true),
        };
        if (points.Length == 1)
            return result;

        V2PathEvaluator? evaluator = null;
        for (var index = 1; index < points.Length; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var curve = previous.CurveToNext ?? V2PathCurve.Linear;
            // 时间不递增的区间只可能来自未提交的草稿预览，此时不做求值（PCHIP 会除以零区间）。
            if (curve != V2PathCurve.Linear && current.Time > previous.Time)
            {
                evaluator ??= new V2PathEvaluator(ToPathNote(shape));
                var subdivisions = RequiredSubdivisions(evaluator, previous.Time, current.Time,
                    positionScalePx, widthScalePx);
                for (var step = 1; step < subdivisions; step++)
                {
                    var time = previous.Time + (current.Time - previous.Time) *
                        ExactBarTime.FromFraction(step, subdivisions);
                    var sample = evaluator.Evaluate(time);
                    result.Add(new EditorPathSample(time, sample.Center, sample.Width, false));
                }
            }
            result.Add(new EditorPathSample(current.Time, current.Center, current.Width, true));
        }
        return result;
    }

    /// <summary>自适应的 2 的幂次细分：弦高误差降到容差以内就停，最差取上限。</summary>
    public static int RequiredSubdivisions(V2PathEvaluator evaluator, ExactBarTime start,
        ExactBarTime end, double positionScalePx, double widthScalePx)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        for (var segments = 2; segments <= MaximumSubdivisions; segments *= 2)
            if (SubdivisionErrorPx(evaluator, start, end, segments, positionScalePx, widthScalePx) <=
                SubdivisionTolerancePx)
                return segments;
        return MaximumSubdivisions;
    }

    private static double SubdivisionErrorPx(V2PathEvaluator evaluator, ExactBarTime start,
        ExactBarTime end, int segments, double positionScalePx, double widthScalePx)
    {
        var maxError = 0d;
        for (var segment = 0; segment < segments; segment++)
        {
            var leftTime = start + (end - start) * ExactBarTime.FromFraction(segment, segments);
            var rightTime = start + (end - start) * ExactBarTime.FromFraction(segment + 1, segments);
            var midpointTime = start + (end - start) * ExactBarTime.FromFraction(2 * segment + 1, 2 * segments);
            var left = evaluator.Evaluate(leftTime);
            var right = evaluator.Evaluate(rightTime);
            var midpoint = evaluator.Evaluate(midpointTime);
            var centerError = Math.Abs(midpoint.Center - (left.Center + right.Center) / 2d) * positionScalePx;
            var widthError = Math.Abs(midpoint.Width - (left.Width + right.Width) / 2d) * widthScalePx / 2d;
            maxError = Math.Max(maxError, centerError + widthError);
        }
        return maxError;
    }

    /// <summary>中间表示：shared 求值器只认 v2 语义模型，这里用一次性合成 ID 构造，不写回文档。</summary>
    private static V2PathNote ToPathNote(EditorPathShape shape)
    {
        IReadOnlyList<EditorPathPoint> nodes = shape.Nodes.Count > 0
            ? shape.Nodes
            : [new EditorPathPoint(shape.Head.Time, shape.Head.Center, shape.Head.Width, null)];
        return new V2PathNote
        {
            Id = "path",
            Type = V2NoteType.Hold,
            Time = shape.Head.Time,
            Center = shape.Head.Center,
            Width = shape.Head.Width,
            CurveToNext = shape.Head.CurveToNext,
            Nodes = nodes.Select((node, index) => new V2PathNode
            {
                Id = $"path-node-{index + 1}",
                Time = node.Time,
                Center = node.Center,
                Width = node.Width,
                CurveToNext = node.CurveToNext,
            }).ToArray(),
        };
    }
}

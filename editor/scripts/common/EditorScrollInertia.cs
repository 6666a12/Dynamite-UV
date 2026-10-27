using System;

namespace DynamiteUniverse.Editor;

/// <summary>滚轮与触摸板滚动的惯性模型（决策见 docs/editor-ui-design.md §4 第 14 条）：滚轮不再
/// 直接改"显示值"，而是推一个"目标值"，显示值按帧率无关的指数趋近追上目标
/// （<c>current += (target − current) × (1 − e^(−Δ/τ))</c>），所以同样的滑动在高刷与 30fps 下手感一致。
///
/// 刻意不做成有状态的类：两处调用方的"显示值"（shell 的预览时间、包页列表偏移）本来就各自是
/// 唯一真值，再包一层只会多出一份可能与画布不同步的副本。τ 由设置真源给出（<see cref="EditorSettings.ScrollInertiaTauSeconds"/>），
/// 所以拖动滑条时正在进行的滑动会立刻换手感。</summary>
internal static class EditorScrollInertia
{
    /// <summary>剩余行程小于它就直接落目标：指数曲线只在无穷处才到终点，留尾差会让视图永远
    /// 差一点、每帧继续重绘。1e-4 s 低于 HUD 读数（mm:ss.mmm）与进度线的可见分辨率。</summary>
    public const double TimeSnapSeconds = 1e-4d;

    /// <summary>像素空间的落点阈值：不足半个像素的行程不改变任何一行或读数的画法。</summary>
    public const float PixelSnap = .5f;

    /// <summary>推进一帧的时间值。τ ≤ 0（滑条最左，或 MOTION = OFF 强制关）直接返回目标值，
    /// 与改动前的瞬时行为逐值一致。</summary>
    public static double Step(double current, double target, double tau, double delta)
    {
        if (tau <= 0d) return target;
        var next = current + (target - current) * (1d - Math.Exp(-delta / tau));
        return Math.Abs(target - next) <= TimeSnapSeconds ? target : next;
    }

    /// <summary>推进一帧的像素值（包页差分行列表）。</summary>
    public static float Step(float current, float target, double tau, double delta)
    {
        if (tau <= 0d) return target;
        var next = current + (target - current) * (float)(1d - Math.Exp(-delta / tau));
        return Math.Abs(target - next) <= PixelSnap ? target : next;
    }
}

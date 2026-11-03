namespace DynamiteUniverse.Ui;

/// <summary>
/// 列表滚动的惯性模型：滚轮/触摸板推力只改"目标值"，显示值按帧率无关的指数趋近追上
/// （<c>current += (target − current) × (1 − e^(−Δ/τ))</c>），高刷与低帧率手感一致。
/// </summary>
public static class ScrollInertia
{
    /// <summary>client 侧的时间常数（秒，指数趋近的 τ）。client 暂无该设置项（用户拍板不做），
    /// 所以这里固定成常量而不是读设置。</summary>
    public const double TauSeconds = 0.112;

    /// <summary>剩余行程小于它就直接落目标：指数曲线只在无穷处到终点，留尾差会每帧继续重绘、
    /// 滚动条也永远差一点。</summary>
    public const float SnapPixels = .5f;

    /// <summary>推进一帧。τ ≤ 0 直接返回目标值（无惯性）。</summary>
    public static float Step(float current, float target, double tau, double delta)
    {
        if (tau <= 0d) return target;
        var next = current + (target - current) * (float)(1d - System.Math.Exp(-delta / tau));
        return System.Math.Abs(target - next) <= SnapPixels ? target : next;
    }
}

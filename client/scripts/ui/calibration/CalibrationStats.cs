namespace DynamiteUniverse.Ui;

/// <summary>
/// 延迟校准向导的统计内核：纯 C#，不依赖 Godot，供 client 页面与 tools/core-tests 共用。
///
/// 样本口径：每次有效击打给出一个**有符号偏差（毫秒）**，正 = 晚（tap 落在过线时刻之后）、
/// 负 = 早。|偏差| > <see cref="MaxAcceptedDeviationMs"/> 的 stray tap 由调用方拒绝入样
/// （<see cref="TryAdd"/> 也会兜底拒绝），不计入中位数与离散度。
/// </summary>
public sealed class CalibrationStats
{
    /// <summary>有效样本的偏差上限（±150ms）：超出的击打视为 stray tap，不入样。</summary>
    public const double MaxAcceptedDeviationMs = 150.0;

    // ---- 目标样本数：8–32，每档 4，默认 16（与 <c>GameSettings.CalibrationTargetSamples</c> 同步演化）----
    public const int MinTargetSamples = 8;
    public const int MaxTargetSamples = 32;
    public const int TargetSampleStep = 4;
    public const int DefaultTargetSamples = 16;

    // ---- 建议偏移：与设置页 JUDGE OFFSET 同一档位与范围 ----
    public const int OffsetStepMs = 5;
    public const int OffsetLimitMs = 300;

    private readonly List<double> _samples = new();

    /// <summary>已入样的偏差（毫秒），按击打顺序。</summary>
    public IReadOnlyList<double> Samples => _samples;

    public int Count => _samples.Count;

    /// <summary>是否已有足够样本给出离散度：少于 2 个样本时离散度无定义（页面显示 —）。</summary>
    public bool HasSpread => _samples.Count >= 2;

    /// <summary>
    /// 尝试把一个偏差记入样本。返回 false 表示拒绝入样（非有限数或 |偏差| &gt; 150ms）。
    /// </summary>
    public bool TryAdd(double deviationMs)
    {
        if (!double.IsFinite(deviationMs) || Math.Abs(deviationMs) > MaxAcceptedDeviationMs)
            return false;
        _samples.Add(deviationMs);
        return true;
    }

    public void Clear() => _samples.Clear();

    /// <summary>中位数：奇数样本取中间值，偶数样本取中间两值平均；无样本时为 null。</summary>
    public double? MedianMs
    {
        get
        {
            if (_samples.Count == 0)
                return null;
            var sorted = _samples.ToArray();
            Array.Sort(sorted);
            var mid = sorted.Length / 2;
            return sorted.Length % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) * 0.5;
        }
    }

    /// <summary>
    /// 离散度 = 总体标准差（除以 N，不是 N−1）：样本已覆盖全部有效击打，不做自由度修正。
    /// 少于 2 个样本时为 null（页面显示 —）。
    /// </summary>
    public double? SpreadMs
    {
        get
        {
            if (_samples.Count < 2)
                return null;
            var mean = _samples.Average();
            var variance = _samples.Sum(value => (value - mean) * (value - mean)) / _samples.Count;
            return Math.Sqrt(variance);
        }
    }

    /// <summary>
    /// 建议偏移 = 当前偏移 − 中位数 → 5ms 步进四舍五入（半档远离零）→ ±300 钳制。
    ///
    /// 符号推导（2026-10 复核源码定案）：<c>JudgeEngine.Judge</c> 的偏差是
    /// <c>inputTime − noteTime</c>（正 = 晚），而 inputTime 来自 <c>SongClock.GetSongTime()</c>，
    /// 其中<strong>加上</strong> UserOffsetMs。玩家整体打得晚（中位数为正）意味着判定时钟需要往回拉，
    /// 所以修正方向是<strong>减</strong>：建议值 = 当前值 − 中位数。页面上的「当前 +12 → 应用后 +3」
    /// 读数方向与此一致。整个项目只有这一处持有该符号约定；实机验收若发现方向相反，改这里即可。
    /// </summary>
    public static int SuggestedOffsetMs(double currentOffsetMs, double medianMs)
    {
        // 非有限中位数（理论上不会出现，TryAdd 已拒绝）退化为「不修正」，但保留 5ms 取整与钳制。
        var offset = double.IsFinite(currentOffsetMs) ? currentOffsetMs : 0.0;
        var median = double.IsFinite(medianMs) ? medianMs : 0.0;
        return ClampOffset(QuantizeOffset(offset - median));
    }

    /// <summary>把毫秒值取到 5ms 步进（恰好半档时远离零，例如 +2.5 → +5、−2.5 → −5）。</summary>
    public static double QuantizeOffset(double valueMs) =>
        Math.Round(valueMs / OffsetStepMs, MidpointRounding.AwayFromZero) * OffsetStepMs;

    /// <summary>建议偏移的最终钳制（与设置页同范围）。</summary>
    public static int ClampOffset(double valueMs) =>
        Math.Clamp((int)Math.Round(valueMs), -OffsetLimitMs, OffsetLimitMs);

    /// <summary>当前样本能给出的建议偏移；无样本时为 null（页面不显示建议值）。</summary>
    public int? SuggestedOffset(double currentOffsetMs) =>
        MedianMs is { } median ? SuggestedOffsetMs(currentOffsetMs, median) : null;

    /// <summary>把任意整数规整为目标样本数：先钳到 8–32，再对齐到 4 的档位。</summary>
    public static int NormalizeTargetSamples(int value)
    {
        var clamped = Math.Clamp(value, MinTargetSamples, MaxTargetSamples);
        var steps = (int)Math.Round(
            (clamped - MinTargetSamples) / (double)TargetSampleStep,
            MidpointRounding.AwayFromZero);
        return Math.Clamp(
            MinTargetSamples + steps * TargetSampleStep, MinTargetSamples, MaxTargetSamples);
    }
}

/// <summary>
/// 循环音源位置展开器：<c>AudioStreamPlayer.GetPlaybackPosition()</c> 在循环流上每圈回卷到 0，
/// 直接当时间轴用会让「第几小节」永远不前进（热身永不结束、采样计数恒为 0）。
/// 本类把回卷展开成单调递增时间轴：raw 比上一帧小超过半圈即视为回卷，累计一圈时长。
/// 圈内的微小回退（混音补偿在两次调用间归零造成的几毫秒抖动）不触发展开。
/// </summary>
public sealed class LoopPositionUnwrapper
{
    private readonly double _loopSeconds;
    private double _lastRaw = -1.0;
    private double _baseSeconds;
    private double _lastPosition = double.NegativeInfinity;

    public LoopPositionUnwrapper(double loopSeconds)
    {
        if (loopSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(loopSeconds));
        _loopSeconds = loopSeconds;
    }

    /// <summary>喂入本帧的流内位置（可含混音修正），返回展开后的单调时间轴位置（秒）。</summary>
    public double Advance(double rawPositionSeconds)
    {
        if (_lastRaw >= 0.0 && rawPositionSeconds < _lastRaw - _loopSeconds * 0.5)
            _baseSeconds += _loopSeconds;
        _lastRaw = rawPositionSeconds;
        _lastPosition = Math.Max(_lastPosition, _baseSeconds + rawPositionSeconds);
        return _lastPosition;
    }
}

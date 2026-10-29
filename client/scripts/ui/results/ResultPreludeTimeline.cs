namespace DynamiteUniverse.Ui;

/// <summary>
/// 结果字标前奏时钟。数值来自独立预览 <c>design/result-outcomes/surge/godot/preview.gd</c>：
/// 压暗 0.30s、斜纹区 0.04–0.28s、字标 0.20s 起拍 0.40s、命中辉光在 0.52s，1.55s 交给屏风。
/// 全部输出都是 <see cref="Elapsed"/> 的纯函数，跳过即落终值，不需要收尾分支。
/// </summary>
public sealed class ResultPreludeTimeline
{
    /// <summary>带字标的 Full 前奏：0.20–0.60s 拍入，0.60–1.55s 短停留。</summary>
    public const double MarkDuration = 1.55;

    /// <summary>无有效成绩数据的保护路径只有压暗与斜纹区：斜纹区 0.28s 到位，再停 0.40s。</summary>
    public const double PlainDuration = 0.68;

    /// <summary>Reduced：静态短淡入，无方向位移、无砸入、无强闪。</summary>
    public const double ReducedDuration = 0.26;

    public const double DimTarget = 0.80;
    public const float EnergyFloor = 1.0f;
    public const float GlowBase = 0.85f;

    private const double DimSeconds = 0.30;
    private const double BandDelay = 0.04;
    private const double BandSeconds = 0.24;
    private const double StampStart = 0.20;
    private const double StampSeconds = 0.40;
    private const double StampFadeSeconds = 0.09;
    private const double ImpactCenter = 0.52;
    private const double ImpactWidth = 0.12;
    private const double EnergyDecayStart = 0.60;
    private const double EnergyDecayRate = 7.0;
    private const float StampScaleFrom = 1.58f;
    private const float EnergyFrom = 0.18f;
    private const float EnergyTo = 2.25f;
    private const float GlowImpact = 0.35f;

    public ResultPreludeTimeline(UiMotionMode mode, bool hasWordmark)
    {
        Mode = mode;
        HasWordmark = hasWordmark && mode != UiMotionMode.Off;
        Duration = mode switch
        {
            UiMotionMode.Off => 0,
            UiMotionMode.Reduced => ReducedDuration,
            _ => hasWordmark ? MarkDuration : PlainDuration,
        };
    }

    public UiMotionMode Mode { get; }
    public bool HasWordmark { get; }
    public double Duration { get; }
    public double Elapsed { get; private set; }

    /// <summary>Off 不进入前奏：宿主直接显示稳定结算页，不留一帧前奏。</summary>
    public static bool RunsFor(UiMotionMode mode) => mode != UiMotionMode.Off;

    public bool IsComplete => Elapsed >= Duration;

    /// <summary>压暗终值 0.80；Off 不会绘制前奏，这里只用于取值一致性。</summary>
    public double DimAlpha => DimTarget * Smooth01(Elapsed / (Mode == UiMotionMode.Reduced
        ? ReducedDuration : DimSeconds));

    public double BandAlpha => Mode == UiMotionMode.Reduced
        ? Smooth01(Elapsed / ReducedDuration)
        : Smooth01((Elapsed - BandDelay) / BandSeconds);

    public double MarkAlpha => !HasWordmark ? 0
        : Mode == UiMotionMode.Reduced ? Smooth01(Elapsed / ReducedDuration)
        : Smooth01((Elapsed - StampStart) / StampFadeSeconds);

    /// <summary>拍入缩放：1.58 → 1.0。Reduced/Off 静态 1.0（无砸入）。</summary>
    public float MarkScale => Mode == UiMotionMode.Full && HasWordmark
        ? Lerp(StampScaleFrom, 1f, EaseOutBack(StampProgress))
        : 1f;

    /// <summary>命中瞬间的亮度冲击，供斜纹区染色与辉光增益使用。</summary>
    public float Impact => Mode == UiMotionMode.Full && HasWordmark
        ? (float)Math.Exp(-Math.Pow((Elapsed - ImpactCenter) / ImpactWidth, 2.0)) : 0f;

    public float Energy
    {
        get
        {
            if (Mode != UiMotionMode.Full || !HasWordmark)
                return EnergyFloor;
            var energy = Lerp(EnergyFrom, EnergyTo, EaseOutCubic(StampProgress));
            if (Elapsed > EnergyDecayStart)
                energy = Lerp(EnergyFloor, energy,
                    (float)Math.Exp(-(Elapsed - EnergyDecayStart) * EnergyDecayRate));
            return energy;
        }
    }

    public float Glow => GlowBase + Impact * GlowImpact;

    public void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0)
            return;
        Elapsed = Math.Min(Duration, Elapsed + delta);
    }

    /// <summary>跳过只落终值：字标停在拍入后的稳定态，屏风随后照常接管。</summary>
    public void Skip() => Elapsed = Duration;

    private double StampProgress => Math.Clamp((Elapsed - StampStart) / StampSeconds, 0.0, 1.0);

    private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;

    private static double Smooth01(double value)
    {
        var x = Math.Clamp(value, 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }

    private static float EaseOutCubic(double value)
    {
        var x = Math.Clamp(value, 0.0, 1.0);
        return (float)(1.0 - Math.Pow(1.0 - x, 3.0));
    }

    /// <summary>easeOutBack（c1=1.35，与预览一致）；<see cref="UiEase"/> 钳在 [0,1] 给不出过冲。</summary>
    private static float EaseOutBack(double value)
    {
        const float c1 = 1.35f;
        const float c3 = c1 + 1f;
        var x = (float)Math.Clamp(value, 0.0, 1.0);
        var u = x - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}

/// <summary>
/// 一次结算只进入一次字标前奏、只交付一次结算页。计时到点与跳过都走
/// <see cref="TryCompletePrelude"/>，重复回调（例如跳过后同一帧计时也到点）直接忽略。
/// </summary>
public sealed class ResultRunSequence
{
    private bool _started;
    private bool _preludeCompleted;

    public bool Started => _started;
    public bool PreludeCompleted => _preludeCompleted;

    public bool TryBegin()
    {
        if (_started)
            return false;
        _started = true;
        return true;
    }

    /// <summary>无前奏（Motion Off）或前奏被跳过时也必须经过此处，结算页只会被交付一次。</summary>
    public bool TryCompletePrelude()
    {
        if (!_started || _preludeCompleted)
            return false;
        _preludeCompleted = true;
        return true;
    }
}

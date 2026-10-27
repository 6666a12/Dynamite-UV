namespace DynamiteUniverse.Ui;

/// <summary>采样相位：Ready（目标样本数可调）→ Sampling（目标锁定，热身结束后入样）→ Complete（读数冻结）。</summary>
public enum SamplingPhase
{
    Ready,
    Sampling,
    Complete,
}

/// <summary>一次击打的口径。</summary>
public enum HitOutcome
{
    /// <summary>已完成：击打不产生读数也不入样（事件仍由页面消费）。</summary>
    Ignored,
    /// <summary>热身 note：有带符号读数、灰显、不入样。</summary>
    Warmup,
    /// <summary>入样：成为一条新样本。</summary>
    Accepted,
    /// <summary>同一颗 note 的重复击打：按 Accepted 口径刷新读数，但不入样。</summary>
    Duplicate,
    /// <summary>stray tap（|偏差| 超出 ±150ms）：读数显示 —，不入样。</summary>
    Stray,
}

/// <summary>一次击打的结果：口径 + 带符号偏差（毫秒，正 = 晚）+ 偏差归属的 note 序号。</summary>
public readonly record struct HitResult(HitOutcome Outcome, double DeviationMs, int AttributedNote);

/// <summary>
/// 延迟校准向导的采样内核：纯 C#，不依赖 Godot，供 client 页面与 tools/core-tests 共用。
///
/// 拥有采样相位、目标样本数（进入 Sampling 后锁定）、热身基准 note、最后入样 note 与
/// <see cref="CalibrationStats"/>；页面只负责读数、动效、建议偏移的保存与导航。
///
/// 计时口径与页面 <c>SongPositionSeconds</c> 一致：note 每 <see cref="BarSeconds"/> 秒过线一颗，
/// 偏差对**最近**的小节头取（<c>Math.Round</c> 为默认银行家舍入），偏差**归属**的 note 也按最近
/// 小节头算序号 —— 提前 ≤150ms 的击打属于下一颗 note，热身与去重都按归属 note 判定。
/// </summary>
public sealed class CalibrationSampling
{
    /// <summary>热身 note 数：进入采样后最先过线的 2 颗 note 的击打只做读数、不入样。</summary>
    public const int WarmupNotes = 2;

    private readonly CalibrationStats _stats = new();

    public CalibrationSampling(double barSeconds, int targetSamples)
    {
        if (!double.IsFinite(barSeconds) || barSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(barSeconds));
        BarSeconds = barSeconds;
        TargetSamples = CalibrationStats.NormalizeTargetSamples(targetSamples);
    }

    /// <summary>note 过线间隔（秒）：与节拍音源的一小节等长。</summary>
    public double BarSeconds { get; }

    public SamplingPhase Phase { get; private set; } = SamplingPhase.Ready;

    /// <summary>目标样本数：Ready 时可调，采样中锁定。</summary>
    public int TargetSamples { get; private set; }

    /// <summary>进入采样时刚过线的那颗 note 的序号：它与其后一颗是热身。</summary>
    public int WarmupBaseNote { get; private set; }

    /// <summary>最近一次入样的归属 note 序号（-1 = 尚未入样）：每颗 note 至多贡献一个样本。</summary>
    public int LastSampledNote { get; private set; } = -1;

    public CalibrationStats Stats => _stats;

    public int Count => _stats.Count;

    public bool IsComplete => Phase == SamplingPhase.Complete;

    /// <summary>
    /// 目标样本数只在 Ready 可调。返回 true 表示接受了这次调整，调用方据此落盘并刷新读数；
    /// 规整后与当前值相同仍算接受，保持原有边界点击的保存节奏。
    /// </summary>
    public bool TrySetTargetSamples(int value)
    {
        if (Phase != SamplingPhase.Ready)
            return false;
        TargetSamples = CalibrationStats.NormalizeTargetSamples(value);
        return true;
    }

    /// <summary>
    /// 一次击打。Ready 的首击同时进入采样（基准 = 已经过线的那颗 note），所以首击总被当热身；
    /// 达到目标样本数的那次入样把相位切到 <see cref="SamplingPhase.Complete"/>。
    /// </summary>
    public HitResult Hit(double songSec)
    {
        var deviation = DeviationMs(songSec);
        var attributedNote = AttributedNote(songSec);

        if (Phase == SamplingPhase.Ready)
        {
            WarmupBaseNote = PassedNoteCount(songSec);
            LastSampledNote = -1;
            Phase = SamplingPhase.Sampling;
        }

        if (Phase == SamplingPhase.Complete)
            return new HitResult(HitOutcome.Ignored, deviation, attributedNote);

        // 热身由偏差归属的那颗 note 判定（最近小节头），而不是「当前小节」——
        // 提前 ≤150ms 的击打属于下一颗 note，否则提前量会把它误记成前一颗的热身。
        if (attributedNote <= WarmupBaseNote + WarmupNotes - 1)
            return new HitResult(
                Math.Abs(deviation) > CalibrationStats.MaxAcceptedDeviationMs
                    ? HitOutcome.Stray
                    : HitOutcome.Warmup,
                deviation,
                attributedNote);

        // 每颗 note 至多贡献一个样本：对同一颗 note 连击（±150ms 内的第二次击打）不是独立观测。
        if (attributedNote == LastSampledNote)
            return new HitResult(HitOutcome.Duplicate, deviation, attributedNote);

        if (!_stats.TryAdd(deviation))
            return new HitResult(HitOutcome.Stray, deviation, attributedNote);

        LastSampledNote = attributedNote;
        if (_stats.Count >= TargetSamples)
            Phase = SamplingPhase.Complete;
        return new HitResult(HitOutcome.Accepted, deviation, attributedNote);
    }

    /// <summary>再测一次：清空样本与 note 记账回到 Ready，保留目标样本数；返回后可从首击重新热身。</summary>
    public void Restart()
    {
        _stats.Clear();
        WarmupBaseNote = 0;
        LastSampledNote = -1;
        Phase = SamplingPhase.Ready;
    }

    /// <summary>击打时刻的带符号偏差（毫秒，正 = 晚）：与最近小节头相减。</summary>
    public double DeviationMs(double songSec) => (songSec - NearestBarHead(songSec)) * 1000.0;

    /// <summary>偏差归属的 note 序号 = 最近小节头的序号 + 1（与 <see cref="DeviationMs"/> 同一取整）。</summary>
    public int AttributedNote(double songSec) => (int)Math.Round(songSec / BarSeconds) + 1;

    /// <summary>已过线的 note 数：小节头序号 + 1，不早于 0。</summary>
    public int PassedNoteCount(double songSec) =>
        Math.Max(0, (int)Math.Floor(songSec / BarSeconds) + 1);

    private double NearestBarHead(double songSec) => Math.Round(songSec / BarSeconds) * BarSeconds;
}

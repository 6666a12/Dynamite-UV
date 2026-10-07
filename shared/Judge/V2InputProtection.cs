namespace DynamiteUniverse.Shared.Judge;

/// <summary>
/// 原版 <c>JudgeState</c> 的同帧目标时间锁（社区实现按毫秒取整）。
/// 语义（RE 确认，见 docs/original-judgement-analysis.md §4.3）：
/// 锁只作用于**早侧**候选（note 尚未过点，noteTime ≥ inputTime）；
/// 异时早侧目标被挡，完全同刻的 Note 继续放行（合法多押）；
/// 晚侧候选（note 已过点）不检查锁，也不武装锁；
/// 武装值取本批已结算目标里的**最早**时刻，因此与事件到达顺序无关。
/// EX-Tap（Burst）与 Tap 逐指令一致，同样参与该锁（检查 + 命中武装）。
/// </summary>
public sealed class V2InputProtection
{
    public const int Infinity = 31_415_926;
    public int Xm { get; private set; }
    public int ProbXm { get; private set; }
    public void Reset() { Xm = Infinity; ProbXm = Infinity; }

    /// <summary>
    /// 候选是否可被本批输入使用。晚侧候选一律放行；早侧候选只接受与已武装时刻完全同刻的目标。
    /// </summary>
    public bool CanUse(int noteTimeMs, int inputTimeMs, bool enforceProbability = true)
    {
        var earlySide = noteTimeMs >= inputTimeMs;
        if (earlySide && Xm != Infinity && noteTimeMs != Xm)
            return false;

        return !enforceProbability || !earlySide || noteTimeMs <= ProbXm;
    }

    /// <summary>记录一次已结算的早侧目标（晚侧命中不武装锁）。</summary>
    public void CommitResolved(int noteTimeMs, int inputTimeMs)
    {
        if (noteTimeMs < inputTimeMs)
            return;
        if (noteTimeMs < Xm)
            Xm = noteTimeMs;
    }

    public void ProtectEarlyMiss(int noteTimeMs, int inputTimeMs)
    {
        if (noteTimeMs > inputTimeMs && noteTimeMs < ProbXm) ProbXm = noteTimeMs;
    }

    public static int ToMilliseconds(double seconds) =>
        !double.IsFinite(seconds) ? (seconds < 0 ? int.MinValue : int.MaxValue) :
        (int)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero);
}

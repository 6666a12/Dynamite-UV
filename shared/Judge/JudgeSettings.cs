namespace DynamiteUniverse.Shared.Judge;

/// <summary>判定预设（规格书 §6.1，Hard 及以上所有难度共用 Hard 档）。</summary>
public enum JudgePreset
{
    Casual,
    Normal,
    Hard,
    Tutorial,
    /// <summary>Hardcore 模式：Hard 窗口 × 0.5，由 <see cref="GameplayMode.Hardcore"/> 强制选用。</summary>
    Hardcore,
}

/// <summary>
/// 游玩判定模式（持久化在客户端 GameSettings）。Hardcore 覆盖难度→预设映射，对**任何难度**
/// 强制使用 <see cref="JudgePreset.Hardcore"/>。
/// </summary>
public enum GameplayMode
{
    Standard = 0,
    Hardcore = 1,
    /// <summary>
    /// 预留：Hardcore 的变体（Bleed），具体数值与机制待用户拍板，见
    /// docs/gameplay-spec.md「游玩模式」与 docs/handoff.md §2.2。当前没有任何运行逻辑。
    /// </summary>
    Bleed = 2,
}

/// <summary>
/// 判定参数（规格书 §6.2 资产原值，窗口单位 BarTime）。
/// </summary>
public sealed class JudgeSettings
{
    public required double PrefectBarTime { get; init; }
    public required double GreatBarTime { get; init; }
    public required double GoodBarTime { get; init; }
    public required double MissBarTime { get; init; }
    public required double HoldContactGraceBarTime { get; init; }
    public required double MixerHoldingJudgeBarTime { get; init; }

    public double MinBPM { get; init; } = 120.0;
    public double MaxBPM { get; init; } = 200.0;
    public double StandardBPM { get; init; } = 150.0;

    private double BarToSec(double bar) => bar * 240.0 / StandardBPM;

    /// <summary>普通双侧窗口固定按 StandardBPM=150 换算。</summary>
    public double PrefectSec => BarToSec(PrefectBarTime);
    public double GreatSec => BarToSec(GreatBarTime);
    public double GoodSec => BarToSec(GoodBarTime);
    public double MissSec => BarToSec(MissBarTime);
    /// <summary>Hold 断触宽限在 StandardBPM 下的秒数。</summary>
    public double HoldContactGraceSec => BarToSec(HoldContactGraceBarTime);

    public double HoldContactGraceSeconds(double currentBpm) =>
        HoldingSeconds(HoldContactGraceBarTime, currentBpm);

    private double HoldingSeconds(double barTime, double currentBpm)
    {
        var effectiveBpm = Math.Clamp(currentBpm, MinBPM, MaxBPM);
        return barTime * 240.0 / effectiveBpm;
    }

    /// <summary>
    /// 谱面难度键 -> 原版 JudgeSettings 实例（原版规则：casual→Casual、normal→Normal、
    /// hard 及以上（含 mega/giga/tech/自定义）→Hard；tutorial 取 Casual 数值）。
    /// </summary>
    public static JudgePreset PresetForDifficulty(string? difficulty) =>
        difficulty?.ToLowerInvariant() switch
        {
            "tutorial" => JudgePreset.Tutorial,
            "casual" => JudgePreset.Casual,
            "normal" => JudgePreset.Normal,
            _ => JudgePreset.Hard,
        };

    /// <summary>
    /// 游玩模式覆盖：Hardcore 对任何难度强制 Hardcore 实例；其余（含预留的 Bleed）走难度映射。
    /// </summary>
    public static JudgePreset ResolvePreset(string? difficulty, GameplayMode mode) =>
        mode == GameplayMode.Hardcore
            ? JudgePreset.Hardcore
            : PresetForDifficulty(difficulty);

    public static JudgeSettings ForPreset(JudgePreset preset) => preset switch    {
        JudgePreset.Casual => new JudgeSettings
        {
            PrefectBarTime = 0.0625,
            GreatBarTime = 0.09375,
            GoodBarTime = 0.125,
            MissBarTime = 0.15625,
            HoldContactGraceBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
        JudgePreset.Normal => new JudgeSettings
        {
            PrefectBarTime = 0.0390625,
            GreatBarTime = 0.09375,
            GoodBarTime = 0.125,
            MissBarTime = 0.15625,
            HoldContactGraceBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
        JudgePreset.Hard => new JudgeSettings
        {
            PrefectBarTime = 0.0390625,
            GreatBarTime = 0.0703125,
            GoodBarTime = 0.1015625,
            MissBarTime = 0.15625,
            HoldContactGraceBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
        JudgePreset.Hardcore => new JudgeSettings
        {
            // Hard 窗口 × 0.5：31.25 / 56.25 / 81.25 / 125 ms（×240/150 = ×1.6）。
            // Holding 宽限沿用 Hard 值，见 docs/gameplay-spec.md「游玩模式」。
            PrefectBarTime = 0.01953125,
            GreatBarTime = 0.03515625,
            GoodBarTime = 0.05078125,
            MissBarTime = 0.078125,
            HoldContactGraceBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
        _ => new JudgeSettings // Tutorial
        {
            PrefectBarTime = 0.0625,
            GreatBarTime = 0.09375,
            GoodBarTime = 0.125,
            MissBarTime = 0.15625,
            HoldContactGraceBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
    };
}

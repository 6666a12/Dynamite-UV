namespace DuxShared.Judge;

/// <summary>4 套判定预设（规格书 §6.1，Hard 及以上所有难度共用 Hard 档）。</summary>
public enum JudgePreset
{
    Casual,
    Normal,
    Hard,
    Tutorial,
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
    public required double HoldHoldingJudgeBarTime { get; init; }
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
    /// <summary>Holding interval at StandardBPM; retained for fallback charts/tests.</summary>
    public double HoldHoldingSec => BarToSec(HoldHoldingJudgeBarTime);
    public double MixerHoldingSec => BarToSec(MixerHoldingJudgeBarTime);

    public double HoldHoldingSeconds(double currentBpm) =>
        HoldingSeconds(HoldHoldingJudgeBarTime, currentBpm);

    public double MixerHoldingSeconds(double currentBpm) =>
        HoldingSeconds(MixerHoldingJudgeBarTime, currentBpm);

    private double HoldingSeconds(double barTime, double currentBpm)
    {
        var effectiveBpm = Math.Clamp(currentBpm, MinBPM, MaxBPM);
        return barTime * 240.0 / effectiveBpm;
    }

    public static JudgeSettings ForPreset(JudgePreset preset) => preset switch
    {
        JudgePreset.Casual => new JudgeSettings
        {
            PrefectBarTime = 0.0625,
            GreatBarTime = 0.09375,
            GoodBarTime = 0.125,
            MissBarTime = 0.15625,
            HoldHoldingJudgeBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
        JudgePreset.Normal => new JudgeSettings
        {
            PrefectBarTime = 0.0390625,
            GreatBarTime = 0.09375,
            GoodBarTime = 0.125,
            MissBarTime = 0.15625,
            HoldHoldingJudgeBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
        JudgePreset.Hard => new JudgeSettings
        {
            PrefectBarTime = 0.0390625,
            GreatBarTime = 0.0703125,
            GoodBarTime = 0.1015625,
            MissBarTime = 0.15625,
            HoldHoldingJudgeBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
        _ => new JudgeSettings // Tutorial
        {
            PrefectBarTime = 0.0625,
            GreatBarTime = 0.09375,
            GoodBarTime = 0.125,
            MissBarTime = 0.15625,
            HoldHoldingJudgeBarTime = 0.125,
            MixerHoldingJudgeBarTime = 0.125,
        },
    };
}

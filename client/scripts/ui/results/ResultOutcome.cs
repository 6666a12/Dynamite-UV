using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

/// <summary>结果字标家族。BLEED 耗尽单独一类，不参与判定计数推导。</summary>
public enum ResultOutcomeKind
{
    /// <summary>无有效成绩数据：只有压暗与斜纹区，不显示任何字标。</summary>
    None,
    TrackComplete,
    AllPerfect,
    FullCombo,
    OneMiss,
    OneGood,
    DoubleOne,
    StageFailure,
}

/// <summary>
/// 结算字标与结算页徽章的唯一分类器。全部输入都是本局已冻结的判定计数、
/// 总判定数和 BLEED 耗尽标记；不读取引擎、设置或时间，因此前奏、屏风与结算页
/// 不会各自得到不同结论。
/// </summary>
public static class ResultOutcome
{
    /// <summary>AP 最高；专属字标优先普通 FC；其他正常完成显示 TRACK COMPLETE；BLEED 耗尽一律 STAGE FAILURE。</summary>
    public static ResultOutcomeKind Classify(ScoreRecord record, int totalUnits, bool stageFailure)
    {
        if (stageFailure)
            return ResultOutcomeKind.StageFailure;
        if (record is null || totalUnits <= 0)
            return ResultOutcomeKind.None;
        if (record.Perfect == totalUnits && record.Great == 0 && record.Good == 0 && record.Miss == 0)
            return ResultOutcomeKind.AllPerfect;
        if (record.Good == 1 && record.Miss == 1)
            return ResultOutcomeKind.DoubleOne;
        if (record.Good == 1 && record.Miss == 0)
            return ResultOutcomeKind.OneGood;
        if (record.Miss == 1)
            return ResultOutcomeKind.OneMiss;
        if (record.Miss == 0 && record.MaxCombo >= totalUnits)
            return ResultOutcomeKind.FullCombo;
        return ResultOutcomeKind.TrackComplete;
    }

    /// <summary>字标资产名（<c>client/assets/results/outcomes/&lt;slug&gt;.png</c>）。</summary>
    public static string AssetSlug(ResultOutcomeKind kind) => kind switch
    {
        ResultOutcomeKind.TrackComplete => "track-complete",
        ResultOutcomeKind.AllPerfect => "all-perfect",
        ResultOutcomeKind.FullCombo => "full-combo",
        ResultOutcomeKind.OneMiss => "one-miss",
        ResultOutcomeKind.OneGood => "one-good",
        ResultOutcomeKind.DoubleOne => "double-one",
        ResultOutcomeKind.StageFailure => "stage-failure",
        _ => "",
    };

    public static bool HasWordmark(ResultOutcomeKind kind) => kind != ResultOutcomeKind.None;

    /// <summary>
    /// 结算页成就徽章位：AP 优先，其次 FC。OneGood 不改写为 FC 以外的东西——它没有 Miss
    /// 且满连，本就属于 FC 家族；OneMiss/DoubleOne 已断连，STAGE FAILURE 不显示任何 AP/FC 徽章，
    /// 以免与刚播放的字标矛盾。
    /// </summary>
    public static ResultOutcomeKind BadgeOf(ResultOutcomeKind kind) => kind switch
    {
        ResultOutcomeKind.AllPerfect => ResultOutcomeKind.AllPerfect,
        ResultOutcomeKind.FullCombo or ResultOutcomeKind.OneGood => ResultOutcomeKind.FullCombo,
        _ => ResultOutcomeKind.None,
    };
}

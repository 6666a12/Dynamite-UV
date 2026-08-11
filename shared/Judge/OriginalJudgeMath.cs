namespace DuxShared.Judge;

/// <summary>
/// Confirmed original-game math kept as an optional policy. DUX-Community does
/// not wire these functions into its approved million-score/main-combo display.
/// </summary>
public static class OriginalJudgeMath
{
    public static double ComboMultiplier(int comboBeforeHit) =>
        1.0 + 0.5 * Math.Clamp(comboBeforeHit / 300.0, 0.0, 1.0);

    public static int RawScoreDelta(int baseJudgeScore, int comboBeforeHit,
        double buffScoreFactor = 1.0) =>
        (int)Math.Floor(baseJudgeScore * 10.0 *
            ComboMultiplier(comboBeforeHit) * Math.Max(0.0, buffScoreFactor));

    /// <summary>Returns the original CLEAR value in the 0..10000 integer scale.</summary>
    public static int ClearPercent100(int prefectMain, int greatMain, int goodMain,
        int totalMainNote)
    {
        if (totalMainNote <= 0)
            return 0;
        if (prefectMain >= totalMainNote)
            return 10000;
        var weighted = prefectMain + 0.699999988 * greatMain + 0.5 * goodMain;
        return Math.Clamp((int)Math.Truncate(weighted * 10000.0 / totalMainNote),
            0, 10000);
    }

    public static JudgeGrade MixerEndGrade(int holdingHit, int holdingTotal)
    {
        var rate = holdingTotal <= 0 ? 0.0 : (double)holdingHit / holdingTotal;
        return rate >= 1.0 ? JudgeGrade.Prefect :
            rate >= 0.699999988 ? JudgeGrade.Great :
            rate >= 0.5 ? JudgeGrade.Good : JudgeGrade.Miss;
    }
}

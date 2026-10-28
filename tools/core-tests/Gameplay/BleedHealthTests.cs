using DynamiteUniverse.Shared.Judge;

namespace CoreTests;

public static class BleedHealthTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("[21] BLEED health rules and depletion");

        foreach (var (grade, delta) in new (JudgeGrade, double)[]
                 {
                     (JudgeGrade.Miss, -2.0), (JudgeGrade.Perfect, 0.2),
                     (JudgeGrade.Great, 0.05), (JudgeGrade.Good, 0.05),
                 })
            check(BleedHealth.DeltaOf(BleedRule.Standard, grade) == delta,
                $"standard {grade} delta is {delta}");

        foreach (var (grade, delta) in new (JudgeGrade, double)[]
                 {
                     (JudgeGrade.Miss, -4.0), (JudgeGrade.Perfect, 0.1),
                     (JudgeGrade.Great, 0.0), (JudgeGrade.Good, 0.0),
                 })
            check(BleedHealth.DeltaOf(BleedRule.Hardcore, grade) == delta,
                $"hardcore {grade} delta is {delta}");

        var standard = new BleedHealth(BleedRule.Standard);
        check(standard.Health == BleedHealth.MaxHealth && !standard.IsDepleted,
            "standard starts at full health");
        standard.Apply(JudgeGrade.Miss);
        standard.Apply(JudgeGrade.Perfect);
        standard.Apply(JudgeGrade.Great);
        standard.Apply(JudgeGrade.Good);
        check(Math.Abs(standard.Health - 98.3) < 1e-9, "standard applies -2+0.2+0.05+0.05");

        var standardFull = new BleedHealth(BleedRule.Standard);
        standardFull.Apply(JudgeGrade.Perfect);
        check(standardFull.Health == BleedHealth.MaxHealth, "standard heal caps at 100");

        var hardcoreFull = new BleedHealth(BleedRule.Hardcore);
        hardcoreFull.Apply(JudgeGrade.Perfect);
        check(hardcoreFull.Health == BleedHealth.MaxHealth, "hardcore heal caps at 100");

        var standardDepleted = new BleedHealth(BleedRule.Standard);
        for (var i = 0; i < 49; i++)
            standardDepleted.Apply(JudgeGrade.Miss);
        check(!standardDepleted.IsDepleted && standardDepleted.Health > 0,
            "49 standard misses do not deplete");
        standardDepleted.Apply(JudgeGrade.Miss);
        check(standardDepleted.IsDepleted && standardDepleted.Health == 0.0,
            "50 standard misses deplete and clamp to 0");

        var hardcoreDepleted = new BleedHealth(BleedRule.Hardcore);
        for (var i = 0; i < 25; i++)
            hardcoreDepleted.Apply(JudgeGrade.Miss);
        check(hardcoreDepleted.IsDepleted && hardcoreDepleted.Health == 0.0,
            "25 hardcore misses deplete and clamp to 0");

        standardDepleted.Apply(JudgeGrade.Perfect);
        standardDepleted.Apply(JudgeGrade.Miss);
        check(standardDepleted.IsDepleted && standardDepleted.Health == 0.0,
            "depleted health ignores later judgements");
    }
}

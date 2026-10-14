using DynamiteUniverse.Shared.Score;

namespace CoreTests;

public static class GradeTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("[19] Eight-grade boundaries and historical score regrading");
        foreach (var (percent, grade) in new (double, string)[]
        {
            (0, "D"), (59.9999, "D"), (60, "C"), (79.9999, "C"),
            (80, "B"), (89.9999, "B"), (90, "A"), (94.9999, "A"),
            (95, "S"), (96.9999, "S"), (97, "X"), (98, "X"), (98.9999, "X"),
            (99, "Y"), (99.999999, "Y"), (100, "Ω"),
        }) check(ScoreStoreCore.GradeOf(percent) == grade, $"{percent:R}% -> {grade}");
        check(ScoreStoreCore.GradeOf(Math.BitDecrement(100.0)) == "Y",
            "display rounding to 100.00% cannot award Omega");
        check(ScoreStoreCore.GradeOf(double.NaN) == "D" &&
              ScoreStoreCore.GradeOf(double.PositiveInfinity) == "D",
            "non-finite accuracy cannot award Omega");
        check(ScoreStoreCore.GradeLabel("Y") == "Ψ" && ScoreStoreCore.GradeAssetKey("Y") == "Y",
            "Psi uses Y storage/asset key and Psi display label");
        var store = new ScoreStoreCore();
        store.LoadJson("""
            {"pack:hard":{"score":980000,"acc":98,"grade":"Ω","maxCombo":98,
            "perfect":98,"great":0,"good":0,"miss":2,"date":"2026-10-01 12:00:00"}}
            """);
        var historical = store.GetLegacy("pack", "hard")!;
        check(historical.Grade == "X" && historical.Score == 980000 && historical.Miss == 2 &&
              historical.Date == "2026-10-01 12:00:00", "legacy regrade preserves score, judgments and date");
        var identity = new ScoreIdentity("pack", "hard", "rules", new string('a', 64));
        store.TryUpdate(identity, new ScoreRecord { Score = 995000, Acc = 99.5 });
        store.Get(identity)!.Grade = "Ω"; // Simulate an old versioned file with the old 98% threshold.
        var reloaded = new ScoreStoreCore();
        reloaded.LoadJson(store.SaveJson());
        check(reloaded.Get(identity)?.Grade == "Y" && reloaded.GetLegacy("pack", "hard")?.Grade == "X",
            "versioned current and historical records both regrade on load");
        var roundTrip = new ScoreStoreCore();
        roundTrip.LoadJson(reloaded.SaveJson());
        check(roundTrip.SaveJson() == reloaded.SaveJson(), "regrading is stable across save/reload");
    }
}

using DynamiteUniverse.Shared.Score;
using DynamiteUniverse.Ui;

namespace CoreTests;

public static class ResultOutcomeTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("[23] Result outcome classification and badge parity");

        const int total = 1000;
        check(ResultOutcome.Classify(Record(total, 0, 0, 0, total), total, false) == ResultOutcomeKind.AllPerfect,
            "all perfect");
        check(ResultOutcome.Classify(Record(990, 10, 0, 0, total), total, false) == ResultOutcomeKind.FullCombo,
            "full combo");
        check(ResultOutcome.Classify(Record(999, 0, 0, 1, total), total, false) == ResultOutcomeKind.OneMiss,
            "one miss");
        check(ResultOutcome.Classify(Record(999, 0, 1, 0, total), total, false) == ResultOutcomeKind.OneGood,
            "one good");
        check(ResultOutcome.Classify(Record(998, 0, 1, 1, total), total, false) == ResultOutcomeKind.DoubleOne,
            "one good + one miss is double one");
        check(ResultOutcome.Classify(Record(990, 0, 0, 2, total), total, false) == ResultOutcomeKind.TrackComplete,
            "ordinary completion has a track complete wordmark");
        check(ResultOutcome.Classify(Record(900, 100, 0, 0, total - 5), total, false) == ResultOutcomeKind.TrackComplete,
            "combo below the chart total still completes the track");
        check(ResultOutcome.Classify(Record(0, 0, 0, 0, 0), 0, false) == ResultOutcomeKind.None,
            "empty chart cannot produce a wordmark");

        // 优先级：AP 最高，专属字标优先普通 FC，BLEED 耗尽单独一类。
        check(ResultOutcome.Classify(Record(total, 0, 0, 0, total), total, false) == ResultOutcomeKind.AllPerfect,
            "AP wins over every other family");
        check(ResultOutcome.Classify(Record(998, 0, 1, 1, total), total, false) == ResultOutcomeKind.DoubleOne,
            "double one wins over one miss");
        check(ResultOutcome.Classify(Record(999, 0, 1, 0, total), total, false) == ResultOutcomeKind.OneGood,
            "one good wins over full combo");
        check(ResultOutcome.Classify(Record(total, 0, 0, 0, total), total, true) == ResultOutcomeKind.StageFailure,
            "BLEED depletion is always a stage failure");

        // 徽章与字标同源：失败和断连结果不显示 AP/FC。
        check(ResultOutcome.BadgeOf(ResultOutcomeKind.AllPerfect) == ResultOutcomeKind.AllPerfect,
            "AP badge");
        check(ResultOutcome.BadgeOf(ResultOutcomeKind.FullCombo) == ResultOutcomeKind.FullCombo,
            "FC badge");
        check(ResultOutcome.BadgeOf(ResultOutcomeKind.OneGood) == ResultOutcomeKind.FullCombo,
            "one good still holds a full combo");
        check(ResultOutcome.BadgeOf(ResultOutcomeKind.TrackComplete) == ResultOutcomeKind.None,
            "track complete does not invent an AP/FC badge");
        check(ResultOutcome.BadgeOf(ResultOutcomeKind.OneMiss) == ResultOutcomeKind.None,
            "one miss shows no AP/FC badge");
        check(ResultOutcome.BadgeOf(ResultOutcomeKind.DoubleOne) == ResultOutcomeKind.None,
            "double one shows no AP/FC badge");
        check(ResultOutcome.BadgeOf(ResultOutcomeKind.StageFailure) == ResultOutcomeKind.None,
            "stage failure never contradicts itself with an AP/FC badge");

        var slugs = new (ResultOutcomeKind Kind, string Slug)[]
        {
            (ResultOutcomeKind.TrackComplete, "track-complete"),
            (ResultOutcomeKind.AllPerfect, "all-perfect"),
            (ResultOutcomeKind.FullCombo, "full-combo"),
            (ResultOutcomeKind.OneMiss, "one-miss"),
            (ResultOutcomeKind.OneGood, "one-good"),
            (ResultOutcomeKind.DoubleOne, "double-one"),
            (ResultOutcomeKind.StageFailure, "stage-failure"),
        };
        foreach (var (kind, slug) in slugs)
        {
            check(ResultOutcome.AssetSlug(kind) == slug, $"{kind} maps to {slug}.png");
            check(ResultOutcome.HasWordmark(kind), $"{kind} has a wordmark");
        }
        check(!ResultOutcome.HasWordmark(ResultOutcomeKind.None) && ResultOutcome.AssetSlug(ResultOutcomeKind.None) == "",
            "invalid result data has no sheet");
    }

    private static ScoreRecord Record(int perfect, int great, int good, int miss, int maxCombo) => new()
    {
        Perfect = perfect,
        Great = great,
        Good = good,
        Miss = miss,
        MaxCombo = maxCombo,
    };
}

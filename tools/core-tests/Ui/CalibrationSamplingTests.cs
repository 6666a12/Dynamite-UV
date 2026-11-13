using DynamiteUniverse.Ui;

namespace CoreTests;

internal static class CalibrationSamplingTests
{
    /// <summary>校准页的 note 过线间隔：与 <c>CalibrationBeatSource.BarSeconds</c> 同值。</summary>
    private const double Bar = 2.0;

    public static void Run(Action<bool, string> check)
    {
        var repeated = New();
        repeated.Hit(0.02);
        repeated.Hit(4.02);
        check(repeated.Hit(4.5).Outcome == HitOutcome.Stray && repeated.Count == 1,
            "out-of-window duplicate is stray and does not append a sample");
        var clock = new LoopPositionUnwrapper(2);
        check(clock.Advance(1) == 1 && clock.Advance(.995) == 1 && clock.Advance(1.01) == 1.01,
            "loop clock clamps small backward jitter without delaying forward progress");
        clock.Advance(1.99);
        check(Math.Abs(clock.Advance(.01) - 2.01) < 1e-9, "loop clock still unwraps actual wrap");
        FirstTapWarmsUp(check);
        WarmupEnds(check);
        HalfBarAttribution(check);
        DuplicateNote(check);
        StrayTaps(check);
        CompletesAtTarget(check);
        CompletedIgnoresHits(check);
        TargetLockedWhileSampling(check);
        RestartKeepsTarget(check);
        DeviationWindow(check);
        Console.WriteLine("  Calibration sampling checks complete.");
    }

    private static CalibrationSampling New(int target = CalibrationStats.DefaultTargetSamples) =>
        new(Bar, target);

    /// <summary>把采样推进到 Complete：首击热身，其后每颗 note 一次 +20ms 的入样。</summary>
    private static CalibrationSampling Completed(int target)
    {
        var sampling = New(target);
        sampling.Hit(0.05);
        AcceptNotes(sampling, 3, target + 2);
        return sampling;
    }

    private static void AcceptNotes(CalibrationSampling sampling, int fromNote, int toNote)
    {
        for (var note = fromNote; note <= toNote; note++)
            sampling.Hit(Head(note) + 0.02);
    }

    /// <summary>第 N 颗 note 的过线时刻（第 1 颗在 0s）。</summary>
    private static double Head(int note) => (note - 1) * Bar;

    private static void FirstTapWarmsUp(Action<bool, string> check)
    {
        var sampling = New();
        check(sampling.Phase == SamplingPhase.Ready && sampling.Count == 0,
            "a fresh sampler is ready and holds no samples");

        var first = sampling.Hit(0.05);
        check(first.Outcome == HitOutcome.Warmup,
            "the first tap of a ready sampler only warms up");
        check(first.AttributedNote == 1, "the first tap belongs to the note passing now");
        check(Math.Abs(first.DeviationMs - 50.0) < 1e-6, "a late tap reports a positive deviation");
        check(sampling.Phase == SamplingPhase.Sampling,
            "the first tap moves the sampler into sampling");
        check(sampling.WarmupBaseNote == 1 && sampling.Count == 0,
            "the first tap sets the warmup base and never becomes a sample");

        // 正好压在小节头上的击打归属当下这颗 note（首击即热身第 1 颗）。
        var onBeat = New();
        check(onBeat.Hit(Head(3)).Outcome == HitOutcome.Warmup,
            "a tap exactly on the beat warms up the note passing now");
        check(onBeat.WarmupBaseNote == 3, "the base note is the one that just passed");
    }

    private static void WarmupEnds(Action<bool, string> check)
    {
        check(CalibrationSampling.WarmupNotes == 2, "the warmup lasts exactly two notes");

        var sampling = New();
        sampling.Hit(0.05);

        var second = sampling.Hit(Head(2) + 0.02);
        check(second.Outcome == HitOutcome.Warmup && second.AttributedNote == 2,
            "the second warmup note is still not sampled");
        check(sampling.Count == 0, "warmup taps never become samples");

        var third = sampling.Hit(Head(3) + 0.02);
        check(third.Outcome == HitOutcome.Accepted && third.AttributedNote == 3,
            "the third note starts sampling");
        check(sampling.Count == 1 && sampling.LastSampledNote == 3,
            "the accepted tap is recorded against its note");
        check(Math.Abs(sampling.Stats.Samples[0] - 20.0) < 1e-6,
            "the sample keeps the standalone deviation of that tap");
    }

    private static void HalfBarAttribution(Action<bool, string> check)
    {
        var sampling = New();
        sampling.Hit(Head(3) + 0.05);
        check(sampling.WarmupBaseNote == 3, "sampling starts from the note that just passed");
        check(sampling.Hit(Head(4) + 0.02).Outcome == HitOutcome.Warmup,
            "the note after the base note is the second warmup");

        // 落在小节尾、提前 20ms 的击打属于**下一颗** note（最近小节头），因此已经过了热身。
        var early = sampling.Hit(Head(5) - 0.02);
        check(early.Outcome == HitOutcome.Accepted && early.AttributedNote == 5,
            "a tap in the last 20ms of a bar belongs to the next note");
        check(Math.Abs(early.DeviationMs + 20.0) < 1e-6, "an early tap reports a negative deviation");
        check(sampling.Count == 1 && sampling.LastSampledNote == 5,
            "the tail-of-bar tap counts as a normal sample");

        // 归属取整与偏差同源：半小节处的中点用默认银行家舍入。
        check(sampling.AttributedNote(3.0) == 3, "a half-bar tie rounds to the even bar index upward");
        check(sampling.AttributedNote(1.0) == 1, "the other tie rounds down to the even bar index");
    }

    private static void DuplicateNote(Action<bool, string> check)
    {
        var sampling = New();
        sampling.Hit(0.05);
        sampling.Hit(Head(2) + 0.02);
        check(sampling.Hit(Head(3) + 0.02).Outcome == HitOutcome.Accepted,
            "note 3 is sampled once");

        var repeat = sampling.Hit(Head(3) + 0.06);
        check(repeat.Outcome == HitOutcome.Duplicate,
            "a second tap on the same note is a duplicate");
        check(Math.Abs(repeat.DeviationMs - 60.0) < 1e-6,
            "the duplicate still reports its own deviation for the readout");
        check(sampling.Count == 1 && sampling.LastSampledNote == 3,
            "duplicates never enter the samples");
        check(Math.Abs(sampling.Stats.Samples[0] - 20.0) < 1e-6,
            "the sample that stays is the first tap on that note");
        check(sampling.Phase == SamplingPhase.Sampling, "a duplicate does not finish sampling");
    }

    private static void StrayTaps(Action<bool, string> check)
    {
        var sampling = New();
        sampling.Hit(0.05);

        var inWarmup = sampling.Hit(Head(2) - 0.8);
        check(inWarmup.Outcome == HitOutcome.Stray,
            "a warmup tap far from the beat is a stray tap");
        check(sampling.Count == 0, "stray warmup taps never become samples");

        var afterWarmup = sampling.Hit(Head(3) + 0.5);
        check(afterWarmup.Outcome == HitOutcome.Stray && afterWarmup.AttributedNote == 3,
            "a tap far from the beat is a stray tap instead of a sample");
        check(sampling.Count == 0 && sampling.LastSampledNote == -1,
            "stray taps never enter the samples");
        check(sampling.Phase == SamplingPhase.Sampling, "stray taps do not finish sampling");
    }

    private static void CompletesAtTarget(Action<bool, string> check)
    {
        var sampling = New(8);
        sampling.Hit(0.05);
        AcceptNotes(sampling, 3, 9);
        check(sampling.Count == 7 && sampling.Phase == SamplingPhase.Sampling,
            "sampling stays open one sample short of the target");

        var last = sampling.Hit(Head(10) + 0.02);
        check(last.Outcome == HitOutcome.Accepted && sampling.Count == 8,
            "the target sample is still accepted");
        check(sampling.Phase == SamplingPhase.Complete && sampling.IsComplete,
            "reaching the target completes the phase");
    }

    private static void CompletedIgnoresHits(Action<bool, string> check)
    {
        var sampling = Completed(8);
        check(sampling.IsComplete, "the fixture sampler is complete");

        var ignored = sampling.Hit(Head(11) + 0.02);
        check(ignored.Outcome == HitOutcome.Ignored, "hits after completion are ignored");
        check(sampling.Count == 8 && sampling.LastSampledNote == 10,
            "ignored hits change neither samples nor the note bookkeeping");
        check(sampling.Phase == SamplingPhase.Complete, "ignored hits keep the phase complete");
    }

    private static void TargetLockedWhileSampling(Action<bool, string> check)
    {
        var sampling = New(16);
        check(sampling.TargetSamples == 16, "the target starts from the requested value");
        check(sampling.TrySetTargetSamples(20) && sampling.TargetSamples == 20,
            "the target can change while ready");
        check(sampling.TrySetTargetSamples(20) && sampling.TargetSamples == 20,
            "an unchanged target is still accepted while ready");
        check(sampling.TrySetTargetSamples(37) && sampling.TargetSamples == 32,
            "the target snaps into the 8-32 range");

        sampling.Hit(0.05);
        check(!sampling.TrySetTargetSamples(8), "the target is locked once sampling starts");
        check(sampling.TargetSamples == 32, "the locked target keeps its value");
    }

    private static void RestartKeepsTarget(Action<bool, string> check)
    {
        var sampling = Completed(8);
        check(sampling.TargetSamples == 8, "the completed sampler remembers its target");

        sampling.Restart();
        check(sampling.Phase == SamplingPhase.Ready, "retry returns to ready");
        check(sampling.Count == 0 && sampling.Stats.MedianMs is null,
            "retry clears every sample");
        check(sampling.TargetSamples == 8, "retry keeps the target");
        check(sampling.LastSampledNote == -1 && sampling.WarmupBaseNote == 0,
            "retry clears the note bookkeeping");
        check(sampling.TrySetTargetSamples(12) && sampling.TargetSamples == 12,
            "retry unlocks the target");

        var again = sampling.Hit(0.05);
        check(again.Outcome == HitOutcome.Warmup && sampling.Count == 0,
            "the first tap after retry warms up again");
    }

    private static void DeviationWindow(Action<bool, string> check)
    {
        // 窗口本身由 CalibrationStats 的 ±150ms 定义（含边界，见 CalibrationStatsTests）；
        // 采样层只做 |偏差| > 150ms 的拒绝，没有更窄的窗口。
        var sampling = New();
        sampling.Hit(0.05);

        var earlyEdge = sampling.Hit(Head(3) - 0.15);
        check(earlyEdge.Outcome == HitOutcome.Accepted && Math.Abs(earlyEdge.DeviationMs + 150.0) < 1e-6,
            "a 150ms-early tap is still inside the accepted window");

        var lateEdge = sampling.Hit(Head(4) + 0.149);
        check(lateEdge.Outcome == HitOutcome.Accepted && Math.Abs(lateEdge.DeviationMs - 149.0) < 1e-6,
            "a 149ms-late tap is inside the accepted window");

        check(sampling.Hit(Head(5) - 0.151).Outcome == HitOutcome.Stray,
            "a 151ms-early tap is outside the accepted window");
        check(sampling.Hit(Head(6) + 0.151).Outcome == HitOutcome.Stray,
            "a 151ms-late tap is outside the accepted window");
    }
}

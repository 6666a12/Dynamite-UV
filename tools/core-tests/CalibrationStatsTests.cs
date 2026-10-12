using DynamiteUniverse.Ui;

namespace CoreTests;

internal static class CalibrationStatsTests
{
    public static void Run(Action<bool, string> check)
    {
        Median(check);
        Spread(check);
        OutlierRejection(check);
        Quantize(check);
        Clamp(check);
        InsufficientSamples(check);
        TargetSamples(check);
        LoopUnwrap(check);
        Console.WriteLine("  Calibration statistics checks complete.");
    }

    private static void Median(Action<bool, string> check)
    {
        var odd = new CalibrationStats();
        foreach (var value in new[] { 40.0, -10.0, 9.0 })
            check(odd.TryAdd(value), "in-window sample is accepted");
        check(odd.MedianMs == 9.0, "odd sample count takes the middle value");

        var even = new CalibrationStats();
        foreach (var value in new[] { 40.0, -10.0, 9.0, 14.0 })
            check(even.TryAdd(value), "in-window sample is accepted");
        check(Math.Abs(even.MedianMs!.Value - 11.5) < 1e-9,
            "even sample count averages the two middle values");

        var boundary = new CalibrationStats();
        check(boundary.TryAdd(150.0) && boundary.TryAdd(-150.0),
            "exactly ±150ms is inside the accepted window");
        check(boundary.MedianMs == 0.0, "±150ms samples median to zero");
    }

    private static void Spread(Action<bool, string> check)
    {
        var stats = new CalibrationStats();
        foreach (var value in new[] { -2.0, 2.0 })
            stats.TryAdd(value);
        check(Math.Abs(stats.SpreadMs!.Value - 2.0) < 1e-9,
            "spread is the population standard deviation");
        check(stats.HasSpread, "two samples define a spread");

        var flat = new CalibrationStats();
        flat.TryAdd(7.0);
        flat.TryAdd(7.0);
        check(flat.SpreadMs == 0.0, "identical samples have zero spread");
    }

    private static void OutlierRejection(Action<bool, string> check)
    {
        var stats = new CalibrationStats();
        check(!stats.TryAdd(151.0), "late stray tap beyond 150ms is rejected");
        check(!stats.TryAdd(-151.0), "early stray tap beyond 150ms is rejected");
        check(!stats.TryAdd(double.NaN), "non-finite deviation is rejected");
        check(!stats.TryAdd(double.PositiveInfinity), "infinite deviation is rejected");
        check(stats.Count == 0 && stats.MedianMs is null && stats.SpreadMs is null,
            "rejected samples never enter the statistics");

        check(stats.TryAdd(12.0) && stats.Count == 1, "accepted sample is retained");
        stats.Clear();
        check(stats.Count == 0 && stats.MedianMs is null, "clear drops every sample");
    }

    private static void Quantize(Action<bool, string> check)
    {
        check(CalibrationStats.QuantizeOffset(21.0) == 20.0, "exact step is unchanged");
        check(CalibrationStats.QuantizeOffset(22.0) == 20.0, "below the half step rounds down");
        check(CalibrationStats.QuantizeOffset(23.0) == 25.0, "above the half step rounds up");
        check(CalibrationStats.QuantizeOffset(2.5) == 5.0 && CalibrationStats.QuantizeOffset(-2.5) == -5.0,
            "exact half step rounds away from zero on both sides");
        check(CalibrationStats.QuantizeOffset(-22.0) == -20.0 &&
              CalibrationStats.QuantizeOffset(-23.0) == -25.0,
            "negative deviations use the same step direction");
        check(CalibrationStats.QuantizeOffset(0.0) == 0.0, "zero stays zero");
        check(Math.Abs(CalibrationStats.QuantizeOffset(21.0) % 5.0) < 1e-9,
            "quantised value is a multiple of the step");
    }

    private static void Clamp(Action<bool, string> check)
    {
        // 修正方向是「减」：判定时钟含 +UserOffsetMs，打得晚（正中位数）要把偏移往回拉。
        check(CalibrationStats.SuggestedOffsetMs(0.0, 21.0) == -20,
            "suggestion subtracts the median, rounded to 5ms");
        check(CalibrationStats.SuggestedOffsetMs(-285.0, 40.0) == -300,
            "late hits on a negative offset clamp at -300ms");
        check(CalibrationStats.SuggestedOffsetMs(285.0, -40.0) == 300,
            "early hits on a positive offset clamp at +300ms");
        check(CalibrationStats.SuggestedOffsetMs(290.0, 50.0) == 240,
            "in-range suggestion is untouched by clamping");
        check(CalibrationStats.SuggestedOffsetMs(37.0, double.NaN) == 35,
            "non-finite median falls back to the current offset");
        check(CalibrationStats.ClampOffset(300.4) == 300 && CalibrationStats.ClampOffset(-300.4) == -300,
            "clamp limit is inclusive");
    }

    private static void InsufficientSamples(Action<bool, string> check)
    {
        var stats = new CalibrationStats();
        check(stats.MedianMs is null && stats.SpreadMs is null && !stats.HasSpread,
            "no samples means no median and no spread");
        check(stats.SuggestedOffset(0.0) is null,
            "no samples means no suggested offset");

        stats.TryAdd(12.0);
        check(stats.MedianMs == 12.0, "a single sample still has a median");
        check(stats.SpreadMs is null && !stats.HasSpread,
            "a single sample has no defined spread");
        check(stats.SuggestedOffset(0.0) == -10,
            "a single sample already yields a quantised suggestion");
    }

    private static void TargetSamples(Action<bool, string> check)
    {
        check(CalibrationStats.DefaultTargetSamples == 16, "default target is 16 samples");
        check(CalibrationStats.NormalizeTargetSamples(0) == 8, "below range clamps to 8");
        check(CalibrationStats.NormalizeTargetSamples(999) == 32, "above range clamps to 32");
        check(CalibrationStats.NormalizeTargetSamples(11) == 12, "value snaps to the nearest step");
        check(CalibrationStats.NormalizeTargetSamples(10) == 12,
            "exact half step snaps upward");
        check(CalibrationStats.NormalizeTargetSamples(16) == 16, "valid steps are preserved");
        check(CalibrationStats.NormalizeTargetSamples(31) == 32, "31 snaps to the top step");
    }

    private static void LoopUnwrap(Action<bool, string> check)
    {
        const double loop = 2.0;

        var noWrap = new LoopPositionUnwrapper(loop);
        check(Math.Abs(noWrap.Advance(0.30) - 0.30) < 1e-9, "first sample passes through");
        check(Math.Abs(noWrap.Advance(1.85) - 1.85) < 1e-9, "monotonic within one loop");

        var wrapped = new LoopPositionUnwrapper(loop);
        wrapped.Advance(1.98);
        check(Math.Abs(wrapped.Advance(0.02) - 2.02) < 1e-9, "wrap extends the timeline by one loop");
        check(Math.Abs(wrapped.Advance(1.90) - 3.90) < 1e-9, "post-wrap values stay on the new lap");
        check(Math.Abs(wrapped.Advance(0.05) - 4.05) < 1e-9, "second wrap keeps accumulating");

        // 混音补偿在两次调用间归零会造成圈内几毫秒的回退，这不算回卷。
        var jitter = new LoopPositionUnwrapper(loop);
        jitter.Advance(1.000);
        check(Math.Abs(jitter.Advance(0.995) - 0.995) < 1e-9,
            "small in-loop backward jitter is not a wrap");
        check(Math.Abs(jitter.Advance(1.10) - 1.10) < 1e-9, "jitter does not corrupt the timeline");

        // 回卷瞬间 raw 可能被 GetTimeSinceLastMix 推过圈长（如 2.01），回卷后仍要识别。
        var overshoot = new LoopPositionUnwrapper(loop);
        overshoot.Advance(2.01);
        check(Math.Abs(overshoot.Advance(0.03) - 2.03) < 1e-9,
            "wrap is detected even when the pre-wrap value overshoots the loop length");
    }
}

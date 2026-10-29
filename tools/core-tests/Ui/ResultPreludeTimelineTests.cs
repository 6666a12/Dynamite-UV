using DynamiteUniverse.Ui;

namespace CoreTests;

/// <summary>
/// 结果字标前奏的三档动效与跳过语义。时间常数来自独立预览
/// <c>design/result-outcomes/surge/godot/preview.gd</c>（1.55s 前奏、0.20–0.60s 拍入、
/// 0.52s 命中辉光、0.60s 后能量衰减）。
/// </summary>
public static class ResultPreludeTimelineTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("[24] Result prelude timeline, motion modes and skip");

        FullWithWordmark(check);
        FullPlain(check);
        Reduced(check);
        Off(check);
        SkipAndInvalidDeltas(check);
        RunSequence(check);
    }

    private static void FullWithWordmark(Action<bool, string> check)
    {
        var full = new ResultPreludeTimeline(UiMotionMode.Full, true);
        check(full.Duration == ResultPreludeTimeline.MarkDuration && ResultPreludeTimeline.MarkDuration == 1.55,
            "Full prelude keeps the preview's 1.55s hand-off");
        check(full.HasWordmark && ResultPreludeTimeline.RunsFor(UiMotionMode.Full), "Full plays the wordmark");
        check(full.DimAlpha == 0 && full.BandAlpha == 0 && full.MarkAlpha == 0,
            "prelude starts from untouched gameplay");
        check(Math.Abs(full.MarkScale - 1.58f) < 0.001f, "wordmark starts oversized (1.58)");

        full.Advance(0.19);
        check(full.MarkAlpha == 0, "wordmark has not appeared before 0.20s");
        check(full.BandAlpha > 0.6 && full.BandAlpha < 0.75, "band sweeps in before the stamp");
        check(full.DimAlpha > 0.5 && full.DimAlpha < 0.6, "dim ramps over 0.30s");

        full.Advance(0.09); // 0.28s
        check(full.BandAlpha > 0.99, "band is fully in by 0.28s");
        check(full.DimAlpha > 0.78, "dim reaches its 0.80 target by 0.30s");
        check(full.MarkAlpha > 0.9, "wordmark fades in inside the stamp window");

        full.Advance(0.24); // 0.52s
        check(full.MarkAlpha > 0.99, "wordmark is fully opaque at the impact frame");
        check(full.Impact > 0.9f, "impact peaks at 0.52s");
        check(full.Glow > ResultPreludeTimeline.GlowBase, "impact brightens the wordmark halo");
        check(full.Energy > 2.0f, "energy peaks with the stamp");

        full.Advance(0.18); // 0.70s: 已过 0.60s 能量衰减起点
        check(full.Energy < 2.0f && full.Energy > 1.5f, "energy decays after 0.60s");
        check(Math.Abs(full.MarkScale - 1f) < 0.02f, "wordmark settles at scale 1");

        full.Advance(0.80);
        check(!full.IsComplete && full.Energy < 1.2f, "short hold keeps a settled wordmark before the shutter");
        full.Advance(0.05);
        check(full.IsComplete, "prelude hands off at 1.55s");
        check(full.MarkAlpha == 1 && Math.Abs(full.MarkScale - 1f) < 1e-4f && Math.Abs(full.Energy - 1f) < 0.01f,
            "hand-off state is the stable wordmark");
    }

    private static void FullPlain(Action<bool, string> check)
    {
        var plain = new ResultPreludeTimeline(UiMotionMode.Full, false);
        check(plain.Duration == ResultPreludeTimeline.PlainDuration && plain.Duration < ResultPreludeTimeline.MarkDuration,
            "ordinary result uses the shorter dim + band prelude");
        plain.Advance(0.28);
        check(plain.BandAlpha > 0.99 && plain.DimAlpha > 0.78, "ordinary result still dims and shows the band");
        check(!plain.HasWordmark && plain.MarkAlpha == 0 && plain.Impact == 0,
            "ordinary result never shows a wordmark");
        plain.Advance(0.40);
        check(plain.IsComplete, "ordinary prelude completes after a short hold");
    }

    private static void Reduced(Action<bool, string> check)
    {
        var reduced = new ResultPreludeTimeline(UiMotionMode.Reduced, true);
        check(reduced.Duration == ResultPreludeTimeline.ReducedDuration, "Reduced uses the 260ms short fade");
        reduced.Advance(0.13);
        check(reduced.MarkAlpha > 0.4 && reduced.MarkAlpha < 0.6, "Reduced fades the wordmark in place");
        check(reduced.MarkScale == 1f, "Reduced has no plunge or directional scale");
        check(reduced.Impact == 0f && reduced.Energy == ResultPreludeTimeline.EnergyFloor,
            "Reduced has no impact flash");
        check(reduced.Glow == ResultPreludeTimeline.GlowBase, "Reduced holds a static halo");
        reduced.Advance(0.13);
        check(reduced.IsComplete && reduced.MarkAlpha == 1 && reduced.DimAlpha > 0.78,
            "Reduced ends on the same stable frame");
    }

    private static void Off(Action<bool, string> check)
    {
        var off = new ResultPreludeTimeline(UiMotionMode.Off, true);
        check(off.Duration == 0 && off.IsComplete, "Off has no prelude time at all");
        check(!ResultPreludeTimeline.RunsFor(UiMotionMode.Off), "Off never draws a prelude frame");
        check(!off.HasWordmark, "Off does not claim a wordmark it never shows");
    }

    private static void SkipAndInvalidDeltas(Action<bool, string> check)
    {
        var full = new ResultPreludeTimeline(UiMotionMode.Full, true);
        full.Advance(0.30);
        full.Skip();
        check(full.IsComplete && full.MarkAlpha == 1 && Math.Abs(full.MarkScale - 1f) < 1e-4f,
            "skip lands on the stable wordmark, not a half-stamped one");
        check(full.BandAlpha == 1 && full.DimAlpha > 0.78, "skip keeps the dim and band in place");

        var guarded = new ResultPreludeTimeline(UiMotionMode.Full, true);
        guarded.Advance(double.NaN);
        guarded.Advance(-1);
        check(guarded.Elapsed == 0, "invalid deltas cannot advance the prelude");
        guarded.Advance(99);
        check(guarded.IsComplete && guarded.Elapsed == guarded.Duration, "a long frame clamps to the hand-off");
        guarded.Advance(1);
        check(guarded.Elapsed == guarded.Duration, "the prelude never runs past its duration");
    }

    private static void RunSequence(Action<bool, string> check)
    {
        var sequence = new ResultRunSequence();
        check(!sequence.TryCompletePrelude(), "a result screen cannot be delivered before the run starts");
        check(sequence.TryBegin() && !sequence.TryBegin(), "a run starts its prelude exactly once");
        check(sequence.TryCompletePrelude() && !sequence.TryCompletePrelude(),
            "skip and the timer cannot deliver the result screen twice");
        check(sequence.Started && sequence.PreludeCompleted, "the sequence records both stages");

        // 防穿透间隔：跳过/交付那一帧之后的 120ms 内按钮不得接收输入。
        const ulong now = 10_000;
        var unlock = ResultInputGuard.Until(now);
        check(ResultInputGuard.Msec == 120, "anti-passthrough window stays 120ms");
        check(ResultInputGuard.Blocks(now, unlock), "the delivering frame is still guarded");
        check(ResultInputGuard.Blocks(unlock - 1, unlock), "the last millisecond is guarded");
        check(!ResultInputGuard.Blocks(unlock, unlock), "buttons unlock after 120ms");
    }
}

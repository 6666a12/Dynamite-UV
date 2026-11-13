using DynamiteUniverse.Ui;

namespace CoreTests;

/// <summary>展开卡的展示/动画状态机（SongCardMotion）：落点、反向续走、三档动效、文字代际、封面与静止门闩。</summary>
internal static class SongCardMotionTests
{
    private const float Row = 96f;
    private const float Card = 392f;
    private static readonly CardMotionProfile Full = new(Animated: true, Directional: true);
    private static readonly CardMotionProfile Reduced = new(Animated: true, Directional: false);
    private static readonly CardMotionProfile Off = new(Animated: false, Directional: false);

    internal static void Run(Action<bool, string> check)
    {
        var immediate = new SongCardMotion(Row, Card);
        immediate.Expand(true, Full);
        check(immediate.Collapse(Full) == SongCardMotion.CollapseOutcome.Completed,
            "collapse before first expansion frame completes immediately");
        immediate.Advance(2, Full, 0);
        check(!immediate.IsPresenting && !immediate.IsCollapsing && !immediate.Animating && immediate.ShownHeight == Row,
            "immediate collapse cannot leave a stuck presenting card");
        LandingPoints(check);
        ReverseContinuesFromCurrentValue(check);
        MotionModes(check);
        TextGeneration(check);
        CoverFade(check);
        SettledLatch(check);
        Console.WriteLine("  Song card motion state checks complete.");
    }

    private static void LandingPoints(Action<bool, string> check)
    {
        var motion = new SongCardMotion(Row, Card);
        check(!motion.IsPresenting && !motion.Animating && motion.ShownHeight == Row,
            "a hidden card starts collapsed at the row height with no animation in flight");
        check(motion.Expand(cardWasHidden: true, Full) && motion.ShownHeight == Row,
            "expanding a hidden card snaps the placeholder to the row height");
        var frame = motion.Advance(0.10, Full, 0);
        check(frame.HeightChanged && motion.ShownHeight > Row && motion.ShownHeight < Card,
            "full motion grows the card height over the resize duration");
        motion.Advance(0.30, Full, 0);
        check(motion.ShownHeight == Card && motion.IsPresenting && !motion.Animating,
            "the expansion lands exactly on the card height and settles");

        check(motion.Collapse(Full) == SongCardMotion.CollapseOutcome.Animating &&
              motion.IsPresenting && motion.IsCollapsing,
            "collapsing keeps the card presenting while the height shrinks");
        motion.Advance(0.05, Full, 0);
        check(motion.IsPresenting && motion.ShownHeight > Row && motion.ShownHeight < Card,
            "a collapsing card keeps presenting until the height reaches the row height");
        frame = motion.Advance(0.30, Full, 0);
        check(!motion.IsPresenting && !motion.IsCollapsing && motion.ShownHeight == Row &&
              frame.CollapseLanded && !motion.Animating,
            "the collapse landing point hands the expanded state back and settles");
        check(motion.Collapse(Full) == SongCardMotion.CollapseOutcome.Ignored,
            "collapsing a card that is not presenting does nothing");
    }

    private static void ReverseContinuesFromCurrentValue(Action<bool, string> check)
    {
        var motion = new SongCardMotion(Row, Card);
        motion.Expand(cardWasHidden: true, Full);
        motion.Advance(0.10, Full, 0);
        var expanding = motion.ShownHeight;
        check(expanding > Row && expanding < Card, "the card is mid-expansion before the reverse");

        motion.Collapse(Full);
        check(motion.ShownHeight == expanding && motion.IsPresenting,
            "a collapse requested mid-expansion starts from the current height");
        check(motion.Collapse(Full) == SongCardMotion.CollapseOutcome.Ignored,
            "a second collapse request during the collapse is ignored");
        motion.Advance(0.05, Full, 0);
        check(motion.ShownHeight < expanding && motion.ShownHeight > Row,
            "the reversed collapse continues towards the row height");
        motion.Advance(0.30, Full, 0);
        check(motion.ShownHeight == Row && !motion.IsPresenting,
            "the reversed collapse still lands on the row height");

        motion.Expand(cardWasHidden: true, Full);
        motion.Advance(0.10, Full, 0);
        motion.Collapse(Full);
        motion.Advance(0.10, Full, 0);
        var collapsing = motion.ShownHeight;
        check(collapsing > Row && collapsing < Card, "the card is mid-collapse before the reversal");
        check(!motion.Expand(cardWasHidden: false, Full) && motion.ShownHeight == collapsing &&
              motion.IsPresenting && !motion.IsCollapsing,
            "re-expanding mid-collapse resumes from the current height without a placeholder snap");
        motion.Advance(0.30, Full, 0);
        check(motion.ShownHeight == Card && motion.IsPresenting,
            "the resumed expansion lands on the card height");
    }

    private static void MotionModes(Action<bool, string> check)
    {
        // Reduced：几何瞬时，文字与封面仍淡。
        var reduced = new SongCardMotion(Row, Card);
        check(reduced.Expand(cardWasHidden: true, Reduced) && reduced.ShownHeight == Card,
            "reduced motion snaps the card open to the card height in the same call");
        reduced.Advance(0.016, Reduced, 0);
        check(reduced.ShownHeight == Card && !reduced.Animating,
            "reduced motion has no height animation to advance");
        check(reduced.BeginTextRefresh(new CardTextContent("A", "B", "C"), 1, 1f, Reduced) ==
              SongCardMotion.TextOutcome.Fading && reduced.IsTextFading,
            "reduced motion still fades the card text");
        var frame = reduced.Advance(0.10, Reduced, 1);
        check(frame.TextReplaced && reduced.TextAlpha == 0f && reduced.Animating,
            "reduced motion fades the old text out, swaps it and keeps fading in");
        frame = reduced.Advance(0.20, Reduced, 1);
        check(reduced.TextAlpha == 1f && frame.TextFadeInSettled && !reduced.Animating,
            "the reduced-motion text fade lands at full alpha and settles");
        reduced.HideCover(Reduced);
        check(reduced.CoverAlpha == 0f, "reduced motion still presses the old cover to zero");
        reduced.ShowCover(Reduced);
        reduced.Advance(0.05, Reduced, 1);
        check(reduced.CoverAlpha > 0f && reduced.CoverAlpha < 1f,
            "reduced motion fades the new cover in");
        check(reduced.Collapse(Reduced) == SongCardMotion.CollapseOutcome.Completed && !reduced.IsPresenting,
            "reduced motion collapses without a height animation");

        // Off：全部瞬时。
        var off = new SongCardMotion(Row, Card);
        check(off.Expand(cardWasHidden: true, Off) && off.ShownHeight == Card,
            "off snaps the card open without an animation");
        var content = new CardTextContent("X", "Y", "Z");
        check(off.BeginTextRefresh(content, 7, 1f, Off) == SongCardMotion.TextOutcome.Applied &&
              !off.IsTextFading && off.PendingText == content,
            "off applies new text immediately while still recording it for the caller");
        off.HideCover(Off);
        check(off.CoverAlpha == 1f && !off.Animating,
            "off keeps the old cover visible instead of fading it out");
        off.ShowCover(Off);
        check(off.CoverAlpha == 1f, "off lands the new cover at full alpha");
        check(off.Collapse(Off) == SongCardMotion.CollapseOutcome.Completed && !off.IsPresenting,
            "off collapses the card in the same call");
    }

    private static void TextGeneration(Action<bool, string> check)
    {
        var motion = new SongCardMotion(Row, Card);
        motion.Expand(cardWasHidden: true, Full);
        motion.Advance(0.30, Full, 0);

        var first = new CardTextContent("Alpha", "Alice", "谱师 Jane");
        check(motion.BeginTextRefresh(first, 1, 1f, Full) == SongCardMotion.TextOutcome.Fading &&
              motion.IsTextFading && motion.PendingText == first && motion.Animating,
            "a text refresh starts by fading the current text out");
        motion.Advance(0.04, Full, 1);
        check(motion.TextAlpha > 0f && motion.TextAlpha < 1f, "the text fade-out is mid-flight");

        // 淡出半途来了新一代请求：代际前进，旧落零不再换文。
        var second = new CardTextContent("Beta", "Bob", "谱师 John");
        check(motion.BeginTextRefresh(second, 2, motion.TextAlpha, Full) == SongCardMotion.TextOutcome.Fading &&
              motion.PendingText == second && motion.TextAlpha < 1f,
            "an interrupted refresh keeps the in-flight alpha as its start");
        var frame = motion.Advance(0.08, Full, 1);
        check(!frame.TextReplaced && !motion.IsTextFading && motion.TextAlpha == 0f && !motion.Animating,
            "a stale generation fades the text to zero without replacing it or fading back in");

        // 代际仍当前时：落零即换文并淡入。
        var third = new CardTextContent("Gamma", "Carol", "谱师 Amy");
        check(motion.BeginTextRefresh(third, 3, 1f, Full) == SongCardMotion.TextOutcome.Fading,
            "a refresh after the alpha recovered fades out again");
        frame = motion.Advance(0.08, Full, 3);
        check(frame.TextReplaced && motion.PendingText == third,
            "the fade-out landing replaces the text only for the current generation");
        check(motion.IsTextFading && motion.TextAlpha == 0f && motion.Animating,
            "the just-started fade-in is not reported as settled (the latch stays on)");
        frame = motion.Advance(0.04, Full, 3);
        check(frame.TextAlphaChanged && motion.TextAlpha > 0f && motion.Animating,
            "the fade-in keeps raising the text alpha across frames");
        frame = motion.Advance(0.30, Full, 3);
        check(motion.TextAlpha == 1f && frame.TextFadeInSettled && !motion.IsTextFading && !motion.Animating,
            "the fade-in lands at full alpha, asks for the dim reset and clears the latch");

        // alpha 已经落零：新请求即时换文，不再淡出。
        check(motion.BeginTextRefresh(new CardTextContent("Delta", "Dan", "谱师 Bo"), 4, 0f, Full) ==
              SongCardMotion.TextOutcome.Applied && !motion.IsTextFading,
            "a refresh while the text alpha is zero applies immediately");
    }

    private static void CoverFade(Action<bool, string> check)
    {
        var motion = new SongCardMotion(Row, Card);
        motion.Expand(cardWasHidden: true, Full);
        motion.Advance(0.30, Full, 0);
        check(motion.CoverAlpha == 1f, "the cover starts fully visible");

        motion.HideCover(Full);
        check(motion.CoverAlpha == 0f, "hiding the cover presses it to zero immediately");
        motion.Advance(0.04, Full, 0);
        check(motion.CoverAlpha == 0f && motion.Animating,
            "the empty 0→0 cover fade keeps the animation latch on");

        motion.ShowCover(Full);
        var frame = motion.Advance(0.05, Full, 0);
        var halfway = motion.CoverAlpha;
        check(frame.CoverAlphaChanged && halfway > 0f && halfway < 1f,
            "showing the cover fades it in from the current alpha");
        motion.ShowCover(Full);
        check(motion.CoverAlpha == halfway,
            "a restarted cover fade keeps the current alpha instead of snapping back to zero");
        frame = motion.Advance(0.30, Full, 0);
        check(motion.CoverAlpha == 1f && frame.CoverAlphaChanged && !motion.Animating,
            "the cover fade lands at full alpha and clears the latch");
        check(!motion.Advance(0.016, Full, 0).CoverAlphaChanged,
            "a landed cover fade stops writing alpha every frame");
    }

    private static void SettledLatch(Action<bool, string> check)
    {
        var motion = new SongCardMotion(Row, Card);
        check(!motion.Animating, "a fresh card reports no animation in flight");
        motion.Expand(cardWasHidden: true, Full);
        check(motion.Animating, "starting the height animation lights the latch");
        motion.Advance(0.30, Full, 0);
        check(!motion.Animating, "a landed height animation clears the latch");
        var idle = motion.Advance(0.016, Full, 0);
        check(!idle.HeightChanged && !idle.TextAlphaChanged && !idle.TextReplaced &&
              !idle.TextFadeInSettled && !idle.CoverAlphaChanged && !idle.CollapseLanded,
            "a settled card reports no per-frame work to apply");

        motion.BeginTextRefresh(new CardTextContent("A", "B", "C"), 1, 1f, Full);
        check(motion.Animating, "starting a text fade lights the latch again");
        motion.Advance(0.30, Full, 3);
        check(!motion.Animating, "a text fade whose generation went stale still clears the latch");
    }
}

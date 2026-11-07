using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

internal static class StagePlacementDraftTests
{
    internal static void Run()
    {
        TestWidthQuadraticResponse();
        TestWidthBoundsKeepCenterLegal();
        TestTailLifecycle();
        TestInvalidGridRejected();
    }

    private static void TestWidthQuadraticResponse()
    {
        var draft = NewDraft(2.5);
        Check(draft.UpdateWidth(3.5) && draft.Width == 2,
            "stage draft widens one unit for a one-unit positive drag");
        Check(Math.Abs(draft.Center - 2.5) < 1e-12,
            "centre is unchanged while the widened span still fits");
        Check(draft.UpdateWidth(3) && Math.Abs(draft.Width - 1.25) < 1e-12,
            "stage draft keeps the signed squared-delta curve below one unit");
        Check(draft.UpdateWidth(2.5) && draft.Width == 1,
            "returning to the anchor restores the base width");
        Check(draft.UpdateWidth(1.5) && Math.Abs(draft.Width - .1) < 1e-12,
            "a one-unit negative drag collapses to the minimum width");
        Check(draft.UpdateWidth(4.5) && Math.Abs(draft.Width - 5) < 1e-12,
            "a two-unit positive drag hits the maximum width");
    }

    private static void TestWidthBoundsKeepCenterLegal()
    {
        foreach (var center in new[] { -2d, .01, .5, 4.5, 4.99, 7d })
        {
            var draft = new StagePlacementDraft(ExactBarTime.One, center, 1, center);
            draft.UpdateWidth(center + 10);
            Check(draft.Width == 101 && draft.Center == center,
                "overscan widening preserves the original centre");
            draft.UpdateWidth(center - 10);
            Check(draft.Width == .1 && draft.Center == center,
                "minimum interactive width never translates an overscan centre");
        }
        Check(NewDraft(2.5).Width == 1 && NewDraft(2.5).Center == 2.5,
            "construction keeps the supplied centre and width untouched");
    }

    private static void TestTailLifecycle()
    {
        var draft = NewDraft(2.5);
        var head = draft.Time;
        draft.UpdateTail(ExactBarTime.One, 4);
        Check(!draft.TailStage && draft.TailTime == head,
            "tail updates are ignored before the tail stage begins");
        draft.BeginTail(4);
        Check(draft.TailStage && draft.TailTime == ExactBarTime.FromFraction(3, 4) &&
              draft.TailTime > head,
            "beginning the tail seeds it one grid step after the head");
        Check(!draft.UpdateWidth(10) && draft.Width == 1,
            "tail stage freezes the head width");
        draft.UpdateTail(head, 4);
        Check(draft.TailTime == ExactBarTime.FromFraction(3, 4),
            "tail cannot move onto the head");
        draft.UpdateTail(ExactBarTime.Zero, 4);
        Check(draft.TailTime == ExactBarTime.FromFraction(3, 4),
            "tail cannot move before the head");
        draft.UpdateTail(ExactBarTime.FromFraction(1, 1), 4);
        Check(draft.TailTime == ExactBarTime.One,
            "tail accepts a candidate past the minimum");
        draft.UpdateTail(ExactBarTime.FromFraction(7, 8), 8);
        Check(draft.TailTime == ExactBarTime.FromFraction(7, 8),
            "a finer grid divisor can re-target an earlier legal tail");
    }

    private static void TestInvalidGridRejected()
    {
        var draft = NewDraft(2.5);
        ExpectGridFailure(() => draft.BeginTail(0));
        ExpectGridFailure(() => draft.BeginTail(-4));
        Check(!draft.TailStage && draft.TailTime == draft.Time,
            "rejected tail beginnings leave the draft outside the tail stage");
        draft.BeginTail(4);
        ExpectGridFailure(() => draft.UpdateTail(ExactBarTime.One, 0));
        ExpectGridFailure(() => draft.UpdateTail(ExactBarTime.One, -1));
        Check(draft.TailTime == ExactBarTime.FromFraction(3, 4),
            "rejected tail updates keep the previous tail time");
    }

    private static void ExpectGridFailure(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("expected ArgumentOutOfRangeException for grid divisor");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static StagePlacementDraft NewDraft(double center) =>
        new(ExactBarTime.FromFraction(1, 2), center, 1, center);
}

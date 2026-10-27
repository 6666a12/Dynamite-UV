using DynamiteUniverse.Ui;

namespace CoreTests;

internal static class ResultRevealTests
{
    public static void Run(Action<bool, string> check)
    {
        var full = new ResultRevealTimeline(UiMotionMode.Full);
        full.Advance(.59, true);
        check(full.Coverage > .98 && !full.GradeVisible, "result shutter closes before grade");
        check(!full.BackgroundVisible, "stage remains visible until the opaque takeover");
        full.Advance(.33, true);
        check(full.Coverage > .98, "result shutter holds closed");
        full.Advance(.48, true);
        check(full.Coverage < .02 && full.GradeVisible, "result shutter opens before grade");
        check(full.GradeFrame == 0, "grade starts at entry frame");
        full.Advance(.5, true);
        check(full.GradeFrame == 12, "grade uses 24fps timeline");
        full.Skip(true);
        check(full.IsComplete, "result tap skips only after grade is ready");
        check(full.Coverage == 0 && full.BackgroundVisible && full.GradeFrame == 29 &&
            full.Opacity(2.36, 2.60) == 1, "skip reaches one complete stable state");

        var waiting = new ResultRevealTimeline(UiMotionMode.Full);
        waiting.Advance(5, false);
        check(waiting.Elapsed <= ResultRevealTimeline.HoldEnd, "missing grade keeps opaque hold");
        waiting.Skip(false);
        check(!waiting.IsComplete, "skip cannot expose an unloaded grade");
        check(waiting.Coverage == 1 && waiting.BackgroundVisible && !waiting.GradeVisible,
            "slow-load hold is fully covered and never exposes grade");
        waiting.Advance(.01, true);
        check(waiting.IsComplete, "pending skip completes after asset readiness");

        var delayed = new ResultRevealTimeline(UiMotionMode.Full);
        delayed.Advance(60, false);
        delayed.Advance(.24, true);
        check(delayed.Coverage > .1 && delayed.Coverage < .9 && !delayed.GradeVisible,
            "slow loading does not eat opening time");
        delayed.Advance(double.NaN, true);
        delayed.Advance(-100, true);
        check(double.IsFinite(delayed.Elapsed) && delayed.Elapsed > 1,
            "invalid clock deltas cannot corrupt reveal state");

        var reduced = new ResultRevealTimeline(UiMotionMode.Reduced);
        reduced.Advance(.26, true);
        check(reduced.Coverage == 0 && reduced.IsComplete, "reduced skips shutters");
        var off = new ResultRevealTimeline(UiMotionMode.Off);
        check(off.IsComplete && off.BackgroundVisible && off.GradeVisible && off.Opacity(3,4)==1,
            "off starts on the complete opaque result");
        Console.WriteLine("  Result reveal timing / loading / skip checks complete.");
    }
}

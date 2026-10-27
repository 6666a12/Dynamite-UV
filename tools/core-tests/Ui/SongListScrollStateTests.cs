using DynamiteUniverse.Ui;

namespace CoreTests;

internal static class SongListScrollStateTests
{
    internal static void Run(Action<bool, string> check)
    {
        InertiaAndExternalWrite(check);
        BoundariesAndImmediateMode(check);
    }

    private static void InertiaAndExternalWrite(Action<bool, string> check)
    {
        var state = new SongListScrollState();
        check(!state.Push(100, 500, 80, .112) && state.Animating && state.Target == 180,
            "a scroll impulse accumulates at the target while the display animates");
        check(state.Step(100, .112, .112) && state.Display > 100 && state.Display < 180,
            "the display approaches the target with exponential inertia");
        check(!state.Step(250, .112, .016) && !state.Animating &&
              state.Display == 250 && state.Target == 250 && state.Written == 250,
            "an external scroll write takes ownership and cancels inertia");
    }

    private static void BoundariesAndImmediateMode(Action<bool, string> check)
    {
        var state = new SongListScrollState();
        check(!state.Push(0, 240, -80, .112) && state.Target == 0 && state.Animating,
            "scrolling above the beginning clamps the target to zero");
        var immediate = new SongListScrollState();
        check(immediate.Push(220, 240, 80, 0) && immediate.Display == 240 &&
              immediate.Target == 240 && !immediate.Animating,
            "motion-off scrolling lands immediately at the bounded target");
        immediate.AcceptWrite(240, 180);
        check(immediate.Display == 180 && immediate.Target == 180 && !immediate.Animating && immediate.Written == 180,
            "a clamped container readback resets display, target, and animation");
        state.Stop(180);
        check(!state.Step(180, .112, .016) && !state.Animating,
            "stopping a gesture leaves the list at its current position");
    }
}

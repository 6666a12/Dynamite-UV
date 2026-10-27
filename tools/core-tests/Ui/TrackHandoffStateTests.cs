using DynamiteUniverse.Ui;

namespace CoreTests;

internal static class TrackHandoffStateTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var mode in new[] { UiMotionMode.Full, UiMotionMode.Reduced, UiMotionMode.Off })
        {
            var allowDirectionalMotion = mode == UiMotionMode.Full;
            var allowLoop = mode == UiMotionMode.Full;
            var state = new TrackHandoffState();
            state.MarkReady(allowDirectionalMotion);
            check(state.Phase == TrackHandoffPhase.Lock, $"{mode} READY enters lock");
            if (mode == UiMotionMode.Full)
            {
                check(state.LockAmount == 0f, "Full READY starts lock ramp");
                state.Advance(0.09, true, allowLoop, 0.50, 1.8);
                check(state.LockAmount == 1f, "Full lock ramp completes at 90ms");
            }
            else
            {
                check(state.LockAmount == 1f, $"{mode} READY locks immediately");
            }
            state.BeginReveal(discharge: allowDirectionalMotion);
            if (allowDirectionalMotion)
            {
                check(state.Phase == TrackHandoffPhase.Discharge && state.Discharge == 0f,
                    "Full reveal starts discharge");
                state.Advance(0.50, allowDirectionalMotion, allowLoop, 0.50, 1.8);
                check(state.Discharge == 1f && !state.IsDischargeRunning,
                    "Full discharge completes at DischargeSeconds");
            }
            else
            {
                check(state.Phase == TrackHandoffPhase.Lock && !state.IsDischargeRunning,
                    $"{mode} reveal skips discharge");
            }
            state.Reset();
            check(state.Phase == TrackHandoffPhase.Charge && state.LockAmount == 0f &&
                state.Discharge == 0f && state.ChargePhase == 0f && !state.IsDischargeRunning,
                $"{mode} clear resets handoff state");
        }

        var reduced = new TrackHandoffState();
        reduced.MarkReady(allowDirectionalMotion: false);
        reduced.BeginReveal(discharge: false);
        check(reduced.Phase == TrackHandoffPhase.Lock && reduced.LockAmount == 1f,
            "reveal without discharge preserves ready lock");
    }
}

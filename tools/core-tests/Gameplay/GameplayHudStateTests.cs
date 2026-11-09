using DynamiteUniverse.Game;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace CoreTests;

public static class GameplayHudStateTests
{
    private static void ProgressAndLines(Action<bool, string> check)
    {
        var progress = new GameplayProgressState();
        progress.Observe(50, 100, 0, 0, 0);
        check(progress.Fraction == 0.5 && progress.Status == ProgressRunStatus.AllPerfect, "progress starts AP and spans whole duration");
        progress.Observe(60, 100, 1, 0, 0);
        check(progress.Status == ProgressRunStatus.FullCombo, "Great loses AP but retains FC");
        progress.Observe(70, 100, 0, 1, 0);
        check(progress.Status == ProgressRunStatus.FullCombo, "Good retains FC under current rules");
        progress.Observe(80, 100, 0, 0, 1);
        progress.Observe(90, 100, 0, 0, 0);
        check(progress.Status == ProgressRunStatus.Broken, "new combo cannot restore FC");
        progress.Observe(-10, 100, 0, 0, 0);
        check(progress.Fraction == 0, "negative pre-roll clamps progress");
        progress.Observe(110, 100, 0, 0, 0);
        check(progress.Fraction == 1, "progress clamps to endpoint");
        check(GameplayProgressState.EndSeconds(80, 100, 0.2, true) == 100.2, "audio tail and timing offset contribute to endpoint");
        check(GameplayProgressState.EndSeconds(100, 80, -0.2, true) == 102, "chart plus two can outlast audio");
        check(GameplayProgressState.EndSeconds(80, 100, 0, false) == 82, "fallback follows actual chart-only completion");
        var end = new GameplayEndCondition(12, true);
        end.Observe(10, true, 0.5);
        end.Observe(10, true, 0.5);
        progress.Observe(end.EffectiveSeconds, 12, 0, 0, 0);
        check(progress.Fraction > 10.0 / 12, "progress advances after audio clock freezes");
        progress.Complete();
        check(progress.Fraction == 1, "normal completion forces exact endpoint");

        var lines = new JudgmentLineTimeline(new[]
        {
            new JudgmentLineTimeline.Activity(Track.Left, 20, 20),
            new JudgmentLineTimeline.Activity(Track.Left, 34, 34),
            new JudgmentLineTimeline.Activity(Track.Center, 10, 25),
            new JudgmentLineTimeline.Activity(Track.Center, 34, 34),
            new JudgmentLineTimeline.Activity(Track.Right, 10, 10),
            new JudgmentLineTimeline.Activity(Track.Right, 20, 20),
        });
        double At(Track track, double time, UiMotionMode mode = UiMotionMode.Full) => lines.Sample(track, time, mode);
        check(At(Track.Left, 16.9) == JudgmentLineTimeline.DarkLevel, "idle line stays dim before three-second lead");
        check(At(Track.Left, 17.05) == 1 && At(Track.Left, 17.25) == JudgmentLineTimeline.DarkLevel && At(Track.Left, 17.45) == 1,
            "two pre-entry flashes use chart time");
        check(At(Track.Left, 18) == 1 && At(Track.Left, 20) == 1, "line stays lit through hit");
        check(At(Track.Left, 20.75) > JudgmentLineTimeline.DarkLevel && At(Track.Left, 20.75) < 1,
            "known long gap fades immediately after last note");
        check(Math.Abs(At(Track.Left, 21.5) - JudgmentLineTimeline.DarkLevel) < 1e-9 && At(Track.Left, 31.05) == 1,
            "fade completes in 1.5 seconds and next entry re-arms");
        check(At(Track.Center, 24) == 1 && At(Track.Center, 30) == 1, "sustain interval and short gap remain continuously lit");
        check(At(Track.Right, 12) < 0.15, "exact ten-second gap qualifies for dimming");
        check(At(Track.Left, 17.25, UiMotionMode.Reduced) > At(Track.Left, 17.1, UiMotionMode.Reduced), "Reduced fades on without blinking");
        check(At(Track.Left, 17, UiMotionMode.Off) == 1 && At(Track.Left, 20.01, UiMotionMode.Off) == JudgmentLineTimeline.DarkLevel, "Off switches immediately");
        var sample = At(Track.Left, 17.25);
        _ = At(Track.Left, 90);
        check(sample == At(Track.Left, 17.25), "seek and repeated paused sample do not restart timers");
        check(new JudgmentLineTimeline(Array.Empty<JudgmentLineTimeline.Activity>()).Sample(Track.Center, 0, UiMotionMode.Full) == JudgmentLineTimeline.DarkLevel,
            "empty track keeps faint reference");
        // Verify plan adaptation with a real sustain rather than only hand-built activity fixtures.
        Note NoteAt(int id, double time) => new() { Id = id, SubNoteId = -1, Track = Track.Right,
            Type = NoteType.MixerNode, BarTime = time, Second = time, BakedSecond = time, Position = 0, Width = 1 };
        var plan = new JudgePlan.Plan { Units = new(), TheoreticalMax = 0, HeadlineUnitCount = 0,
            Sustains = new Dictionary<int, SustainPath> { [1] = new() { HeadId = 1, Track = Track.Right,
                Kind = SustainKind.Mixer, Nodes = new[] { NoteAt(1, 10), NoteAt(2, 40) } } } };
        check(JudgmentLineTimeline.FromPlan(plan).Sample(Track.Right, 35, UiMotionMode.Full) == 1, "plan adapter keeps long Mixer tail active");
    }

    public static void Run(Action<bool, string> check)
    {
        ProgressAndLines(check);
        var s = new BleedHudState();
        s.Observe(0.98);
        check(s.Display == 0.98 && s.Trail == 1, "HUD damage is immediate with prior-health trace");
        s.Advance(0.05, UiMotionMode.Full, false);
        check(s.Trail == 1 && s.Impact > 0, "HUD damage trace hold and local impact");
        s.Observe(0.94);
        check(s.Trail == 1, "consecutive damage preserves visible trace");
        s.Advance(0.5, UiMotionMode.Full, false);
        check(s.Trail == s.Display && s.Impact == 0, "trace catches up and impact stops");
        s.Observe(0.942);
        check(s.Display < s.Health, "healing target never creates a hit flash");
        s.Advance(1, UiMotionMode.Full, false);
        check(Math.Abs(s.Display - s.Health) < 0.00001, "micro healing converges");
        s.Observe(0.2);
        s.Advance(0.03, UiMotionMode.Full, false);
        var frozen = (s.Display, s.Trail, s.Impact, s.LowPulse);
        s.Advance(4, UiMotionMode.Full, true);
        check(frozen == (s.Display, s.Trail, s.Impact, s.LowPulse), "paused blood animation freezes");
        s.Advance(0.01, UiMotionMode.Reduced, false);
        check(s.Impact == 0 && s.LowPulse == 0 && s.Trail == s.Display, "Reduced removes flare and breathing");
        s.Observe(0.4);
        s.Advance(0, UiMotionMode.Off, true);
        check(s.Display == 0.4 && s.Trail == 0.4, "Off snaps even when paused");
        s.Observe(0);
        s.Advance(0, UiMotionMode.Off, false);
        check(s.Display == 0 && s.Trail == 0 && s.LowPulse == 0, "depleted snapshot is empty");
        s.Observe(double.NaN);
        check(s.Health == 0, "invalid display sample stays bounded");

        var light = new HudLightState();
        check(light.SetText("ACC 100.00%", UiMotionMode.Full) && light.Amount == 0, "initial HUD value is quiet");
        check(!light.SetText("ACC 100.00%", UiMotionMode.Full), "same displayed ACC does not pulse");
        light.SetText("ACC 99.99%", UiMotionMode.Full);
        check(light.Amount > 0, "display change pulses");
        for (var i = 0; i < 100; i++) light.Trigger(UiMotionMode.Full);
        check(light.Amount == 1, "rapid events saturate rather than grow unbounded");
        light.Advance(0.1, UiMotionMode.Reduced, 0.24);
        check(light.Amount <= 0.3, "Reduced caps light amplitude");
        light.Advance(0, UiMotionMode.Off, 0.24);
        check(light.Amount == 0, "Off removes old light");
        light.Trigger(UiMotionMode.Off);
        check(light.Amount == 0, "Off events stay quiet");
    }
}

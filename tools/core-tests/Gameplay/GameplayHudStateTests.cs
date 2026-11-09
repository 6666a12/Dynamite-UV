using DynamiteUniverse.Game;
using DynamiteUniverse.Ui;

namespace CoreTests;

public static class GameplayHudStateTests
{
    public static void Run(Action<bool, string> check)
    {
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

using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Display-only BLEED interpolation. Never computes damage, healing or failure.</summary>
internal sealed class BleedHudState
{
    public double Health { get; private set; } = 1;
    public double Display { get; private set; } = 1;
    public double Trail { get; private set; } = 1;
    public double Impact { get; private set; }
    public double LowPulse { get; private set; }
    private double _trailStart = 1;
    private double _damageAge = 99;
    private double _clock;

    public void Observe(double health)
    {
        var next = Math.Clamp(double.IsFinite(health) ? health : 0, 0, 1);
        if (next < Health)
        {
            _trailStart = Math.Max(Trail, Math.Max(Health, Display));
            Trail = _trailStart;
            Display = next;
            _damageAge = 0;
        }
        Health = next;
    }

    public void Advance(double delta, UiMotionMode motion, bool frozen)
    {
        if (motion == UiMotionMode.Off)
        {
            Display = Trail = Health;
            Impact = LowPulse = 0;
            _damageAge = 99;
            return;
        }
        if (frozen) return;
        delta = Math.Max(0, delta);
        _clock += delta;
        _damageAge += delta;
        Display += (Health - Display) * (1 - Math.Exp(-delta / 0.08));
        Trail = Display;
        if (motion == UiMotionMode.Full && _trailStart > Display)
        {
            if (_damageAge < 0.09) Trail = _trailStart;
            else if (_damageAge < 0.37)
                Trail = Display + (_trailStart - Display) * Math.Pow(1 - (_damageAge - 0.09) / 0.28, 3);
        }
        Impact = motion == UiMotionMode.Full ? Math.Pow(1 - Math.Clamp(_damageAge / 0.14, 0, 1), 2) : 0;
        LowPulse = motion == UiMotionMode.Full && Health > 0 && Health <= 0.25
            ? 0.5 + 0.5 * Math.Sin(_clock * Math.Tau / 1.8) : 0;
    }
}

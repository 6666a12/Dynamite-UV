using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Bounded event light envelope; unchanged display strings never retrigger it.</summary>
internal sealed class HudLightState
{
    public double Amount { get; private set; }
    private string? _text;
    public bool SetText(string text, UiMotionMode motion)
    {
        if (_text == text) return false;
        if (_text is not null) Trigger(motion);
        _text = text;
        return true;
    }
    public void Trigger(UiMotionMode motion) => Amount = motion switch
    {
        UiMotionMode.Full => Math.Min(1, Amount + 0.65),
        UiMotionMode.Reduced => Math.Min(0.3, Amount + 0.2),
        _ => 0,
    };
    public bool Advance(double delta, UiMotionMode motion, double duration)
    {
        var before = Amount;
        Amount = motion == UiMotionMode.Off ? 0 : Amount * Math.Exp(-Math.Max(0, delta) / duration);
        if (motion == UiMotionMode.Reduced) Amount = Math.Min(0.3, Amount);
        if (Amount < 0.001) Amount = 0;
        return before != Amount;
    }
}

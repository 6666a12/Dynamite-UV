using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Native ScrollContainer/scrollbar adapter. The surface owns pointer capture and
/// projection, not the inertia model. Wheel/pan retain Page/8, the original time constant,
/// clamp/rounding and cancellation on external writes (focus, scrollbar, EnsureControlVisible).</summary>
internal sealed class SongListViewport(ScrollContainer scroll, Func<double> tau)
{
    private readonly SongListScrollState _state = new();
    public bool Animating => _state.Animating;
    public void Push(float factor)
    {
        var b = scroll.GetVScrollBar();
        float maximum = Mathf.Max(0, (float)(b.MaxValue - b.Page));
        if (_state.Push(scroll.ScrollVertical, maximum, (float)(b.Page / 8.0) * factor, tau())) Write();
    }
    public void Step(double delta)
    {
        if (_state.Step(scroll.ScrollVertical, tau(), delta)) Write();
    }
    public void StopInertia() => _state.Stop(scroll.ScrollVertical);
    private void Write()
    {
        int target = Mathf.RoundToInt(_state.Display);
        scroll.ScrollVertical = target;
        _state.AcceptWrite(target, scroll.ScrollVertical);
    }
}

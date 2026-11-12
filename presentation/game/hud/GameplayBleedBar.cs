using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>BLEED visual node. Receives the real health snapshot and owns only display animation.</summary>
public partial class GameplayBleedBar : ColorRect
{
    public static readonly Color StandardColor = new("3689d9");
    public static readonly Color HardcoreColor = new("bd3445");
    public Func<UiMotionMode> Motion { get; init; } = () => UiMotionMode.Full;
    public Func<bool> Frozen { get; init; } = () => false;
    private readonly BleedHudState _state = new();
    private ShaderMaterial _shader = null!;
    private bool _configured;
    private bool _hardcore;
    private static readonly StringName Health = "u_health", Trail = "u_trail_health",
        Impact = "u_impact_alpha", Pulse = "u_low_pulse", Glow = "u_glow";

    public override void _Ready()
    {
        Name = "BleedBar";
        Position = new Vector2(384, 12); // TopHud adds 8: body=(440,76), shader pad=56.
        Size = new Vector2(1152, 194);
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 0;
        _shader = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/bleed_hud.gdshader") };
        Material = _shader;
        Visible = false;
    }
    public void Observe(bool enabled, bool hardcore, double health)
    {
        Visible = enabled;
        if (!enabled) return;
        if (!_configured || _hardcore != hardcore)
        {
            _configured = true;
            _hardcore = hardcore;
            var color = hardcore ? HardcoreColor : StandardColor;
            _shader.SetShaderParameter("u_color", new Vector3(color.R, color.G, color.B));
            _shader.SetShaderParameter("u_hardcore", hardcore ? 1f : 0f);
        }
        _state.Observe(health / 100.0);
        if (health <= 0) _state.Advance(0, UiMotionMode.Off, false); // no stale fill behind failure prelude
        Apply();
    }
    public override void _Process(double delta)
    {
        if (!Visible) return;
        _state.Advance(delta, Motion(), Frozen());
        Apply();
    }
    private void Apply()
    {
        var motion = Motion();
        _shader.SetShaderParameter(Health, (float)_state.Display);
        _shader.SetShaderParameter(Trail, (float)_state.Trail);
        _shader.SetShaderParameter(Impact, (float)_state.Impact);
        _shader.SetShaderParameter(Pulse, (float)_state.LowPulse);
        _shader.SetShaderParameter(Glow, motion == UiMotionMode.Full ? 1f : motion == UiMotionMode.Reduced ? 0.35f : 0f);
    }
}

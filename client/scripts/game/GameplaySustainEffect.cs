using Godot;

namespace DynamiteUniverse.Game;

public enum SustainEffectKind
{
    Hold,
    Mixer,
}

/// <summary>
/// Hold/Mixer 接触期间的十二帧循环效果。整体亮度只在首次接入时爬升，
/// 满亮后保持不变；后续变化仅来自内部纹理帧。
/// </summary>
public partial class GameplaySustainEffect : Node2D
{
    public SustainEffectKind Kind { get; init; }
    public Color Accent { get; init; } = new(1f, 0.72f, 0.30f);
    public float SpanPx { get; set; } = 120f;

    private const int FrameCount = 12;
    private const double AttackSecond = 0.28;
    private const double LoopSecond = 0.80;
    private const float SustainBrightnessBoost = 0.72f;
    private double _age;
    private readonly Vector2[] _bracketPoints = new Vector2[3];

    public override void _Process(double delta)
    {
        _age += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var attack = Mathf.Clamp((float)(_age / AttackSecond), 0f, 1f);
        var brightness = attack * attack * (3f - 2f * attack) * SustainBrightnessBoost;
        var frame = (int)(_age / LoopSecond * FrameCount) % FrameCount;
        var phase = frame / (float)FrameCount;

        if (Kind == SustainEffectKind.Hold)
            DrawHold(phase, brightness);
        else
            DrawMixer(phase, brightness);
    }

    private void DrawHold(float phase, float brightness)
    {
        var halfSpan = Mathf.Clamp(SpanPx * 0.5f, 22f, 440f);
        var pulse = 0.78f + 0.22f * (0.5f + 0.5f * Mathf.Sin((float)_age * 14f));
        var flowHalf = 112f;
        var active = 1f;
        DrawLine(new Vector2(-halfSpan, 0f), new Vector2(halfSpan, 0f),
            new Color(Accent, brightness * 0.08f), 20f, true);
        DrawLine(new Vector2(-halfSpan, 0f), new Vector2(halfSpan, 0f),
            new Color(Accent, brightness * (0.27f + 0.05f * pulse)), 4f, true);
        DrawLine(new Vector2(-halfSpan, -5f), new Vector2(halfSpan, -5f),
            new Color(1f, 0.94f, 0.78f, brightness * 0.14f), 1.5f, true);

        var g = 34f + active * 18f;
        var b = 25f + active * 12f;
        for (var side = -1f; side <= 1f; side += 2f)
        {
            _bracketPoints[0] = new Vector2(side * g, -b);
            _bracketPoints[1] = new Vector2(side * (g - 12f), 0f);
            _bracketPoints[2] = new Vector2(side * g, b);
            DrawPolyline(_bracketPoints,
                new Color(Accent, brightness * (0.31f + 0.08f * pulse)),
                3f, true);
        }

        DrawLine(new Vector2(-flowHalf, 0f), new Vector2(flowHalf, 0f),
            new Color(Accent, brightness * (0.16f + 0.09f * pulse)), 2f, true);
        for (var i = 0; i < 5; i++)
        {
            var travel = Mathf.PosMod(phase + i * 0.2f, 1f);
            var y = Mathf.Lerp(-flowHalf, flowHalf, travel);
            var envelope = Mathf.Sin(travel * Mathf.Pi);
            var alpha = brightness * (0.10f + 0.14f * envelope) * pulse;
            DrawCircle(new Vector2(0f, y), 2f + 2f * envelope,
                new Color(1f, 1f, 1f, alpha));
            DrawLine(new Vector2(-22f, y), new Vector2(22f, y),
                new Color(Accent, alpha * 0.72f), 1.5f, true);
        }

        var apexPulse = Mathf.Pow(
            Mathf.Clamp(1f - Mathf.Min(phase, 1f - phase) / 0.12f, 0f, 1f), 2f);
        DrawCircle(new Vector2(0f, -flowHalf), 3f + 7f * apexPulse,
            new Color(1f, 1f, 1f,
                brightness * (0.14f + 0.30f * apexPulse)));
        DrawCircle(new Vector2(-halfSpan, 0f), 5f,
            new Color(1f, 0.88f, 0.60f, brightness * (0.22f + 0.12f * pulse)));
        DrawCircle(new Vector2(halfSpan, 0f), 5f,
            new Color(1f, 0.88f, 0.60f, brightness * (0.22f + 0.12f * pulse)));
    }

    private void DrawMixer(float phase, float brightness)
    {
        var radius = Mathf.Clamp(SpanPx * 0.24f, 18f, 52f);
        var angle = phase * Mathf.Tau;
        var pulse = 0.78f + 0.22f * (0.5f + 0.5f * Mathf.Sin((float)_age * 14f));
        DrawCircle(Vector2.Zero, radius * 0.72f,
            new Color(Accent, brightness * 0.07f));
        DrawArc(Vector2.Zero, radius, angle, angle + 4.25f, 28,
            new Color(Accent, brightness * (0.42f + 0.06f * pulse)), 3f, true);
        DrawArc(Vector2.Zero, radius * 1.34f, -angle * 0.65f,
            -angle * 0.65f + 3.5f, 24,
            new Color(1f, 0.76f, 0.87f, brightness * 0.18f), 1.5f, true);

        for (var i = 0; i < 4; i++)
        {
            var markerAngle = angle + i * Mathf.Pi * 0.5f;
            var direction = new Vector2(Mathf.Cos(markerAngle), Mathf.Sin(markerAngle));
            DrawCircle(direction * radius, 2.5f,
                new Color(1f, 0.86f, 0.92f, brightness * 0.36f));
        }

        var flowHalf = 112f;
        DrawLine(new Vector2(0f, -flowHalf), new Vector2(0f, flowHalf),
            new Color(Accent, brightness * (0.12f + 0.08f * pulse)), 2f, true);
        for (var i = 0; i < 5; i++)
        {
            var travel = Mathf.PosMod(phase + i * 0.2f, 1f);
            var y = Mathf.Lerp(-flowHalf, flowHalf, travel);
            var envelope = Mathf.Sin(travel * Mathf.Pi);
            var alpha = brightness * (0.10f + 0.14f * envelope) * pulse;
            DrawCircle(new Vector2(0f, y), 2f + 2f * envelope,
                new Color(1f, 1f, 1f, alpha));
        }

        var apexPulse = Mathf.Pow(
            Mathf.Clamp(1f - Mathf.Min(phase, 1f - phase) / 0.12f, 0f, 1f), 2f);
        DrawCircle(new Vector2(0f, -flowHalf), 3f + 7f * apexPulse,
            new Color(1f, 1f, 1f,
                brightness * (0.14f + 0.30f * apexPulse)));
    }
}

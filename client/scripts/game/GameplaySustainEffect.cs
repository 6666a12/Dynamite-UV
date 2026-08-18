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
    private double _age;

    public override void _Process(double delta)
    {
        _age += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var attack = Mathf.Clamp((float)(_age / AttackSecond), 0f, 1f);
        var brightness = attack * attack * (3f - 2f * attack);
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
        DrawLine(new Vector2(-halfSpan, 0f), new Vector2(halfSpan, 0f),
            new Color(Accent, brightness * 0.16f), 24f, true);
        DrawLine(new Vector2(-halfSpan, 0f), new Vector2(halfSpan, 0f),
            new Color(Accent, brightness * 0.54f), 5f, true);
        DrawLine(new Vector2(-halfSpan, -5f), new Vector2(halfSpan, -5f),
            new Color(1f, 0.94f, 0.78f, brightness * 0.34f), 2f, true);

        for (var i = 0; i < 3; i++)
        {
            var travel = Mathf.PosMod(phase + i / 3f, 1f);
            var x = Mathf.Lerp(-halfSpan, halfSpan, travel);
            var envelope = Mathf.Sin(travel * Mathf.Pi);
            var alpha = brightness * (0.42f + 0.24f * envelope);
            DrawCircle(new Vector2(x, 0f), 11f,
                new Color(Accent, alpha * 0.13f));
            DrawLine(new Vector2(x - 8f, -9f), new Vector2(x + 8f, 9f),
                new Color(1f, 0.93f, 0.72f, alpha), 2.5f, true);
        }

        DrawCircle(new Vector2(-halfSpan, 0f), 7f,
            new Color(1f, 0.88f, 0.60f, brightness * 0.46f));
        DrawCircle(new Vector2(halfSpan, 0f), 7f,
            new Color(1f, 0.88f, 0.60f, brightness * 0.46f));
    }

    private void DrawMixer(float phase, float brightness)
    {
        var radius = Mathf.Clamp(SpanPx * 0.24f, 18f, 52f);
        var angle = phase * Mathf.Tau;
        DrawCircle(Vector2.Zero, radius * 0.72f,
            new Color(Accent, brightness * 0.13f));
        DrawArc(Vector2.Zero, radius, angle, angle + 4.25f, 28,
            new Color(Accent, brightness * 0.78f), 4f, true);
        DrawArc(Vector2.Zero, radius * 1.34f, -angle * 0.65f,
            -angle * 0.65f + 3.5f, 24,
            new Color(1f, 0.76f, 0.87f, brightness * 0.42f), 2f, true);

        for (var i = 0; i < 4; i++)
        {
            var markerAngle = angle + i * Mathf.Pi * 0.5f;
            var direction = new Vector2(Mathf.Cos(markerAngle), Mathf.Sin(markerAngle));
            DrawCircle(direction * radius, 3.5f,
                new Color(1f, 0.86f, 0.92f, brightness * 0.72f));
        }

        DrawLine(new Vector2(-SpanPx * 0.34f, 0f), new Vector2(SpanPx * 0.34f, 0f),
            new Color(Accent, brightness * 0.46f), 3f, true);
    }
}

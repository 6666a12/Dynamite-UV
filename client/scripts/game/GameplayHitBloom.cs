using Godot;

namespace DynamiteUniverse.Game;

public enum HitEffectKind
{
    Tap,
    ExTap,
    Drag,
    Hold,
    Mixer,
    Mine,
}

/// <summary>
/// 十二帧式程序化打击爆发。颜色、纹理和长轴范围由 Note 类型决定；Node2D 的
/// Rotation 负责把同一份素材语法旋转到侧轨，保证左右亮度一致。
/// </summary>
public partial class GameplayHitBloom : Node2D
{
    public Color Accent { get; init; } = new(0.21f, 0.88f, 1.0f);
    public HitEffectKind Kind { get; init; } = HitEffectKind.Tap;
    public float SpanPx { get; init; } = 120f;
    public float Strength { get; init; } = 1f;

    private const int FrameCount = 12;
    private const double Lifetime = 0.42;
    private const float BrightnessBoost = 1.30f;
    private double _age;

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= Lifetime)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rawProgress = Mathf.Clamp((float)(_age / Lifetime), 0f, 1f);
        var frame = Mathf.Min(FrameCount - 1, (int)(rawProgress * FrameCount));
        var progress = frame / (float)(FrameCount - 1);
        var onset = Mathf.Clamp(progress / 0.12f, 0f, 1f);
        var fade = Mathf.Min(1f,
            Mathf.Pow(1f - progress, 1.7f) * onset * Strength * BrightnessBoost);
        var radius = Mathf.Lerp(12f, 96f, progress);
        var halfSpan = Mathf.Clamp(SpanPx * 0.58f, 48f, 210f);

        if (Kind == HitEffectKind.Mine)
        {
            DrawMineStrike(radius, fade, halfSpan);
            return;
        }

        for (var i = 5; i >= 1; i--)
        {
            DrawCircle(Vector2.Zero, radius * i / 5f,
                new Color(Accent, fade * (0.014f + i * 0.011f)));
        }

        var streakHalf = Mathf.Lerp(22f, halfSpan, Mathf.Clamp(progress * 1.8f, 0f, 1f));
        DrawLine(new Vector2(-streakHalf, 0f), new Vector2(streakHalf, 0f),
            new Color(Accent, fade * 0.96f), Mathf.Lerp(7.5f, 2.2f, progress), true);
        DrawLine(new Vector2(-streakHalf * 0.82f, 0f), new Vector2(streakHalf * 0.82f, 0f),
            new Color(1f, 1f, 1f, fade * 0.68f), 1.7f, true);

        var ringAlpha = fade * Mathf.Clamp(progress * 2.8f, 0f, 1f);
        DrawArc(Vector2.Zero, radius * 0.72f, -2.75f, -0.35f, 28,
            new Color(Accent, ringAlpha * 0.82f), 3.2f, true);
        DrawArc(Vector2.Zero, radius * 0.72f, 0.40f, 2.55f, 28,
            new Color(Accent, ringAlpha * 0.64f), 2.2f, true);
        DrawArc(Vector2.Zero, radius, -1.35f, 0.25f, 24,
            new Color(Accent, ringAlpha * 0.28f), 2f, true);

        DrawRays(radius, fade, progress);
        DrawTypeDetails(radius, fade, progress);

        var coreRadius = Mathf.Lerp(10f, 3f, progress);
        DrawCircle(Vector2.Zero, coreRadius,
            new Color(1f, 1f, 1f, fade * Mathf.Lerp(1f, 0.35f, progress)));
    }

    private void DrawRays(float radius, float fade, float progress)
    {
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Mathf.Pi / 4f + 0.17f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var inner = radius * (0.28f + 0.08f * (i % 2));
            var outer = radius * (0.72f + 0.12f * ((i + 1) % 3));
            DrawLine(direction * inner, direction * outer,
                new Color(Accent, fade * (0.22f + 0.10f * (1f - progress))),
                i % 2 == 0 ? 2.2f : 1.4f, true);
        }
    }

    private void DrawTypeDetails(float radius, float fade, float progress)
    {
        if (Kind == HitEffectKind.ExTap)
        {
            DrawArc(Vector2.Zero, radius * 0.48f, 0f, Mathf.Tau, 32,
                new Color(0.92f, 0.99f, 1f, fade * 0.72f), 3f, true);
            DrawArc(Vector2.Zero, radius * 0.88f, 0.35f, 4.8f, 36,
                new Color(Accent, fade * 0.34f), 2f, true);
        }
        else if (Kind == HitEffectKind.Drag)
        {
            for (var i = -1; i <= 1; i++)
            {
                var x = i * radius * 0.34f + progress * radius * 0.12f;
                DrawPolyline(new[]
                {
                    new Vector2(x - 10f, -8f), new Vector2(x, 0f),
                    new Vector2(x - 10f, 8f),
                }, new Color(0.90f, 1f, 0.92f, fade * 0.58f), 2f, true);
            }
        }
        else if (Kind is HitEffectKind.Hold or HitEffectKind.Mixer)
        {
            DrawArc(Vector2.Zero, radius * 0.58f, -2.6f, 2.6f, 30,
                new Color(Accent, fade * 0.66f), 4f, true);
        }
    }

    private void DrawMineStrike(float radius, float fade, float halfSpan)
    {
        var extent = Mathf.Min(halfSpan, Mathf.Lerp(30f, 86f, radius / 96f));
        var danger = new Color(Accent, fade * 0.88f);
        DrawCircle(Vector2.Zero, radius * 0.74f, new Color(Accent, fade * 0.07f));
        DrawLine(new Vector2(-extent, -extent * 0.42f),
            new Vector2(extent, extent * 0.42f), danger, 5f, true);
        DrawLine(new Vector2(extent, -extent * 0.42f),
            new Vector2(-extent, extent * 0.42f), danger, 5f, true);
        DrawArc(Vector2.Zero, radius * 0.72f, 0f, Mathf.Tau, 32,
            new Color(Accent, fade * 0.56f), 3f, true);
        DrawCircle(Vector2.Zero, Mathf.Lerp(12f, 4f, radius / 96f),
            new Color(1f, 0.76f, 0.78f, fade));
    }
}

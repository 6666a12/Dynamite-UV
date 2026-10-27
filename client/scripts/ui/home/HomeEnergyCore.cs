using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>Programmatic home-screen orbital reactor; decorative only and never receives input.</summary>
public partial class HomeEnergyCore : Control
{
    private const int OrbitSegments = 72;
    private const float LoopSeconds = 8f;

    private static readonly Color Metal = new("24405f");
    private static readonly Color MetalDark = new("101a35");
    private static readonly Color Orange = new("ff9d45");
    private static readonly Color Violet = new("a78bfa");
    private static readonly Color Ion = new("c8f4ff");

    private double _elapsed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (UiMotionProfile.For(GameSession.Settings.MotionMode).AllowLoop)
            QueueRedraw();
    }

    public override void _Draw()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        var animatedTime = profile.AllowLoop ? (float)_elapsed : LoopSeconds * 0.28f;
        var loopTime = animatedTime % LoopSeconds;
        var pulse = profile.AllowLoop ? (Mathf.Sin(loopTime * Mathf.Tau / LoopSeconds) + 1f) * 0.5f : 0.55f;
        var center = Size * new Vector2(0.51f, 0.49f);
        var radius = Mathf.Min(Size.X, Size.Y) * 0.235f;

        DrawCircle(center, radius * 0.92f, new Color(UiFonts.Cyan, 0.016f + pulse * 0.014f));

        DrawOrbit(center, radius * 1.48f, 0.34f, -0.42f, loopTime * 0.55f, UiFonts.Cyan, 1.00f, pulse);
        DrawOrbit(center, radius * 1.76f, 0.58f, 0.72f, -loopTime * 0.42f, UiFonts.Pink, 0.92f, pulse);
        DrawOrbit(center, radius * 1.98f, 0.82f, -0.18f, loopTime * 0.27f, Violet, 0.84f, pulse);

        for (var i = 0; i < 6; i++)
        {
            var angle = i * Mathf.Tau / 6f - Mathf.Pi / 2f;
            DrawConstraintArm(center, radius, angle, UiFonts.Cyan, UiFonts.Pink, pulse, i);
        }

        DrawCore(center, radius, loopTime, pulse);
    }

    private void DrawOrbit(Vector2 center, float orbitRadius, float flatten, float tilt,
        float phase, Color color, float strength, float pulse)
    {
        var metalColor = new Color(Metal, 0.36f * strength);
        var glowColor = new Color(color, (0.50f + pulse * 0.20f) * strength);
        var highlightColor = new Color(Ion, 0.18f * strength);
        DrawEllipse(center, orbitRadius, orbitRadius * flatten, tilt, metalColor, 7.5f);
        DrawEllipse(center, orbitRadius, orbitRadius * flatten, tilt, glowColor, 2.6f);
        DrawEllipse(center, orbitRadius * 1.012f, orbitRadius * flatten * 1.012f, tilt,
            highlightColor, 1.2f);

        for (var i = 0; i < 3; i++)
        {
            var angle = phase + i * Mathf.Tau / 3f;
            var point = OrbitPoint(center, orbitRadius, orbitRadius * flatten, tilt, angle);
            var direction = OrbitDirection(orbitRadius, orbitRadius * flatten, tilt, angle);
            var normal = new Vector2(-direction.Y, direction.X);
            var podGlow = new Color(color, 0.82f * strength);
            DrawCircle(point, 9f + pulse * 2.5f, podGlow);
            DrawCircle(point, 3.8f, Ion);
            DrawLine(point - normal * 15f, point + normal * 15f,
                new Color(MetalDark, 0.70f), 4.5f, true);
            DrawLine(point - normal * 10f, point + normal * 10f,
                new Color(color, 0.72f), 1.6f, true);
        }
    }

    private void DrawConstraintArm(Vector2 center, float radius, float angle,
        Color cyan, Color pink, float pulse, int index)
    {
        var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var normal = new Vector2(-direction.Y, direction.X);
        var inner = center + direction * radius * 0.78f;
        var outer = center + direction * radius * 1.36f;
        var joint = center + direction * radius * 1.45f;
        var armColor = new Color(Metal, 0.72f);
        var edgeColor = new Color(Ion, 0.20f + pulse * 0.08f);
        var lightColor = new Color(index % 2 == 0 ? cyan : pink, 0.64f + pulse * 0.18f);

        DrawLine(inner + normal * 4.5f, outer + normal * 4.5f, new Color(MetalDark, 0.72f), 4f, true);
        DrawLine(inner - normal * 4.5f, outer - normal * 4.5f, new Color(MetalDark, 0.72f), 4f, true);
        DrawLine(inner, outer, armColor, 8f, true);
        DrawLine(inner + normal * 6.5f, outer + normal * 6.5f, edgeColor, 1.4f, true);
        DrawLine(center + direction * radius * 0.90f, center + direction * radius * 1.24f,
            lightColor, 2.5f, true);

        DrawCircle(joint, 11f, new Color(MetalDark, 0.92f));
        DrawArc(joint, 12.5f, 0f, Mathf.Tau, 24, new Color(Metal, 0.78f), 2f, true);
        DrawCircle(joint, 4.5f + pulse * 1.5f, index is 0 or 3 ? Orange : cyan);
    }

    private void DrawCore(Vector2 center, float radius, float loopTime, float pulse)
    {
        var shellRadius = radius * 0.70f;
        var glowRadius = radius * (0.60f + pulse * 0.025f);
        var coreRadius = radius * (0.38f + pulse * 0.018f);

        DrawCircle(center, glowRadius, new Color(UiFonts.Cyan, 0.12f + pulse * 0.05f));
        DrawCircle(center, coreRadius, new Color(Ion, 0.82f));
        DrawCircle(center, coreRadius * 0.70f, new Color(UiFonts.Cyan, 0.72f));
        DrawCircle(center, coreRadius * 0.36f, Ion);

        DrawArc(center, shellRadius, 0f, Mathf.Tau, 96, new Color(MetalDark, 0.95f), 7f, true);
        DrawArc(center, shellRadius, 0f, Mathf.Tau, 96, new Color(Metal, 0.82f), 2.5f, true);
        DrawArc(center, shellRadius * 0.88f, loopTime * 0.45f,
            loopTime * 0.45f + Mathf.Tau * 0.76f, 96, new Color(UiFonts.Cyan, 0.64f), 2f, true);

        for (var i = 0; i < 6; i++)
        {
            var angle = i * Mathf.Tau / 6f - Mathf.Pi / 2f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var normal = new Vector2(-direction.Y, direction.X);
            var start = center + direction * radius * 0.48f;
            var end = center + direction * radius * 0.66f;
            DrawLine(start + normal * 7f, end + normal * 7f, new Color(MetalDark, 0.92f), 3f, true);
            DrawLine(start - normal * 7f, end - normal * 7f, new Color(MetalDark, 0.92f), 3f, true);
            DrawLine(start, end, new Color(Metal, 0.90f), 6f, true);
            DrawLine(start + normal * 5f, end + normal * 5f,
                new Color(Ion, 0.20f + pulse * 0.08f), 1f, true);
        }
    }

    private void DrawEllipse(Vector2 center, float radiusX, float radiusY, float tilt,
        Color color, float width)
    {
        var previous = OrbitPoint(center, radiusX, radiusY, tilt, 0f);
        for (var i = 1; i <= OrbitSegments; i++)
        {
            var angle = i * Mathf.Tau / OrbitSegments;
            var next = OrbitPoint(center, radiusX, radiusY, tilt, angle);
            DrawLine(previous, next, color, width, true);
            previous = next;
        }
    }

    private static Vector2 OrbitPoint(Vector2 center, float radiusX, float radiusY,
        float tilt, float angle)
    {
        var x = Mathf.Cos(angle) * radiusX;
        var y = Mathf.Sin(angle) * radiusY;
        var cos = Mathf.Cos(tilt);
        var sin = Mathf.Sin(tilt);
        return center + new Vector2(x * cos - y * sin, x * sin + y * cos);
    }

    private static Vector2 OrbitDirection(float radiusX, float radiusY, float tilt, float angle)
    {
        var x = -Mathf.Sin(angle) * radiusX;
        var y = Mathf.Cos(angle) * radiusY;
        var cos = Mathf.Cos(tilt);
        var sin = Mathf.Sin(tilt);
        return new Vector2(x * cos - y * sin, x * sin + y * cos).Normalized();
    }
}

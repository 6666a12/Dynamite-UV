using Godot;

namespace DuxCommunity.Ui;

/// <summary>Clean-room procedural cover used whenever a chart pack has no decodable artwork.</summary>
public partial class CoverPlaceholder : Control
{
    public bool Compact { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0)
            return;

        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0b1228"));
        var grid = new Color(UiFonts.Line, 0.42f);
        var spacing = Mathf.Max(24f, Mathf.Min(Size.X, Size.Y) / 10f);
        for (var x = 0f; x <= Size.X; x += spacing)
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), grid, 1f);
        for (var y = 0f; y <= Size.Y; y += spacing)
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), grid, 1f);

        var cut = Mathf.Min(28f, Mathf.Min(Size.X, Size.Y) * 0.08f);
        Vector2[] border =
        {
            new(cut, 2), new(Size.X - 2, 2), new(Size.X - 2, Size.Y - cut),
            new(Size.X - cut, Size.Y - 2), new(2, Size.Y - 2), new(2, cut),
            new(cut, 2),
        };
        DrawPolyline(border, new Color(UiFonts.Cyan, 0.72f), 2f, true);

        var waveY = Size.Y * 0.43f;
        var left = Size.X * 0.12f;
        var width = Size.X * 0.76f;
        var amplitude = Size.Y * 0.08f;
        DrawWave(left, width, waveY, amplitude, new Color(UiFonts.Cyan, 0.88f), 0f);
        DrawWave(left, width, waveY, amplitude * 0.72f, new Color(UiFonts.Pink, 0.72f), 1.35f);
        DrawWave(left, width, waveY, amplitude * 0.52f, new Color(UiFonts.Casual, 0.62f), 2.45f);

        var titleSize = Compact ? 27 : Mathf.RoundToInt(Mathf.Clamp(Size.Y * 0.09f, 27f, 72f));
        var subtitleSize = Compact ? 13 : Mathf.RoundToInt(Mathf.Clamp(Size.Y * 0.03f, 13f, 26f));
        DrawString(UiFonts.TechBold, new Vector2(0, Size.Y * 0.69f), "NO COVER",
            HorizontalAlignment.Center, Size.X, titleSize, UiFonts.Text);
        DrawString(UiFonts.Tech, new Vector2(0, Size.Y * 0.77f), "COMMUNITY CHART",
            HorizontalAlignment.Center, Size.X, subtitleSize, UiFonts.Dim);
    }

    private void DrawWave(float left, float width, float centerY, float amplitude,
        Color color, float phase)
    {
        const int segments = 64;
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var u = i / (float)segments;
            var envelope = Mathf.Sin(Mathf.Pi * u);
            var wave = Mathf.Sin(u * Mathf.Pi * 8f + phase) * 0.58f +
                Mathf.Sin(u * Mathf.Pi * 17f + phase * 0.7f) * 0.22f;
            points[i] = new Vector2(left + width * u,
                centerY + amplitude * envelope * wave);
        }
        DrawPolyline(points, color, 2f, true);
    }
}

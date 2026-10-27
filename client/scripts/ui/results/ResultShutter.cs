using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Two opaque, translating Signal Lock panels. Geometry matches the Penpot boards.</summary>
public partial class ResultShutter : Control
{
    private float _coverage;
    private static readonly Vector2[] Seam =
    [new(922, 0), new(922, 326), new(956, 360), new(960, 360), new(960, 686),
     new(994, 720), new(998, 720), new(998, 1046), new(964, 1080)];

    public float Coverage
    {
        get => _coverage;
        set { if (_coverage == value) return; _coverage = value; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (_coverage <= 0) return;
        DrawPanel(true);
        DrawPanel(false);
    }

    private void DrawPanel(bool left)
    {
        var dx = 1020 * (1 - _coverage) * (left ? -1 : 1);
        Vector2 P(float x, float y) => new(x + dx, y);
        var edge = Seam.Select(v => v + new Vector2(dx, 0)).ToArray();
        var poly = left
            ? new[] { P(0, 0) }.Concat(edge).Append(P(0, 1080)).ToArray()
            : edge.Concat([P(1920, 1080), P(1920, 0)]).ToArray();
        DrawColoredPolygon(poly, new Color("070b16"));
        foreach (var (y0, y1, ex) in new[] { (0f, 326f, 922f), (360f, 686f, 960f), (720f, 1046f, 998f) })
        {
            var lo = left ? 0 : ex + dx + 26;
            var hi = left ? ex + dx - 26 : 1920;
            lo = Math.Max(0, lo); hi = Math.Min(1920, hi);
            if (lo < hi)
                for (var b = -1080; b < 2100; b += 170)
                {
                    var c = b + dx + 380;
                    var a = Math.Max(y0, (c - hi) * 1080 / 380);
                    var z = Math.Min(y1, (c - lo) * 1080 / 380);
                    if (z > a)
                        DrawLine(new(c - 380 * a / 1080, a), new(c - 380 * z / 1080, z),
                            new Color(UiFonts.Line, .28f), 2, true);
                }
            DrawRect(new Rect2(P(left ? ex - 116 : ex, y0), new Vector2(116, y1 - y0)), new Color("101a2f"));
        }
        DrawPolyline(edge, new Color(UiFonts.Cyan, .045f), 14, true);
        DrawPolyline(edge, new Color(UiFonts.Cyan, .65f), 5, true);
        DrawPolyline(edge, new Color("b9f5ff", .8f), 1, true);
        var start = left ? 112 : 1576;
        DrawRect(new Rect2(P(start, 842), new(232, 3)), new Color(UiFonts.Cyan, .8f));
        DrawRect(new Rect2(P(start, 976), new(100, 2)), new Color(UiFonts.Cyan, .4f));
        for (var i = 0; i < 3; i++)
            DrawRect(new Rect2(P(start + 118 + i * 24, 972), new(12, 6)), new Color(UiFonts.Cyan, .35f + i * .18f));
    }
}

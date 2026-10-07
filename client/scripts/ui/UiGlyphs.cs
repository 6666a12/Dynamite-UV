using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>选曲页等程序化 UI 用的 26×26 控制图标（与设计稿 glyph() 同一套几何）。</summary>
public enum UiGlyphKind
{
    Standard,
    Hardcore,
    Bleed,
    Mirror,
    Auto,
    Sliders,
    Pause,
    Play,
    Search,
    Funnel,
    Lock,
}

/// <summary>把设计稿的图标几何直接画到某个 CanvasItem 上（图标先画、文字后画）。</summary>
public static class UiGlyphs
{
    public static void Draw(CanvasItem item, UiGlyphKind kind, Vector2 topLeft, float size,
        Color color, float width = 2f)
    {
        var u = size / 26f;
        Vector2 P(float x, float y) => topLeft + new Vector2(x * u, y * u);
        var w = width * u;

        switch (kind)
        {
            case UiGlyphKind.Standard:
                item.DrawLine(P(3, 19), P(23, 19), color, w, true);
                item.DrawRect(new Rect2(P(9, 9), new Vector2(8 * u, 6 * u)), color);
                break;

            case UiGlyphKind.Hardcore:
                item.DrawLine(P(3, 19), P(23, 19), color, w, true);
                item.DrawLine(P(10, 6), P(10, 19), color, w, true);
                item.DrawLine(P(16, 6), P(16, 19), color, w, true);
                item.DrawRect(new Rect2(P(11, 12), new Vector2(4 * u, 5 * u)), color);
                break;

            case UiGlyphKind.Bleed:
            {
                // 设计稿是液滴轮廓：两条对称三次贝塞尔侧边 + 底部半径 8 的半圆。
                // CanvasItem 没有贝塞尔绘制，按折线采样（20px 下弧线已足够平滑）。
                var outline = new List<Vector2>();
                SampleCubic(outline, P(13, 3), P(13, 3), P(21, 12), P(21, 17), 8);
                const int arcSteps = 14;
                for (var i = 0; i <= arcSteps; i++)
                {
                    var t = Mathf.Pi * i / arcSteps;
                    outline.Add(P(13, 17) + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * 8f * u);
                }
                SampleCubic(outline, P(5, 17), P(5, 12), P(13, 3), P(13, 3), 8);
                item.DrawPolyline(outline.ToArray(), color, w, true);
                item.DrawLine(P(9, 17), P(17, 17), color, w, true);
                break;
            }

            case UiGlyphKind.Mirror:
            {
                item.DrawPolyline([P(10, 5), P(3, 13), P(10, 21), P(10, 5)], color, w, true);
                item.DrawPolyline([P(16, 5), P(23, 13), P(16, 21), P(16, 5)], color, w, true);
                DrawDashed(item, [P(13, 4), P(13, 22)], 2f * u, 3f * u,
                    new Color(color, 0.5f), w * 0.8f);
                break;
            }

            case UiGlyphKind.Auto:
            {
                // 设计稿是 rx=5 的圆角方框 + 虚线描边，这里按圆角路径采样后再打虚线。
                const float radius = 5f;
                var path = new List<Vector2> { P(3 + radius, 3) };
                AddCorner(path, P(23 - radius, 3 + radius), radius * u, -Mathf.Pi / 2f, 0f);
                AddCorner(path, P(23 - radius, 23 - radius), radius * u, 0f, Mathf.Pi / 2f);
                AddCorner(path, P(3 + radius, 23 - radius), radius * u, Mathf.Pi / 2f, Mathf.Pi);
                AddCorner(path, P(3 + radius, 3 + radius), radius * u, Mathf.Pi, Mathf.Pi * 1.5f);
                path.Add(P(3 + radius, 3));
                DrawDashed(item, path, 3f * u, 2.6f * u, color, w * 0.9f);
                item.DrawColoredPolygon([P(10.6f, 8.6f), P(17.6f, 13f), P(10.6f, 17.4f)], color);
                break;
            }

            case UiGlyphKind.Sliders:
                for (var i = 0; i < 3; i++)
                {
                    var y = 7f + i * 6f;
                    item.DrawLine(P(3, y), P(23, y), color, w, true);
                }
                item.DrawCircle(P(9, 7), 2.8f * u, color);
                item.DrawCircle(P(17, 13), 2.8f * u, color);
                item.DrawCircle(P(11, 19), 2.8f * u, color);
                break;

            case UiGlyphKind.Pause:
                item.DrawRect(new Rect2(P(8, 5), new Vector2(3.6f * u, 16 * u)), color);
                item.DrawRect(new Rect2(P(14.4f, 5), new Vector2(3.6f * u, 16 * u)), color);
                break;

            case UiGlyphKind.Play:
                item.DrawColoredPolygon([P(8, 5), P(20, 13), P(8, 21)], color);
                break;

            case UiGlyphKind.Search:
                item.DrawArc(P(11, 11), 6.5f * u, 0f, Mathf.Tau, 32, color, w, true);
                item.DrawLine(P(15.8f, 15.8f), P(21, 21), color, w, true);
                break;

            case UiGlyphKind.Funnel:
                item.DrawPolyline(
                    [P(4, 5), P(22, 5), P(15, 13.5f), P(15, 21), P(11, 18.6f), P(11, 13.5f), P(4, 5)],
                    color, w, true);
                break;

            case UiGlyphKind.Lock:
                item.DrawRect(new Rect2(P(5, 10), new Vector2(16 * u, 12 * u)),
                    color, false, w);
                item.DrawArc(P(13, 10), 4.5f * u, Mathf.Pi, Mathf.Tau, 16, color, w, true);
                break;
        }
    }

    /// <summary>把三次贝塞尔 p0→p3（控制点 c1/c2）采样进折线，不含起点。</summary>
    private static void SampleCubic(List<Vector2> into, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p3,
        int steps)
    {
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            var mt = 1f - t;
            var a = mt * mt * mt;
            var b = 3f * mt * mt * t;
            var c = 3f * mt * t * t;
            var d = t * t * t;
            into.Add(new Vector2(
                a * p0.X + b * c1.X + c * c2.X + d * p3.X,
                a * p0.Y + b * c1.Y + c * c2.Y + d * p3.Y));
        }
    }

    /// <summary>采样一段圆角，追加进折线（不含起点）。</summary>
    private static void AddCorner(List<Vector2> into, Vector2 center, float radius, float from,
        float to, int steps = 4)
    {
        for (var i = 1; i <= steps; i++)
        {
            var angle = Mathf.Lerp(from, to, (float)i / steps);
            into.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    /// <summary>按弧长给折线打虚线（设计稿 stroke-dasharray 的等价实现）。</summary>
    private static void DrawDashed(CanvasItem item, IReadOnlyList<Vector2> path, float dash, float gap,
        Color color, float width)
    {
        var drawing = true;
        var remaining = dash;
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var from = path[i];
            var segment = path[i + 1] - from;
            var length = segment.Length();
            if (length <= 0f)
                continue;
            var direction = segment / length;
            var walked = 0f;
            while (walked < length - 0.0001f)
            {
                var step = Mathf.Min(remaining, length - walked);
                if (drawing)
                    item.DrawLine(from + direction * walked, from + direction * (walked + step),
                        color, width, true);
                walked += step;
                remaining -= step;
                if (remaining <= 0.0001f)
                {
                    drawing = !drawing;
                    remaining = drawing ? dash : gap;
                }
            }
        }
    }
}

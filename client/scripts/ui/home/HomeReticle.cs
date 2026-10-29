using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Home v2 的参考环：模型后方的一圈细环、四个刻度、两段四分之一弧和两条粉色短划线。
/// 几何取自 Penpot 稿「Core / reticle」(870,70,1050×960)，纯装饰、不接收输入。
/// </summary>
public partial class HomeReticle : Control
{
    private static readonly Color Ring = new(new Color("34435c"), 0.38f);
    private static readonly Color Tick = new(new Color("51647e"), 0.55f);
    private static readonly Color Arc = new(new Color("3f526c"), 0.42f);
    private static readonly Color Accent = new(new Color("ef5da8"), 0.55f);

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var center = new Vector2(525f, 480f);
        DrawArc(center, 410f, 0f, Mathf.Tau, 128, Ring, 1f, true);

        DrawLine(new Vector2(115f, 480f), new Vector2(151f, 480f), Tick, 1f, true);
        DrawLine(new Vector2(899f, 480f), new Vector2(935f, 480f), Tick, 1f, true);
        DrawLine(new Vector2(525f, 70f), new Vector2(525f, 103f), Tick, 1f, true);
        DrawLine(new Vector2(525f, 857f), new Vector2(525f, 890f), Tick, 1f, true);

        DrawCubic(new Vector2(139f, 318f), new Vector2(169f, 244.5f),
            new Vector2(219.7f, 181.2f), new Vector2(285f, 136f));
        DrawCubic(new Vector2(765f, 824f), new Vector2(830.3f, 778.8f),
            new Vector2(881f, 715.5f), new Vector2(911f, 642f));

        DrawLine(new Vector2(850f, 192f), new Vector2(874f, 168f), Accent, 2f, true);
        DrawLine(new Vector2(176f, 792f), new Vector2(200f, 768f), Accent, 2f, true);
    }

    private void DrawCubic(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end)
    {
        const int segments = 24;
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (float)segments;
            var inverse = 1f - t;
            points[i] = inverse * inverse * inverse * start
                + 3f * inverse * inverse * t * control1
                + 3f * inverse * t * t * control2
                + t * t * t * end;
        }
        DrawPolyline(points, Arc, 1f, true);
    }
}

using Godot;

namespace DuxCommunity.Game;

/// <summary>
/// Clean-room 游玩背景：仅用低透明度几何提供地平线、侧壁与纵深参照。
/// 不参与判定布局，也不改变任何轨道几何。
/// </summary>
public partial class GameplayBackdrop : Node2D
{
    private static readonly Color Bg = new(0.025f, 0.032f, 0.065f);
    private static readonly Color Cyan = new(0.21f, 0.88f, 1.0f);
    private static readonly Color Pink = new(1.0f, 0.30f, 0.58f);

    public override void _Draw()
    {
        const float width = 1920f;
        const float height = 1080f;
        const float horizonY = 248f;
        const float judgeLineY = 861f;
        var vanishing = new Vector2(width * 0.5f, horizonY);

        DrawRect(new Rect2(0, 0, width, height), Bg);

        // 中央纵深辉光：多层半透明梯形叠加，最亮处仍低于 12%。
        for (var i = 7; i >= 1; i--)
        {
            var t = i / 7f;
            var topHalf = 34f + 22f * i;
            var bottomHalf = 220f + 92f * i;
            Vector2[] glow =
            {
                new(vanishing.X - topHalf, horizonY - 22f),
                new(vanishing.X + topHalf, horizonY - 22f),
                new(vanishing.X + bottomHalf, height),
                new(vanishing.X - bottomHalf, height),
            };
            DrawColoredPolygon(glow, new Color(Cyan.Lerp(Pink, 0.20f), 0.008f + 0.006f * t));
        }

        // 左右侧壁暗面，让三面轨道在纯色背景上有明确空间关系。
        DrawColoredPolygon(new[]
        {
            Vector2.Zero, new Vector2(0, height), vanishing, new Vector2(0, horizonY - 80f),
        }, new Color(Cyan, 0.022f));
        DrawColoredPolygon(new[]
        {
            new Vector2(width, 0), new Vector2(width, horizonY - 80f), vanishing,
            new Vector2(width, height),
        }, new Color(Pink, 0.022f));

        // 判定线以下不是空白区，而是隧道前景地板；几何仅作视觉分层。
        DrawColoredPolygon(new[]
        {
            new Vector2(60f, judgeLineY), new Vector2(width - 60f, judgeLineY),
            new Vector2(width, height), new Vector2(0, height),
        }, new Color(Cyan.Lerp(Pink, 0.24f), 0.026f));

        var rayColor = new Color(Cyan, 0.055f);
        foreach (var x in new[] { 60f, 184f, 420f, 700f, 1220f, 1500f, 1736f, 1860f })
            DrawLine(vanishing, new Vector2(x, height), rayColor, 1.5f, true);

        // 非等距横截线模拟透视压缩，仅作为深度参照，不对应谱面坐标。
        for (var i = 1; i <= 8; i++)
        {
            var t = i / 8f;
            var eased = t * t;
            var y = Mathf.Lerp(horizonY + 18f, judgeLineY - 16f, eased);
            var half = Mathf.Lerp(90f, 870f, t);
            var alpha = Mathf.Lerp(0.035f, 0.075f, t);
            DrawLine(new Vector2(vanishing.X - half, y),
                new Vector2(vanishing.X + half, y), new Color(Cyan, alpha), 1.2f, true);
        }

        // 前景横截线和外扩结构，让判定线成为地板接缝而不是舞台终点。
        foreach (var (y, alpha) in new[]
        {
            (900f, 0.060f), (946f, 0.052f), (996f, 0.044f), (1048f, 0.036f),
        })
            DrawLine(new Vector2(24f, y), new Vector2(width - 24f, y),
                new Color(Cyan, alpha), 1.2f, true);

        DrawLine(new Vector2(184f, judgeLineY), new Vector2(36f, height),
            new Color(Cyan, 0.075f), 2f, true);
        DrawLine(new Vector2(width - 184f, judgeLineY), new Vector2(width - 36f, height),
            new Color(Pink, 0.075f), 2f, true);
        DrawLine(new Vector2(710f, judgeLineY), new Vector2(560f, height),
            new Color(Cyan, 0.045f), 1.5f, true);
        DrawLine(new Vector2(1210f, judgeLineY), new Vector2(1360f, height),
            new Color(Pink, 0.045f), 1.5f, true);

        DrawRect(new Rect2(0, 1030f, width, 50f), new Color(0.01f, 0.015f, 0.04f, 0.28f));

        DrawCircle(vanishing, 116f, new Color(Cyan, 0.025f));
        DrawCircle(vanishing, 54f, new Color(Pink, 0.035f));
    }
}

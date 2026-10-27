using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>样式稿背景：深海军蓝底 + 极淡斜向透视线 + 底部辉光（菜单/选曲用）。</summary>
public partial class NeonBackground : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), UiFonts.Bg);

        // 底部辉光：同心圆叠出近似径向渐变（center 略低于底边）
        var glowCenter = new Vector2(size.X * 0.5f, size.Y * 1.12f);
        var glowCol = new Color("18204a");
        for (var i = 3; i >= 1; i--)
            DrawCircle(glowCenter, size.X * 0.28f * i / 3f, new Color(glowCol, 0.05f));

        // 斜向透视线（105°，间距 120px，alpha≈0.05）
        var dir = new Vector2(Mathf.Cos(Mathf.DegToRad(105f)), Mathf.Sin(Mathf.DegToRad(105f)));
        var nrm = new Vector2(-dir.Y, dir.X);
        var center = size * 0.5f;
        var lineCol = new Color(UiFonts.Cyan, 0.05f);
        var half = size.Length();
        for (var o = -half; o <= half; o += 120f)
        {
            var c = center + nrm * o;
            DrawLine(c - dir * half, c + dir * half, lineCol, 2f, true);
        }
    }
}

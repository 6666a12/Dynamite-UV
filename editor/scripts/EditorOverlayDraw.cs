using DynamiteUniverse.ChartEditor.Core;
using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>编辑器叠加层的切角面板与文字定位助手。设计稿的切角语言只切左上 + 右下，
/// 与 <see cref="UiGeometry.CutCorners"/> 的多边形一致；浮动 chip 与右键菜单共用这一份，
/// 避免两处各写一套基线换算。</summary>
internal static class EditorOverlayDraw
{
    /// <summary>设计稿 §3 的难度色板里 tech 是独立的 #E879F9；客户端 `UiFonts` 只具名到 giga，
    /// 按设计稿补一个（不改 client/）。</summary>
    public static readonly Color Tech = new("e879f9");

    /// <summary>设计稿播报的 Note 表面四色（`svg_note` 的 top / base / bottom / border，
    /// 取 `.tmp/penpot_build4.py` 的板面参数）。client 的运行时调色板是 <c>NoteView</c> 的
    /// private `SurfacePalette`（top / base / bottom 与这里同值），设计另有一档更深的「浅色边」，
    /// 编辑器的手柄描边与 body 描边都要它，所以按类型在这里列出一次——
    /// 取色处一律走 <see cref="Surface"/>，不要把某个类型的色值写进绘制代码。</summary>
    public readonly record struct NoteSurface(Color Top, Color Base, Color Bottom, Color Border);

    /// <summary>Note 类型的表面四色。BarLine 在板面上是 flat 色块（没有四色），按编辑器既有的
    /// 浅色推导兜底，且它本来也不会成为路径节点。</summary>
    public static NoteSurface Surface(AuthoringNoteKind kind) => kind switch
    {
        AuthoringNoteKind.Chain => new(new("e0ffe8"), new("59ff8c"), new("1a8c47"), new("8cffc0")),
        AuthoringNoteKind.Hold => new(new("fff2d1"), new("ffb84d"), new("ad660f"), new("ffcc59")),
        AuthoringNoteKind.Mixer => new(new("ffd6e8"), new("ff4d8f"), new("a81f59"), new("ff9cc4")),
        AuthoringNoteKind.ExTap => new(new("ffffff"), new("99e0ff"), new("368cc4"), new("c9f0ff")),
        AuthoringNoteKind.Mine => new(new("c44a59"), new("752936"), new("36121c"), new("a84a55")),
        _ => new(new("d9f5ff"), new("4cbfff"), new("176bad"), new("8cd8ff")),
    };

    /// <summary>切角多边形：可选填充 + 描边。</summary>
    public static void CutPanel(CanvasItem item, Rect2 rect, float cut, Color fill, Color stroke, float strokeWidth)
    {
        var points = UiGeometry.CutCorners(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, cut);
        if (fill.A > 0f) item.DrawColoredPolygon(points, fill);
        if (stroke.A > 0f) item.DrawPolyline(UiGeometry.Close(points), stroke, strokeWidth, true);
    }

    /// <summary>只有描边的切角框（55% 占位块用的就是这一档）。</summary>
    public static void CutStroke(CanvasItem item, Rect2 rect, float cut, Color stroke, float strokeWidth) =>
        CutPanel(item, rect, cut, new Color(0, 0, 0, 0), stroke, strokeWidth);

    /// <summary>单行文字：基线 = 框心 y + 0.35×字号（设计稿 §5.1 的居中换算）。</summary>
    public static void CenteredText(CanvasItem item, Font font, Vector2 center, string text, int size, Color color)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        item.DrawString(font, new Vector2(center.X - width * .5f, center.Y + size * .35f), text,
            HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>左缘对齐到 <paramref name="leftCenter"/>.X，垂直居中对齐到 <paramref name="leftCenter"/>.Y。</summary>
    public static void LeftText(CanvasItem item, Font font, Vector2 leftCenter, string text, int size, Color color) =>
        item.DrawString(font, new Vector2(leftCenter.X, leftCenter.Y + size * .35f), text,
            HorizontalAlignment.Left, -1, size, color);

    /// <summary>右对齐到 <paramref name="rightEdge"/>，垂直居中对齐到 <paramref name="centerY"/>。</summary>
    public static void RightText(CanvasItem item, Font font, float rightEdge, float centerY, string text, int size, Color color)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        item.DrawString(font, new Vector2(rightEdge - width, centerY + size * .35f), text,
            HorizontalAlignment.Left, -1, size, color);
    }
}

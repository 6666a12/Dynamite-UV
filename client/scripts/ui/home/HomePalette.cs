using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Home v2（Penpot「Game — Home v2 · 01 Compact」）的颜色、字距与几何 token。
/// 数值取自设计稿：底板/文字色是稿内实色，字距用 FontVariation 整数像素表达。
/// </summary>
public static class HomePalette
{
    public static readonly Color Eyebrow = new("55d8e9");
    public static readonly Color Title = new("edf2ff");
    public static readonly Color TitleAccent = new("45dcee");
    public static readonly Color Subtitle = new("8595af");
    public static readonly Color SectionLabel = new("7386a2");
    public static readonly Color Accent = new("f065ac");
    public static readonly Color UtilityText = new("8a9ab3");
    public static readonly Color FooterText = new("52647e");

    public static readonly Color PrimaryFill = new("35d6e9");
    public static readonly Color PrimaryText = new("062b40");
    public static readonly Color PrimarySubText = new("0a4a61");
    public static readonly Color PrimaryArrow = new("07334a");
    public static readonly Color PrimaryHoverFill = new("52e4f5");

    public static readonly Color TileFill = new Color(new Color("0f1429"), 0.55f);
    public static readonly Color TileBorder = new Color(new Color("2a3560"), 0.62f);
    public static readonly Color TileText = new("c6d4e8");
    public static readonly Color TileSubText = new("7488a4");
    public static readonly Color TileHoverFill = new Color(new Color("16203c"), 0.78f);
    public static readonly Color TileHoverBorder = new("4a6fa5");
    public static readonly Color TileHoverText = new("edf2ff");
    public static readonly Color TileHoverSubText = new("9fb2cc");

    public static readonly Color DisabledFill = new Color(new Color("141a2e"), 0.55f);
    public static readonly Color DisabledBorder = new Color(new Color("333a59"), 0.55f);
    public static readonly Color DisabledText = new("5b6c86");
    public static readonly Color DisabledSubText = new("4d607b");

    /// <summary>设计稿字距（ls0/ls1/ls2）在 Godot 侧用整数像素字形间距表达。</summary>
    public static Font Tech(int spacing) => Spaced(UiFonts.Tech, spacing);

    public static Font TechBold(int spacing) => Spaced(UiFonts.TechBold, spacing);

    private static Font Spaced(Font baseFont, int spacing) =>
        spacing == 0 ? baseFont : new FontVariation { BaseFont = baseFont, SpacingGlyph = spacing };
}

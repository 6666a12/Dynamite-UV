using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Home v2 左侧品牌块：eyebrow、DYNAMITE / UNIVERSE 两行标题、粉色强调条和副标题。
/// 坐标、字号、字距取自 Penpot「Game — Home v2 · 01 Compact」；不参与导航。
/// </summary>
internal static class HomeBrandBlock
{
    public static void Build(Node parent, Action<Control, int> register)
    {
        var eyebrow = Text("DYNAMIX-STYLE  /  CLEAN-ROOM COMMUNITY",
            new Vector2(96f, 174f), new Vector2(700f, 22f),
            HomePalette.Tech(2), 15, HomePalette.Eyebrow);
        parent.AddChild(eyebrow);
        register(eyebrow, 0);

        var dynamite = Text("DYNAMITE", new Vector2(88f, 206f), new Vector2(900f, 140f),
            HomePalette.TechBold(-2), 116, HomePalette.Title);
        parent.AddChild(dynamite);
        register(dynamite, 1);

        var universe = Text("UNIVERSE", new Vector2(90f, 326f), new Vector2(900f, 126f),
            HomePalette.TechBold(-2), 102, HomePalette.TitleAccent);
        parent.AddChild(universe);
        register(universe, 1);

        var accent = new ColorRect
        {
            Position = new Vector2(96f, 457f),
            Size = new Vector2(112f, 3f),
            Color = HomePalette.Accent,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(accent);
        register(accent, 2);

        var subtitle = Text("COMMUNITY RHYTHM, REIMAGINED.",
            new Vector2(96f, 486f), new Vector2(700f, 26f),
            HomePalette.Tech(1), 19, HomePalette.Subtitle);
        parent.AddChild(subtitle);
        register(subtitle, 2);
    }

    private static Label Text(string text, Vector2 position, Vector2 size, Font font,
        int fontSize, Color color)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiLabels.Apply(label, font, fontSize, color);
        return label;
    }
}

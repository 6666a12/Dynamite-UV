using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Home v2 顶部右侧的工具入口与右下版本串。CREDITS / LOGIN 按用户决定保留但置灰：
/// 纯 Label、不接收输入、不接任何路由，也不带登录外链箭头。版本串仍由 Main 的构建身份描述生成。
/// </summary>
internal static class HomeUtilityRow
{
    public static void Build(Node parent, string buildDescription, Action<Control, int> register)
    {
        var credits = Text("CREDITS", new Vector2(1512f, 62f), new Vector2(68f, 18f),
            HomePalette.Tech(2), 14, HomePalette.UtilityText, HorizontalAlignment.Right);
        parent.AddChild(credits);
        register(credits, 6);

        var login = Text("LOGIN", new Vector2(1670f, 58f), new Vector2(78f, 20f),
            HomePalette.TechBold(2), 15, HomePalette.UtilityText, HorizontalAlignment.Right);
        parent.AddChild(login);
        register(login, 6);

        var version = Text(buildDescription, new Vector2(1240f, 1020f), new Vector2(616f, 18f),
            HomePalette.Tech(1), 11, HomePalette.FooterText, HorizontalAlignment.Right);
        parent.AddChild(version);
        register(version, 6);
    }

    private static Label Text(string text, Vector2 position, Vector2 size, Font font,
        int fontSize, Color color, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiLabels.Apply(label, font, fontSize, color);
        return label;
    }
}

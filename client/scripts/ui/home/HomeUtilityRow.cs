using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Home v2 工具入口：CREDITS保持占位，LOGIN/ACCOUNT进入独立账号页。
/// 本地游玩和商店禁用态不随账号入口改变。
/// </summary>
internal static class HomeUtilityRow
{
    public static void Build(Node parent, string buildDescription, Action<Control, int> register)
    {
        var credits = Text("CREDITS", new Vector2(1512f, 62f), new Vector2(68f, 18f),
            HomePalette.Tech(2), 14, HomePalette.UtilityText, HorizontalAlignment.Right);
        parent.AddChild(credits);
        register(credits, 6);

        var login = new CutButton
        {
            Text = Game.OnlineSession.Connection.State.Account is null ? "LOGIN" : "ACCOUNT",
            Position = new Vector2(1640f, 44f), Size = new Vector2(180f, 52f),
            FontSize = 18, TechFont = true, LowBorder = true,
        };
        login.Pressed += () => TransitionDirector.Navigate(UiRoutes.Online, TransitionKind.Standard, "CONNECT", "ACCOUNT");
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

using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Home v2 的 SELECT DESTINATION 区：650×108 主按钮 + 2×2 次级底板（谱面工坊 / 设置 / 段位模式 / 联机大厅）。
/// 只有“游玩”和“设置”接真实导航；其余三个沿用设计稿的禁用灰板，不接收输入也不占 Tab 焦点。
/// </summary>
internal static class HomeActions
{
    private const float Left = 96f;

    public static void Build(Node parent, Action<Control, int> register)
    {
        var eyebrow = new Label
        {
            Position = new Vector2(Left, 578f),
            Size = new Vector2(400f, 20f),
            Text = "SELECT DESTINATION",
        };
        UiLabels.Apply(eyebrow, HomePalette.Tech(2), 14, HomePalette.SectionLabel);
        parent.AddChild(eyebrow);
        register(eyebrow, 3);

        var play = new HomeActionTile
        {
            Position = new Vector2(Left, 616f),
            Size = new Vector2(650f, 108f),
            Title = "游玩",
            SubTitle = "PLAY  /  ENTER SONG LIBRARY",
            Style = HomeActionTile.TileStyle.Primary,
            TitleOffset = new Vector2(32f, 18f),
            SubTitleOffset = new Vector2(32f, 64f),
            TitleFontSize = 32,
            SubTitleFontSize = 14,
            CutSize = new Vector2(13.5f, 13.5f),
            ShowArrow = true,
            ArrowOffset = new Vector2(554f, 40f),
        };
        play.Pressed += () => TransitionDirector.Navigate(
            UiRoutes.SongSelect,
            TransitionKind.Standard,
            "OPEN CHANNEL",
            "SONG LIBRARY");
        parent.AddChild(play);
        register(play, 4);

        var workshop = SecondaryTile("谱面工坊", "WORKSHOP  /  OFFLINE", HomeActionTile.TileStyle.Disabled,
            new Vector2(Left, 748f));
        parent.AddChild(workshop);
        register(workshop, 5);

        var settings = SecondaryTile("设置", "SETTINGS  /  SYSTEM", HomeActionTile.TileStyle.Outline,
            new Vector2(432f, 748f));
        settings.Pressed += () => TransitionDirector.Navigate(
            UiRoutes.Settings,
            TransitionKind.Standard,
            "OPEN CHANNEL",
            "SETTINGS");
        parent.AddChild(settings);
        register(settings, 5);

        var rank = SecondaryTile("段位模式", "RANK MODE  /  OFFLINE", HomeActionTile.TileStyle.Disabled,
            new Vector2(Left, 864f));
        parent.AddChild(rank);
        register(rank, 6);

        var lobby = SecondaryTile("联机大厅", "ONLINE LOBBY  /  OFFLINE", HomeActionTile.TileStyle.Disabled,
            new Vector2(432f, 864f));
        parent.AddChild(lobby);
        register(lobby, 6);
    }

    private static HomeActionTile SecondaryTile(string title, string subTitle,
        HomeActionTile.TileStyle style, Vector2 position) => new()
    {
        Position = position,
        Size = new Vector2(314f, 92f),
        Title = title,
        SubTitle = subTitle,
        Style = style,
    };
}

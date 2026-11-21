using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Three actual destination ports on one shallow drum; the underlying VBox remains the
/// keyboard/focus tree. The projected scroll region and the stationary conductor share one origin.</summary>
internal static class HomeActions
{
    internal static readonly Vector2 Origin = new(78, 513);
    internal static readonly Vector2 Extent = new(735, 387);
    private const float Pitch = 147f;

    public static void Build(Node home, Action<Control, int> register)
    {
        var deck = new Control { Name = "HomePortDeck", Position = Origin, Size = Extent,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        home.AddChild(deck);
        var scroll = new ScrollContainer { Name = "HomePortScroll", Size = Extent, ClipContents = true,
            FollowFocus = true, ScrollVerticalCustomStep = Pitch,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever };
        var rows = new VBoxContainer { Name = "HomePorts", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 17);
        deck.AddChild(scroll); scroll.AddChild(rows);
        var drum = CatalogueDrum.Attach(scroll);
        var wire = new HomePortWiring { Name = "HomePortWiring", Size = Extent,
            MouseFilter = Control.MouseFilterEnum.Ignore, ProcessMode = Node.ProcessModeEnum.Always };
        deck.AddChild(wire);
        var tiles = new HomeActionTile[3];
        var names = new[] { "HomePlay", "HomeStore", "HomeSettings" };
        var labels = new[] { "游玩", "谱面商店", "设置" };
        // Keep nearly half a row in view at the crop. There are three real ports, not repeated
        // filler rows. No scrollbar track is painted through the electrical contacts.
        for (int i = 0; i < tiles.Length; ++i)
        {
            int index = i;
            var tile = new HomeActionTile { Name = names[i], Title = labels[i], PortIndex = i,
                AccessibilityName = labels[i], CustomMinimumSize = new Vector2(0, Pitch - 17),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            tiles[i] = tile; rows.AddChild(tile);
            tile.Pressed += () =>
            {
                if (TransitionDirector.IsBusy) return;
                bool accepted = index switch
                {
                    0 => TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Standard, "OPEN CHANNEL", "SONG LIBRARY"),
                    1 => HomeStoreRoute.Open(home),
                    _ => TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Standard, "OPEN CHANNEL", "SETTINGS"),
                };
                // The director has already accepted and paused the departing scene. Only the
                // visual receipt runs through that pause; no callback drives navigation from it.
                if (accepted) wire.Acknowledge(index);
            };
        }
        wire.Bind(drum, scroll, tiles);
        register(deck, 3);
    }
}

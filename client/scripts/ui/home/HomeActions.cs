using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Five destination rows share one shallow drum. Only routes with a live action
/// participate in the native focus tree and connect to the conductor.</summary>
internal static class HomeActions
{
    internal static readonly Vector2 Origin = new(78, 513);
    private const int Pitch = 147, Gap = 17;
    private static readonly Vector2 Extent = new(735, 387);

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
        rows.AddThemeConstantOverride("separation", Gap);
        deck.AddChild(scroll); scroll.AddChild(rows);
        var drum = CatalogueDrum.Attach(scroll);
        var wire = new HomePortWiring { Name = "HomePortWiring", Size = Extent,
            MouseFilter = Control.MouseFilterEnum.Ignore, ProcessMode = Node.ProcessModeEnum.Always };
        deck.AddChild(wire);
        var livePorts = new List<HomeActionTile>();

        Add(HomeDestination.Play, "游玩",
            () => TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Standard, "OPEN CHANNEL", "SONG LIBRARY"));
        Add(HomeDestination.Store, "谱面商店", () => HomeStoreRoute.Open(home));
        Add(HomeDestination.Ranked, "段位模式");
        Add(HomeDestination.Lobby, "联机大厅");
        Add(HomeDestination.Settings, "设置",
            () => TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Standard, "OPEN CHANNEL", "SETTINGS"));

        wire.Bind(drum, scroll, livePorts.ToArray());
        register(deck, 3);

        void Add(HomeDestination destination, string title, Func<bool>? navigate = null)
        {
            var tile = new HomeActionTile { Name = $"Home{destination}", Title = title, Destination = destination,
                IsAvailable = navigate is not null, AccessibilityName = title,
                CustomMinimumSize = new Vector2(0, Pitch - Gap), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rows.AddChild(tile);
            if (navigate is null) return;
            livePorts.Add(tile);
            tile.Pressed += () =>
            {
                // The director accepts and pauses the departing scene before the visual receipt.
                if (!TransitionDirector.IsBusy && navigate()) wire.Acknowledge(tile);
            };
        }
    }
}

internal enum HomeDestination { Play, Store, Settings, Ranked, Lobby }

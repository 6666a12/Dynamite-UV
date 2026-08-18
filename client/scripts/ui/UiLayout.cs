using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Fixed-coordinate UI contract shared by every menu scene.</summary>
public static class UiLayout
{
    public const float DesignWidth = 1920f;
    public const float DesignHeight = 1080f;
    public const int TransitionLayer = 1000;
    public static readonly Vector2 DesignSize = new(DesignWidth, DesignHeight);

    public static void AddBackground(Node owner) =>
        owner.AddChild(new NeonBackground { Size = DesignSize });
}

/// <summary>Scene routes owned by the programmatic UI.</summary>
public static class UiRoutes
{
    public const string Main = "res://scenes/main.tscn";
    public const string SongSelect = "res://scenes/song_select.tscn";
    public const string Settings = "res://scenes/settings.tscn";
    public const string Gameplay = "res://scenes/gameplay.tscn";
}

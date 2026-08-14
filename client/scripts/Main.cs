using Godot;
using DuxCommunity.Game;
using DuxCommunity.Ui;

namespace DuxCommunity;

/// <summary>主菜单（样式稿 #mmenu）：Logo + 游玩/谱面工坊(阶段3)/设置。</summary>
public partial class Main : Node2D
{
	public override void _Ready()
	{
		GameSession.EnsureInit();
		GD.Print($"DUX-Community running on Godot {Engine.GetVersionInfo()["string"]}");

		AddChild(new NeonBackground { Size = new Vector2(1920, 1080) });

		var title = new Label { Position = new Vector2(210, 300), Text = "DUX·COMMUNITY" };
		title.AddThemeFontOverride("font", UiFonts.TechBold);
		title.AddThemeFontSizeOverride("font_size", 110);
		title.AddThemeColorOverride("font_color", UiFonts.Text);
		AddChild(title);

		var sub = new Label { Position = new Vector2(216, 440), Text = "COMMUNITY-DRIVEN RHYTHM GAME" };
		sub.AddThemeFontOverride("font", UiFonts.Tech);
		sub.AddThemeFontSizeOverride("font_size", 26);
		sub.AddThemeColorOverride("font_color", UiFonts.Dim);
		AddChild(sub);

		var play = new CutButton
		{
			Position = new Vector2(210, 570),
			Size = new Vector2(480, 104),
			Text = "游玩",
			SubText = "PLAY / SONG SELECT",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 34,
			AlignLeft = true,
		};
		play.Pressed += () =>
			GetTree().ChangeSceneToFile("res://scenes/song_select.tscn");
		AddChild(play);

		var workshop = new CutButton
		{
			Position = new Vector2(210, 700),
			Size = new Vector2(480, 104),
			Text = "谱面工坊",
			SubText = "WORKSHOP（阶段3）",
			FontSize = 34,
			AlignLeft = true,
			Disabled = true,
		};
		AddChild(workshop);

		var settings = new CutButton
		{
			Position = new Vector2(210, 830),
			Size = new Vector2(480, 104),
			Text = "设置",
			SubText = "SETTINGS",
			FontSize = 34,
			AlignLeft = true,
		};
		settings.Pressed += () =>
			GetTree().ChangeSceneToFile("res://scenes/settings.tscn");
		AddChild(settings);

		var ver = new Label
		{
			Position = new Vector2(1420, 1020),
			Size = new Vector2(440, 30),
			Text = BuildDescription(),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		ver.AddThemeFontSizeOverride("font_size", 18);
		ver.AddThemeColorOverride("font_color", UiFonts.Dim);
		AddChild(ver);
	}

	private static string BuildDescription()
	{
		if (OS.HasFeature("internal_testdata"))
			return "v0.1.2 · INTERNAL TESTDATA · DO NOT DISTRIBUTE";
		if (OS.HasFeature("public_release"))
			return "v0.1.2 · clean-room public build · no bundled charts";
		if (OS.HasFeature("editor") || OS.HasFeature("editor_runtime"))
			return "v0.1.2-dev · editor/development build";
		return "v0.1.2 · UNCLASSIFIED EXPORT · DO NOT DISTRIBUTE";
	}
}

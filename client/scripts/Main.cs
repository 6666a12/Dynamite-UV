using Godot;
using DuxCommunity.Game;
using DuxCommunity.Ui;

namespace DuxCommunity;

/// <summary>主菜单（样式稿 #mmenu）：Logo + 游玩/谱面工坊(阶段3)/设置。</summary>
public partial class Main : Node2D
{
	private readonly List<(Control Item, Vector2 BasePosition, int Order)> _enterItems = new();
	private UiMotionProfile? _enterProfile;
	private double _enterElapsed;

	public override void _Ready()
	{
		GameSession.EnsureInit();
		GD.Print($"DUX-Community running on Godot {Engine.GetVersionInfo()["string"]}");

		UiLayout.AddBackground(this);

		var title = new Label { Position = new Vector2(210, 300), Text = "DUX·COMMUNITY" };
		title.AddThemeFontOverride("font", UiFonts.TechBold);
		title.AddThemeFontSizeOverride("font_size", 110);
		title.AddThemeColorOverride("font_color", UiFonts.Text);
		AddChild(title);
		RegisterEntrance(title, 0);

		var sub = new Label { Position = new Vector2(216, 440), Text = "COMMUNITY-DRIVEN RHYTHM GAME" };
		sub.AddThemeFontOverride("font", UiFonts.Tech);
		sub.AddThemeFontSizeOverride("font_size", 26);
		sub.AddThemeColorOverride("font_color", UiFonts.Dim);
		AddChild(sub);
		RegisterEntrance(sub, 1);

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
		play.Pressed += () => TransitionDirector.Navigate(
			UiRoutes.SongSelect,
			TransitionKind.Standard,
			"OPEN CHANNEL",
			"SONG LIBRARY");
		AddChild(play);
		RegisterEntrance(play, 2);

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
		RegisterEntrance(workshop, 3);

		var settings = new CutButton
		{
			Position = new Vector2(210, 830),
			Size = new Vector2(480, 104),
			Text = "设置",
			SubText = "SETTINGS",
			FontSize = 34,
			AlignLeft = true,
		};
		settings.Pressed += () => TransitionDirector.Navigate(
			UiRoutes.Settings,
			TransitionKind.Standard,
			"OPEN CHANNEL",
			"SETTINGS");
		AddChild(settings);
		RegisterEntrance(settings, 4);

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

		SetProcess(false);
		TransitionDirector.ReportSceneReady(BeginEnterAnimation);
	}

	public override void _Process(double delta)
	{
		if (_enterProfile is null)
			return;

		_enterElapsed += delta;
		var duration = Math.Max(0.01, _enterProfile.SceneDuration);
		var allComplete = true;
		foreach (var (item, basePosition, order) in _enterItems)
		{
			var delay = _enterProfile.AllowStagger ? order * _enterProfile.Stagger : 0.0;
			var progress = Math.Clamp((_enterElapsed - delay) / duration, 0.0, 1.0);
			var eased = UiEase.Enter((float)progress);
			item.Modulate = new Color(1f, 1f, 1f, eased);
			if (_enterProfile.AllowDirectionalMotion)
				item.Position = basePosition + new Vector2(
					-_enterProfile.ContentShift * (1f - eased), 0f);
			allComplete &= progress >= 1.0;
		}

		if (!allComplete)
			return;

		_enterProfile = null;
		SetProcess(false);
	}

	private void RegisterEntrance(Control item, int order) =>
		_enterItems.Add((item, item.Position, order));

	private void BeginEnterAnimation()
	{
		var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
		if (!profile.IsAnimated)
		{
			foreach (var (item, basePosition, _) in _enterItems)
			{
				item.Position = basePosition;
				item.Modulate = Colors.White;
			}
			return;
		}

		_enterProfile = profile;
		_enterElapsed = 0.0;
		foreach (var (item, basePosition, _) in _enterItems)
		{
			item.Position = profile.AllowDirectionalMotion
				? basePosition - new Vector2(profile.ContentShift, 0f)
				: basePosition;
			item.Modulate = new Color(1f, 1f, 1f, 0f);
		}
		SetProcess(true);
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

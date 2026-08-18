using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse;

/// <summary>Branded home screen with a fixed 1920x1080 action rail.</summary>
public partial class Main : Node2D
{
	private readonly List<(Control Item, Vector2 BasePosition, int Order)> _enterItems = new();
	private UiMotionProfile? _enterProfile;
	private double _enterElapsed;

	public override void _Ready()
	{
		GameSession.EnsureInit();
		GD.Print($"Dynamite Universe running on Godot {Engine.GetVersionInfo()["string"]}");

		UiLayout.AddBackground(this);

		var energyCore = new HomeEnergyReactor
		{
			Position = new Vector2(915, 92),
			Size = new Vector2(900, 830),
		};
		AddChild(energyCore);
		RegisterEntrance(energyCore, 0);

		var eyebrow = new Label
		{
			Position = new Vector2(176, 194),
			Size = new Vector2(600, 34),
			Text = "COMMUNITY RHYTHM SYSTEM  /  DU-01",
		};
		UiLabels.Tech(eyebrow, 18, UiFonts.Cyan);
		AddChild(eyebrow);
		RegisterEntrance(eyebrow, 0);

		var titlePrimary = new Label
		{
			Position = new Vector2(166, 238),
			Size = new Vector2(790, 126),
			Text = "DYNAMITE",
		};
		UiLabels.TechBold(titlePrimary, 112, UiFonts.Text);
		AddChild(titlePrimary);
		RegisterEntrance(titlePrimary, 1);

		var titleSecondary = new Label
		{
			Position = new Vector2(166, 342),
			Size = new Vector2(790, 112),
			Text = "UNIVERSE",
		};
		UiLabels.TechBold(titleSecondary, 96, UiFonts.Cyan);
		AddChild(titleSecondary);
		RegisterEntrance(titleSecondary, 1);

		var accentRail = new ColorRect
		{
			Position = new Vector2(176, 466),
			Size = new Vector2(176, 4),
			Color = UiFonts.Pink,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(accentRail);
		RegisterEntrance(accentRail, 2);

		var sub = new Label
		{
			Position = new Vector2(176, 486),
			Size = new Vector2(700, 34),
			Text = "CLEAN-ROOM COMMUNITY RHYTHM GAME",
		};
		UiLabels.Tech(sub, 22, UiFonts.Dim);
		AddChild(sub);
		RegisterEntrance(sub, 2);

		var actionPanel = new CutPanel
		{
			Position = new Vector2(152, 548),
			Size = new Vector2(746, 390),
			Cut = 28,
			Fill = new Color(0.025f, 0.045f, 0.105f, 0.88f),
			Border = new Color(UiFonts.Line, 0.92f),
			BorderWidth = 2f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(actionPanel);
		RegisterEntrance(actionPanel, 3);

		var actionLabel = new Label
		{
			Position = new Vector2(190, 576),
			Size = new Vector2(650, 28),
			Text = "SELECT DESTINATION",
		};
		UiLabels.Tech(actionLabel, 16, UiFonts.Dim);
		AddChild(actionLabel);
		RegisterEntrance(actionLabel, 3);

		var play = new CutButton
		{
			Position = new Vector2(188, 620),
			Size = new Vector2(674, 112),
			Text = "游玩",
			SubText = "PLAY  /  ENTER SONG LIBRARY",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 36,
			TechFont = true,
			AlignLeft = true,
		};
		play.Pressed += () => TransitionDirector.Navigate(
			UiRoutes.SongSelect,
			TransitionKind.Standard,
			"OPEN CHANNEL",
			"SONG LIBRARY");
		AddChild(play);
		RegisterEntrance(play, 4);

		var workshop = new CutButton
		{
			Position = new Vector2(188, 752),
			Size = new Vector2(325, 142),
			Text = "谱面工坊",
			SubText = "WORKSHOP  /  OFFLINE",
			FontSize = 28,
			TechFont = true,
			AlignLeft = true,
			Disabled = true,
		};
		AddChild(workshop);
		RegisterEntrance(workshop, 5);

		var settings = new CutButton
		{
			Position = new Vector2(537, 752),
			Size = new Vector2(325, 142),
			Text = "设置",
			SubText = "SETTINGS  /  SYSTEM",
			FontSize = 28,
			TechFont = true,
			AlignLeft = true,
		};
		settings.Pressed += () => TransitionDirector.Navigate(
			UiRoutes.Settings,
			TransitionKind.Standard,
			"OPEN CHANNEL",
			"SETTINGS");
		AddChild(settings);
		RegisterEntrance(settings, 5);

		var ver = new Label
		{
			Position = new Vector2(1240, 1014),
			Size = new Vector2(620, 30),
			Text = BuildDescription(),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		UiLabels.Tech(ver, 16, UiFonts.Dim);
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

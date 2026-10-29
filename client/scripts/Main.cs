using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse;

/// <summary>
/// Branded home screen: Penpot「Game — Home v2 · 01 Compact」的固定 1920×1080 布局，
/// 右侧 Mk VI 核心模型 + 弥散光，左侧品牌块与动作区。只协调装配与入场，控件细节在 ui/home。
/// </summary>
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

		// 参考环先画，模型与其弥散光覆盖在上面。
		var reticle = new HomeReticle
		{
			Position = new Vector2(870, 70),
			Size = new Vector2(1050, 960),
		};
		AddChild(reticle);
		RegisterEntrance(reticle, 0);

		var energyCore = new HomeEnergyReactor
		{
			Position = new Vector2(900, 90),
			Size = new Vector2(1000, 920),
		};
		AddChild(energyCore);
		RegisterEntrance(energyCore, 0);

		HomeBrandBlock.Build(this, RegisterEntrance);
		HomeActions.Build(this, RegisterEntrance);
		HomeUtilityRow.Build(this, BuildDescription(), RegisterEntrance);

		SetProcess(false);
		PrepareEnterAnimation();
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

	/// <summary>
	/// 在把场景交给 TransitionDirector 之前先把入场元素藏起来。转场覆盖层退场时会露出整个
	/// 场景：若此时元素还是终态，就会先完整显示一帧、再归零重播左侧渐入（从别的页面返回首页
	/// 时看到的闪烁）。与 Settings / SongSelect / Calibration 的 PrepareEnterAnimation 同一约定。
	/// </summary>
	private void PrepareEnterAnimation()
	{
		var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
		if (!profile.IsAnimated)
			return;
		foreach (var (item, basePosition, _) in _enterItems)
		{
			item.Modulate = new Color(1f, 1f, 1f, 0f);
			if (profile.AllowDirectionalMotion)
				item.Position = basePosition - new Vector2(profile.ContentShift, 0f);
		}
	}

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
		SetProcess(true);
	}

	private static string BuildDescription()
	{
		if (OS.HasFeature("internal_testdata"))
			return "v0.1.2 · INTERNAL TESTDATA · DO NOT DISTRIBUTE";
		if (OS.HasFeature("public_release"))
			return "v0.1.2 · clean-room public build · no bundled charts";
		if (OS.HasFeature("editor"))
			return "v0.1.2-dev · editor/development build";
		return "v0.1.2 · UNCLASSIFIED EXPORT · DO NOT DISTRIBUTE";
	}
}

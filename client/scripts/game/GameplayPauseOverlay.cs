using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>
/// Owns the gameplay pause layer: paused state, entry/exit motion, action gating and the three
/// menu buttons. The host keeps pointer cleanup, playback stop/resume, navigation and results.
/// </summary>
internal sealed class GameplayPauseOverlay
{
	private readonly Node _host;
	private readonly Func<UiMotionProfile> _motion;
	private readonly Func<bool> _finished;
	private readonly Action _onSuspend;
	private readonly Action _onResume;
	private readonly Action _onRestart;
	private readonly Action _onExitToSelect;

	private readonly Control _layer;
	private readonly ColorRect _blocker;
	private readonly Control _content;
	private readonly CutButton _resumeButton;
	private readonly CutButton _retryButton;
	private readonly CutButton _quitButton;
	private Tween? _tween;
	private bool _motionInFlight;

	internal GameplayPauseOverlay(Node host, Func<UiMotionProfile> motion, Func<bool> finished,
		Action onSuspend, Action onResume, Action onRestart, Action onExitToSelect)
	{
		_host = host;
		_motion = motion;
		_finished = finished;
		_onSuspend = onSuspend;
		_onResume = onResume;
		_onRestart = onRestart;
		_onExitToSelect = onExitToSelect;

		_layer = new Control
		{
			Visible = false,
			ZIndex = 60,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		host.AddChild(_layer);

		// Blocker/dim intentionally stays outside the animated content root so input is
		// intercepted over the full screen throughout pause entry and exit.
		_blocker = new ColorRect
		{
			Color = new Color(0.02f, 0.03f, 0.07f, 0.72f),
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_layer.AddChild(_blocker);

		_content = new Control
		{
			Name = "PauseContent",
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_layer.AddChild(_content);

		_content.AddChild(new CutPanel
		{
			Position = new Vector2(610, 300),
			Size = new Vector2(700, 480),
		});

		var title = new Label
		{
			Position = new Vector2(610, 340),
			Size = new Vector2(700, 70),
			Text = "PAUSED",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontOverride("font", UiFonts.Tech);
		title.AddThemeFontSizeOverride("font_size", 52);
		title.AddThemeColorOverride("font_color", UiFonts.Text);
		_content.AddChild(title);

		_resumeButton = new CutButton
		{
			Position = new Vector2(770, 450),
			Size = new Vector2(380, 76),
			Text = "继续",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 28,
		};
		_resumeButton.Pressed += Toggle;
		_content.AddChild(_resumeButton);

		_retryButton = new CutButton
		{
			Position = new Vector2(770, 546),
			Size = new Vector2(380, 76),
			Text = "重开",
			FontSize = 28,
		};
		_retryButton.Pressed += () =>
		{
			if (!_motionInFlight)
				_onRestart();
		};
		_content.AddChild(_retryButton);

		_quitButton = new CutButton
		{
			Position = new Vector2(770, 642),
			Size = new Vector2(380, 76),
			Text = "返回选曲",
			FontSize = 28,
		};
		_quitButton.Pressed += () =>
		{
			if (!_motionInFlight)
				_onExitToSelect();
		};
		_content.AddChild(_quitButton);
	}

	/// <summary>Gameplay reads this instead of keeping its own pause flag.</summary>
	internal bool IsPaused { get; private set; }

	internal void Toggle()
	{
		if (_finished() || _motionInFlight)
			return;

		if (!IsPaused)
			BeginEntry(_motion());
		else
			BeginExit(_motion());
	}

	/// <summary>Clears the paused flag only; navigation owns hiding, visuals and playback.</summary>
	internal void ResetPausedForNavigation() => IsPaused = false;

	/// <summary>
	/// Tears the layer down for the result screen without resuming playback, so the song tail
	/// keeps playing beneath the shutter.
	/// </summary>
	internal void ForceCloseForResult()
	{
		_tween?.Kill();
		_tween = null;
		_motionInFlight = false;
		_layer.Visible = false;
	}

	private void BeginEntry(UiMotionProfile motion)
	{
		// Gameplay must stop before either visual begins; the host clears contacts before the
		// clock stops so pause never leaves held pointers alive across the menu.
		IsPaused = true;
		_onSuspend();
		_motionInFlight = motion.IsAnimated;
		SetActionsEnabled(!motion.IsAnimated);
		_layer.Visible = true;
		_blocker.Modulate = new Color(1f, 1f, 1f, motion.IsAnimated ? 0f : 1f);
		_content.Modulate = new Color(1f, 1f, 1f, motion.IsAnimated ? 0f : 1f);
		_content.Position = motion.IsAnimated && motion.AllowDirectionalMotion
			? new Vector2(0f, motion.ContentShift)
			: Vector2.Zero;

		if (!motion.IsAnimated)
			return;

		_tween?.Kill();
		_tween = _host.CreateTween()
			.SetTrans(Tween.TransitionType.Expo)
			.SetEase(Tween.EaseType.Out);
		_tween.TweenProperty(
			_blocker, "modulate:a", 1f, motion.PanelDuration);
		_tween.Parallel().TweenProperty(
			_content, "modulate:a", 1f, motion.PanelDuration);
		_tween.Parallel().TweenProperty(
			_content, "position", Vector2.Zero, motion.PanelDuration);
		_tween.TweenCallback(Callable.From(FinishEntry));
	}

	private void FinishEntry()
	{
		_tween = null;
		_motionInFlight = false;
		SetActionsEnabled(true);
	}

	private void BeginExit(UiMotionProfile motion)
	{
		// The paused flag and playback stay stopped until FinishExit, including Reduced.
		if (!motion.IsAnimated)
		{
			FinishExit();
			return;
		}

		_motionInFlight = true;
		SetActionsEnabled(false);
		var targetPosition = motion.AllowDirectionalMotion
			? new Vector2(0f, motion.ContentShift)
			: Vector2.Zero;
		_tween?.Kill();
		_tween = _host.CreateTween()
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.In);
		_tween.TweenProperty(
			_blocker, "modulate:a", 0f, motion.PanelDuration);
		_tween.Parallel().TweenProperty(
			_content, "modulate:a", 0f, motion.PanelDuration);
		_tween.Parallel().TweenProperty(
			_content, "position", targetPosition, motion.PanelDuration);
		_tween.TweenCallback(Callable.From(FinishExit));
	}

	private void FinishExit()
	{
		_tween = null;
		_layer.Visible = false;
		_blocker.Modulate = Colors.White;
		_content.Modulate = Colors.White;
		_content.Position = Vector2.Zero;
		_motionInFlight = false;
		IsPaused = false;
		SetActionsEnabled(true);
		_onResume();
	}

	private void SetActionsEnabled(bool enabled)
	{
		_resumeButton.Disabled = !enabled;
		_retryButton.Disabled = !enabled;
		_quitButton.Disabled = !enabled;
	}
}

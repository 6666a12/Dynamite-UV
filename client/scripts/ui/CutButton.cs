using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 切角按钮：样式稿的 clip-path 按钮（描边款 / 实心青款），自绘多边形+文字，
/// 悬停高亮，支持主副两行文字。Godot 原生 Button 画不了切角，故自定义。
/// </summary>
public partial class CutButton : Control
{
	public enum ButtonStyle { Outline, Solid }

	private string _text = "";
	private string _subText = "";
	private ButtonStyle _styleKind = ButtonStyle.Outline;
	private Color _accent = UiFonts.Cyan;
	private bool _techFont;
	private bool _disabled;
	private bool _alignLeft;
	private int _fontSize = 28;
	private float _cut = 14f;
	private bool _hover;
	private float _hoverAmount;
	private float _pressAmount;
	private float _commitAmount;
	private double _commitElapsed;
	private double _commitDuration;
	private Vector2[]? _points;
	private Vector2[]? _closedPoints;

	[Signal]
	public delegate void PressedEventHandler();

	public string Text { get => _text; set { _text = value; Refresh(); } }
	public string SubText { get => _subText; set { _subText = value; Refresh(); } }
	public ButtonStyle StyleKind { get => _styleKind; set { _styleKind = value; Refresh(); } }
	public Color Accent { get => _accent; set { _accent = value; Refresh(); } }
	public bool TechFont { get => _techFont; set { _techFont = value; Refresh(); } }
	public bool Disabled
	{
		get => _disabled;
		set
		{
			_disabled = value;
			SnapInteractionIfMotionIsOff();
			Refresh();
		}
	}
	public bool AlignLeft { get => _alignLeft; set { _alignLeft = value; Refresh(); } }
	public int FontSize { get => _fontSize; set { _fontSize = value; Refresh(); } }

	/// <summary>切角尺寸：样式稿默认 14px，96px 触控键用 18px。</summary>
	public float Cut
	{
		get => _cut;
		set
		{
			_cut = value;
			_points = null;
			_closedPoints = null;
			Refresh();
		}
	}

	public override void _Ready()
	{
		_hoverAmount = HoverTarget;
		MouseDefaultCursorShape = CursorShape.PointingHand;
		MouseEntered += OnMouseEntered;
		MouseExited += OnMouseExited;
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized)
		{
			_points = null;
			_closedPoints = null;
		}
	}

	public override void _Process(double delta)
	{
		var profile = MotionProfile;
		var nextHover = Advance(_hoverAmount, HoverTarget, delta, profile.HoverDuration);
		var nextPress = Advance(_pressAmount, 0f, delta, profile.PressDuration);
		var nextCommit = _commitAmount;
		if (_commitDuration > 0.0)
		{
			_commitElapsed += delta;
			var progress = Mathf.Clamp((float)(_commitElapsed / _commitDuration), 0f, 1f);
			nextCommit = Mathf.Sin(progress * Mathf.Pi);
			if (progress >= 1f)
			{
				_commitDuration = 0.0;
				nextCommit = 0f;
			}
		}
		if (nextHover == _hoverAmount && nextPress == _pressAmount &&
			nextCommit == _commitAmount)
			return;

		_hoverAmount = nextHover;
		_pressAmount = nextPress;
		_commitAmount = nextCommit;
		QueueRedraw();
	}

	/// <summary>Plays a focus confirmation without changing the button hit box or click timing.</summary>
	public void CommitPulse(double? duration = null)
	{
		var profile = MotionProfile;
		if (!profile.IsAnimated)
		{
			_commitAmount = 0f;
			return;
		}
		_commitElapsed = 0.0;
		_commitDuration = Math.Max(0.01, duration ?? profile.FocusDuration);
		_commitAmount = 0f;
		QueueRedraw();
	}

	public void Refresh()
	{
		if (IsInsideTree())
			QueueRedraw();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (_disabled)
			return;
		var pressed = e is InputEventMouseButton
			{
				ButtonIndex: MouseButton.Left,
				Pressed: true,
			} or InputEventScreenTouch { Pressed: true };
		if (pressed)
		{
			TriggerPress();
			AcceptEvent();
			EmitSignal(SignalName.Pressed);
		}
	}

	public override void _Draw()
	{
		var profile = MotionProfile;
		var hover = UiEase.Standard(profile.IsAnimated ? _hoverAmount : HoverTarget);
		var press = !_disabled && profile.IsAnimated ? UiEase.Standard(_pressAmount) : 0f;
		var commit = profile.IsAnimated ? UiEase.Echo(_commitAmount) : 0f;
		var cut = Mathf.Min(_cut, Mathf.Min(Size.X, Size.Y) * 0.4f);
		Vector2[] pts = _points ??= UiGeometry.CutCorners(Size, cut);

		Color fill, border, fg;
		if (_disabled)
		{
			fill = new Color(0.08f, 0.10f, 0.18f, 0.6f);
			border = new Color(0.2f, 0.23f, 0.35f);
			fg = new Color(0.35f, 0.40f, 0.55f);
		}
		else if (_styleKind == ButtonStyle.Solid)
		{
			fill = _accent.Lerp(_accent.Lerp(Colors.White, 0.18f), hover);
			border = fill;
			fg = UiFonts.InkText;
		}
		else
		{
			fill = new Color(0.06f, 0.08f, 0.16f, 0.85f)
				.Lerp(UiFonts.PanelHover, hover);
			border = UiFonts.Line.Lerp(_accent, hover);
			fg = UiFonts.Text.Lerp(_accent, hover);
		}

		if (press > 0f || commit > 0f)
		{
			var confirmation = Mathf.Max(press, commit);
			fill = fill.Lerp(Colors.White, 0.10f * confirmation);
			border = border.Lerp(Colors.White, 0.42f * confirmation);
			fg = fg.Lerp(Colors.White, 0.16f * confirmation);
		}

		DrawColoredPolygon(pts, fill);
		if (profile.AllowDirectionalMotion && hover > 0f && !_disabled)
		{
			var locatorHeight = Mathf.Min(28f, Size.Y - cut * 2f) * hover;
			var locatorColor = _styleKind == ButtonStyle.Solid ? UiFonts.InkText : _accent;
			locatorColor.A *= 0.72f * hover;
			DrawRect(new Rect2(2f, (Size.Y - locatorHeight) * 0.5f, 3f, locatorHeight),
				locatorColor);
		}

		var closed = _closedPoints ??= UiGeometry.Close(pts);
		DrawPolyline(closed, border, 2f, true);
		if (profile.AllowDirectionalMotion && (press > 0f || commit > 0f))
		{
			var trace = Mathf.Max(press, commit);
			DrawPolyline(closed, new Color(1f, 1f, 1f, 0.55f * trace), 1f + trace, true);
		}

		var font = _techFont ? UiFonts.TechBold : UiFonts.Cjk;
		var hAlign = _alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Center;
		var textShift = profile.AllowDirectionalMotion ? 3f * hover : 0f;
		var textX = (_alignLeft ? 28f : 0f) + textShift;
		var textW = _alignLeft ? Size.X - 56f : Size.X;
		if (_subText.Length > 0)
		{
			var subSize = Mathf.Max(12, (int)(_fontSize * 0.5f));
			var yMain = Size.Y * 0.40f + _fontSize * 0.34f;
			DrawString(font, new Vector2(textX, yMain), _text,
				hAlign, textW, _fontSize, fg);
			var fgSub = _styleKind == ButtonStyle.Solid && !_disabled
				? new Color(fg, 0.65f)
				: (_disabled ? fg : UiFonts.Dim);
			DrawString(font, new Vector2(textX, yMain + subSize + 8), _subText,
				hAlign, textW, subSize, fgSub);
		}
		else
		{
			DrawString(font, new Vector2(textX, Size.Y * 0.5f + _fontSize * 0.35f), _text,
				hAlign, textW, _fontSize, fg);
		}
	}

	private UiMotionProfile MotionProfile => UiMotionProfile.For(GameSession.Settings.MotionMode);
	private float HoverTarget => !_disabled && _hover ? 1f : 0f;

	private void OnMouseEntered() => SetHover(true);
	private void OnMouseExited() => SetHover(false);

	private void SetHover(bool hover)
	{
		if (_hover == hover)
			return;
		_hover = hover;
		SnapInteractionIfMotionIsOff();
		Refresh();
	}

	private void TriggerPress()
	{
		_pressAmount = MotionProfile.IsAnimated ? 1f : 0f;
		Refresh();
	}

	private void SnapInteractionIfMotionIsOff()
	{
		if (MotionProfile.IsAnimated)
			return;
		_hoverAmount = HoverTarget;
		_pressAmount = 0f;
	}

	private static float Advance(float current, float target, double delta, double duration) =>
		duration <= 0.0 ? target : Mathf.MoveToward(current, target, (float)(delta / duration));
}

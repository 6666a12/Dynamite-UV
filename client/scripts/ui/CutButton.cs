using Godot;

namespace DuxCommunity.Ui;

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

	[Signal]
	public delegate void PressedEventHandler();

	public string Text { get => _text; set { _text = value; Refresh(); } }
	public string SubText { get => _subText; set { _subText = value; Refresh(); } }
	public ButtonStyle StyleKind { get => _styleKind; set { _styleKind = value; Refresh(); } }
	public Color Accent { get => _accent; set { _accent = value; Refresh(); } }
	public bool TechFont { get => _techFont; set { _techFont = value; Refresh(); } }
	public bool Disabled { get => _disabled; set { _disabled = value; Refresh(); } }
	public bool AlignLeft { get => _alignLeft; set { _alignLeft = value; Refresh(); } }
	public int FontSize { get => _fontSize; set { _fontSize = value; Refresh(); } }

	private bool _hover;

	public override void _Ready()
	{
		MouseDefaultCursorShape = CursorShape.PointingHand;
		MouseEntered += () => { _hover = true; Refresh(); };
		MouseExited += () => { _hover = false; Refresh(); };
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
			AcceptEvent();
			EmitSignal(SignalName.Pressed);
		}
	}

	public override void _Draw()
	{
		var cut = Mathf.Min(14f, Mathf.Min(Size.X, Size.Y) * 0.4f);
		Vector2[] pts =
		{
			new(cut, 0), new(Size.X, 0), new(Size.X, Size.Y - cut),
			new(Size.X - cut, Size.Y), new(0, Size.Y), new(0, cut),
		};

		Color fill, border, fg;
		if (_disabled)
		{
			fill = new Color(0.08f, 0.10f, 0.18f, 0.6f);
			border = new Color(0.2f, 0.23f, 0.35f);
			fg = new Color(0.35f, 0.40f, 0.55f);
		}
		else if (_styleKind == ButtonStyle.Solid)
		{
			fill = _hover ? _accent.Lerp(Colors.White, 0.18f) : _accent;
			border = fill;
			fg = UiFonts.InkText;
		}
		else
		{
			fill = _hover ? UiFonts.PanelHover : new Color(0.06f, 0.08f, 0.16f, 0.85f);
			border = _hover ? _accent : UiFonts.Line;
			fg = _hover ? _accent : UiFonts.Text;
		}

		DrawColoredPolygon(pts, fill);
		var closed = new Vector2[pts.Length + 1];
		pts.CopyTo(closed, 0);
		closed[^1] = pts[0];
		DrawPolyline(closed, border, 2f, true);

		var font = _techFont ? UiFonts.TechBold : UiFonts.Cjk;
		var hAlign = _alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Center;
		var textX = _alignLeft ? 28f : 0f;
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
}

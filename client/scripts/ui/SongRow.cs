using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 选曲列表行（样式稿）：左对齐曲名+曲师，右侧彩色难度徽章（切角小块+等级数字）。
/// </summary>
public partial class SongRow : Control
{
    private ChartPack? _pack;
    private bool _selected;
    private bool _hover;
    private float _hoverAmount;
    private float _selectedAmount;
    private float _commitAmount;
    private double _commitElapsed;
    private double _commitDuration;

    [Signal]
    public delegate void PressedEventHandler();

    public ChartPack? Pack { get => _pack; set { _pack = value; Refresh(); } }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            SnapInteractionIfMotionIsOff();
            Refresh();
        }
    }

    public override void _Ready()
    {
        _hoverAmount = _hover ? 1f : 0f;
        _selectedAmount = _selected ? 1f : 0f;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public override void _Process(double delta)
    {
        var profile = MotionProfile;
        var nextHover = Advance(_hoverAmount, _hover ? 1f : 0f, delta, profile.HoverDuration);
        var nextSelected = Advance(_selectedAmount, _selected ? 1f : 0f,
            delta, profile.ValueDuration);
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
        if (nextHover == _hoverAmount && nextSelected == _selectedAmount &&
            nextCommit == _commitAmount)
            return;

        _hoverAmount = nextHover;
        _selectedAmount = nextSelected;
        _commitAmount = nextCommit;
        QueueRedraw();
    }

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
        var pressed = e is InputEventMouseButton
        {
            ButtonIndex: MouseButton.Left,
            Pressed: true,
        };
        if (pressed)
        {
            AcceptEvent();
            EmitSignal(SignalName.Pressed);
        }
    }

    public override void _Draw()
    {
        var profile = MotionProfile;
        var hover = UiEase.Standard(profile.IsAnimated ? _hoverAmount : (_hover ? 1f : 0f));
        var selected = UiEase.Standard(profile.IsAnimated ? _selectedAmount : (_selected ? 1f : 0f));
        var commit = profile.IsAnimated ? UiEase.Echo(_commitAmount) : 0f;
        var active = Mathf.Max(Mathf.Max(hover, selected), commit);
        const float cut = 10f;
        Vector2[] pts = UiGeometry.CutCorners(Size, cut);
        var idleFill = new Color(0.06f, 0.08f, 0.16f, 0.35f);
        var hoverFill = new Color(UiFonts.PanelHover, 0.6f);
        var fill = idleFill.Lerp(hoverFill, hover)
            .Lerp(new Color("18244a"), selected)
            .Lerp(new Color("27496d"), commit * 0.44f);
        DrawColoredPolygon(pts, fill);

        if (active > 0f)
        {
            var closed = UiGeometry.Close(pts);
            var border = UiFonts.Line.Lerp(UiFonts.Cyan, Mathf.Max(selected, commit));
            border.A *= active;
            DrawPolyline(closed, border, 2f, true);
        }

        if (profile.AllowDirectionalMotion && selected > 0f)
        {
            var locatorHeight = Mathf.Min(62f, Size.Y - cut * 2f) * selected;
            var locatorColor = new Color(UiFonts.Cyan, 0.92f * selected);
            DrawRect(new Rect2(2f, (Size.Y - locatorHeight) * 0.5f,
                4f + 2f * selected, locatorHeight), locatorColor);
        }

        if (_pack == null)
            return;

        var textShift = profile.AllowDirectionalMotion ? 8f * selected : 0f;
        var textX = 24f + textShift;

        // 左：曲名 + 曲师·谱师
        DrawString(UiFonts.Cjk, new Vector2(textX, Size.Y * 0.44f), _pack.Title,
            HorizontalAlignment.Left, Size.X * 0.55f, 30, UiFonts.Text);
        DrawString(UiFonts.Cjk, new Vector2(textX, Size.Y * 0.78f),
            $"{_pack.Artist} · 谱师 {_pack.Charter}",
            HorizontalAlignment.Left, Size.X * 0.55f, 18, UiFonts.Dim);

        // 右：难度徽章（从右往左排，giga 在最右）
        const float chipW = 46f, chipH = 32f, gap = 8f;
        var x = Size.X - 20f;
        var y = (Size.Y - chipH) * 0.5f;
        for (var i = _pack.Charts.Count - 1; i >= 0; i--)
        {
            var chart = _pack.Charts[i];
            var badge = chart.Level is { } level
                ? level.ToString()
                : chart.Unrated ? "—" : "?";
            x -= chipW;
            var col = UiFonts.DiffColor(chart.Difficulty);
            const float cc = 6f;
            Vector2[] chip =
            {
                new(x + cc, y), new(x + chipW, y), new(x + chipW, y + chipH - cc),
                new(x + chipW - cc, y + chipH), new(x, y + chipH), new(x, y + cc),
            };
            DrawColoredPolygon(chip, col);
            DrawString(UiFonts.TechBold, new Vector2(x, y + chipH * 0.5f + 7f),
                badge, HorizontalAlignment.Center, chipW, 19, UiFonts.InkText);
            x -= gap;
        }
    }

    private UiMotionProfile MotionProfile => UiMotionProfile.For(GameSession.Settings.MotionMode);

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

    private void SnapInteractionIfMotionIsOff()
    {
        if (MotionProfile.IsAnimated)
            return;
        _hoverAmount = _hover ? 1f : 0f;
        _selectedAmount = _selected ? 1f : 0f;
    }

    private static float Advance(float current, float target, double delta, double duration) =>
        duration <= 0.0 ? target : Mathf.MoveToward(current, target, (float)(delta / duration));
}

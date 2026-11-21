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

    public int Ordinal { get; init; }
    public ChartPack? Pack { get => _pack; set { _pack = value; Refresh(); } }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            AccessibilityDescription = value ? "当前选择" : "";
            RefreshInteraction();
        }
    }

    public override void _Ready()
    {
        AccessibilityName = _pack?.Title ?? "";
        _hoverAmount = _hover ? 1f : 0f;
        _selectedAmount = _selected ? 1f : 0f;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        SetProcess(false);
        FocusMode = FocusModeEnum.All;
        FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public override void _Process(double delta)
    {
        var profile = MotionProfile;
        var nextHover = Advance(_hoverAmount, _hover ? 1f : 0f, delta, UiHoverMotion.Seconds);
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
        if (nextHover == (_hover ? 1f : 0f) && nextSelected == (_selected ? 1f : 0f) && _commitDuration <= 0)
            SetProcess(false);
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
        SetProcess(true);
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
        if (MouseFilter != MouseFilterEnum.Ignore && (pressed || (HasFocus() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space })))
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
        CatalogueCardFace.Draw(this, hover, selected, commit: commit);
        if (HasFocus()) DrawRect(new Rect2(Vector2.One * 2f, Size - Vector2.One * 4f), CatalogueCardFace.Accent, false, 2f);
        if (_pack == null) return;
        DrawString(UiFonts.TechBold, new(28, Size.Y * .47f), Ordinal.ToString("00"), HorizontalAlignment.Left, 48, 20, MenuStyle.Secondary);
        float xTitle = 84f + (profile.AllowDirectionalMotion ? 8f * selected : 0f);
        var byline = _pack.SourceLabel.Length == 0 ? $"{_pack.Artist} · 谱师 {_pack.Charter}" : $"{_pack.SourceLabel} · {_pack.Artist}";
        CatalogueCardFace.DrawHeading(this, _pack.Title, byline, xTitle);

        // 右：难度徽章（从右往左排，giga 在最右）
        const float chipW = 44f, chipH = 34f, gap = 8f;
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
            const float cc = 5f;
            Vector2[] chip =
            {
                new(x + cc, y), new(x + chipW, y), new(x + chipW, y + chipH - cc),
                new(x + chipW - cc, y + chipH), new(x, y + chipH), new(x, y + cc),
            };
            DrawColoredPolygon(chip, col);
            DrawString(UiFonts.TechBold, new Vector2(x, y + MenuStyle.Baseline(UiFonts.TechBold, 18, chipH)),
                badge, HorizontalAlignment.Center, chipW, 18, MenuStyle.Ink);
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
        RefreshInteraction();
    }

    private void RefreshInteraction()
    {
        var animate = IsVisibleInTree() && MotionProfile.IsAnimated;
        if (!animate)
        {
            // The selected row is replaced by the expanded card. Keep its ink current
            // without processing while hidden, so folding can reveal it immediately.
            _hoverAmount = _hover ? 1f : 0f;
            _selectedAmount = _selected ? 1f : 0f;
        }
        SetProcess(animate);
        Refresh();
    }

    private static float Advance(float current, float target, double delta, double duration) =>
        duration <= 0.0 ? target : Mathf.MoveToward(current, target, (float)(delta / duration));
}

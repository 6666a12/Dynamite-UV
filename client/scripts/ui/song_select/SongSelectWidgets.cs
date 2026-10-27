using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;


// ---- 设计稿专用小控件 ----

/// <summary>只画一个设计稿图标。</summary>
internal partial class GlyphIcon : Control
{
    public UiGlyphKind Glyph { get; init; }
    public Color Color { get; set; } = UiFonts.Text;
    public float Stroke { get; init; } = 2f;

    public override void _Draw() => UiGlyphs.Draw(this, Glyph, Vector2.Zero, Size.X, Color, Stroke);
}

/// <summary>筛选占位块：按设计稿只画描边。</summary>
internal partial class DisabledPlaceholder : Control
{
    public float Cut { get; init; } = 14f;

    public override void _Draw()
    {
        var points = UiGeometry.CutCorners(Size, Cut);
        DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 2f, true);
    }
}

/// <summary>进度条末端 14×14 切角旋钮。</summary>
internal partial class KnobMark : Control
{
    public override void _Draw() =>
        DrawColoredPolygon(UiGeometry.CutCorners(Size, 4f), UiFonts.Cyan);
}

/// <summary>MODE 两段互斥行：选中 = #18244A 填充 + cyan 描边 + 左侧定位条。</summary>
internal partial class QuickModeRow : Control
{
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }

    private bool _selected;
    private bool _hover;
    private UiHoverFade _hoverFade;

    [Signal]
    public delegate void ChosenEventHandler();

    public string Label { get; init; } = "";
    public UiGlyphKind Glyph { get; init; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
        SetProcess(false);
    }

    /// <summary>hover 渐变只在过渡期跑 _Process：落终值即停表，空闲不逐帧重绘
    /// （推进条件与重绘条件必须同为「值未到终值」，任一写反都会退化成空闲无限重绘）。</summary>
    private void SetHover(bool hover)
    {
        if (_hover == hover)
            return;
        _hover = hover;
        if (!MotionProfileProvider().IsAnimated)
            _hoverFade.Snap(hover);
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_hoverFade.Update(_hover, delta))
            QueueRedraw();
        if (_hoverFade.Settled(_hover))
            SetProcess(false);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
            InputEventScreenTouch { Pressed: true })
        {
            EmitSignal(SignalName.Chosen);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var points = UiGeometry.CutCorners(Size, 12f);
        var ink = _selected ? UiFonts.Cyan : UiFonts.Dim;
        if (_selected)
        {
            DrawColoredPolygon(points, new Color("18244a"));
            DrawPolyline(UiGeometry.Close(points), UiFonts.Cyan, 2f, true);
            DrawRect(new Rect2(2, 14, 6, Size.Y - 28), new Color(UiFonts.Cyan, 0.92f));
        }
        else
        {
            if (_hoverFade.Value > 0f)
                DrawColoredPolygon(points, new Color(0.06f, 0.08f, 0.16f, 0.35f)
                    .Lerp(new Color(UiFonts.PanelHover, 0.55f), _hoverFade.Value));
            DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 1.5f, true);
        }

        UiGlyphs.Draw(this, Glyph, new Vector2(24, (Size.Y - 26) * 0.5f), 26, ink, 2f);
        DrawString(UiFonts.TechBold, new Vector2(64, Size.Y * 0.5f + 6.5f), Label,
            HorizontalAlignment.Left, Size.X - 64, 18, ink);
    }
}

/// <summary>108×50 小开关（方块 40）；锁定态整体压暗 + 方块停右侧 60% + 左侧挂锁。</summary>
internal partial class QuickSwitch : Control
{
    internal const float SwitchKnob = 40f;
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }

    private bool _on;
    private bool _locked;
    private float _knob;
    private Tween? _tween;
    private bool _disabled;

    [Signal]
    public delegate void ToggledEventHandler(bool value);

    public bool IsOn => _on;
    public bool Locked => _locked;

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        _knob = _on ? 1f : 0f;
    }

    public void SetState(bool on, bool locked)
    {
        _on = on;
        _locked = locked;
        // 锁定态方块停在右侧 60%（设计稿），解锁后回到正常端点。
        var target = locked ? 0.6f : on ? 1f : 0f;
        if (Math.Abs(_knob - target) < 0.01f)
        {
            QueueRedraw();
            return;
        }
        if (MotionProfileProvider().IsAnimated)
            StartKnobTween(target);
        else
            _knob = target;
        QueueRedraw();
    }

    private void StartKnobTween(float target)
    {
        _tween?.Kill();
        var duration = MotionProfileProvider().ValueDuration;
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(value =>
            {
                _knob = value;
                QueueRedraw();
            }), _knob, target, duration)
            .SetTrans(Tween.TransitionType.Linear);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_locked || _disabled)
            return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
            InputEventScreenTouch { Pressed: true })
        {
            _on = !_on;
            StartKnobTween(_on ? 1f : 0f);
            EmitSignal(SignalName.Toggled, _on);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var dim = _locked ? 0.55f : 1f;
        var track = UiGeometry.CutCorners(Size, 14f);
        if (_on)
        {
            DrawColoredPolygon(track, new Color(UiFonts.Cyan, 0.14f * dim));
            DrawPolyline(UiGeometry.Close(track), new Color(UiFonts.Cyan, dim), 2.5f, true);
        }
        else
        {
            DrawPolyline(UiGeometry.Close(track), new Color(UiFonts.Line, dim), 2.5f, true);
        }

        var knobX = 12f + _knob * (Size.X - 24f - SwitchKnob);
        var knob = UiGeometry.CutCorners(knobX, (Size.Y - SwitchKnob) * 0.5f, SwitchKnob, SwitchKnob, 10f);
        DrawColoredPolygon(knob, _on ? new Color(UiFonts.Cyan, dim) : new Color(UiFonts.Dim, 0.55f));
        if (_locked)
            UiGlyphs.Draw(this, UiGlyphKind.Lock, new Vector2(12f, Size.Y * 0.5f - 10f), 20,
                new Color(UiFonts.Cyan, 0.85f), 1.8f);
    }
}

/// <summary>「全部设置」入口：滑杆图标 + 主副两行。</summary>
internal partial class QuickActionRow : Control
{
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }

    private bool _hover;
    private UiHoverFade _hoverFade;

    [Signal]
    public delegate void ChosenEventHandler();

    public UiGlyphKind Glyph { get; init; }
    public string Text { get; init; } = "";
    public string SubText { get; init; } = "";

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
        SetProcess(false);
    }

    /// <summary>hover 渐变只在过渡期跑 _Process，落终值即停表（同 QuickModeRow 的约定）。</summary>
    private void SetHover(bool hover)
    {
        if (_hover == hover)
            return;
        _hover = hover;
        if (!MotionProfileProvider().IsAnimated)
            _hoverFade.Snap(hover);
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_hoverFade.Update(_hover, delta))
            QueueRedraw();
        if (_hoverFade.Settled(_hover))
            SetProcess(false);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
            InputEventScreenTouch { Pressed: true })
        {
            EmitSignal(SignalName.Chosen);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var points = UiGeometry.CutCorners(Size, 14f);
        var hover = _hoverFade.Value;
        DrawColoredPolygon(points, new Color(0.06f, 0.08f, 0.16f, 0.85f)
            .Lerp(new Color(UiFonts.PanelHover, 0.9f), hover));
        DrawPolyline(UiGeometry.Close(points),
            UiFonts.Line.Lerp(UiFonts.Cyan, hover), 2f, true);
        UiGlyphs.Draw(this, Glyph, new Vector2(26, (Size.Y - 26) * 0.5f), 26, UiFonts.Cyan, 2.2f);
        DrawString(UiFonts.Cjk, new Vector2(72, Size.Y * 0.5f - 2f), Text,
            HorizontalAlignment.Left, Size.X - 88, 24, UiFonts.Text);
        DrawString(UiFonts.Tech, new Vector2(72, Size.Y * 0.5f + 24f), SubText,
            HorizontalAlignment.Left, Size.X - 88, 13, UiFonts.Dim);
    }
}

/// <summary>64×64 预览传输键：描边 + cyan 图标，图标在暂停/播放之间切换。</summary>
internal partial class PreviewTransport : Control
{
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }

    private bool _playing;
    private bool _disabled;
    private bool _hover;
    private UiHoverFade _hoverFade;

    [Signal]
    public delegate void PressedEventHandler();

    public bool Disabled
    {
        get => _disabled;
        set
        {
            _disabled = value;
            QueueRedraw();
        }
    }

    public void SetPlaying(bool playing)
    {
        if (_playing == playing)
            return;
        _playing = playing;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
        SetProcess(false);
    }

    /// <summary>hover 渐变只在过渡期跑 _Process（同 QuickModeRow 的约定）。</summary>
    private void SetHover(bool hover)
    {
        if (_hover == hover)
            return;
        _hover = hover;
        if (!MotionProfileProvider().IsAnimated)
            _hoverFade.Snap(hover);
        SetProcess(true);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_hoverFade.Update(_hover, delta))
            QueueRedraw();
        if (_hoverFade.Settled(_hover))
            SetProcess(false);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_disabled)
            return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
            InputEventScreenTouch { Pressed: true })
        {
            EmitSignal(SignalName.Pressed);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var points = UiGeometry.CutCorners(Size, 18f);
        var ink = _disabled ? new Color(UiFonts.Dim, 0.5f) : UiFonts.Cyan;
        DrawColoredPolygon(points, new Color(0.06f, 0.08f, 0.16f, 0.85f));
        DrawPolyline(UiGeometry.Close(points), _disabled
            ? new Color(UiFonts.Line, 0.6f)
            : new Color(UiFonts.Cyan, 0.85f).Lerp(UiFonts.Cyan, _hoverFade.Value), 2f, true);
        UiGlyphs.Draw(this, _playing ? UiGlyphKind.Pause : UiGlyphKind.Play,
            new Vector2((Size.X - 24) * 0.5f, (Size.Y - 24) * 0.5f), 24, ink, 2f);
    }
}

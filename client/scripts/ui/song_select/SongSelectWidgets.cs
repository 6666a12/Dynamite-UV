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

/// <summary>The real progress head remains readable in every mode; the powered state is steady, not a loop.</summary>
internal partial class KnobMark : Control
{
    private bool _powered;
    internal void SetPlaying(bool playing)
    {
        var on = playing && MenuContactFeedback.Mode != UiMotionMode.Off;
        if (on == _powered) return;
        _powered = on; QueueRedraw();
    }
    public override void _Draw()
    {
        var ink = CatalogueCardFace.Accent;
        if (_powered) DrawCircle(Size*.5f,12f,new Color(ink,.17f),true,-1,true);
        DrawRect(new Rect2(Vector2.Zero, Size), ink);
        if (_powered) DrawRect(new Rect2(Size*.25f,Size*.5f), ink.Lerp(Colors.White,.72f));
    }
}

/// <summary>64×64 预览传输键：中性图标与深色金属底板，图标在暂停/播放之间切换。</summary>
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
        FocusMode = FocusModeEnum.All; FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
        SetProcess(false);
    }

    /// <summary>hover 渐变只在过渡期跑 _Process。</summary>
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
            InputEventScreenTouch { Pressed: true } ||
            (HasFocus() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space }))
        {
            EmitSignal(SignalName.Pressed);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var ink = _disabled ? new Color(MenuStyle.Secondary, 0.5f) : MenuStyle.Text;
        DrawCircle(Size * 0.5f, Size.X * 0.5f, _disabled ? new Color(MenuStyle.Idle, 0.5f)
            : new Color("30596B").Lerp(MenuStyle.Active, _hoverFade.Value));
        if (HasFocus()) DrawArc(Size * .5f, Size.X * .5f - 2, 0, Mathf.Tau, 50, CatalogueCardFace.Accent, 2f, true);
        UiGlyphs.Draw(this, _playing ? UiGlyphKind.Pause : UiGlyphKind.Play,
            new Vector2((Size.X - 24) * 0.5f, (Size.Y - 24) * 0.5f), 24, ink, 2f);
    }
}

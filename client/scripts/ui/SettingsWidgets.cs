using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;


// ---- 设计稿专用控件 ----

/// <summary>竖排 tab：选中 = panel 填充 + 左侧 cyan 竖条，未选中 = 1.5px 描边。</summary>
internal partial class SettingsTabItem : Control
{
    private bool _selected;
    private bool _hover;

    [Signal]
    public delegate void ChosenEventHandler();

    public string Text { get; set; } = "";

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
        MouseEntered += () =>
        {
            _hover = true;
            QueueRedraw();
        };
        MouseExited += () =>
        {
            _hover = false;
            QueueRedraw();
        };
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
        if (_selected)
        {
            DrawColoredPolygon(points, new Color(UiFonts.Panel, 0.95f));
            DrawRect(new Rect2(0f, 14f, 8f, Size.Y - 14f), UiFonts.Cyan);
        }
        else
        {
            if (_hover)
                DrawColoredPolygon(points, new Color(UiFonts.PanelHover, 0.55f));
            DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 1.5f, true);
        }

        DrawString(_selected ? UiFonts.TechBold : UiFonts.Tech,
            new Vector2(40f, Size.Y * 0.5f + 8.4f), Text,
            HorizontalAlignment.Left, Size.X - 40f, 24,
            _selected ? UiFonts.Text : UiFonts.Dim);
    }
}

/// <summary>132×60 开关：cyan 半透明轨 + 48px 切角方块，切换时方块滑动。</summary>
internal partial class SettingsToggle : Control
{
    internal const float ToggleKnob = 48f;
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }

    private bool _on;
    private float _knob;
    private Tween? _tween;

    [Signal]
    public delegate void PressedEventHandler();

    public override void _Ready() =>
        MouseDefaultCursorShape = CursorShape.PointingHand;

    public void SetOn(bool on, bool animate)
    {
        if (_on == on)
            return;
        _on = on;
        _tween?.Kill();
        _tween = null;
        var motion = MotionProfile;
        if (!animate || !motion.IsAnimated)
        {
            _knob = on ? 1f : 0f;
            QueueRedraw();
            return;
        }

        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(value =>
            {
                _knob = value;
                QueueRedraw();
            }), _knob, on ? 1f : 0f, motion.ValueDuration)
            .SetTrans(Tween.TransitionType.Linear);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
            InputEventScreenTouch { Pressed: true })
        {
            SetOn(!_on, animate: true);
            EmitSignal(SignalName.Pressed);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var track = UiGeometry.CutCorners(Size, 16f);
        if (_on)
        {
            DrawColoredPolygon(track, new Color(UiFonts.Cyan, 0.14f));
            DrawPolyline(UiGeometry.Close(track), UiFonts.Cyan, 2.5f, true);
        }
        else
        {
            DrawPolyline(UiGeometry.Close(track), UiFonts.Line, 2.5f, true);
        }

        var knobX = 12f + _knob * (Size.X - 24f - ToggleKnob);
        var knob = UiGeometry.CutCorners(knobX, (Size.Y - ToggleKnob) * 0.5f,
            ToggleKnob, ToggleKnob, 12f);
        DrawColoredPolygon(knob, _on ? UiFonts.Cyan : new Color(UiFonts.Dim, 0.55f));
    }

    private UiMotionProfile MotionProfile => MotionProfileProvider();
}

/// <summary>620×6 轨 + 44px 切角手柄；点按/拖动直接改值（按 5 取整）。</summary>
internal partial class SettingsSlider : Control
{
    internal const float TrackWidth = 620f, TrackHandle = 44f;
    internal const int VolumeStepPercent = 5;

    private int _value;
    private bool _dragging;

    [Signal]
    public delegate void ChangedEventHandler(int value);

    public Color Accent { get; set; } = UiFonts.Cyan;

    /// <summary>滑块两端对应的实际数值（默认 0–100，即音量百分比）。</summary>
    public int Min { get; set; }

    public int Max { get; set; } = 100;

    public void SetValue(int value)
    {
        if (_value == value)
            return;
        _value = value;
        QueueRedraw();
    }

    public override void _Ready() =>
        MouseDefaultCursorShape = CursorShape.PointingHand;

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _dragging = button.Pressed;
                if (button.Pressed)
                    ApplyAt(button.Position.X);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                ApplyAt(motion.Position.X);
                AcceptEvent();
                break;
            case InputEventScreenTouch touch:
                _dragging = touch.Pressed;
                if (touch.Pressed)
                    ApplyAt(touch.Position.X);
                AcceptEvent();
                break;
            case InputEventScreenDrag drag:
                ApplyAt(drag.Position.X);
                AcceptEvent();
                break;
        }
    }

    private float Ratio => Max > Min ? (_value - Min) / (float)(Max - Min) : 0f;

    private void ApplyAt(float localX)
    {
        var ratio = Mathf.Clamp((localX - TrackHandle * 0.5f) / TrackWidth, 0f, 1f);
        var raw = Min + Mathf.RoundToInt(ratio * (Max - Min));
        var stepped = Mathf.RoundToInt((float)raw / VolumeStepPercent) * VolumeStepPercent;
        var value = Mathf.Clamp(stepped, Min, Max);
        if (value == _value)
            return;
        _value = value;
        QueueRedraw();
        EmitSignal(SignalName.Changed, value);
    }

    public override void _Draw()
    {
        var trackLeft = TrackHandle * 0.5f;
        var centerY = Size.Y * 0.5f;
        DrawRect(new Rect2(trackLeft, centerY - 3f, TrackWidth, 6f),
            new Color(UiFonts.Text, 0.14f));
        var fill = TrackWidth * Ratio;
        DrawRect(new Rect2(trackLeft, centerY - 3f, fill, 6f), Accent);
        var handle = UiGeometry.CutCorners(
            trackLeft + fill - TrackHandle * 0.5f, centerY - TrackHandle * 0.5f,
            TrackHandle, TrackHandle, 12f);
        DrawColoredPolygon(handle, UiFonts.Text);
    }
}

/// <summary>168×72 分段选择（FULL / REDUCED / OFF）。</summary>
internal partial class SettingsSegments : Control
{
    internal const float SegmentWidth = 168f, SegmentHeight = 72f, SegmentGap = 12f;

    private string[] _labels = [];
    private int _selected;
    private int _hover = -1;

    [Signal]
    public delegate void ChosenEventHandler(int index);

    public void Configure(string[] labels, int selected)
    {
        _labels = labels;
        _selected = selected;
        QueueRedraw();
    }

    public void SetSelected(int selected)
    {
        if (_selected == selected)
            return;
        _selected = selected;
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseExited += () =>
        {
            if (_hover < 0)
                return;
            _hover = -1;
            QueueRedraw();
        };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion)
        {
            var index = IndexAt(motion.Position.X);
            if (index != _hover)
            {
                _hover = index;
                QueueRedraw();
            }
            return;
        }

        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
        {
            EmitSignal(SignalName.Chosen, IndexAt(button.Position.X));
            AcceptEvent();
        }
        else if (e is InputEventScreenTouch { Pressed: true } touch)
        {
            EmitSignal(SignalName.Chosen, IndexAt(touch.Position.X));
            AcceptEvent();
        }
    }

    private int IndexAt(float x) =>
        Mathf.Clamp((int)(x / (SegmentWidth + SegmentGap)), 0, _labels.Length - 1);

    public override void _Draw()
    {
        for (var i = 0; i < _labels.Length; i++)
        {
            var origin = new Vector2(i * (SegmentWidth + SegmentGap), 0f);
            var points = UiGeometry.CutCorners(origin.X, origin.Y, SegmentWidth, SegmentHeight, 16f);

            var selected = i == _selected;
            if (selected)
                DrawColoredPolygon(points, new Color(UiFonts.Cyan, 0.16f));
            else if (i == _hover)
                DrawColoredPolygon(points, new Color(UiFonts.PanelHover, 0.55f));
            DrawPolyline(UiGeometry.Close(points), selected ? UiFonts.Cyan : UiFonts.Line,
                2.5f, true);
            DrawString(UiFonts.TechBold,
                origin + new Vector2(0f, SegmentHeight * 0.5f + 7f), _labels[i],
                HorizontalAlignment.Center, SegmentWidth, 20,
                selected ? UiFonts.Cyan : UiFonts.Dim);
        }
    }
}

/// <summary>样式稿的无边框文字动作（RESET DEFAULTS）。</summary>
internal partial class FlatTextButton : Control
{
    private bool _hover;

    [Signal]
    public delegate void PressedEventHandler();

    public string Text { get; set; } = "";

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () =>
        {
            _hover = true;
            QueueRedraw();
        };
        MouseExited += () =>
        {
            _hover = false;
            QueueRedraw();
        };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
            InputEventScreenTouch { Pressed: true })
        {
            EmitSignal(SignalName.Pressed);
            AcceptEvent();
        }
    }

    public override void _Draw() =>
        DrawString(UiFonts.Tech, new Vector2(0f, Size.Y * 0.5f + 5.6f), Text,
            HorizontalAlignment.Left, Size.X, 16,
            _hover ? UiFonts.Cyan : UiFonts.Dim);
}

using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>配色页复用的自绘控件：切角按钮 / 色行 / 预设槽 / 分段器 / SV 面 / 色相条。</summary>
public static class NoteColorsWidgets
{
    public static Vector2[] CutCorners(Vector2 size, float cut) => UiGeometry.CutCorners(size, cut);

    public static void DrawDashedFrame(Control target, Vector2[] points, Color color, float width)
    {
        for (var i = 0; i < points.Length; i++)
            target.DrawDashedLine(points[i], points[(i + 1) % points.Length], color, width, 6f, true);
    }
}

/// <summary>
/// One physical pointer per gesture. A control may acquire it only from a GUI press inside itself;
/// subsequent viewport events must match both device and touch id. Emulated input never acquires it.
/// </summary>
internal sealed class NoteColorPointerCapture
{
    internal enum Update { None, Move, Release, Cancel }
    private enum Owner { None, Mouse, Touch }
    private Owner _owner;
    private int _device;
    private int _touchId;

    public bool Active => _owner != Owner.None;

    public bool TryBegin(InputEvent e, Vector2 size, out Vector2 localPosition)
    {
        localPosition = Vector2.Zero;
        if (Active || e.Device == InputEvent.DeviceIdEmulation)
            return false;
        Owner owner;
        var touchId = 0;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, Canceled: false } button:
                owner = Owner.Mouse;
                localPosition = button.Position;
                break;
            case InputEventScreenTouch { Pressed: true, Canceled: false } touch:
                owner = Owner.Touch;
                touchId = touch.Index;
                localPosition = touch.Position;
                break;
            default:
                return false;
        }
        if (!new Rect2(Vector2.Zero, size).HasPoint(localPosition))
            return false;
        _owner = owner;
        _device = e.Device;
        _touchId = touchId;
        return true;
    }

    /// <summary>Positions returned here are viewport coordinates, unlike _GuiInput positions.</summary>
    public Update Read(InputEvent e, out Vector2 viewportPosition)
    {
        viewportPosition = Vector2.Zero;
        if (!Active || e.Device != _device || e.Device == InputEvent.DeviceIdEmulation)
            return Update.None;
        if (_owner == Owner.Mouse)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            {
                viewportPosition = button.Position;
                return button.Canceled ? Update.Cancel : button.Pressed ? Update.None : Update.Release;
            }
            if (e is InputEventMouseMotion motion)
            {
                viewportPosition = motion.Position;
                return (motion.ButtonMask & MouseButtonMask.Left) != 0 ? Update.Move : Update.Cancel;
            }
        }
        else
        {
            if (e is InputEventScreenTouch touch && touch.Index == _touchId)
            {
                viewportPosition = touch.Position;
                return touch.Canceled ? Update.Cancel : touch.Pressed ? Update.None : Update.Release;
            }
            if (e is InputEventScreenDrag drag && drag.Index == _touchId)
            {
                viewportPosition = drag.Position;
                return Update.Move;
            }
        }
        return Update.None;
    }

    public bool Reset()
    {
        var active = Active;
        _owner = Owner.None;
        return active;
    }
}

/// <summary>
/// Neutral v2 cut button. Ordinary activation occurs on a matching release inside the control;
/// Momentary buttons emit HeldChanged only. Tab focuses, Enter/Space press and release activate.
/// </summary>
public partial class TechButton : Control
{
    public enum Kind { Outline, Accent, Solid }

    private readonly NoteColorPointerCapture _pointer = new();
    private string _text = "";
    private int _fontSize = 14;
    private Kind _kind = Kind.Outline;
    private bool _selected;
    private bool _enabled = true;
    private bool _hover;
    private bool _pointerInside;
    private bool _momentary;
    private bool _held;
    private Key? _keyOwner;
    private int _keyDevice;
    private float _cut = 10f;
    private Color _accent = UiFonts.Cyan;
    private Font? _fontOverride;

    [Signal]
    public delegate void PressedEventHandler();

    [Signal]
    public delegate void HeldChangedEventHandler(bool held);

    public string Text { get => _text; set { _text = value; QueueRedraw(); } }
    public int FontSize { get => _fontSize; set { _fontSize = Math.Max(1, value); QueueRedraw(); } }
    public Kind StyleKind { get => _kind; set { _kind = value; QueueRedraw(); } }
    public bool Selected { get => _selected; set { _selected = value; QueueRedraw(); } }
    public float Cut { get => _cut; set { _cut = value; QueueRedraw(); } }
    public Color Accent { get => _accent; set { _accent = value; QueueRedraw(); } }

    /// <summary>Null chooses Cjk for CJK text, otherwise TechBold. Callers may override explicitly.</summary>
    public Font? FontOverride { get => _fontOverride; set { _fontOverride = value; QueueRedraw(); } }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (!value)
            {
                CancelInteraction();
                if (HasFocus())
                    ReleaseFocus();
            }
            FocusMode = _enabled ? FocusModeEnum.All : FocusModeEnum.None;
            MouseDefaultCursorShape = _enabled ? CursorShape.PointingHand : CursorShape.Arrow;
            QueueRedraw();
        }
    }

    public bool Momentary
    {
        get => _momentary;
        set
        {
            if (_momentary == value)
                return;
            CancelInteraction();
            _momentary = value;
        }
    }

    private bool CanInteract => _enabled && IsInsideTree() && IsVisibleInTree();

    public override void _Ready()
    {
        FocusMode = _enabled ? FocusModeEnum.All : FocusModeEnum.None;
        MouseDefaultCursorShape = _enabled ? CursorShape.PointingHand : CursorShape.Arrow;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
        SetProcessInput(false);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationFocusEnter)
            QueueRedraw();
        if (what == NotificationFocusExit || what == NotificationWMWindowFocusOut ||
            what == NotificationApplicationFocusOut ||
            (what == NotificationVisibilityChanged && !IsVisibleInTree()))
        {
            _hover = false;
            CancelInteraction();
        }
    }

    public override void _ExitTree() => CancelInteraction();

    public override void _GuiInput(InputEvent e)
    {
        if (!CanInteract || _pointer.Active || _keyOwner is not null)
            return;
        if (e is InputEventKey key)
        {
            var code = KeyOf(key);
            if (!HasFocus() || !key.Pressed || key.Echo || key.AltPressed || key.CtrlPressed ||
                key.MetaPressed || code is not (Key.Enter or Key.KpEnter or Key.Space))
                return;
            _keyOwner = code;
            _keyDevice = key.Device;
            AcceptEvent();
            BeginInteraction();
            return;
        }
        if (!_pointer.TryBegin(e, Size, out _))
            return;
        _pointerInside = true;
        AcceptEvent();
        BeginInteraction();
        if (_pointer.Active && CanInteract)
            GrabFocus();
    }

    public override void _Input(InputEvent e)
    {
        if (!CanInteract)
        {
            CancelInteraction();
            return;
        }
        if (_keyOwner is { } code)
        {
            if (e is not InputEventKey key || key.Device != _keyDevice || KeyOf(key) != code)
                return;
            GetViewport().SetInputAsHandled();
            if (!key.Pressed)
                FinishInteraction(HasFocus());
            return;
        }
        var update = _pointer.Read(e, out var position);
        if (update == NoteColorPointerCapture.Update.None)
            return;
        // Consume before callbacks: release may navigate away or open a modal immediately.
        GetViewport().SetInputAsHandled();
        var local = GetGlobalTransformWithCanvas().AffineInverse() * position;
        _pointerInside = new Rect2(Vector2.Zero, Size).HasPoint(local);
        if (update == NoteColorPointerCapture.Update.Move)
            QueueRedraw();
        else
            FinishInteraction(update == NoteColorPointerCapture.Update.Release && _pointerInside);
    }

    private static Key KeyOf(InputEventKey key) =>
        key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;

    private void BeginInteraction()
    {
        SetProcessInput(true);
        QueueRedraw();
        if (_momentary)
        {
            _held = true;
            EmitSignal(SignalName.HeldChanged, true);
        }
    }

    private void FinishInteraction(bool activate)
    {
        var press = activate && CanInteract && !_momentary;
        CancelInteraction();
        if (press)
            EmitSignal(SignalName.Pressed);
    }

    /// <summary>Cancel before switching pages or showing a modal; never emits Pressed.</summary>
    public void CancelInteraction()
    {
        _pointer.Reset();
        _pointerInside = false;
        _keyOwner = null;
        SetProcessInput(false);
        var wasHeld = _held;
        _held = false;
        QueueRedraw();
        if (wasHeld)
            EmitSignal(SignalName.HeldChanged, false);
    }

    public override void _Draw()
    {
        var points = NoteColorsWidgets.CutCorners(Size, Cut);
        var down = _held || _keyOwner is not null || (_pointer.Active && _pointerInside);
        Color fill;
        Color border;
        Color text;
        if (!_enabled)
        {
            fill = new Color("0C1119");
            border = new Color("252D38");
            text = new Color("657182");
        }
        else if (_kind == Kind.Solid)
        {
            fill = Accent.Lerp(Colors.White, down ? 0.22f : _hover ? 0.12f : 0f);
            border = fill;
            text = UiFonts.InkText;
        }
        else if (_kind == Kind.Accent || _selected)
        {
            fill = new Color("162C38").Lerp(Accent, down ? 0.16f : _hover ? 0.08f : 0f);
            border = Accent;
            text = Accent;
        }
        else
        {
            fill = new Color("10161F").Lerp(Accent, down ? 0.12f : _hover ? 0.05f : 0f);
            border = _hover || down ? Accent : new Color("36404F");
            text = new Color("EDF2FA");
        }

        DrawColoredPolygon(points, fill);
        DrawPolyline(UiGeometry.Close(points), border, _kind == Kind.Solid ? 1.5f : 2f, true);
        if (_enabled && HasFocus() && Size.X > 12f && Size.Y > 12f)
        {
            var inset = NoteColorsWidgets.CutCorners(Size - new Vector2(8f, 8f), Math.Max(0f, Cut - 3f));
            for (var i = 0; i < inset.Length; i++)
                inset[i] += new Vector2(4f, 4f);
            DrawPolyline(UiGeometry.Close(inset), _kind == Kind.Solid ? UiFonts.InkText : Accent, 1f, true);
        }
        var font = _fontOverride ?? (_text.Any(c => c is >= '\u2E80' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF' or >= '\uFF00' and <= '\uFFEF')
            ? UiFonts.Cjk : UiFonts.TechBold);
        var baseline = (Size.Y - font.GetHeight(_fontSize)) * 0.5f + font.GetAscent(_fontSize);
        DrawString(font, new Vector2(0f, baseline), _text,
            HorizontalAlignment.Center, Size.X, _fontSize, text);
    }
}

/// <summary>编辑器里的一行颜色：色块 / 名称 / HEX / 警告 chip / 单类复位。</summary>
public partial class ColorRow : Control
{
    private Color _color;
    private string _name = "";
    private string? _warning;
    private bool _hover;

    [Signal]
    public delegate void PressedEventHandler(int channel);

    [Signal]
    public delegate void ResetRequestedEventHandler(int channel);

    public int Channel { get; set; }
    public Color Color { get => _color; set { _color = value; QueueRedraw(); } }
    public string Title { get => _name; set { _name = value; QueueRedraw(); } }
    public string? Warning { get => _warning; set { _warning = value; QueueRedraw(); } }

    private static Rect2 ResetRect => new(692f, 5f, 44f, 40f);

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public override void _GuiInput(InputEvent e)
    {
        var position = Vector2.Zero;
        var hit = false;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
        {
            position = button.Position;
            hit = true;
        }
        else if (e is InputEventScreenTouch { Pressed: true } touch)
        {
            position = touch.Position;
            hit = true;
        }

        if (!hit)
            return;
        if (ResetRect.HasPoint(position))
            EmitSignal(SignalName.ResetRequested, Channel);
        else
            EmitSignal(SignalName.Pressed, Channel);
        AcceptEvent();
    }

    public override void _Draw()
    {
        if (_hover)
            DrawColoredPolygon(NoteColorsWidgets.CutCorners(new Vector2(Size.X, 74f), 10f),
                new Color(UiFonts.PanelHover, 0.4f));

        var swatch = NoteColorsWidgets.CutCorners(new Rect2(0f, 5f, 50f, 50f).Size, 12f);
        for (var i = 0; i < swatch.Length; i++)
            swatch[i] += new Vector2(0f, 5f);
        DrawColoredPolygon(swatch, _color);
        DrawPolyline(UiGeometry.Close(swatch), new Color(Colors.White, 0.14f), 1f, true);

        DrawString(UiFonts.TechBold, new Vector2(68f, 32f), _name,
            HorizontalAlignment.Left, 400f, 20, UiFonts.Text);
        DrawString(UiFonts.Tech, new Vector2(68f, 54f), NotePalette.ToHex(_color),
            HorizontalAlignment.Left, 200f, 13, UiFonts.Dim);

        if (_warning != null)
            DrawString(UiFonts.Tech, new Vector2(300f, 32f), _warning,
                HorizontalAlignment.Right, 370f, 12, new Color("ffb454"));

        DrawResetIcon(new Vector2(ResetRect.Position.X + ResetRect.Size.X * 0.5f, 25f),
            _hover ? UiFonts.Cyan : UiFonts.Dim);
    }

    private void DrawResetIcon(Vector2 center, Color color)
    {
        DrawArc(center, 7f, -Mathf.Pi * 0.35f, Mathf.Pi * 1.45f, 20, color, 1.6f, true);
        var tip = center + new Vector2(4.5f, -6.5f);
        DrawColoredPolygon(
        [
            tip + new Vector2(-3.5f, -0.5f),
            tip + new Vector2(3.5f, -1.5f),
            tip + new Vector2(0.5f, 3.5f),
        ], color);
    }
}

/// <summary>底部预设槽：六色点 / 槽名 / 类型 / 警告点；空槽画虚线 + NEW PRESET。</summary>
public partial class PresetSlot : Control
{
    private const double LongPressSeconds = 0.55;

    private Color[] _colors = [];
    private string _name = "";
    private string _kind = "";
    private bool _selected;
    private bool _empty;
    private bool _warning;
    private bool _hover;
    private bool _holding;
    private double _heldTime;

    [Signal]
    public delegate void ChosenEventHandler(int index);

    [Signal]
    public delegate void MenuRequestedEventHandler(int index);

    public int Index { get; set; }

    public void SetPalette(NotePalette palette, bool selected)
    {
        _colors =
        [
            palette.Tap, palette.ExTap, palette.Drag, palette.Hold, palette.Mixer, palette.Mine,
        ];
        _name = palette.IsEmpty ? "" : palette.Name;
        _kind = palette.IsEmpty ? "" : palette.MatchesFactory() ? "FACTORY" : "CUSTOM";
        _empty = palette.IsEmpty;
        _selected = selected;
        _warning = !palette.IsEmpty && NotePaletteMetrics.HasWarnings(palette);
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () =>
        {
            _hover = false;
            CancelHold();
            QueueRedraw();
        };
        SetProcess(false);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                EmitSignal(SignalName.MenuRequested, Index);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                BeginHold();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                EndHold();
                AcceptEvent();
                break;
            case InputEventScreenTouch { Pressed: true }:
                BeginHold();
                AcceptEvent();
                break;
            case InputEventScreenTouch { Pressed: false }:
                EndHold();
                AcceptEvent();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_holding)
            return;
        _heldTime += delta;
        if (_heldTime < LongPressSeconds)
            return;
        _holding = false;
        SetProcess(false);
        EmitSignal(SignalName.MenuRequested, Index);
    }

    private void BeginHold()
    {
        _holding = true;
        _heldTime = 0.0;
        SetProcess(true);
    }

    private void EndHold()
    {
        var wasHolding = _holding;
        CancelHold();
        if (wasHolding)
            EmitSignal(SignalName.Chosen, Index);
    }

    private void CancelHold()
    {
        _holding = false;
        SetProcess(false);
    }

    public override void _Draw()
    {
        var points = NoteColorsWidgets.CutCorners(Size, 14f);
        var border = _selected ? UiFonts.Cyan : (_hover ? new Color(UiFonts.Cyan, 0.55f) : UiFonts.Line);
        if (_selected)
        {
            DrawColoredPolygon(points, new Color(UiFonts.Panel, 0.95f));
        }
        else if (!_empty)
        {
            DrawColoredPolygon(points, new Color(0.04f, 0.06f, 0.13f, 0.55f));
        }
        else
        {
            DrawColoredPolygon(points, new Color(0.04f, 0.06f, 0.13f, 0.28f));
        }

        if (_empty)
            NoteColorsWidgets.DrawDashedFrame(this, points, _hover ? new Color(UiFonts.Cyan, 0.55f) : UiFonts.Line, 1.5f);
        else
            DrawPolyline(UiGeometry.Close(points), border, _selected ? 2.5f : 1.5f, true);

        if (_empty)
        {
            DrawString(UiFonts.TechBold, new Vector2(0f, 54f), "+",
                HorizontalAlignment.Center, Size.X, 26, _hover ? UiFonts.Cyan : UiFonts.Dim);
            DrawString(UiFonts.Tech, new Vector2(0f, 84f), "NEW PRESET",
                HorizontalAlignment.Center, Size.X, 10, UiFonts.Dim);
            return;
        }

        for (var i = 0; i < _colors.Length; i++)
            DrawCircle(new Vector2(22f + i * 22f, 22f), 7.5f, _colors[i]);

        DrawString(UiFonts.TechBold, new Vector2(16f, 64f), _name,
            HorizontalAlignment.Left, Size.X - 32f, 14,
            _selected ? UiFonts.Cyan : UiFonts.Text);
        DrawString(UiFonts.Tech, new Vector2(16f, 86f), _kind,
            HorizontalAlignment.Left, Size.X - 32f, 10, UiFonts.Dim);
        if (_warning)
            DrawCircle(new Vector2(154f, 16f), 4f, new Color("ffb454"));
    }
}

/// <summary>两段芯片（BURST · JUDGE / NOTE）。</summary>
public partial class SegmentedChips : Control
{
    private string[] _labels = [];
    private int _selected;

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

    public override void _Ready() => MouseDefaultCursorShape = CursorShape.PointingHand;

    public override void _GuiInput(InputEvent e)
    {
        var position = Vector2.Zero;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
            position = button.Position;
        else if (e is InputEventScreenTouch { Pressed: true } touch)
            position = touch.Position;
        else
            return;

        var index = Mathf.Clamp((int)(position.X / 108f), 0, _labels.Length - 1);
        EmitSignal(SignalName.Chosen, index);
        AcceptEvent();
    }

    public override void _Draw()
    {
        for (var i = 0; i < _labels.Length; i++)
        {
            var origin = new Vector2(i * 108f, 0f);
            var points = NoteColorsWidgets.CutCorners(new Vector2(100f, Size.Y), 10f);
            for (var p = 0; p < points.Length; p++)
                points[p] += origin;
            var selected = i == _selected;
            DrawColoredPolygon(points, selected
                ? new Color(UiFonts.Cyan, 0.16f)
                : new Color(0.04f, 0.06f, 0.13f, 0.5f));
            DrawPolyline(UiGeometry.Close(points), selected ? UiFonts.Cyan : UiFonts.Line, 2f, true);
            DrawString(UiFonts.TechBold, origin + new Vector2(0f, Size.Y * 0.5f + 5f), _labels[i],
                HorizontalAlignment.Center, 100f, 14, selected ? UiFonts.Cyan : UiFonts.Dim);
        }
    }
}

/// <summary>48px minimum hit area with a centered 24px hue strip and a black/white handle.</summary>
public partial class HueBar : Control
{
    private readonly NoteColorPointerCapture _pointer = new();
    private float _hue;

    [Signal]
    public delegate void ChangedEventHandler(float hue);

    [Signal]
    public delegate void DragStartedEventHandler();

    [Signal]
    public delegate void DragEndedEventHandler();

    public float Hue
    {
        get => _hue;
        set
        {
            var hue = float.IsFinite(value) ? Mathf.Clamp(value, 0f, 1f) : 0f;
            if (Mathf.IsEqualApprox(_hue, hue))
                return;
            _hue = hue;
            QueueRedraw();
        }
    }

    public override Vector2 _GetMinimumSize() => new(0f, 48f);

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.Click;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        SetProcessInput(false);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationFocusExit || what == NotificationWMWindowFocusOut ||
            what == NotificationApplicationFocusOut ||
            (what == NotificationVisibilityChanged && !IsVisibleInTree()))
            CancelInteraction();
    }

    public override void _ExitTree() => CancelInteraction();

    public override void _GuiInput(InputEvent e)
    {
        if (!IsVisibleInTree() || Size.X <= 0f || !_pointer.TryBegin(e, Size, out var position))
            return;
        AcceptEvent();
        SetProcessInput(true);
        // DragStarted precedes the first value change, allowing one undo snapshot per gesture.
        EmitSignal(SignalName.DragStarted);
        if (!_pointer.Active || !IsVisibleInTree())
            return;
        GrabFocus();
        if (_pointer.Active)
            ApplyPosition(position);
    }

    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree())
        {
            CancelInteraction();
            return;
        }
        var update = _pointer.Read(e, out var position);
        if (update == NoteColorPointerCapture.Update.None)
            return;
        GetViewport().SetInputAsHandled();
        if (update == NoteColorPointerCapture.Update.Cancel)
        {
            CancelInteraction();
            return;
        }
        ApplyPosition(GetGlobalTransformWithCanvas().AffineInverse() * position);
        if (update == NoteColorPointerCapture.Update.Release)
            CancelInteraction();
    }

    private void ApplyPosition(Vector2 position)
    {
        Hue = Mathf.Clamp(position.X / Math.Max(Size.X, 1f), 0f, 1f);
        EmitSignal(SignalName.Changed, _hue);
    }

    /// <summary>Ends an owned gesture once, including modal/page changes and canceled releases.</summary>
    public void CancelInteraction()
    {
        var active = _pointer.Reset();
        SetProcessInput(false);
        if (active)
            EmitSignal(SignalName.DragEnded);
    }

    public override void _Draw()
    {
        if (Size.X <= 0f || Size.Y <= 0f)
            return;
        const int segments = 96;
        var stripHeight = Math.Min(24f, Size.Y);
        var stripY = (Size.Y - stripHeight) * 0.5f;
        var width = Size.X / segments;
        for (var i = 0; i < segments; i++)
        {
            var x = i * width;
            DrawRect(new Rect2(x, stripY, Math.Min(width + 1f, Size.X - x), stripHeight),
                Color.FromHsv(i / (float)(segments - 1), 1f, 1f));
        }
        var halfHandle = Math.Min(9f, Size.X * 0.5f);
        var handleX = Mathf.Clamp(_hue * Size.X, halfHandle, Size.X - halfHandle);
        var handleHeight = Math.Min(36f, Size.Y);
        var outer = new Rect2(handleX - halfHandle, (Size.Y - handleHeight) * 0.5f,
            halfHandle * 2f, handleHeight);
        DrawRect(outer, new Color("080D14"));
        if (outer.Size.X > 6f && outer.Size.Y > 6f)
        {
            var inner = new Rect2(outer.Position + new Vector2(3f, 3f), outer.Size - new Vector2(6f, 6f));
            DrawRect(inner, Color.FromHsv(_hue, 1f, 1f));
            DrawRect(inner, Colors.White, false, 2f);
        }
    }
}

/// <summary>SV field with owned pointer dragging and a high-contrast black/white selection ring.</summary>
public partial class SvField : Control
{
    private static readonly Shader Shader = new()
    {
        Code = """
            shader_type canvas_item;
            uniform vec4 hue : source_color = vec4(0.0, 1.0, 0.4, 1.0);
            void fragment() {
                vec3 color = mix(vec3(1.0), hue.rgb, UV.x);
                color = mix(color, vec3(0.0), UV.y);
                COLOR = vec4(color, 1.0) * COLOR;
            }
            """,
    };

    private readonly NoteColorPointerCapture _pointer = new();
    private readonly ColorRect _surface = new();
    private readonly Ring _ring = new();
    private ShaderMaterial? _material;
    private float _hue;

    [Signal]
    public delegate void ChangedEventHandler(Color color);

    [Signal]
    public delegate void DragStartedEventHandler();

    [Signal]
    public delegate void DragEndedEventHandler();

    public float Hue
    {
        get => _hue;
        set
        {
            _hue = float.IsFinite(value) ? Mathf.Clamp(value, 0f, 1f) : 0f;
            ApplyHue();
        }
    }
    public float Saturation { get; private set; } = 0.55f;
    public float Value { get; private set; } = 1f;

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = Shader };
        _surface.Material = _material;
        _surface.MouseFilter = MouseFilterEnum.Ignore;
        _surface.Size = Size;
        AddChild(_surface);
        _ring.MouseFilter = MouseFilterEnum.Ignore;
        _ring.Size = new Vector2(26f, 26f);
        AddChild(_ring);
        FocusMode = FocusModeEnum.Click;
        MouseDefaultCursorShape = CursorShape.Cross;
        SetProcessInput(false);
        ApplyHue();
        ApplyPoint();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            _surface.Size = Size;
            ApplyPoint();
        }
        if (what == NotificationFocusExit || what == NotificationWMWindowFocusOut ||
            what == NotificationApplicationFocusOut ||
            (what == NotificationVisibilityChanged && !IsVisibleInTree()))
            CancelInteraction();
    }

    public override void _ExitTree() => CancelInteraction();

    public void SetColor(Color color)
    {
        Hue = color.H;
        Saturation = float.IsFinite(color.S) ? Mathf.Clamp(color.S, 0f, 1f) : 0f;
        Value = float.IsFinite(color.V) ? Mathf.Clamp(color.V, 0f, 1f) : 0f;
        ApplyPoint();
    }

    public void SetHue(float hue) => Hue = hue;

    private void ApplyHue()
    {
        if (_material == null)
            return;
        var hue = Color.FromHsv(Hue, 1f, 1f);
        _material.SetShaderParameter("hue", new Vector4(hue.R, hue.G, hue.B, 1f));
    }

    private void ApplyPoint() => _ring.Position = new Vector2(
        Saturation * Size.X - 13f, (1f - Value) * Size.Y - 13f);

    public override void _GuiInput(InputEvent e)
    {
        if (!IsVisibleInTree() || Size.X <= 0f || Size.Y <= 0f ||
            !_pointer.TryBegin(e, Size, out var position))
            return;
        AcceptEvent();
        SetProcessInput(true);
        EmitSignal(SignalName.DragStarted);
        if (!_pointer.Active || !IsVisibleInTree())
            return;
        GrabFocus();
        if (_pointer.Active)
            ApplyPosition(position);
    }

    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree())
        {
            CancelInteraction();
            return;
        }
        var update = _pointer.Read(e, out var position);
        if (update == NoteColorPointerCapture.Update.None)
            return;
        GetViewport().SetInputAsHandled();
        if (update == NoteColorPointerCapture.Update.Cancel)
        {
            CancelInteraction();
            return;
        }
        ApplyPosition(GetGlobalTransformWithCanvas().AffineInverse() * position);
        if (update == NoteColorPointerCapture.Update.Release)
            CancelInteraction();
    }

    private void ApplyPosition(Vector2 position)
    {
        Saturation = Mathf.Clamp(position.X / Math.Max(Size.X, 1f), 0f, 1f);
        Value = Mathf.Clamp(1f - position.Y / Math.Max(Size.Y, 1f), 0f, 1f);
        ApplyPoint();
        EmitSignal(SignalName.Changed, Color.FromHsv(Hue, Saturation, Value));
    }

    /// <summary>Ends an owned gesture once; parent screens should call this before modal/page changes.</summary>
    public void CancelInteraction()
    {
        var active = _pointer.Reset();
        SetProcessInput(false);
        if (active)
            EmitSignal(SignalName.DragEnded);
    }

    private partial class Ring : Control
    {
        public override void _Draw()
        {
            var center = Size * 0.5f;
            DrawArc(center, 10f, 0f, Mathf.Tau, 48, new Color("080D14"), 6f, true);
            DrawArc(center, 10f, 0f, Mathf.Tau, 48, Colors.White, 2f, true);
        }
    }
}

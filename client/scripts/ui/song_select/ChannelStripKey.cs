using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>One transparent hit/focus region in the instrument panel. Painting is owned by
/// its parent. Release-inside commits; a drag never toggles a mode or gameplay modifier.</summary>
internal sealed partial class ChannelStripKey : Control
{
    internal event Action? Activated;
    internal event Action? InkChanged;
    internal float Hover => _fade.Value;
    internal bool Held => _owner != -2 || _key != Key.None;
    private UiHoverFade _fade;
    private bool _hover, _cancelled;
    private int _owner = -2, _device;
    private Key _key = Key.None;
    private Vector2 _origin;
    private ContactRim _rim = null!;
    internal bool Unavailable { get; private set; }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All; MouseDefaultCursorShape = CursorShape.PointingHand;
        // Captured releases are handled by _Input before the GUI signal can see them.
        _rim = ContactRim.Attach(this, () => !Unavailable, observeGuiInput: false);
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);
        FocusEntered += Changed; FocusExited += Cancel;
        GetWindow().FocusExited += Cancel;
        VisibilityChanged += OnVisibility;
        SetProcess(false); SetProcessInput(false);
    }
    public override void _ExitTree()
    {
        GetWindow().FocusExited -= Cancel;
        Cancel();
    }
    private void OnVisibility() { if (!IsVisibleInTree()) Cancel(); }
    internal void Lock(bool locked)
    {
        if (Unavailable == locked) return;
        Unavailable = locked;
        if (locked) { Cancel(); SetHover(false); }
        MouseDefaultCursorShape = locked ? CursorShape.Arrow : CursorShape.PointingHand;
        FocusMode = locked ? FocusModeEnum.None : FocusModeEnum.All; Changed();
    }
    private void SetHover(bool on)
    {
        on &= !Unavailable;
        if (_hover == on) return;
        _hover = on; SetProcess(true);
    }
    public override void _Process(double delta)
    {
        if (_fade.Update(_hover, delta)) Changed();
        if (_fade.Settled(_hover)) SetProcess(false);
    }
    private void Changed() => InkChanged?.Invoke();
    private void ClearCapture() { _owner = -2; _key = Key.None; _cancelled = false; SetProcessInput(false); Changed(); }
    private void Cancel()
    {
        if (_owner != -2) _rim.CancelContact(_owner);
        ClearCapture();
    }
    public override void _GuiInput(InputEvent e)
    {
        if (Unavailable || Held || e.Device == InputEvent.DeviceIdEmulation) return;
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, Canceled: false, ButtonIndex: MouseButton.Left } b:
                _owner = -1; _origin = b.Position; break;
            case InputEventScreenTouch { Pressed: true, Canceled: false } t:
                _owner = t.Index; _origin = t.Position; break;
            case InputEventKey { Pressed: true, Echo: false, CtrlPressed: false, MetaPressed: false, AltPressed: false } k when HasFocus() && k.Keycode is Key.Space or Key.Enter or Key.KpEnter:
                _key = k.Keycode; break;
            default: return;
        }
        _device = e.Device; _cancelled = false;
        AcceptEvent(); GrabFocus();
        if (_owner != -2) _rim.BeginContact(_owner, _origin);
        SetProcessInput(true); Changed();
    }
    public override void _Input(InputEvent e)
    {
        if (!Held || e.Device != _device || e.Device == InputEvent.DeviceIdEmulation) return;
        if (_key != Key.None)
        {
            if (e is not InputEventKey k || k.Keycode != _key) return;
            GetViewport().SetInputAsHandled();
            if (!k.Pressed) Finish(HasFocus() && !Unavailable);
            return;
        }
        Vector2 viewport; bool release = false, cancel = false;
        switch (e)
        {
            case InputEventMouseMotion m when _owner == -1: viewport = m.Position; cancel = !m.ButtonMask.HasFlag(MouseButtonMask.Left); break;
            case InputEventScreenDrag d when _owner == d.Index: viewport = d.Position; break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when _owner == -1 && (!b.Pressed || b.Canceled): viewport = b.Position; release = true; cancel = b.Canceled; break;
            case InputEventScreenTouch t when _owner == t.Index && (!t.Pressed || t.Canceled): viewport = t.Position; release = true; cancel = t.Canceled; break;
            default: return;
        }
        var p = GetGlobalTransformWithCanvas().AffineInverse() * viewport;
        _cancelled |= p.DistanceSquaredTo(_origin) > 18f * 18f || !new Rect2(Vector2.Zero, Size).HasPoint(p);
        GetViewport().SetInputAsHandled();
        if (cancel || (release && (_cancelled || Unavailable))) Cancel();
        else if (release)
        {
            _rim.EndContact(_owner, p);
            Finish(true);
        }
    }
    private void Finish(bool submit) { ClearCapture(); if (submit) Activated?.Invoke(); }
}

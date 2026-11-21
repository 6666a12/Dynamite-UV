using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>A six-line store and a long library use the same display and input projection. The
/// real ScrollContainer/VBox stay in the original GUI tree: one GPU canvas copy, no image
/// readback, no per-row viewports, and native keyboard focus, scrolling and accessibility.</summary>
internal sealed partial class CatalogueDrum : Control
{
    private const float Slop = 12f;
    private const int MouseOwner = -1, NoOwner = -2;
    private readonly ShaderMaterial _ink = new();
    // One GPU copy for this visible deck, not for each row. On Mobile the partial-copy path
    // can use the unsized/untranslated design rect against a scaled render target: short panes
    // then sample an uninitialised black band. Viewport mode is independent of backend pixel
    // coordinates, content-scale letterboxing, resize and inherited entrance transforms.
    private readonly BackBufferCopy _copy = new() { CopyMode = BackBufferCopy.CopyModeEnum.Viewport };
    private readonly ColorRect _projection = new() { MouseFilter = MouseFilterEnum.Ignore };
    private SongListViewport _wheel = null!;
    private ScrollContainer _list = null!;
    private Control? _pressedCard, _waveCard;
    private int _pointer = NoOwner, _startOffset;
    private float _width = -1, _age = 8f, _olderAge = 8f, _sign, _olderSign;
    private Vector2 _down, _downMaterial, _origin, _olderOrigin;
    private Vector4 _protectedCover;
    private bool _moved, _replaying;
    internal Func<bool>? CanInteract { get; set; }
    private static UiMotionMode Mode => MenuContactFeedback.Mode;
    private CatalogueProjection Lens => new(Size.Y);

    public static CatalogueDrum Attach(ScrollContainer list)
    {
        var node = new CatalogueDrum { Name = "CatalogueDrum", _list = list,
            MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false,
            FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.Arrow };
        list.AddSibling(node); // display over this list only, not overlays or later siblings
        return node;
    }

    public override void _Ready()
    {
        _wheel = new SongListViewport(_list, () => GameSession.Settings.MotionMode == UiMotionMode.Off ? 0d : ScrollInertia.TauSeconds);
        _ink.Shader = GD.Load<Shader>("res://shaders/catalogue_drum.gdshader");
        AddChild(_copy); _projection.Material = _ink; AddChild(_projection);
        _list.ItemRectChanged += SyncLayout;
        _list.VisibilityChanged += SyncLayout;
        _list.GetVScrollBar().VisibilityChanged += SyncLayout;
        _list.GetVScrollBar().ItemRectChanged += SyncLayout;
        _list.GetVScrollBar().ValueChanged += OnScroll;
        GetWindow().FocusExited += Cancel;
        SyncLayout(); SetProcess(false);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_list))
        {
            _list.ItemRectChanged -= SyncLayout; _list.VisibilityChanged -= SyncLayout;
            var b = _list.GetVScrollBar(); b.VisibilityChanged -= SyncLayout;
            b.ItemRectChanged -= SyncLayout; b.ValueChanged -= OnScroll;
        }
        GetWindow().FocusExited -= Cancel;
    }

    private void SyncLayout()
    {
        Position = _list.Position; Size = _list.Size; Visible = _list.Visible;
        _width = _list.GetVScrollBar().Visible ? Mathf.Clamp(_list.GetVScrollBar().Position.X, 0f, Size.X) : Size.X;
        // The native scrollbar lane is outside both the output and the input proxy.
        _copy.Rect = new Rect2(Vector2.Zero, new Vector2(_width, Size.Y));
        _projection.Size = new(_width, Size.Y);
        _ink.SetShaderParameter("pane", _projection.Size);
        _ink.SetShaderParameter("ink_width", _width);
        _ink.SetShaderParameter("lens", new Vector3(Lens.Radius, Lens.Eye, Lens.HalfArc));
        if (!IsVisibleInTree()) Cancel();
    }

    private void OnScroll(double _) { if (_waveCard is not null) UpdateWave(); }
    private void Cancel()
    {
        _pointer = NoOwner; _pressedCard = _waveCard = null; _age = _olderAge = 8; _sign = _olderSign = 0;
        _wheel.StopInertia(); UpdateWave(); SetProcess(false);
    }
    public override bool _HasPoint(Vector2 p) => !_replaying && p.X >= 0 && p.X < _width && p.Y >= 0 && p.Y < Size.Y;
    public Vector2 ProjectPoint(Vector2 materialLocal) => GetGlobalTransformWithCanvas() * Lens.Project(materialLocal);
    public Vector2 MaterialPoint(Vector2 viewportPoint) => Lens.MaterialAt(GetGlobalTransformWithCanvas().AffineInverse() * viewportPoint);

    private Control? CardAt(Vector2 material)
    {
        if (material.X < 0 || material.X >= _width || material.Y < 0 || material.Y >= Size.Y) return null;
        var global = _list.GetGlobalTransform() * material;
        // Click-only lookup (not a scroll or frame loop), using current filtered VBox geometry.
        foreach (var child in _list.GetChildren())
            if (child is BoxContainer box && box.Visible)
                foreach (var entry in box.GetChildren())
                    if (entry is Control c && c.Visible && c.MouseFilter != MouseFilterEnum.Ignore && c.GetGlobalRect().HasPoint(global)) return c;
        return null;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (_replaying) return;
        if (TransitionDirector.IsBusy || CanInteract?.Invoke() == false) { Cancel(); AcceptEvent(); return; }
        switch (e)
        {
            case InputEventMouseMotion m:
                if (_pointer == MouseOwner) Move(MouseOwner, m.Position);
                else if (_pointer == NoOwner && m.ButtonMask == 0) Hover(m.Position, m.Relative);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } w:
                if (w.Pressed && _pointer == NoOwner) Wheel((w.ButtonIndex == MouseButton.WheelUp ? -1 : 1) * w.Factor);
                break;
            case InputEventPanGesture pan: if (_pointer == NoOwner) Wheel(pan.Delta.Y); break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                if (b.Pressed && !b.Canceled) Begin(MouseOwner, b.Position); else End(MouseOwner, b.Position, b.Canceled); break;
            case InputEventScreenTouch t:
                if (t.Pressed && !t.Canceled) Begin(t.Index, t.Position); else End(t.Index, t.Position, t.Canceled); break;
            case InputEventScreenDrag drag: Move(drag.Index, drag.Position); break;
            default:return; // keyboard and native navigation always bypass this pointer-only surface
        }
        AcceptEvent();
    }

    private void Wheel(float factor)
    {
        _wheel.Push(factor); SetProcess(true);
    }
    private void Begin(int pointer, Vector2 p)
    {
        if (_pointer != NoOwner) return;
        _pointer = pointer; _down = p; _downMaterial = Lens.MaterialAt(p); _startOffset = _list.ScrollVertical;
        _pressedCard = CardAt(_downMaterial); _moved = false; _wheel.StopInertia();
        if (_pressedCard != null)
        {
            if (_waveCard != _pressedCard) { _age = 8; _sign = 0; }
            _waveCard = _pressedCard; _protectedCover = Vector4.Zero;
            // TextureRect is a real cover if present; use its live geometry, never a source-specific index.
            foreach (var child in _waveCard.GetChildren()) if (child is TextureRect cover && cover.Size.X > 80)
            { _protectedCover = new(cover.Position.X, cover.Position.Y, cover.Size.X, cover.Size.Y); break; }
            Emit(_downMaterial, 1);
        }
    }
    private void Move(int pointer, Vector2 p)
    {
        if (_pointer != pointer) return;
        var q = Lens.MaterialAt(p);
        _moved |= p.DistanceTo(_down) >= Slop;
        if (_moved) _list.ScrollVertical = _startOffset - Mathf.RoundToInt(q.Y - _downMaterial.Y);
    }
    private void End(int pointer, Vector2 p, bool cancelled)
    {
        if (_pointer != pointer) return;
        Move(pointer, p); var q = Lens.MaterialAt(p);
        bool tap = !cancelled && !_moved && _HasPoint(p) && GodotObject.IsInstanceValid(_pressedCard) && CardAt(q) == _pressedCard && SameNativeTarget(_downMaterial, q);
        if (GodotObject.IsInstanceValid(_waveCard) && _waveCard == _pressedCard && !_moved) Emit(q, -.66f);
        _pressedCard = null; _pointer = NoOwner;
        if (tap) NativeClick(q);
    }

    private void Hover(Vector2 p, Vector2 relative)
    {
        Vector2 q = Lens.MaterialAt(p);
        if (q.X >= _width) q.X = -2000; // curled-away gutter must not hover the scrollbar
        var t = GetGlobalTransformWithCanvas(); var screen = t * q;
        using var ev = new InputEventMouseMotion { Position = screen, GlobalPosition = screen,
            Relative = t.BasisXform(q - Lens.MaterialAt(p - relative)) };
        Replay(ev);
        var target = GetViewport().GuiGetHoveredControl();
        MouseDefaultCursorShape = target != null && _list.IsAncestorOf(target) ? target.MouseDefaultCursorShape : CursorShape.Arrow;
    }
    // Compare native leaf targets as well as row identity, so pressing artwork and releasing on
    // the transport (or an adjacent difficulty) can never operate the other command.
    private bool SameNativeTarget(Vector2 from, Vector2 to)
    {
        if (from.DistanceSquaredTo(to) < .01f) return true;
        Hover(Lens.Project(from), Vector2.Zero);
        var a = GetViewport().GuiGetHoveredControl();
        Hover(Lens.Project(to), Vector2.Zero);
        return a == GetViewport().GuiGetHoveredControl();
    }
    private void NativeClick(Vector2 q)
    {
        var screen = GetGlobalTransformWithCanvas() * q;
        Hover(Lens.Project(q), Vector2.Zero);
        using var down = new InputEventMouseButton { Position = screen, GlobalPosition = screen, ButtonIndex = MouseButton.Left,
            Pressed = true, ButtonMask = MouseButtonMask.Left };
        using var up = new InputEventMouseButton { Position = screen, GlobalPosition = screen, ButtonIndex = MouseButton.Left };
        Replay(down); if (IsInsideTree()) Replay(up);
    }
    private void Replay(InputEvent e)
    {
        _replaying = true;
        try { GetViewport().PushInput(e, true); } finally { _replaying = false; }
    }

    private void Emit(Vector2 q, float sign)
    {
        if (Mode == UiMotionMode.Off) return;
        _olderOrigin = _origin; _olderAge = _age; _olderSign = _sign;
        _origin = q + Vector2.Down * _list.ScrollVertical; _age = 0; _sign = sign;
        UpdateWave(); SetProcess(true);
    }
    private void UpdateWave()
    {
        float scroll = _list.ScrollVertical;
        _ink.SetShaderParameter("pulse0", new Vector4(_origin.X, _origin.Y - scroll, _age, _sign));
        _ink.SetShaderParameter("pulse1", new Vector4(_olderOrigin.X, _olderOrigin.Y - scroll, _olderAge, _olderSign));
        Vector3 clip = new(-1000, -1000, 0);
        if (GodotObject.IsInstanceValid(_waveCard) && _waveCard!.IsVisibleInTree() && Mode != UiMotionMode.Off)
        {
            float y = (_list.GetGlobalTransform().AffineInverse() * _waveCard.GetGlobalRect().Position).Y;
            clip = new(y, y + _waveCard.Size.Y, Mode == UiMotionMode.Reduced ? 1 : 0);
        }
        _ink.SetShaderParameter("wave_clip", clip);
        _ink.SetShaderParameter("protect_rect", new Vector4(_protectedCover.X, clip.X + _protectedCover.Y, _protectedCover.Z, _protectedCover.W));
    }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) { Cancel(); return; }
        _wheel.Step(delta);
        _age += (float)delta; _olderAge += (float)delta;
        bool held = _pointer != NoOwner && !_moved && GodotObject.IsInstanceValid(_pressedCard) && Mode == UiMotionMode.Full;
        if (held && _age >= .82f) Emit(_downMaterial, .54f);
        UpdateWave();
        if (!_wheel.Animating && !held && _age > .88f && _olderAge > .88f) SetProcess(false);
    }
}

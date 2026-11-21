using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>A contact travels on the inside edge of an existing plate. This is deliberately
/// not a screen-texture effect: pixels inside the plate (including colour specimens) never
/// enter the effect. No input capture, new hit area, callbacks to the owner, or idle processing.</summary>
internal sealed partial class ContactRim : Control
{
    private const double Flight = .48;
    private const int NoContact = -2;
    private static readonly Color Ice = new("75dadb"), Violet = new("ac9de1");
    private float _origin, _age = 1, _release = 1;
    private int _contact = NoContact;
    private Control? _surface;
    private Func<bool>? _enabled;
    private bool _observeGuiInput;
    private bool Enabled => _surface is not BaseButton { Disabled: true } && _enabled?.Invoke() != false;

    internal static ContactRim Attach(Control surface, Func<bool>? enabled = null, bool observeGuiInput = true)
    {
        var rim = new ContactRim { Name = "ContactRim", MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None, ClipContents = true, _surface = surface, _enabled = enabled,
            _observeGuiInput = observeGuiInput };
        surface.AddChild(rim); rim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (observeGuiInput) surface.GuiInput += rim.OnSurfaceInput;
        surface.VisibilityChanged += rim.CheckVisible;
        rim.SetProcess(false);
        return rim;
    }

    private void OnSurfaceInput(InputEvent input)
    {
        if (input.Device == InputEvent.DeviceIdEmulation) return;
        var press = false; var cancel = false; int id; Vector2 at;
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                id = -1; at = b.Position; press = b.Pressed; cancel = b.Canceled; break;
            case InputEventScreenTouch t:
                id = t.Index; at = t.Position; press = t.Pressed; cancel = t.Canceled; break;
            default: return;
        }
        if (cancel) CancelContact(id);
        else if (press) BeginContact(id, at);
        else EndContact(id, at);
    }

    internal void BeginContact(int id, Vector2 local)
    {
        // An expired press animation must not let a second pointer take the first one's place.
        if (_contact == NoContact && Excite(local)) _contact = id;
    }

    internal void EndContact(int id, Vector2 local)
    {
        if (_contact != id) return;
        _contact = NoContact;
        if (Enabled && IsVisibleInTree() && MenuContactFeedback.Mode == UiMotionMode.Full &&
            new Rect2(Vector2.Zero, Size).HasPoint(local))
        {
            _release = 0;
            SetProcess(true);
            QueueRedraw();
        }
    }

    internal void CancelContact(int id) { if (_contact == id) Reset(); }

    internal bool Excite(Vector2 local)
    {
        if (!Enabled || !IsVisibleInTree() || MenuContactFeedback.Mode == UiMotionMode.Off || Size.X < 8 || Size.Y < 8) return false;
        var w = Size.X - 6; var h = Size.Y - 6;
        var p = (local - new Vector2(3, 3)).Clamp(Vector2.Zero, new Vector2(w, h));
        var nearest = Mathf.Min(Mathf.Min(p.X, w - p.X), Mathf.Min(p.Y, h - p.Y));
        _origin = nearest == p.Y ? p.X : nearest == w - p.X ? w + p.Y : nearest == h - p.Y ? 2*w + h - p.X : 2*(w+h)-p.Y;
        _age = 0; _release = 1; SetProcess(true); QueueRedraw();
        return true;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta; _release += (float)delta;
        var mode = MenuContactFeedback.Mode;
        if (!Enabled || !IsVisibleInTree() || mode == UiMotionMode.Off) { Reset(); return; }
        if (Mathf.Min(_age, _release) >= (mode == UiMotionMode.Full ? Flight : .13))
            StopAnimation(); // retain the contact until an actual release or cancellation
        else QueueRedraw();
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _surface is not null) Reset();
    }
    private void CheckVisible() { if (!IsVisibleInTree()) Reset(); }
    private void StopAnimation() { _age = _release = 1; SetProcess(false); QueueRedraw(); }
    private void Reset() { _contact = NoContact; StopAnimation(); }
    public override void _ExitTree()
    {
        if (_surface != null && IsInstanceValid(_surface))
        {
            if (_observeGuiInput) _surface.GuiInput -= OnSurfaceInput;
            _surface.VisibilityChanged -= CheckVisible;
        }
        _surface = null;
    }
    private Vector2 Edge(float distance)
    {
        var w = Size.X-6; var h = Size.Y-6; var s = Mathf.PosMod(distance,2*(w+h));
        Vector2 p = s <= w ? new(s,0) : (s-=w) <= h ? new(w,s) : (s-=h) <= w ? new(w-s,h) : new(0,h-(s-w));
        return p + new Vector2(3,3);
    }
    public override void _Draw()
    {
        if (Size.X <= 7 || Size.Y <= 7 || !IsProcessing()) return;
        var mode = MenuContactFeedback.Mode;
        if (mode == UiMotionMode.Full) { Front(_age,1); Front(_release,-.56f); }
        else if (mode == UiMotionMode.Reduced) DrawLine(Edge(_origin-7),Edge(_origin+7),new Color(Ice,.48f*Mathf.Max(0,1-_age/.13f)),2,true);
    }
    private void Front(float age,float polarity)
    {
        if (age < 0 || age >= Flight) return;
        var fade = (1 - age/(float)Flight); var d = 335f*age;
        for (var side=-1; side<=1; side+=2)
        for (var k=0; k<9; k++)
        {
            var head = d-k*3.5f; if (head<0) break;
            var signed = Mathf.Cos(k*.61f)*polarity; var alpha = Mathf.Abs(signed)*fade*(1-k/10f)*.63f;
            DrawLine(Edge(_origin+side*head),Edge(_origin+side*Mathf.Max(0,head-3.5f)),new Color(signed>=0?Ice:Violet,alpha),1.9f,true);
        }
    }
}

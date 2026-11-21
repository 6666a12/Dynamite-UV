using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>Only the plate and its engraved terminals bend. The list's drum owns drag vs tap;
/// a real tap is replayed to this native focusable control after release.</summary>
public partial class HomeActionTile : Control
{
    [Signal] public delegate void PressedEventHandler();
    public string Title { get; init; } = "";
    public int PortIndex { get; init; }
    internal Vector2 Contact => new(Size.X - 3f, Size.Y * .5f);
    internal Vector2 LastActivation { get; private set; }
    private bool _hover;
    private UiHoverFade _fade;
    private float _press;
    private Color Tint => PortIndex == 0 ? new Color("65e2e2") : new Color("89a4b4");
    private static UiMotionProfile Motion => UiMotionProfile.For(GameSession.Settings.MotionMode);

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => Hover(true); MouseExited += () => Hover(false);
        FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
        var title = new Label { Text = Title, Name = "Caption", Position = new Vector2(74, 0),
            Size = new Vector2(420, Size.Y), VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore };
        UiLabels.Cjk(title, PortIndex == 0 ? 46 : 35, new Color("edf5fa"));
        AddChild(title); Resized += () => title.Size = new Vector2(420, Size.Y);
        SetProcess(false);
    }
    private void Hover(bool value)
    {
        _hover = value;
        if (Motion.IsAnimated) SetProcess(true); else _fade.Snap(value);
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        var repaint = _fade.Update(_hover, delta);
        if (_press > 0f) { _press = Mathf.MoveToward(_press, 0f, (float)(delta / Math.Max(.01, Motion.PressDuration))); repaint = true; }
        if (repaint) QueueRedraw();
        if (_fade.Settled(_hover) && _press <= 0f) SetProcess(false);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (TransitionDirector.IsBusy) return;
        // Pointer events reaching this tile are the common drum's confirmed, inverse-mapped tap;
        // a physical swipe or cancelled contact is never replayed. Keyboard needs no proxy.
        if (input is InputEventMouseButton { Pressed: true, Canceled: false, ButtonIndex: MouseButton.Left } mouse)
            LastActivation = mouse.Position;
        else if (HasFocus() && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space })
            LastActivation = new Vector2(94, Size.Y * .5f);
        else return;
        _press = Motion.IsAnimated ? 1f : 0f; SetProcess(_press > 0f || !_fade.Settled(_hover));
        QueueRedraw(); AcceptEvent(); EmitSignal(SignalName.Pressed);
    }
    public override void _Draw()
    {
        if (Size.X < 100f || Size.Y < 40f) return;
        var cy = Contact.Y; var right = Size.X;
        var hover = UiEase.Standard(_fade.Value);
        var fill = PortIndex == 0 ? new Color("143540") : new Color("172a35");
        DrawColoredPolygon(UiGeometry.CutCorners(Size, 13.5f), fill.Lerp(new Color("395263"), .38f*hover + .28f*_press));
        var axis = Tint;
        DrawLine(new(16, 2), new(right-16, 2), new Color("355463"), 1.3f, true);
        DrawLine(new(14, Size.Y-9), new(right-10, Size.Y-9), new Color("2c4350"), 1.5f, true);
        DrawLine(new(3, 18), new(3, Size.Y-18), axis, PortIndex == 0 ? 3.2f : 1.5f, true);
        for (int i=0;i<6;i++)
        {
            float x=right-172+i*10.5f;
            DrawLine(new(x, Size.Y-9), new(x, Size.Y-15-i%3*1.5f), new Color("45616c"), 1.3f, true);
        }
        if (PortIndex==0)
            DrawPolyline([new(30,cy-10),new(47,cy),new(30,cy+10)], axis, 2.5f, true);
        else for (int i=0;i<3;i++)
        {
            float y=cy-8+i*8;
            DrawLine(new(26,y), new(PortIndex==1?51-i*4:55,y),axis,2f,true);
            if (PortIndex==2) { float x=31+(i*7)%17; DrawLine(new(x,y-3),new(x,y+3),axis,2.5f,true); }
        }
        var dx = Motion.AllowDirectionalMotion ? 3f*hover : 0f;
        float tip=right-35+dx;
        DrawLine(new(right-91+dx,cy),new(tip,cy),axis,2f,true);
        DrawPolyline([new(tip-10,cy-9),new(tip,cy),new(tip-10,cy+9)],axis,2f,true);
        DrawLine(new(right-20,cy), Contact, new Color("7895a5"), 2f, true);
        DrawRect(new Rect2(Contact.X-3,cy-3,6,6),axis);
        if(HasFocus()) DrawLine(new(25,Size.Y-10),new(104,Size.Y-10),new Color("c0e8e9"),2.2f,true);
    }
}

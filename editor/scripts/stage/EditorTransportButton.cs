using Godot;

namespace DynamiteUniverse.Editor;

/// <summary>Editor-only transport control; audio state and lifecycle belong to the controller.</summary>
public partial class EditorTransportButton : Control
{
    public Action? ToggleRequested { get; init; }
    public bool Playing { get; private set; }
    public bool Available { get; private set; }
    private bool _hover;
    private readonly Vector2[] _shape =
        [new(12, 0), new(172, 0), new(184, 12), new(184, 68), new(172, 80), new(12, 80), new(0, 68), new(0, 12)];

    public override void _Ready()
    {
        Name = "EditorTransport";
        Position = new Vector2(868, 32);
        Size = new Vector2(184, 80);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; QueueRedraw(); };
    }

    public void Observe(bool available, bool playing)
    {
        if (Available == available && Playing == playing) return;
        Available = available;
        Playing = playing;
        MouseDefaultCursorShape = available ? CursorShape.PointingHand : CursorShape.Arrow;
        QueueRedraw();
    }

    public override bool _HasPoint(Vector2 point) => Geometry2D.IsPointInPolygon(point, _shape);

    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            or InputEventScreenTouch { Pressed: true })
        {
            AcceptEvent();
            if (Available) ToggleRequested?.Invoke();
        }
    }

    public override void _Draw()
    {
        var fill = new Color("18172d");
        if (Available) fill = fill.Lerp(new Color("7463c7"), _hover ? .65f : .35f);
        DrawColoredPolygon(_shape, fill);
        var ink = new Color("e6ecff");
        if (!Available) ink.A = .3f;
        if (Playing)
        {
            DrawRect(new Rect2(77, 24, 10, 32), ink);
            DrawRect(new Rect2(97, 24, 10, 32), ink);
        }
        else DrawColoredPolygon([new(82, 23), new(110, 40), new(82, 57)], ink);
    }
}

using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Borderless gameplay plate: cached shape, display-change lighting and optional pause input.</summary>
public partial class GameplayHudPlate : Control
{
    public Color Accent { get; init; }
    public int CornerSide { get; init; } // -1 original, +1 mirrored, 0 symmetric pause
    public int FontSize { get; init; } = 16;
    public Func<UiMotionMode> Motion { get; init; } = () => UiMotionMode.Full;
    public Action? Activated { get; init; }
    public Func<bool>? CanActivate { get; init; }
    private readonly HudLightState _light = new();
    private Label _label = null!;
    private Vector2[] _shape = [];
    private Vector2[] _near = [];
    private Vector2[] _far = [];
    private static readonly Color Ink = new("e6ecff");
    private static readonly Color Base = new("0a1020");

    public override void _Ready()
    {
        MouseFilter = Activated is null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        MouseDefaultCursorShape = Activated is null ? CursorShape.Arrow : CursorShape.PointingHand;
        _shape = Points(0);
        _near = Points(4);
        _far = Points(8);
        _label = new Label
        {
            Size = Size, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", UiFonts.Tech);
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_color", Ink);
        _label.AddThemeColorOverride("font_outline_color", Base);
        _label.AddThemeConstantOverride("outline_size", 2);
        AddChild(_label);
    }

    public void SetText(string text)
    {
        if (!_light.SetText(text, Motion())) return;
        _label.Text = text;
        QueueRedraw();
    }
    public void Pulse()
    {
        _light.Trigger(Motion());
        if (Activated is not null) _light.Trigger(Motion()); // pause uses full confirmation peak
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (_light.Advance(delta, Motion(), Activated is null ? 0.24 : 0.32)) QueueRedraw();
    }
    public override bool _HasPoint(Vector2 point) => Geometry2D.IsPointInPolygon(point, _shape);
    public override void _GuiInput(InputEvent e)
    {
        if (Activated is null) return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
            or InputEventScreenTouch { Pressed: true })
        {
            AcceptEvent();
            if (CanActivate?.Invoke() == false) return;
            Activated();
        }
    }
    public override void _Draw()
    {
        var amount = (float)_light.Amount;
        if (amount > 0 && Motion() == UiMotionMode.Full)
        {
            DrawColoredPolygon(_far, new Color(Accent, amount * 0.0525f));
            DrawColoredPolygon(_near, new Color(Accent, amount * 0.0525f));
        }
        DrawColoredPolygon(_shape, Base.Lerp(Accent, 0.25f + amount * 0.375f));
    }
    private Vector2[] Points(float pad)
    {
        var w = Size.X + pad * 2;
        var h = Size.Y + pad * 2;
        var c = 12 + pad;
        Vector2[] points = CornerSide == 0
            ? [new(c, 0), new(w-c, 0), new(w, c), new(w, h-c), new(w-c, h), new(c,h), new(0,h-c), new(0,c)]
            : [new(c,0), new(w,0), new(w,h-c), new(w-c,h), new(0,h), new(0,c)];
        for (var i = 0; i < points.Length; i++)
        {
            if (CornerSide > 0) points[i].X = w - points[i].X;
            points[i] -= new Vector2(pad, pad);
        }
        return points;
    }
}

using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>The account and catalog desks share a static chassis, not navigation or session state.</summary>
internal sealed partial class NetworkDeskChrome : Control
{
    internal static readonly Color Ink = new("EDF5FB"), Muted = new("A4B8C7"), Teal = new("65E2E2"),
        Purple = new("B49BFF"), Rule = new("293D4C"), Base = new("0D1722");
    private bool _store;

    internal static Button Install(Node parent, string section, string backName, Action back, bool store = false)
    {
        var chrome = new NetworkDeskChrome { Name = "NetworkDeskChrome", Size = UiLayout.DesignSize,
            MouseFilter = MouseFilterEnum.Ignore, _store = store };
        parent.AddChild(chrome);
        var button = OnlineWidgets.Link(parent, "←", new(54, 24, 99, 63), 33, back, Teal);
        button.Name = backName; button.AccessibilityName = "返回"; button.TooltipText = "返回";
        button.Alignment = HorizontalAlignment.Center;
        OnlineWidgets.Text(parent, "Dynamite Universe", new(174, 23, 368, 65), 33, Ink, true);
        OnlineWidgets.Text(parent, section, new(562, 28, 360, 54), 19, Muted);
        return button;
    }

    public override void _Draw()
    {
        DrawRect(new(Vector2.Zero, Size), Base);
        for (var x = -116; x <= Size.X + 300; x += 119)
            DrawLine(new(x, 0), new(x - 294, Size.Y), new Color("172632"), 1.5f, true);
        DrawColoredPolygon(UiGeometry.CutCorners(60, 27, 88, 57, 8), new Color("172C39"));
        DrawLine(new(530, 45), new(530, 72), Rule, 1.5f);
        DrawLine(new(60, 110), new(1860, 110), Rule, 1.5f);
        if (_store)
        {
            DrawRect(new(0, 963, Size.X, Size.Y - 963), new Color("132334"));
            DrawLine(new(550, 962), new(1855, 962), Rule, 1.5f);
        }
    }

    /// <summary>A filled, keyboard-accessible button. Styling only; the normal button owns activation.</summary>
    internal static Action<Color> KeyStyle(Button button, Color face, Color ink, bool withArrow = true)
    {
        button.AddThemeFontOverride("font", UiFonts.Cjk);
        button.AddThemeFontSizeOverride("font_size", 25);
        var paper = new Polygon2D { Name = "KeyFace", Polygon = UiGeometry.CutCorners(button.Size, 9),
            Color = face, ShowBehindParent = true };
        button.AddChild(paper);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 25, ContentMarginRight = withArrow ? 69 : 21 });
        button.AddThemeColorOverride("font_color", ink);
        button.AddThemeColorOverride("font_hover_color", ink);
        button.AddThemeColorOverride("font_pressed_color", ink);
        button.AddThemeColorOverride("font_disabled_color", ink);
        button.MouseEntered += () => paper.Color = button.Disabled ? face : face.Lightened(.09f);
        button.MouseExited += () => paper.Color = face;
        if (withArrow) OnlineWidgets.Text(button, "→", new(button.Size.X - 60, 0, 47, button.Size.Y), 28, ink, true);
        return value => { face = value; paper.Color = button.IsHovered() && !button.Disabled ? face.Lightened(.09f) : face; };
    }

    internal static void InputStyle(LineEdit input, int endMargin = 20)
    {
        foreach (var state in new[] { "normal", "read_only", "focus" })
            input.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = new Color("142532"),
                ContentMarginLeft = 19, ContentMarginRight = endMargin, ContentMarginTop = 4, ContentMarginBottom = 5,
                BorderWidthBottom = state == "focus" ? 3 : 1, BorderColor = state == "focus" ? Teal : Rule });
    }
}

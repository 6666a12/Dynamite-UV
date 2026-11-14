using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Online v2 visual primitives. Native controls retain keyboard and touch input.</summary>
internal static class OnlineWidgets
{
    private static readonly Font Heading = new SystemFont
    {
        FontNames = new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "sans-serif" }, FontWeight = 600,
    };
    public static Label Text(Node parent, string text, Rect2 rect, int size, Color color, bool tech = false)
    {
        var label = new Label { Text = text, Position = rect.Position, Size = rect.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true,
            VerticalAlignment = VerticalAlignment.Center };
        UiLabels.Apply(label, tech ? UiFonts.Tech : size >= 32 ? Heading : UiFonts.Cjk, size, color);
        parent.AddChild(label); return label;
    }
    public static Button Link(Node parent, string text, Rect2 rect, int size, Action action, Color? color = null)
    {
        var button = new Button { Text = text, Position = rect.Position, Size = rect.Size,
            Alignment = HorizontalAlignment.Left, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        var focus = new StyleBoxFlat { BgColor = Colors.Transparent, BorderWidthBottom = 2,
            BorderColor = MenuStyle.Accent };
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeFontOverride("font", UiFonts.Cjk); button.AddThemeFontSizeOverride("font_size", size);
        button.AddThemeColorOverride("font_color", color ?? MenuStyle.Text);
        button.AddThemeColorOverride("font_hover_color", MenuStyle.Accent);
        button.AddThemeColorOverride("font_pressed_color", MenuStyle.Secondary);
        button.AddThemeColorOverride("font_disabled_color", MenuStyle.Muted);
        button.Pressed += () => { if (!TransitionDirector.IsBusy) action(); };
        parent.AddChild(button); return button;
    }
    public static Button Primary(Node parent, string text, Rect2 rect, Action action)
    {
        var button = Link(parent, text, rect, 26, action, MenuStyle.Ink);
        button.AddThemeFontOverride("font", Heading);
        var surface = new Polygon2D { Polygon = UiGeometry.CutCorners(rect.Size, 14),
            Color = MenuStyle.Accent, ShowBehindParent = true };
        button.AddChild(surface);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginLeft = 32, ContentMarginRight = 72 });
        button.AddThemeColorOverride("font_hover_color", MenuStyle.Ink);
        button.AddThemeColorOverride("font_pressed_color", MenuStyle.Ink);
        button.AddThemeColorOverride("font_disabled_color", MenuStyle.Ink);
        Text(button, "→", new(rect.Size.X - 64, 0, 40, rect.Size.Y), 32, MenuStyle.Ink, true);
        button.MouseEntered += () => surface.Color = MenuStyle.Accent.Lightened(0.12f);
        button.MouseExited += () => surface.Color = MenuStyle.Accent;
        return button;
    }
    public static LineEdit Input(Node parent, string placeholder, Rect2 rect, int max, bool secret = false)
    {
        var input = new LineEdit { Position = rect.Position, Size = rect.Size, PlaceholderText = placeholder,
            MaxLength = max, Secret = secret };
        input.AddThemeFontOverride("font", UiFonts.Cjk); input.AddThemeFontSizeOverride("font_size", 26);
        input.AddThemeColorOverride("font_color", MenuStyle.Text);
        input.AddThemeColorOverride("font_placeholder_color", MenuStyle.Secondary);
        input.AddThemeColorOverride("caret_color", MenuStyle.Accent);
        foreach (var state in new[] { "normal", "read_only", "focus" })
            input.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginBottom = 22 });
        var line = new ColorRect { Position = new(0, rect.Size.Y - 2), Size = new(rect.Size.X, 2),
            Color = new Color("283C50"), MouseFilter = Control.MouseFilterEnum.Ignore };
        input.AddChild(line);
        input.FocusEntered += () => line.Color = MenuStyle.Accent;
        input.FocusExited += () => line.Color = new Color("283C50");
        parent.AddChild(input); return input;
    }
    public static void Surface(Node parent, Rect2 rect)
    {
        parent.AddChild(new Polygon2D { Position = rect.Position,
            Polygon = UiGeometry.CutCorners(rect.Size, 18), Color = MenuStyle.Surface });
    }
}

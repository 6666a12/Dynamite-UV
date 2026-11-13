using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>v3 menu appearance only; no settings, input or navigation state.</summary>
internal static class MenuStyle
{
    public static readonly Color Background = new("0D1524");
    public static readonly Color Surface = new("18283D");
    public static readonly Color Idle = new("192D3F");
    public static readonly Color Active = new("24445A");
    public static readonly Color Text = new("F0F5FF");
    public static readonly Color Secondary = new("AFBED2");
    public static readonly Color Muted = new("899DB7");
    public static readonly Color Accent = new("54E2EC");
    public static readonly Color Ink = new("082A37");
    public static readonly Font HeadingFont = GD.Load<Font>("res://assets/fonts/SpaceGrotesk-SemiBold.ttf");

    public static void Heading(Label label) => UiLabels.Apply(label, HeadingFont, 36, Text);

    public static float Baseline(Font font, int size, float height) =>
        (height - font.GetHeight(size)) * 0.5f + font.GetAscent(size);

    public static void BackChevron(Control target, Color color)
    {
        var center = target.Size * 0.5f;
        target.DrawLine(center + new Vector2(7, -12), center + new Vector2(-7, 0), color, 2f, true);
        target.DrawLine(center + new Vector2(-7, 0), center + new Vector2(7, 12), color, 2f, true);
    }
}

using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Two independent baseline-aligned wordmarks share the established group entrance.</summary>
internal static class HomeBrandBlock
{
    public static void Build(Node home, Action<Control, int> register)
    {
        Word(home, register, "DYNAMITE", 321, new Color("edf5fa"));
        Word(home, register, "UNIVERSE", 444, new Color("85eff1"));
        var line = new ColorRect { Position = new(81, 473), Size = new(93, 3), Color = new("b49bff"), MouseFilter = Control.MouseFilterEnum.Ignore };
        home.AddChild(line); register(line, 2);
    }
    private static void Word(Node home, Action<Control, int> register, string value, float baseline, Color color)
    {
        var face = UiFonts.TechBold;
        var label = new Label { Name = value, Text = value, Position = new(73.5f, baseline - face.GetAscent(125)),
            Size = new(827, 149), VerticalAlignment = VerticalAlignment.Top, MouseFilter = Control.MouseFilterEnum.Ignore };
        UiLabels.Apply(label, face, 125, color); home.AddChild(label); register(label, 1);
    }
}

using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Named label treatments shared by the programmatic menu screens.</summary>
public static class UiLabels
{
    public static void Tech(Label label, int size, Color color) =>
        Apply(label, UiFonts.Tech, size, color);

    public static void TechBold(Label label, int size, Color color) =>
        Apply(label, UiFonts.TechBold, size, color);

    public static void Cjk(Label label, int size, Color color) =>
        Apply(label, UiFonts.Cjk, size, color);

    public static void Apply(Label label, Font font, int size, Color color)
    {
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
    }
}

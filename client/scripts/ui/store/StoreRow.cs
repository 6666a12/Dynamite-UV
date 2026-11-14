using Godot;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>One selectable catalog row, matching the Online v2 list geometry.</summary>
internal sealed partial class StoreRow : Button
{
    private readonly StoreRelease _release;
    private readonly Action<StoreRelease> _selected;
    public StoreRelease Release => _release;
    public Texture2D? Thumbnail { get; set; }
    private bool _active;
    public StoreRow(StoreRelease release, Action<StoreRelease> selected)
    {
        _release = release; _selected = selected;
        CustomMinimumSize = new(800, 132); Size = CustomMinimumSize;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        TooltipText = release.Title;
        Pressed += () => _selected(_release);
        MouseEntered += QueueRedraw; MouseExited += QueueRedraw;
    }
    public void Select(bool selected) { _active = selected; QueueRedraw(); }
    private static string Fit(string text, int size, float width)
    {
        if (UiFonts.Cjk.GetStringSize(text, fontSize: size).X <= width) return text;
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        var lo = 0; var hi = elements.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var candidate = text[..(mid == elements.Length ? text.Length : elements[mid])] + "…";
            if (UiFonts.Cjk.GetStringSize(candidate, fontSize: size).X <= width) lo = mid; else hi = mid - 1;
        }
        return text[..(lo == elements.Length ? text.Length : elements[lo])] + "…";
    }
    public override void _Draw()
    {
        var x = _active ? 0 : 22;
        if (_active || IsHovered() || HasFocus())
            DrawColoredPolygon(UiGeometry.CutCorners(new(Size.X, 132), 14), _active ? MenuStyle.Active : MenuStyle.Surface);
        if (_active) DrawRect(new(0, 15, 4, 102), MenuStyle.Accent);
        DrawRect(new(x + 24, 18, 96, 96), new Color("21354D"));
        if (Thumbnail is { } texture && GodotObject.IsInstanceValid(texture)) DrawTextureRect(texture, new(x + 24, 18, 96, 96), false);
        else DrawLine(new(x + 44, 92), new(x + 100, 40), MenuStyle.Muted, 2);
        DrawString(UiFonts.Cjk, new(x + 148, 50), Fit(_release.Title, _active ? 34 : 28, 490), HorizontalAlignment.Left, 490,
            _active ? 34 : 28, _active ? MenuStyle.Text : MenuStyle.Secondary);
        var charters = _release.Display?.Charts.SelectMany(c => c.Charters).Distinct().ToArray() ?? [];
        DrawString(UiFonts.Cjk, new(x + 150, 101), Fit("谱师 " + (charters.Length == 0 ? "—" : string.Join(", ", charters)), 17, 390),
            HorizontalAlignment.Left, 390, 17, MenuStyle.Muted);
        var charts = _release.Display?.Charts.Take(2).ToArray() ?? [];
        for (var i = 0; i < charts.Length; i++)
        {
            var c = charts[i]; var bx = Size.X - 156 + 52 * i;
            DrawColoredPolygon(UiGeometry.CutCorners(bx, 70, 44, 44, 6), UiFonts.DiffColor(c.Difficulty));
            DrawString(UiFonts.Tech, new(bx, 99), c.Unrated ? "?" : c.Level?.ToString("00") ?? "?",
                HorizontalAlignment.Center, 44, 20, MenuStyle.Ink);
        }
    }
}

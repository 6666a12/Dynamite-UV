using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>Shared ink and type layout for actual library packs and catalog releases. No dummy media,
/// level, producer or transport: the page supplies only the data it owns.</summary>
internal static class CatalogueCardFace
{
    public static readonly Color Accent = new("B49BFF");
    private static readonly Color Ink = new("251F35"), Hover = new("312A43"), Selected = new("3C324D");
    private static readonly Color Etching = new("51465F");

    public static void Draw(Control owner, float hover = 0f, float selected = 0f, bool expanded = false, float commit = 0f)
    {
        var s = owner.Size;
        if (s.X < 24 || s.Y < 16) return;
        var fill = Ink.Lerp(Hover, hover).Lerp(Selected, selected * .75f).Lerp(Accent, .12f * commit);
        owner.DrawColoredPolygon(UiGeometry.CutCorners(s, expanded ? 14f : 10f), fill);
        // Continuous conductors and etched timing marks are part of the original card pigment:
        // the drum shader refracts these very pixels, rather than painting a luminous ring over them.
        float y = s.Y - (expanded ? 21f : 8f);
        var wire = new Color(Etching, expanded ? .78f : .46f);
        owner.DrawLine(new(15, y), new(s.X - 15, y), wire, 1f, true);
        for (float x = 35; x < s.X - 20; x += 32)
            owner.DrawLine(new(x, y - 3), new(x + 3, y), wire, 1f, true);
        if (expanded)
        {
            for (var i = 0; i < 8; i++)
            {
                var x = s.X - 22 - i * 18;
                owner.DrawLine(new(x - s.Y * .78f, s.Y - 4), new(x, 4), new Color(Accent, .062f), 1.35f, true);
            }
        }
        if (selected > 0)
        {
            float h = Mathf.Min(expanded ? 148f : 62f, s.Y - 16f) * selected;
            owner.DrawRect(new Rect2(2, (s.Y - h) * .5f, 4f, h), new Color(Accent, .9f * selected));
        }
    }

    public static void DrawHeading(Control owner, string title, string byline, float x = 24f, float availableWidth = -1f)
    {
        float w = availableWidth < 0 ? owner.Size.X * .55f : availableWidth;
        if (w <= 1) return;
        owner.DrawString(UiFonts.Cjk, new(x, owner.Size.Y * .44f), title, HorizontalAlignment.Left, w, 30, MenuStyle.Text);
        owner.DrawString(UiFonts.Cjk, new(x, owner.Size.Y * .78f), byline, HorizontalAlignment.Left, w, 18, MenuStyle.Secondary);
    }
}

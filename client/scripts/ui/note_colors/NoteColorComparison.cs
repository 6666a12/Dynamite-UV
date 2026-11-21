using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;

/// <summary>A read-only pigment comparison. Both sides share the exact same matte,
/// note geometry, material parameters and brightness. There is no input or effect layer here.</summary>
internal sealed class NoteColorComparison
{
    private readonly Rect2 _rect;
    private readonly Color _background;
    private readonly bool _metrics;
    private readonly float _scale;
    private readonly NotePaletteSpecimen[] _notes = new NotePaletteSpecimen[2];
    private readonly Label[] _values = new Label[2];
    private readonly Label[] _titles = new Label[2];
    private (NoteType Type, Color Original, Color Current, bool Comparing, float Brightness)? _shown;

    internal NoteColorComparison(Node parent, Rect2 rect, Color background, bool metrics)
    {
        _rect = rect;
        _background = background;
        _metrics = metrics;
        _scale = metrics ? .92f : .51f;
        var matte = new ColorRect { Position = rect.Position, Size = rect.Size,
            Color = background, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(matte);
        var half = rect.Size.X / 2f;
        var gutter = metrics ? 20f : 9f;
        var top = metrics ? 10f : 0f;
        matte.AddChild(new ColorRect { Position = new(half, 8), Size = new(1, rect.Size.Y - 16),
            Color = new Color("334250"), MouseFilter = Control.MouseFilterEnum.Ignore });
        for (var i = 0; i < 2; i++)
        {
            var title = LabelAt(matte, half * i + gutter, top, half - 2 * gutter, 29, metrics ? 17 : 15, true);
            title.Text = i == 0 ? "原方案" : "草稿";
            _titles[i] = title;
            _notes[i] = new NotePaletteSpecimen { Scale = Vector2.One * _scale };
            matte.AddChild(_notes[i]);
            _values[i] = LabelAt(matte, half * i + gutter, rect.Size.Y - (metrics ? 49 : 29),
                half - 2 * gutter, metrics ? 40 : 25, metrics ? 27 : 14, false);
        }
    }

    internal void Show(NoteType type, Color original, Color current, bool comparing, float brightness)
    {
        var next = (type, original, current, comparing, brightness);
        if (_shown is { } previous && previous == next) return;
        _shown = next;
        _titles[1].Text = comparing ? "原方案" : "草稿";
        for (var i = 0; i < 2; i++)
        {
            var c = i == 0 || comparing ? original : current;
            var half = _rect.Size.X * .5f;
            var hold = type == NoteType.HoldHead;
            var gap = _metrics ? 72f : 68f;
            _notes[i].Position = new Vector2(half * (i + .5f), _rect.Size.Y * .50f + (hold ? gap * _scale * .5f : 0f));
            _notes[i].Configure(type, c, 208f, brightness, showPath: hold, tailWidth: 173f, gap: gap);
            var ratio = NotePaletteMetrics.Contrast(c, _background);
            _values[i].Text = _metrics ? $"{ratio:0.00} : 1" : "#" + NotePalette.ToHex(c);
            _values[i].AddThemeColorOverride("font_color", _metrics && ratio < NotePaletteMetrics.MinContrast
                ? new Color("E8BB79") : MenuStyle.Text);
        }
    }

    private static Label LabelAt(Control parent, float x, float y, float w, float h, int size, bool chinese)
    {
        var label = new Label { Position = new(x, y), Size = new(w, h), VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", chinese ? UiFonts.Cjk : UiFonts.Tech);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", MenuStyle.Secondary);
        parent.AddChild(label);
        return label;
    }
}

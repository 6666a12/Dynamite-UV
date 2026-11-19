using System.Globalization;
using Godot;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor;

/// <summary>Low-border stage palette. It borrows the game's dark plate, restrained accents,
/// and brightness-based state hierarchy instead of rendering a grid of button cards.</summary>
public partial class EditorContextMenu : Control
{
    private const float Width = 352, Height = 496, Pad = 16, Inner = Width - Pad * 2;
    private readonly record struct Cell(string Kind, string Value, string Key, string Fallback, Rect2 Box, string Hint = "");
    private static readonly Cell[] Cells = MakeCells();
    private static readonly Color Surface = new("0b1228", .92f), Line = new("74859a", .30f),
        Hover = new("28d9ff", .07f), Accent = new("28d9ff"), Ink = new("edf3fb"), Muted = new("a7b5c7");
    private IReadOnlyDictionary<string, string> _text = new Dictionary<string, string>();
    private string _tool = "SELECT", _type = "Tap", _track = "Center", _effects = "Full";
    // Keep the packaged Latin face, but also request real semibold CJK glyphs for translated labels.
    private readonly FontVariation _menuFont = new()
    {
        BaseFont = DynamiteUniverse.Ui.UiFonts.TechBold,
        Fallbacks = [new SystemFont { FontNames = ["Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC"], FontWeight = 600 }],
    };
    private string? _pendingTrack;
    private int _volume = 100, _hover = -1, _cursor = -1, _pressed = -1;
    private bool _moved, _keyboard;
    private Vector2 _openingPoint, _origin;
    private float _scale = 1;
    private Vector2[] _plate = [];

    public event Action<string, string>? Requested;
    public bool IsOpen => Visible;
    public Rect2 MenuRect => new(_origin, new Vector2(Width, Height) * _scale);
    public Vector2 FirstButtonPoint => ItemRect("track", "Left").GetCenter();

    public EditorContextMenu()
    {
        Name = "ContextMenu";
        ZIndex = 100;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private static Cell[] MakeCells()
    {
        var cells = new List<Cell>();
        void Add(string kind, string value, string key, string fallback, float x, float y, float w, float h, string hint = "") =>
            cells.Add(new(kind, value, key, fallback, new Rect2(x, y, w, h), hint));
        var third = Inner / 3f;
        foreach (var (value, index) in new[] { ("Left", 0), ("Center", 1), ("Right", 2) })
            Add("track", value, "Context." + value, value.ToUpperInvariant(), Pad + index * third, 12, third, 34);

        (string Type, string Label, string Key)[] notes = [("Tap", "TAP", "1"), ("Drag", "DRAG", "2"), ("Hold", "HOLD", "3"),
            ("ExTap", "EX TAP", "5"), ("Mixer", "MIXER", "6"), ("Mine", "MINE", "7"), ("BarLine", "BAR LINE", "")];
        for (var i = 0; i < notes.Length; ++i)
            Add("type", notes[i].Type, "Inspector." + notes[i].Type, notes[i].Label, Pad, 68 + i * 36, Inner, 32, notes[i].Key);

        Add("tool", "SELECT", "Tools.Select", "SELECT", Pad, 336, Inner / 2 - 3, 34);
        Add("tool", "PATH", "Tools.Path", "PATH", Pad + Inner / 2 + 3, 336, Inner / 2 - 3, 34);
        const float legend = 52;
        var effectWidth = (Inner - legend) / 3f;
        foreach (var (value, index) in new[] { ("Off", 0), ("Reduced", 1), ("Full", 2) })
            Add("effects", value, "Context." + value, value.ToUpperInvariant(), Pad + legend + index * effectWidth, 386, effectWidth, 30);
        Add("volume", "", "Context.Volume", "MUSIC", Pad, 436, Inner, 44);
        return cells.ToArray();
    }

    internal Rect2 ItemRect(string kind, string value)
    {
        var box = Cells.First(c => c.Kind == kind && c.Value == value).Box;
        return new(_origin + box.Position * _scale, box.Size * _scale);
    }

    public void SetText(IReadOnlyDictionary<string, string> text) { _text = text; QueueRedraw(); }
    public void SetVolume(double value) { _volume = (int)Math.Round(Math.Clamp(value, 0, 100)); QueueRedraw(); }

    public void Sync(string tool, V2NoteType type, EditorTrack track, string effects)
    {
        (_tool, _type, _track, _effects) = (tool, type.ToString(), track.ToString(), effects);
        if (_pendingTrack == _track) _pendingTrack = null;
        QueueRedraw();
    }

    internal bool IsCurrent(string kind, string value) => kind switch
    {
        "track" => value == (_pendingTrack ?? _track),
        "type" => _tool == "PLACE" && value == _type,
        "tool" => value == _tool,
        "effects" => value == _effects,
        _ => false,
    };

    internal static Rect2 PopupRect(Vector2 point, Vector2 available)
    {
        var scale = Math.Min(1f, Math.Min(available.X / Width, available.Y / Height));
        var size = new Vector2(Width, Height) * scale;
        // Flip each axis independently. When neither side fits, keep the text at this
        // scale and slide just that axis inside the viewport instead of shrinking again.
        static float Origin(float cursor, float extent, float room)
        {
            if (cursor + extent <= room) return cursor;
            if (cursor >= extent) return cursor - extent;
            return cursor <= room / 2 ? Math.Max(0f, room - extent) : 0f;
        }
        point = point.Clamp(Vector2.Zero, available);
        return new(new Vector2(Origin(point.X, size.X, available.X), Origin(point.Y, size.Y, available.Y)), size);
    }

    public void OpenAt(Vector2 point, Vector2 available)
    {
        if (available.X <= 0 || available.Y <= 0) { Close(); return; }
        Size = available;
        var bounds = PopupRect(point, available);
        _origin = bounds.Position;
        _scale = bounds.Size.X / Width;
        _plate = PlateAt((point - _origin) / _scale);
        _openingPoint = point;
        _moved = _keyboard = false;
        _hover = _cursor = _pressed = -1;
        Show();
        QueueRedraw();
    }

    public void Close() { Hide(); _pendingTrack = null; _pressed = _cursor = _hover = -1; }

    private int Hit(Vector2 designPoint) => Array.FindIndex(Cells, c => c.Box.HasPoint(designPoint));

    public bool FilterInput(InputEvent input, Vector2 localPoint)
    {
        if (!Visible) return false;
        if (input is InputEventKey key) { if (key.Pressed) KeyPressed(key); return true; }
        var point = (localPoint - _origin) / _scale;
        if (input is InputEventMouseMotion)
        {
            if (!_moved && localPoint.DistanceTo(_openingPoint) < 3 * _scale) return true;
            _moved = true;
            _keyboard = false;
            var hit = Hit(point);
            if (hit != _hover) { _hover = hit; QueueRedraw(); }
            if (hit >= 0 && Cells[hit].Kind is "track" or "volume") Activate(hit, point);
        }
        else if (input is InputEventMouseButton button)
        {
            if (button.Pressed && (button.ButtonIndex == MouseButton.Right || !MenuRect.HasPoint(localPoint))) Close();
            else if (button.ButtonIndex == MouseButton.Left)
            {
                var hit = Hit(point);
                if (button.Pressed) { _pressed = _cursor = hit; _keyboard = false; }
                else { if (_pressed == hit && hit >= 0) Activate(hit, point); _pressed = -1; }
                QueueRedraw();
            }
        }
        return true;
    }

    private void KeyPressed(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Escape: Close(); return;
            case Key.Tab:
                var backward = key.ShiftPressed;
                _cursor = _cursor < 0 ? (backward ? Cells.Length - 1 : 0) :
                    (_cursor + (backward ? -1 : 1) + Cells.Length) % Cells.Length;
                _keyboard = true; _hover = -1; QueueRedraw();
                break;
            case Key.Enter or Key.KpEnter or Key.Space when _keyboard && _cursor >= 0 && !key.Echo:
                Activate(_cursor, null); break;
            case Key.Left or Key.Right when _keyboard && _cursor >= 0 && Cells[_cursor].Kind == "volume":
                RequestVolume(Math.Clamp(_volume + (key.Keycode == Key.Left ? -1 : 1), 0, 100)); break;
        }
    }

    private void Activate(int index, Vector2? point)
    {
        var cell = Cells[index];
        if (cell.Kind == "volume")
        {
            if (point is { } position)
                RequestVolume((int)Math.Round(100 * Math.Clamp((position.X - cell.Box.Position.X) / cell.Box.Size.X, 0, 1)));
            return;
        }
        if (cell.Kind == "track")
        {
            if (IsCurrent(cell.Kind, cell.Value)) return;
            _pendingTrack = cell.Value;
        }
        Requested?.Invoke(cell.Kind, cell.Value);
        QueueRedraw();
    }

    private void RequestVolume(int percent)
    {
        if (_volume == percent) return;
        _volume = percent;
        Requested?.Invoke("volume", percent.ToString(CultureInfo.InvariantCulture));
        QueueRedraw();
    }

    private static Color TypeColor(string value) => value switch
    {
        "Drag" => new Color("ff5d96"),
        "Hold" => new Color("ffd166"),
        "Mixer" => new Color("b8a2ff"),
        "Mine" => new Color("ff5964"),
        "ExTap" => new Color("7eeaff"),
        _ => new Color("edf3fb"),
    };

    // Keep the three free corners bevelled; the attached corner meets the cursor tip.
    internal static Vector2[] PlateAt(Vector2 anchor)
    {
        const float cut = 12;
        var vertices = new List<Vector2>(8);
        void Corner(Vector2 tip, Vector2 before, Vector2 after)
        {
            if (tip.DistanceSquaredTo(anchor) < .01f) vertices.Add(tip);
            else { vertices.Add(before); vertices.Add(after); }
        }
        Corner(new(0, 0), new(0, cut), new(cut, 0));
        Corner(new(Width, 0), new(Width - cut, 0), new(Width, cut));
        Corner(new(Width, Height), new(Width, Height - cut), new(Width - cut, Height));
        Corner(new(0, Height), new(cut, Height), new(0, Height - cut));
        return vertices.ToArray();
    }

    public override void _Draw()
    {
        DrawSetTransform(_origin, 0, Vector2.One * _scale);
        DrawColoredPolygon([new(4, 5), new(Width - 8, 5), new(Width - 4, 12), new(Width - 4, Height - 8),
            new(Width - 12, Height - 4), new(4, Height - 4), new(0, Height - 12), new(0, 12)], new Color(0, 0, 0, .28f));
        DrawColoredPolygon(_plate, Surface);
        DrawLine(new(0, 12), new(0, Height - 12), new Color(Accent, .70f), 2, true);
        DrawLine(new(12, Height - 1), new(Width - 12, Height - 1), new Color(Accent, .20f), 1, true);
        DrawLine(new(Pad, 56), new(Width - Pad, 56), Line, 1, true);
        DrawLine(new(Pad, 326), new(Width - Pad, 326), Line, 1, true);
        DrawLine(new(Pad, 378), new(Width - Pad, 378), Line, 1, true);
        DrawLine(new(Pad, 426), new(Width - Pad, 426), Line, 1, true);

        var font = _menuFont;
        void Text(Rect2 box, string label, Color color, HorizontalAlignment align, int size)
        {
            var width = box.Size.X - 10;
            while (size > 11 && font.GetStringSize(label, fontSize: size).X > width) --size;
            var baseline = box.Position + new Vector2(5, (box.Size.Y - font.GetHeight(size)) / 2 + font.GetAscent(size));
            DrawString(font, baseline, label, align, width, size, color);
        }
        Text(new Rect2(Pad, 386, 49, 30), _text.GetValueOrDefault("Context.Effects", "FX"), Muted, HorizontalAlignment.Left, 16);
        for (var index = 0; index < Cells.Length; ++index)
        {
            var cell = Cells[index];
            var current = IsCurrent(cell.Kind, cell.Value);
            var hovered = index == _hover || index == _pressed;
            var label = _text.GetValueOrDefault(cell.Key, cell.Fallback);
            if (hovered) DrawRect(cell.Box, Hover);
            if (_keyboard && index == _cursor) DrawRect(cell.Box.Grow(-2), new Color(Ink, .75f), false, 1);
            if (cell.Kind == "volume")
            {
                var track = new Rect2(cell.Box.Position + new Vector2(5, 34), new Vector2(cell.Box.Size.X - 10, 3));
                DrawRect(track, new Color(Muted, .20f));
                DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * _volume / 100f, 3)), new Color(Accent, .72f));
                DrawCircle(new Vector2(track.Position.X + track.Size.X * _volume / 100f, track.Position.Y + 1.5f), 3.5f, Accent);
                Text(new Rect2(cell.Box.Position, new Vector2(cell.Box.Size.X * .5f, 28)), label, Ink, HorizontalAlignment.Left, 18);
                Text(new Rect2(cell.Box.Position + new Vector2(cell.Box.Size.X * .5f, 0), new Vector2(cell.Box.Size.X * .5f, 28)),
                    $"{_volume}%", Ink, HorizontalAlignment.Right, 18);
                continue;
            }
            var color = cell.Kind == "type" ? TypeColor(cell.Value) : Ink;
            if (!current) color.A = hovered ? 1f : .86f;
            if (cell.Kind == "type")
            {
                if (current) DrawRect(new Rect2(cell.Box.Position, new Vector2(3, cell.Box.Size.Y)), new Color(color, .90f));
                Text(new Rect2(cell.Box.Position + new Vector2(11, 0), new Vector2(cell.Box.Size.X - 52, cell.Box.Size.Y)), label, color, HorizontalAlignment.Left, 21);
                if (cell.Hint.Length > 0) Text(new Rect2(cell.Box.End - new Vector2(32, cell.Box.Size.Y), new Vector2(26, cell.Box.Size.Y)), cell.Hint, Muted, HorizontalAlignment.Right, 16);
            }
            else
            {
                if (current) DrawRect(new Rect2(cell.Box.Position + new Vector2(0, cell.Box.Size.Y - 2), new Vector2(cell.Box.Size.X, 2)), new Color(Accent, .90f));
                Text(cell.Box, label, current ? Accent : color, HorizontalAlignment.Center, cell.Kind == "track" ? 20 : 18);
            }
        }
    }
}

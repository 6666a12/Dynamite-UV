using Godot;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor;

/// <summary>Stage-local menu surface. Emits requests; never edits the document or owns audio.</summary>
public partial class EditorContextMenu : Control
{
    private readonly PanelContainer _panel = new();
    private readonly VBoxContainer _rows = new();
    private readonly Dictionary<string, Label> _labels = new();
    private readonly Dictionary<(string Kind, string Value), Button> _buttons = new();
    private readonly HSlider _volume = new() { MinValue = 0, MaxValue = 100, Step = 1, Value = 100, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label _percent = new() { Text = "100%", CustomMinimumSize = new Vector2(48, 0) };
    public event Action<string, string>? Requested;
    public bool IsOpen => Visible;
    public Vector2 FirstButtonPoint => _buttons[("track", "Left")].GetGlobalRect().GetCenter();
    public Rect2 MenuRect => new(_panel.Position, _panel.Size * _panel.Scale);

    public EditorContextMenu()
    {
        Name = "ContextMenu";
        ZIndex = 100;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AddChild(_panel);
        var background = new StyleBoxFlat
        {
            BgColor = new Color("0e1528"), BorderColor = new Color("2a3560"),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 14, ContentMarginBottom = 14,
            ShadowColor = new Color(0, 0, 0, .45f), ShadowSize = 12,
        };
        _panel.AddThemeStyleboxOverride("panel", background);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _panel.AddChild(scroll);
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 9);
        scroll.AddChild(_rows);
        AddLabel("Context.Title");
        AddLabel("Context.Track");
        var tracks = new HBoxContainer(); _rows.AddChild(tracks);
        AddButton(tracks, "track", "Left", "Inspector.LeftStage");
        AddButton(tracks, "track", "Center", "Inspector.CenterStage");
        AddButton(tracks, "track", "Right", "Inspector.RightStage");
        AddLabel("Inspector.NoteType");
        var types = new GridContainer { Columns = 2 }; _rows.AddChild(types);
        foreach (var type in new[] { V2NoteType.Tap, V2NoteType.Drag, V2NoteType.Hold, V2NoteType.ExTap, V2NoteType.Mixer, V2NoteType.Mine, V2NoteType.BarLine })
            AddButton(types, "type", type.ToString(), "Inspector." + type);
        AddLabel("Context.Mode");
        var modes = new HBoxContainer(); _rows.AddChild(modes);
        AddButton(modes, "tool", "SELECT", "Tools.Select");
        AddButton(modes, "tool", "PATH", "Tools.Path");
        _rows.AddChild(new HSeparator());
        AddLabel("Context.Volume");
        var volumeRow = new HBoxContainer(); _rows.AddChild(volumeRow);
        volumeRow.AddChild(_volume); volumeRow.AddChild(_percent);
        _volume.ValueChanged += value =>
        {
            _percent.Text = $"{value:0}%";
            Requested?.Invoke("volume", ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        };
        AddLabel("Context.Effects");
        var effects = new HBoxContainer(); _rows.AddChild(effects);
        foreach (var value in new[] { "Off", "Reduced", "Full" }) AddButton(effects, "effects", value, "Context." + value);
        AddLabel("Context.Dismiss");
        Sync("SELECT", V2NoteType.Tap, EditorTrack.Center, "Full");
    }

    private void AddLabel(string key)
    {
        var label = new Label { Text = key };
        label.AddThemeFontSizeOverride("font_size", key == "Context.Title" ? 17 : 13);
        label.AddThemeColorOverride("font_color", new Color(key == "Context.Title" ? "35e0ff" : "7c88b0"));
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(100, 0);
        _labels[key] = label; _rows.AddChild(label);
    }

    private void AddButton(Container row, string kind, string value, string key)
    {
        var button = new Button
        {
            Text = key, ToggleMode = true, FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        button.SetMeta("translation", key);
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", new Color("dfe6ff"));
        button.AddThemeColorOverride("font_pressed_color", new Color("35e0ff"));
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" })
            button.AddThemeStyleboxOverride(state, new StyleBoxFlat
            {
                BgColor = new Color(state == "normal" ? "141e36" : "1a2947"),
                BorderColor = new Color(state.Contains("pressed") ? "35e0ff" : "2a3560"),
                BorderWidthBottom = 2, ContentMarginLeft = 8, ContentMarginRight = 8,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            });
        button.Pressed += () => Requested?.Invoke(kind, value);
        _buttons[(kind, value)] = button; row.AddChild(button);
    }

    public void SetText(IReadOnlyDictionary<string, string> text)
    {
        foreach (var (key, label) in _labels) label.Text = text.GetValueOrDefault(key, key);
        foreach (var button in _buttons.Values)
        {
            var key = button.GetMeta("translation").AsString();
            button.Text = text.GetValueOrDefault(key, key);
        }
    }

    public void SetVolume(double value)
    {
        _volume.SetValueNoSignal(Math.Clamp(value, 0, 100));
        _percent.Text = $"{_volume.Value:0}%";
    }

    public void Sync(string tool, V2NoteType type, EditorTrack track, string effects)
    {
        foreach (var (key, button) in _buttons)
            button.SetPressedNoSignal(key.Kind switch
            {
                "track" => key.Value == track.ToString(),
                "type" => tool == "PLACE" && key.Value == type.ToString(),
                "tool" => key.Value == tool,
                "effects" => key.Value == effects,
                _ => false,
            });
    }

    public void OpenAt(Vector2 point, Vector2 available)
    {
        Size = available;
        _panel.Scale = Vector2.One * Math.Clamp(available.X / 900f, 1f, 1.8f);
        var scale = _panel.Scale.X;
        _panel.Size = new Vector2(Math.Min(420, available.X / scale), Math.Min(540, available.Y / scale));
        _panel.Position = new Vector2(Math.Clamp(point.X, 0, Math.Max(0, available.X - _panel.Size.X * scale)),
            Math.Clamp(point.Y, 0, Math.Max(0, available.Y - _panel.Size.Y * scale)));
        Show();
        _buttons[("track", "Left")].GrabFocus();
    }

    public void Close() { Hide(); GetViewport()?.GuiReleaseFocus(); }

    // Called before stage forwarding. Outside presses are consumed, never authoring clicks.
    public bool FilterInput(InputEvent input, Vector2 localPoint)
    {
        if (!Visible) return false;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape } ||
            input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { Close(); return true; }
        if (input is InputEventMouseButton { Pressed: true } && !MenuRect.HasPoint(localPoint))
        { Close(); return true; }
        if (input is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.Space })
        {
            if (GetViewport().GuiGetFocusOwner() is Button button) button.EmitSignal(BaseButton.SignalName.Pressed);
            return true;
        }
        return input is InputEventKey { Keycode: not Key.Tab };
    }
}

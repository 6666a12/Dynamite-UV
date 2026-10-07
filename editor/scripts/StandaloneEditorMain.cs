using Godot;
using System;

namespace DynamiteUniverse.Editor;

/// <summary>Small Godot shell for the desktop DynaMaker interaction model.</summary>
public partial class StandaloneEditorMain : Control
{
    private AuthoringCanvas _canvas = null!;
    private Label _status = null!;
    private Label _transport = null!;
    private Label _inspector = null!;
    private ContextMenuOverlay _menu = null!;
    private double _time;
    private double? _mark;
    private bool _playing;
    private int _side;
    private int _lastPlayableSide;
    private double _hispeed = 1.0;
    private double _audioRate = 1.0;
    private int _offsetMs;
    private bool _rollReverse = true;
    private bool _restrictMixerHeight = true;
    private bool _showParticles = true;
    private bool _showHitSound;

    private static readonly Color Cyan = new("35e0ff");
    private static readonly Font TechFont = GD.Load<Font>("res://assets/fonts/SpaceGrotesk-Regular.woff2");

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildShell();
        _canvas.StatusChanged += message =>
        {
            _status.Text = message;
            RefreshInspector();
        };
        _canvas.SelectionChanged += _ => RefreshInspector();
        _canvas.ContextMenuRequested += ShowCanvasMenu;
        _canvas.SetSide(0);
        _canvas.SetTool(4);
        RefreshInspector();
        InitializeEditorFlow();
    }

    public override void _Process(double delta)
    {
        if (!_playing) return;
        _time += delta * _hispeed * _audioRate;
        _canvas.PreviewTime = _time;
        UpdateTransport();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton wheel && wheel.Pressed &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown &&
            _canvas is not null && _canvas.GetGlobalRect().HasPoint(wheel.Position))
        {
            if (_menu is null || !_menu.Visible)
            {
                var up = wheel.ButtonIndex == MouseButton.WheelUp;
                var direction = (_rollReverse == up) ? 1d : -1d;
                var step = 100d / (AuthoringCanvas.PixelsPerSecond * Math.Max(.1d, _hispeed));
                _time = Math.Clamp(_time + direction * step, 0d, _canvas.DurationSeconds);
                _canvas.PreviewTime = _time;
                UpdateTransport();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (_menu is null || !_menu.Visible) return;
        if (@event is InputEventMouseMotion motion)
        {
            _menu.HandleGlobalMouse(motion.Position, MouseButton.None, false);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventMouseButton button || !button.Pressed) return;
        if (_menu.ContainsGlobalPoint(button.Position))
        {
            _menu.HandleGlobalMouse(button.Position, button.ButtonIndex, true);
            if (!_menu.Visible) CallDeferred(nameof(ReleaseMenuInput));
            GetViewport().SetInputAsHandled();
        }
        else
        {
            _menu.Hide();
            CallDeferred(nameof(ReleaseMenuInput));
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildShell()
    {
        // ChartCanvas and its SharedGameplayStage are scene-owned. Keeping the
        // stage in the scene makes the editor and gameplay use the same visible
        // hierarchy instead of hiding the migration inside runtime construction.
        _canvas = GetNodeOrNull<AuthoringCanvas>("ChartCanvas")
            ?? throw new InvalidOperationException("editor_main.tscn is missing ChartCanvas");

        AddChild(LabelAt(18, 14, 700, 26, "DynaMaker UV  ·  RIGHT CLICK FOR MENU", 14, Cyan));
        AddChild(LabelAt(1500, 14, 400, 26, "1–4 TOOLS  ·  SPACE PLAY", 12, new Color(.52f, .66f, .76f)));

        /*
        sideBar.AddChild(LabelAt(18, 548, 180, 100, "↑  SIDE\n←/→  SELECT SIDE\nSPACE  PLAY / PAUSE\nM  MARK   R  REPLAY", 12, new Color(.52f, .66f, .76f)));

        */
        _status = LabelAt(18, 46, 1200, 24, "READY  /  EDIT MODE", 12, new Color(.62f, .78f, .86f));
        AddChild(_status);
        _inspector = LabelAt(18, 76, 1200, 24, "No selection", 12, new Color(.58f, .7f, .78f));
        AddChild(_inspector);
        _transport = LabelAt(18, 1048, 900, 24, "00:00.000  /  CENTER  /  1/32", 13, new Color(.7f, .82f, .88f));
        AddChild(_transport);

        _menu = new ContextMenuOverlay();
        _menu.MenuClicked += HandleMenu;
        AddChild(_menu);
        UpdateTransport();
    }

    private void ShowCanvasMenu(Vector2 position)
    {
        _side = _canvas.CurrentSide;
        if (_canvas.HasSelection && _canvas.ContextMenuOnSelection)
        {
            _canvas.InputSuspended = true;
            _menu.Open(position, true, _canvas.SideName, _side, _playing, _restrictMixerHeight, _showParticles, _showHitSound, _mark, _time);
            return;
        }
        _canvas.InputSuspended = true;
        _menu.Open(position, false, _canvas.SideName, _side, _playing, _restrictMixerHeight, _showParticles, _showHitSound, _mark, _time);
    }

    private void HandleMenu(long id)
    {
        switch (id)
        {
            case 10: break;
            case 11: SetPlayableSide(1); break; case 12: SetPlayableSide(0); break; case 13: SetPlayableSide(2); break; case 14: _side = 3; _canvas.SetSide(3); break;
            case 21: if (_side == 3) { _canvas.SetTool(4); _status.Text = "BPM EVENT MODE"; } else _canvas.SetTool(1); break;
            case 22: if (_side == 3) _status.Text = "SAVE FOR DYNAMITE EVENT MODE"; else _canvas.SetTool(2); break;
            case 23: if (_side != 3) _canvas.SetTool(3); break; case 24: _canvas.SetTool(4); break;
            case 31: _playing = !_playing; break; case 32: _mark = _time; break; case 33: _time = _mark ?? 0; _playing = true; break; case 34: _time = 0; _playing = true; break;
            case 40: _canvas.DeleteSelection(); break;
            case 51: _status.Text = "XML export is intentionally v2-only in this clean-room editor."; break;
            case 52: _status.Text = "DY export is intentionally v2-only in this clean-room editor."; break;
            case 53: _status.Text = "Background preview is not loaded in the new blank project."; break;
            case 54: _restrictMixerHeight = !_restrictMixerHeight; _status.Text = $"MIXER LIMITER {(_restrictMixerHeight ? "ON" : "OFF")}"; break;
            case 55: _showParticles = !_showParticles; _status.Text = $"PARTICLES {(_showParticles ? "ON" : "OFF")}"; break;
            case 56: _showHitSound = !_showHitSound; _status.Text = $"HITSOUND {(_showHitSound ? "ON" : "OFF")}"; break;
            case 57: _status.Text = "Music volume is controlled by the desktop audio settings."; break;
        }
        CallDeferred(nameof(ReleaseMenuInput));
        UpdateTransport(); RefreshInspector();
    }

    private void ReleaseMenuInput()
    {
        _canvas.InputSuspended = false;
        _canvas.ClearPointerHover();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || key.Echo) return;
        if (_menu is not null && _menu.Visible)
        {
            if (key.Pressed && key.Keycode == Key.Escape)
            {
                _menu.Hide();
                CallDeferred(nameof(ReleaseMenuInput));
            }
            return;
        }
        if (key.Keycode == Key.Z) { _canvas.TimeSnapOverride = !key.Pressed; return; }
        if (key.Keycode == Key.X) { _canvas.SpaceSnapOverride = !key.Pressed; return; }
        if (!key.Pressed) return;
        if (key.Keycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4)
        {
            if (_side == 3)
            {
                if (key.Keycode == Key.Key1) { _canvas.SetTool(4); _status.Text = "BPM EVENT MODE"; }
                else if (key.Keycode == Key.Key2) _status.Text = "SAVE FOR DYNAMITE EVENT MODE";
                return;
            }
            _canvas.SetTool((int)key.Keycode - (int)Key.Key1 + 1);
            return;
        }
        if (key.Keycode is Key.Delete or Key.Backspace) { _canvas.DeleteSelection(); return; }
        if (key.Keycode == Key.Space) { _playing = !_playing; return; }
        if (key.Keycode == Key.M) { _time = _mark ?? 0; _canvas.PreviewTime = _time; _playing = true; return; }
        if (key.Keycode == Key.R)
        {
            if (key.ShiftPressed)
            {
                _canvas.ResetGridDivisor();
                _hispeed = 1.0;
                _audioRate = 1.0;
            }
            _time = 0;
            _canvas.PreviewTime = _time;
            _playing = true;
            UpdateTransport();
            return;
        }
        if (key.Keycode == Key.Enter) { _time = 0; _canvas.PreviewTime = _time; _canvas.SetTool(4); return; }
        if (key.Keycode == Key.B) { _rollReverse = !_rollReverse; _status.Text = $"SCROLL {(_rollReverse ? "FORWARD" : "REVERSE")}"; return; }
        if (key.Keycode is Key.Left or Key.Right)
        {
            if (key.ShiftPressed)
            {
                if (key.Keycode == Key.Left) _canvas.Undo(); else _canvas.Redo();
                _status.Text = key.Keycode == Key.Left ? "UNDO" : "REDO";
                RefreshInspector();
                return;
            }
            _canvas.CycleTrackVisibility(key.Keycode == Key.Left ? 1 : 2);
            return;
        }
        if (key.Keycode == Key.Up) { SetPlayableSide((_side + 1) % 3); return; }
        if (key.Keycode == Key.Down) { _canvas.CycleTrackVisibility(0); return; }
        if (key.Keycode is Key.A or Key.D) { _time = Math.Max(0, _time + (key.Keycode == Key.A ? -1 : 1) * (key.ShiftPressed ? .01 : 1)); _canvas.PreviewTime = _time; return; }
        if (key.Keycode is Key.O or Key.P) { _offsetMs += key.Keycode == Key.O ? -1 : 1; _status.Text = $"OFFSET {_offsetMs:+#;-#;0}ms"; return; }
        if (key.Keycode is Key.Q or Key.E) { _hispeed = Math.Clamp(_hispeed + (key.Keycode == Key.Q ? -.1 : .1), .1, 8); _status.Text = $"HISPEED {_hispeed:0.0}x"; return; }
        if (key.Keycode is Key.S or Key.W) { _audioRate = Math.Clamp(_audioRate + (key.Keycode == Key.S ? -.01 : .01), .25, 2); _status.Text = $"AUDIO RATE {_audioRate:0.00}x"; return; }
        if (key.Keycode == Key.C) { _canvas.AdjustGridDivisor(false, key.ShiftPressed); UpdateTransport(); return; }
        if (key.Keycode == Key.V) { _canvas.AdjustGridDivisor(true, key.ShiftPressed); UpdateTransport(); return; }
    }

    private void RefreshInspector() => _inspector.Text = _canvas.SelectedSummary;
    private void SetPlayableSide(int side)
    {
        _lastPlayableSide = Math.Clamp(side, 0, 2);
        _side = _lastPlayableSide;
        _canvas.SetSide(_side);
    }
    private void RefreshRail() => UpdateTransport();
    private void UpdateTransport() => _transport.Text = $"{TimeSpan.FromSeconds(_time):mm\\:ss\\.fff}  /  {_canvas.SideName}  /  1/{_canvas.GridDivisor}";

    private static Label LabelAt(float x, float y, float w, float h, string text, int size, Color color)
    {
        var label = new Label
        { Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", TechFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private sealed partial class ContextMenuOverlay : Control
    {
        private const float Width = 400f, Row = 38f;
        private bool _deleteOnly;
        private int _side;
        private string _sideName = "CENTER";
        private bool _playing, _limiter, _particles, _hitsound;
        private double? _mark;
        private double _time;
        private int _hover = -1;
        private static readonly Font MenuFont = GD.Load<Font>("res://assets/fonts/SpaceGrotesk-Bold.woff2");
        public event Action<long>? MenuClicked;

        public ContextMenuOverlay()
        {
            // Input is routed explicitly by the root so the canvas cannot see a
            // menu click as an editor click.
            MouseFilter = MouseFilterEnum.Ignore;
            ZIndex = 20;
            SetProcess(true);
            Hide();
        }

        public void Open(Vector2 pointer, bool deleteOnly, string sideName, int side, bool playing, bool limiter, bool particles, bool hitsound, double? mark, double time)
        {
            _deleteOnly = deleteOnly; _sideName = sideName; _side = side; _playing = playing; _limiter = limiter; _particles = particles; _hitsound = hitsound; _mark = mark; _time = time;
            var size = GetViewportRect().Size;
            var height = deleteOnly ? 50f : 650f;
            Position = new Vector2(Mathf.Clamp(pointer.X, 0, Mathf.Max(0, size.X - Width)), Mathf.Clamp(pointer.Y - (deleteOnly ? 0 : 75), 0, Mathf.Max(0, size.Y - height)));
            Size = new Vector2(Width, height);
            _hover = -1; Show(); QueueRedraw();
        }

        public void HandleGlobalMouse(Vector2 viewportPosition, MouseButton button, bool pressed)
        {
            var local = GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
            if (button == MouseButton.None)
            {
                _hover = RowAt(local);
                QueueRedraw();
                return;
            }
            if (!pressed) return;
            if (button is MouseButton.Right or MouseButton.Middle) { Hide(); return; }
            if (button != MouseButton.Left) return;
            var id = ActionAt(local);
            if (id >= 0) MenuClicked?.Invoke(id);
            Hide();
        }

        public bool ContainsGlobalPoint(Vector2 viewportPosition)
        {
            var local = GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
            return new Rect2(Vector2.Zero, Size).HasPoint(local);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion motion) { _hover = RowAt(motion.Position); QueueRedraw(); return; }
            if (@event is InputEventMouseButton button && button.Pressed)
            {
                if (button.ButtonIndex == MouseButton.Right) { Hide(); AcceptEvent(); return; }
                if (button.ButtonIndex == MouseButton.Left)
                {
                    var id = ActionAt(button.Position);
                    if (id >= 0) MenuClicked?.Invoke(id);
                    Hide(); AcceptEvent();
                }
            }
        }

        private int RowAt(Vector2 p)
        {
            if (_deleteOnly) return p.Y >= 6 && p.Y <= 44 ? 0 : -1;
            if (p.Y < 44) return 0;
            var row = Mathf.FloorToInt((p.Y - 46) / 40f) + 1;
            return row is >= 1 and <= 15 ? row : -1;
        }

        private long ActionAt(Vector2 p)
        {
            if (_deleteOnly) return RowAt(p) == 0 ? 40 : -1;
            if (p.Y >= 6 && p.Y < 44)
            {
                if (p.X < 100) return 11; if (p.X < 200) return 12; if (p.X < 300) return 13; return 14;
            }
            var row = RowAt(p);
            return row switch
            {
                1 => 21, 2 => 22, 3 => _side == 3 ? -1 : 23, 4 => 24, 5 => 31, 6 => 32, 7 => 33, 8 => 34,
                9 => 51, 10 => 52, 11 => 53, 12 => 54, 13 => 55, 14 => 56, 15 => 57, _ => -1
            };
        }

        public override void _Draw()
        {
            DrawStyleBox(new StyleBoxFlat { BgColor = new Color(0.015f, .04f, .07f, .94f), BorderColor = new Color(0.2f, .85f, 1f, .9f), BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 }, new Rect2(Vector2.Zero, Size));
            if (_deleteOnly) { DrawRow(0, "DELETE", new Color(1f, .35f, .4f, .9f)); return; }
            var labels = _side == 3
                ? new[] { "[1]  BPM CHANGE", "[2]  SAVE FOR DYNAMITE", "[3]  NONE" }
                : new[] { "[1]  NORMAL NOTE", "[2]  CHAIN NOTE", "[3]  HOLD NOTE" };
            DrawSideHeader();
            for (var i = 0; i < 3; i++) DrawRow(i + 1, labels[i], _side == 3 && i == 2 ? new Color(.5f, .5f, .5f, .75f) : i == 0 ? new Color(0, 1, 1, .85f) : i == 1 ? new Color(1, .5f, .5f, .85f) : new Color(1, 1, 0, .85f));
            DrawRow(4, "[4]  EDIT MODE", Colors.White);
            DrawRow(5, _playing ? "[_]  PAUSE" : "[_]  PLAY", _playing ? new Color(1, .25f, .25f, .9f) : new Color(.3f, 1f, .4f, .9f));
            DrawRow(6, $"     MARK AT {_time:0.000}", new Color(.5f, .5f, 1f, .9f));
            DrawRow(7, $"[M]  START FROM {_mark ?? 0:0.000}", new Color(.5f, .5f, 1f, .9f));
            DrawRow(8, "[R]  REPLAY", Colors.White);
            DrawRow(9, "     SAVE AS .XML", new Color(0, 1, 1, .85f));
            DrawRow(10, "     SAVE AS .DY", new Color(0, 1, 1, .85f));
            DrawRow(11, "     BACKGROUND", new Color(0, 1, 1, .85f));
            DrawRow(12, $"     MIXER LIMITER {(_limiter ? "ON" : "OFF")}", new Color(0, 1, 1, .85f));
            DrawRow(13, $"     PARTICLES {(_particles ? "ON" : "OFF")}", new Color(0, 1, 1, .85f));
            DrawRow(14, $"     HITSOUND {(_hitsound ? "ON" : "OFF")}", new Color(0, 1, 1, .85f));
            DrawRow(15, "     MUSIC VOLUME", new Color(0, 1, 1, .85f));
        }

        private void DrawSideHeader()
        {
            // The reference does not show four ordinary buttons. It draws one
            // 400px-wide "Edit side" row, shades the active quarter, and uses
            // the arrow glyphs as the affordance for cycling/choosing a side.
            var activeQuarter = _side == 1 ? 0 : _side == 0 ? 1 : _side == 2 ? 2 : 3;
            DrawRect(new Rect2(activeQuarter * 100 + 2, 6, 98, 38), new Color(.35f, .35f, .4f, .85f));
            var hint = _side switch
            {
                1 => "[←]  EDIT  →",
                2 => "←  EDIT  [→]",
                3 => "[#]  EDIT  #",
                _ => "[↑]  EDIT  ↓",
            };
            DrawString(MenuFont, new Vector2(20, 34), hint, HorizontalAlignment.Left, -1, 22, Colors.White);
        }

        private void DrawRow(int row, string text, Color color)
        {
            var y = row == 0 ? 6f : 46f + (row - 1) * 40f;
            if (_hover == row) DrawRect(new Rect2(2, y, Width - 4, Row), new Color(color, .22f));
            DrawString(MenuFont, new Vector2(18, y + 28), text, HorizontalAlignment.Left, -1, 22, color);
        }
    }

}

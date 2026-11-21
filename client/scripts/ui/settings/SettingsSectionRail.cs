using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>The four real setting sections on a short strip. Geometry and captured pointer
/// handling use the same left-pinned 8° lens as the catalogue, without using its purple ink.</summary>
internal sealed partial class SettingsSectionRail : Control
{
    internal static readonly Color Ink = new("edf5fb"), Muted = new("a4b5c5"), Rule = new("2b3b48"),
        Teal = new("65e2e2"), Violet = new("b49bff");
    internal static readonly string[] Sections = ["Gameplay", "Audio", "Display", "Effects"];
    internal static readonly string[] Titles = ["判定与操作", "声音与监听", "画面与动效", "命中与材质"];
    private readonly ScrollContainer _scroll = new();
    private readonly SettingsSectionKey[] _keys = new SettingsSectionKey[4];
    private CatalogueDrum _drum = null!;
    internal Func<bool>? CanChoose { get; set; }
    internal event Action<int>? Chosen;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _scroll.Name = "SettingsSections"; _scroll.Size = Size; _scroll.ClipContents = true;
        _scroll.FollowFocus = true; _scroll.DrawFocusBorder = false;
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        _scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var cells = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cells.AddThemeConstantOverride("separation", 12);
        AddChild(_scroll); _scroll.AddChild(cells);
        for (var i = 0; i < _keys.Length; ++i)
        {
            var n = i;
            var key = _keys[i] = new SettingsSectionKey { Name = "SettingsTab" + Sections[i],
                Section = i, AccessibilityName = Titles[i], CustomMinimumSize = new(0,116),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All,
                ActionMode = BaseButton.ActionModeEnum.Release, MouseDefaultCursorShape = CursorShape.PointingHand };
            key.Pressed += () => Request(n);
            key.GuiInput += e =>
            {
                if (e is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode is not (Key.Up or Key.Down)) return;
                var target = Math.Clamp(n + (k.Keycode == Key.Down ? 1 : -1), 0, _keys.Length-1);
                key.AcceptEvent(); if (CanChoose?.Invoke() != false) { _keys[target].GrabFocus(); Request(target); }
            };
            cells.AddChild(key);
        }
        _drum = CatalogueDrum.Attach(_scroll); _drum.CanInteract = () => CanChoose?.Invoke() != false;
    }
    private void Request(int n) { if (CanChoose?.Invoke() != false) Chosen?.Invoke(n); }
    internal void Mark(int n)
    {
        for (var i = 0; i < _keys.Length; ++i) _keys[i].Mark(i == n);
        // Wait for first VBox layout when re-entering via the colour/calibration screen.
        var key = _keys[n]; Callable.From(() => { if (GodotObject.IsInstanceValid(key) && key.IsInsideTree()) _scroll.EnsureControlVisible(key); }).CallDeferred();
    }
    internal Vector2 Jack(int n)
    {
        var key = _keys[n];
        var local = key.GetGlobalRect().Position - _scroll.GetGlobalRect().Position + new Vector2(key.Size.X-13,key.Size.Y*.5f);
        local.Y = Mathf.Clamp(local.Y, 3, Size.Y-3); return _drum.ProjectPoint(local);
    }
}

internal sealed partial class SettingsSectionKey : BaseButton
{
    internal int Section { get; init; }
    private bool _chosen;
    internal void Mark(bool chosen) { if (_chosen != chosen) { _chosen = chosen; QueueRedraw(); } }
    public override void _Ready()
    {
        MouseEntered += QueueRedraw; MouseExited += QueueRedraw; FocusEntered += QueueRedraw; FocusExited += QueueRedraw;
    }
    public override void _Draw()
    {
        var accent = Section == 3 ? SettingsSectionRail.Violet : SettingsSectionRail.Teal;
        var plate = _chosen ? new Color("1c3341") : new Color("142431");
        if (IsHovered()) plate = plate.Lerp(accent, .065f);
        DrawColoredPolygon(UiGeometry.CutCorners(Size, 9), plate);
        var y = Size.Y*.5f;
        DrawRect(new Rect2(1.5f, y-27, 3, 54), _chosen ? accent : SettingsSectionRail.Rule);
        var font = _chosen ? UiFonts.TechBold : UiFonts.Tech;
        DrawString(font,new(25,MenuStyle.Baseline(font,30,Size.Y)),SettingsSectionRail.Sections[Section].ToUpperInvariant(),
            HorizontalAlignment.Left,Size.X-116,30,_chosen ? SettingsSectionRail.Ink : SettingsSectionRail.Muted);
        var end = Size.X - 14;
        var line = _chosen ? accent : new Color("637588");
        DrawLine(new(end-49,y),new(end,y),line,1.5f,true);
        for (var i = 1; i <= 3; i++) DrawLine(new(end-i*8,y-3),new(end-i*8,y+3),new Color(line,.58f),1.2f,true);
        DrawRect(new Rect2(end-2,y-3,6,6),line);
        if (HasFocus()) DrawLine(new(24,Size.Y-5),new(90,Size.Y-5),accent,2,true);
    }
}

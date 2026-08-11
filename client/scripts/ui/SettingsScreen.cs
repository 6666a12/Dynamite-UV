using Godot;
using DuxCommunity.Game;

namespace DuxCommunity.Ui;

/// <summary>v0.1 设置页：判定偏移、落速和三类音量。</summary>
public partial class SettingsScreen : Node2D
{
    private readonly List<Action> _valueRefreshers = new();

    public override void _Ready()
    {
        GameSession.EnsureInit();
        AddChild(new NeonBackground { Size = new Vector2(1920, 1080) });

        var title = new Label
        {
            Position = new Vector2(180, 80),
            Size = new Vector2(1000, 72),
            Text = "设置 / SETTINGS",
        };
        title.AddThemeFontOverride("font", UiFonts.TechBold);
        title.AddThemeFontSizeOverride("font_size", 52);
        title.AddThemeColorOverride("font_color", UiFonts.Text);
        AddChild(title);

        var backTop = new CutButton
        {
            Position = new Vector2(1640, 76),
            Size = new Vector2(180, 64),
            Text = "返回",
            FontSize = 24,
        };
        backTop.Pressed += Back;
        AddChild(backTop);

        AddChild(new CutPanel
        {
            Position = new Vector2(300, 170),
            Size = new Vector2(1320, 720),
            Fill = new Color(0.035f, 0.05f, 0.11f, 0.88f),
        });

        var s = GameSession.Settings;
        AddSettingRow(220, "判定偏移", "TIMING OFFSET",
            () => $"{s.TimingOffsetMs:+0;-0;0} ms",
            direction => s.TimingOffsetMs = Math.Clamp(
                s.TimingOffsetMs + direction * 5, -300, 300));
        AddSettingRow(340, "音符落速", "FALL SPEED",
            () => $"Lv {s.FallSpeedLevel:D2}  ·  {s.FallSpeedMultiplier:F1}x",
            direction => s.FallSpeedLevel = Math.Clamp(
                s.FallSpeedLevel + direction, 1, 20));
        AddSettingRow(460, "音乐音量", "MUSIC",
            () => $"{s.MusicVolume}%",
            direction => s.MusicVolume = Math.Clamp(
                s.MusicVolume + direction * 5, 0, 100));
        AddSettingRow(580, "打击音量", "HIT SFX",
            () => $"{s.HitVolume}%",
            direction => s.HitVolume = Math.Clamp(
                s.HitVolume + direction * 5, 0, 100));
        AddSettingRow(700, "界面音量", "UI SFX",
            () => $"{s.UiVolume}%",
            direction => s.UiVolume = Math.Clamp(
                s.UiVolume + direction * 5, 0, 100));

        var reset = new CutButton
        {
            Position = new Vector2(630, 930),
            Size = new Vector2(300, 76),
            Text = "恢复默认",
            FontSize = 26,
        };
        reset.Pressed += () =>
        {
            GameSession.Settings.ResetToDefaults();
            Commit();
        };
        AddChild(reset);

        var back = new CutButton
        {
            Position = new Vector2(990, 930),
            Size = new Vector2(300, 76),
            Text = "完成",
            StyleKind = CutButton.ButtonStyle.Solid,
            FontSize = 26,
        };
        back.Pressed += Back;
        AddChild(back);

        RefreshValues();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            Back();
    }

    private void AddSettingRow(float y, string titleText, string tag,
        Func<string> valueText, Action<int> adjust)
    {
        AddChild(new ColorRect
        {
            Position = new Vector2(360, y + 92),
            Size = new Vector2(1200, 1),
            Color = new Color(UiFonts.Line, 0.75f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var title = new Label
        {
            Position = new Vector2(390, y),
            Size = new Vector2(520, 48),
            Text = titleText,
        };
        title.AddThemeFontOverride("font", UiFonts.Cjk);
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", UiFonts.Text);
        AddChild(title);

        var sub = new Label
        {
            Position = new Vector2(390, y + 43),
            Size = new Vector2(520, 28),
            Text = tag,
        };
        sub.AddThemeFontOverride("font", UiFonts.Tech);
        sub.AddThemeFontSizeOverride("font_size", 16);
        sub.AddThemeColorOverride("font_color", UiFonts.Dim);
        AddChild(sub);

        var minus = new CutButton
        {
            Position = new Vector2(1030, y + 8),
            Size = new Vector2(82, 64),
            Text = "-",
            TechFont = true,
            FontSize = 32,
        };
        minus.Pressed += () => { adjust(-1); Commit(); };
        AddChild(minus);

        var value = new Label
        {
            Position = new Vector2(1125, y + 12),
            Size = new Vector2(250, 56),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.AddThemeFontOverride("font", UiFonts.TechBold);
        value.AddThemeFontSizeOverride("font_size", 24);
        value.AddThemeColorOverride("font_color", UiFonts.Cyan);
        AddChild(value);
        _valueRefreshers.Add(() => value.Text = valueText());

        var plus = new CutButton
        {
            Position = new Vector2(1390, y + 8),
            Size = new Vector2(82, 64),
            Text = "+",
            TechFont = true,
            FontSize = 32,
        };
        plus.Pressed += () => { adjust(1); Commit(); };
        AddChild(plus);
    }

    private void Commit()
    {
        GameSession.Settings.Save();
        GameSession.Settings.ApplyAudioBuses();
        RefreshValues();
    }

    private void RefreshValues()
    {
        foreach (var refresh in _valueRefreshers)
            refresh();
    }

    private void Back() => GetTree().ChangeSceneToFile("res://scenes/main.tscn");
}

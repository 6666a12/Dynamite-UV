using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.IO;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

public partial class SongSelect
{

    // ---- QUICK SETTINGS 列几何 ----
    private const float QsX = 40f;
    private const float QsY = 112f;
    private const float QsWidth = 480f;
    private const float QsHeight = 828f;
    private const float QsL = 76f;
    private const float QsR = 484f;
    private const float ModeTop = 300f;
    private const float ModeRowHeight = 72f;
    private const float ModeRowGap = 8f;
    private const float SwitchTop = 540f;
    private const float SwitchPitch = 80f;
    private const float SwitchWidth = 108f;
    private const float SwitchHeight = 50f;
    // 三个开关的 13px 副文案（设计稿 §2.1）。BLEED 在 HARDCORE 下换成锁定说明。
    private const string BleedSubStandard = "可自由开关的游玩修饰";
    private const string BleedSubHardcore = "HARDCORE 下强制开启";
    private const string MirrorSub = "谱面左右镜像";
    private const string AutoSub = "自动演示 · 不写成绩";
    private const float SwitchKnob = QuickSwitch.SwitchKnob;

    // QUICK SETTINGS
    private readonly QuickModeRow[] _modeRows = new QuickModeRow[2];
    private Label _modeDesc = null!;
    private QuickSwitch _bleedSwitch = null!;
    private QuickSwitch _mirrorSwitch = null!;
    private QuickSwitch _autoSwitch = null!;
    private GlyphIcon _bleedIcon = null!;
    private GlyphIcon _mirrorIcon = null!;
    private GlyphIcon _autoIcon = null!;
    private Label _bleedSub = null!;

    // ---- 左列 QUICK SETTINGS ----

    private void BuildQuickSettings()
    {
        _settingsRoot.AddChild(new CutPanel
        {
            Position = new Vector2(QsX, QsY),
            Size = new Vector2(QsWidth, QsHeight),
        });

        var heading = MakeLabel(new Vector2(QsL, 142), new Vector2(300, 28), 14, UiFonts.Dim);
        heading.Text = "QUICK SETTINGS";
        _settingsRoot.AddChild(heading);
        AddLine(_settingsRoot, new Vector2(QsL, 176), new Vector2(QsR - QsL, 1.5f));

        var modeLabel = MakeLabel(new Vector2(QsL, 218), new Vector2(200, 32), 26, UiFonts.Text);
        modeLabel.Text = "MODE";
        _settingsRoot.AddChild(modeLabel);
        _modeDesc = MakeLabel(new Vector2(QsL, 252), new Vector2(408, 26), 15, UiFonts.Dim);
        _settingsRoot.AddChild(_modeDesc);

        for (var i = 0; i < 2; i++)
        {
            var index = i;
            var row = new QuickModeRow
            {
                MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
                Position = new Vector2(QsL, ModeTop + i * (ModeRowHeight + ModeRowGap)),
                Size = new Vector2(QsR - QsL, ModeRowHeight),
                Label = index == 0 ? "STANDARD" : "HARDCORE",
                Glyph = index == 0 ? UiGlyphKind.Standard : UiGlyphKind.Hardcore,
            };
            row.Chosen += () => SetGameplayMode(index == 0
                ? GameplayMode.Standard : GameplayMode.Hardcore);
            _settingsRoot.AddChild(row);
            _modeRows[i] = row;
        }

        AddLine(_settingsRoot, new Vector2(QsL, 480), new Vector2(QsR - QsL, 1.5f),
            new Color(UiFonts.Line, 0.6f));

        _bleedSwitch = AddQuickSwitch(0, "BLEED", BleedSubStandard, UiGlyphKind.Bleed,
            out _bleedIcon, out _bleedSub);
        _mirrorSwitch = AddQuickSwitch(1, "MIRROR", MirrorSub, UiGlyphKind.Mirror,
            out _mirrorIcon, out _);
        _autoSwitch = AddQuickSwitch(2, "AUTO", AutoSub, UiGlyphKind.Auto,
            out _autoIcon, out _);
        _bleedSwitch.Toggled += value =>
        {
            GameSession.Settings.BleedEnabled = value;
            PersistSettings();
            RefreshQuickSettings();
        };
        _mirrorSwitch.Toggled += value =>
        {
            GameSession.Settings.MirrorEnabled = value;
            PersistSettings();
            RefreshQuickSettings();
        };
        _autoSwitch.Toggled += value =>
        {
            GameSession.Settings.AutoEnabled = value;
            PersistSettings();
            RefreshQuickSettings();
        };

        AddLine(_settingsRoot, new Vector2(QsL, 772), new Vector2(QsR - QsL, 1.5f),
            new Color(UiFonts.Line, 0.6f));

        var all = new QuickActionRow
        {
            MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
            Position = new Vector2(QsL, 824),
            Size = new Vector2(QsR - QsL, 80),
            Glyph = UiGlyphKind.Sliders,
            Text = "全部设置",
            SubText = "ALL SETTINGS",
        };
        all.Chosen += () =>
        {
            if (_localTransitionBusy || TransitionDirector.IsBusy)
                return;
            StopPreview();
            TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Standard,
                "OPEN CHANNEL", "SETTINGS");
        };
        _settingsRoot.AddChild(all);
    }

    private QuickSwitch AddQuickSwitch(int index, string label, string subText, UiGlyphKind glyph,
        out GlyphIcon icon, out Label sub)
    {
        var cy = SwitchTop + index * SwitchPitch;
        icon = new GlyphIcon
        {
            Position = new Vector2(QsL + 27, cy - 10),
            Size = new Vector2(20, 20),
            Glyph = glyph,
            Color = UiFonts.Text,
        };
        _settingsRoot.AddChild(icon);

        var title = MakeLabel(new Vector2(QsL + 64, cy - 25), new Vector2(260, 28), 20, UiFonts.Text);
        title.Text = label;
        _settingsRoot.AddChild(title);
        sub = MakeLabel(new Vector2(QsL + 64, cy - 2), new Vector2(300, 22), 13, UiFonts.Dim);
        // 副文案必须在这里落字：三个开关各有各的说明，只在 RefreshQuickSettings 里改 BLEED
        // 会让 MIRROR / AUTO 的 13px 小字永远空着。
        sub.Text = subText;
        _settingsRoot.AddChild(sub);

        var toggle = new QuickSwitch
        {
            MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
            Position = new Vector2(QsR - SwitchWidth, cy - SwitchHeight * 0.5f),
            Size = new Vector2(SwitchWidth, SwitchHeight),
        };
        _settingsRoot.AddChild(toggle);
        return toggle;
    }

    private void SetGameplayMode(GameplayMode mode)
    {
        if (GameSession.Settings.GameplayMode == mode)
            return;
        GameSession.Settings.GameplayMode = mode;
        // 锁定期（HARDCORE）不改写 BleedEnabled，切回 STANDARD 时恢复用户此前的选择。
        PersistSettings();
        RefreshQuickSettings();
    }

    /// <summary>把设置值刷到控件上；HARDCORE 下 BLEED 强制开并锁定。</summary>
    private void RefreshQuickSettings()
    {
        var settings = GameSession.Settings;
        var hardcore = settings.GameplayMode == GameplayMode.Hardcore;
        _modeRows[0].Selected = !hardcore;
        _modeRows[1].Selected = hardcore;
        _modeDesc.Text = hardcore
            ? SettingsScreen.GameplayModeTag(GameplayMode.Hardcore)
            : SettingsScreen.GameplayModeTag(GameplayMode.Standard);
        _modeDesc.Modulate = Colors.White;

        _bleedSwitch.SetState(hardcore || settings.BleedEnabled, locked: hardcore);
        _bleedSub.Text = hardcore ? BleedSubHardcore : BleedSubStandard;
        _mirrorSwitch.SetState(settings.MirrorEnabled, locked: false);
        _autoSwitch.SetState(settings.AutoEnabled, locked: false);

        _bleedIcon.Color = _bleedSwitch.IsOn ? UiFonts.Cyan : UiFonts.Text;
        _mirrorIcon.Color = _mirrorSwitch.IsOn ? UiFonts.Cyan : UiFonts.Text;
        _autoIcon.Color = _autoSwitch.IsOn ? UiFonts.Cyan : UiFonts.Text;
        _bleedIcon.QueueRedraw();
        _mirrorIcon.QueueRedraw();
        _autoIcon.QueueRedraw();
    }

    private static void PersistSettings() => GameSession.Settings.Save();
}

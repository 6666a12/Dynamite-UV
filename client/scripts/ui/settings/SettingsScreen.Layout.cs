using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Ui;

public partial class SettingsScreen
{
    // ---- 设计稿几何（penpot_build5.py 的常量） ----
    private const float BarHeight = 120f;
    private const float TabX = 48f;
    private const float TabWidth = 340f;
    private const float TabHeight = 96f;
    private const float TabPitch = 108f;
    private const float CardX = 428f;
    private const float CardY = 176f;
    private const float CardWidth = 1444f;
    private const float CardHeight = 832f;
    private const float ColumnLeft = 476f;
    private const float ColumnRight = 1824f;
    private const float RowHeight = 156f;
    private const float TouchButton = 96f;
    private const float StepperValueWidth = 220f;
    private const float SegmentWidth = SettingsSegments.SegmentWidth;
    private const float SegmentHeight = SettingsSegments.SegmentHeight;
    private const float SegmentGap = SettingsSegments.SegmentGap;
    private const float ToggleWidth = 132f;
    private const float ToggleHeight = 60f;
    private const float ToggleKnob = SettingsToggle.ToggleKnob;
    private const float TrackX = 1048f;
    private const float TrackWidth = SettingsSlider.TrackWidth;
    private const float TrackHandle = SettingsSlider.TrackHandle;
    private const float FooterLineY = 876f;
    private const float FooterTextY = 942f;

    // ---- 顶部栏 ----

    private void BuildTopBar()
    {
        var back = new CutButton
        {
            LowBorder = true,
            Position = new Vector2(64f, 50f),
            Size = new Vector2(72f, 72f),
            Cut = 12f,
            Text = "‹",
            FontSize = 36,
            Accent = UiFonts.Cyan,
        };
        back.Pressed += Back;
        AddChild(back);
        _enterItems.Add(back);

        var title = new Label
        {
            Position = new Vector2(176f, 58f),
            Size = new Vector2(700f, 56f),
            Text = "SETTINGS",
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        MenuStyle.Heading(title);
        AddChild(title);
        _enterItems.Add(title);

        var version = new Label
        {
            Position = new Vector2(ColumnRight - 400f, 46f),
            Size = new Vector2(400f, 28f),
            Text = ClientVersionLabel(),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiLabels.Tech(version, 13, MenuStyle.Secondary);
        AddChild(version);
        _enterItems.Add(version);
    }

    // 版本号与 client/export_presets.cfg 的 version/name、release/apk-policy.json 的
    // version_name 同源，沿用 Main.cs 的构建特性分流（不硬编 XAPK 的 0.18.00）。
    private static string ClientVersionLabel()
    {
        if (OS.HasFeature("internal_testdata"))
            return "v0.1.2-internal-testdata";
        if (OS.HasFeature("editor") && !OS.HasFeature("public_release"))
            return "v0.1.2-dev";
        return "v0.1.2";
    }

    // ---- 左侧 tab 栏 ----

    private void BuildTabs()
    {
        for (var i = 0; i < TabNames.Length; i++)
        {
            var tab = (SettingsTab)i;
            var item = new SettingsTabItem
            {
                Position = new Vector2(TabX, CardY + i * TabPitch),
                Size = new Vector2(TabWidth, TabHeight),
                Text = TabNames[i],
            };
            item.Chosen += () => SelectTab(tab, animate: true);
            AddChild(item);
            _tabItems[i] = item;
            _enterItems.Add(item);
        }
    }

    // ---- 内容卡 ----

    private void BuildContent()
    {
        var card = new CutPanel
        {
            Position = new Vector2(CardX, CardY),
            Size = new Vector2(CardWidth, CardHeight),
            Cut = 20f,
            Fill = Colors.Transparent,
            Border = UiFonts.Line,
            BorderWidth = 0f,
        };
        AddChild(card);
        _enterItems.Add(card);

        for (var i = 0; i < _tabRoots.Length; i++)
        {
            var root = new Control
            {
                Position = Vector2.Zero,
                Size = UiLayout.DesignSize,
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(root);
            _tabRoots[i] = root;
        }

        BuildGameplayTab(_tabRoots[(int)SettingsTab.Gameplay]);
        BuildAudioTab(_tabRoots[(int)SettingsTab.Audio]);
        BuildDisplayTab(_tabRoots[(int)SettingsTab.Display]);
        BuildEffectsTab(_tabRoots[(int)SettingsTab.Effects]);
    }

    private void BuildGameplayTab(Control root)
    {
        var s = GameSession.Settings;
        AddGroupHeader(root, "GAMEPLAY", 220f);

        // 四行改用音频板的 156 行距（设计稿只为三行开了更宽的 pitch）。
        var offsetRow = CreateRow(root, 344f);
        AddRowLabel(offsetRow, "JUDGE OFFSET", "-300 … +300 ms");
        AddStepper(offsetRow,
            () => $"{s.TimingOffsetMs:+0;-0;0} ms",
            direction => s.TimingOffsetMs = Math.Clamp(
                s.TimingOffsetMs + direction * TimingStepMs, -300, 300));
        AddCalibrationEntry(offsetRow);

        var speedRow = CreateRow(root, 500f);
        var speedSub = AddRowLabel(speedRow, "DROP SPEED", $"{s.FallSpeedMultiplier:F1}x");
        if (speedSub != null)
            _valueBindings.Add(new ValueBinding(speedSub,
                () => $"{s.FallSpeedMultiplier:F1}x", speedSub.Position, speedSub.Text));
        AddStepper(speedRow,
            () => $"Lv {s.FallSpeedLevel:D2}",
            direction => s.FallSpeedLevel = Math.Clamp(s.FallSpeedLevel + direction, 1, 20));

        var judgeTextRow = CreateRow(root, 656f);
        AddRowLabel(judgeTextRow, "JUDGE TEXT", "PERFECT / GREAT 浮字");
        AddToggle(judgeTextRow,
            () => s.JudgeTextEnabled,
            () => s.JudgeTextEnabled = !s.JudgeTextEnabled);

        var modeRow = CreateRow(root, 812f);
        var modeSub = AddRowLabel(modeRow, "MODE", GameplayModeTag(s.GameplayMode));
        if (modeSub != null)
            _valueBindings.Add(new ValueBinding(modeSub,
                () => GameplayModeTag(s.GameplayMode), modeSub.Position, modeSub.Text));
        AddSegments(modeRow, ModeNames,
            () => (int)s.GameplayMode,
            index => s.GameplayMode = index == 1
                ? GameplayMode.Hardcore : GameplayMode.Standard);

        AddFooter(root);
    }

    private void BuildAudioTab(Control root)
    {
        var s = GameSession.Settings;
        AddGroupHeader(root, "AUDIO", 220f);

        AddVolumeRow(root, 344f, "MUSIC VOLUME", UiFonts.Pink,
            () => s.MusicVolume, value => s.MusicVolume = value);
        AddVolumeRow(root, 344f + RowHeight, "HITSOUND VOLUME", UiFonts.Cyan,
            () => s.HitVolume, value => s.HitVolume = value);
        AddVolumeRow(root, 344f + 2f * RowHeight, "UI VOLUME", UiFonts.Cyan,
            () => s.UiVolume, value => s.UiVolume = value);

        AddFooter(root);
    }

    private void BuildDisplayTab(Control root)
    {
        var s = GameSession.Settings;
        AddGroupHeader(root, "DISPLAY", 220f);

        // 本 tab 只有一个设置项：行位沿用其余两个 tab 的首行节奏，避免卡片中段空成一块。
        var motionRow = CreateRow(root, 344f);
        AddRowLabel(motionRow, "UI MOTION", null);
        AddSegments(motionRow, MotionNames,
            () => (int)s.MotionMode,
            index => s.MotionMode = index switch
            {
                1 => UiMotionMode.Reduced,
                2 => UiMotionMode.Off,
                _ => UiMotionMode.Full,
            });

        AddFooter(root);
    }

    private void BuildEffectsTab(Control root)
    {
        var s = GameSession.Settings;
        AddGroupHeader(root, "EFFECTS", 220f);

        var levelRow = CreateRow(root, 344f);
        AddRowLabel(levelRow, "HIT EFFECTS", "REDUCED 减粒子 / OFF 只留残影");
        AddSegments(levelRow, EffectLevelNames,
            () => (int)s.HitEffectLevel,
            index => s.HitEffectLevel = (HitEffectLevel)index);

        var brightnessRow = CreateRow(root, 500f);
        AddRowLabel(brightnessRow, "NOTE BRIGHTNESS", "100% = 设计原值");
        AddSlider(brightnessRow, UiFonts.Cyan, 80, 140,
            () => s.NoteBrightnessPercent, value => s.NoteBrightnessPercent = value);

        var flashRow = CreateRow(root, 656f);
        AddRowLabel(flashRow, "FLASH INTENSITY", "100% = 设计原值");
        AddSlider(flashRow, UiFonts.Cyan, 60, 150,
            () => s.FlashIntensityPercent, value => s.FlashIntensityPercent = value);

        var residualRow = CreateRow(root, 812f);
        AddRowLabel(residualRow, "HIT RESIDUAL", "判定线上的命中光斑");
        AddSegments(residualRow, ResidualNames,
            () => ResidualIndex(s.HitResidualMs),
            index => s.HitResidualMs = ResidualValuesMs[index]);

        AddFooter(root);
        AddColorsEntry(root);
    }

    /// <summary>
    /// EFFECTS 页脚的「音符配色 ▶」：与 RESET DEFAULTS 同一行、靠右，和校准入口一样
    /// 是独立页面而不是第五个 tab。见 Penpot 板 Game — Settings (Effects)。
    /// </summary>
    private void AddColorsEntry(Control root)
    {
        var entry = new CutButton
        {
            LowBorder = true,
            Position = new Vector2(ColumnRight - 240f, FooterLineY + 46f),
            Size = new Vector2(240f, 48f),
            Cut = 12f,
            Text = "音符配色 ▶",
            FontSize = 20,
            Accent = UiFonts.Cyan,
        };
        entry.Pressed += () =>
        {
            if (_localMotionBusy || TransitionDirector.IsBusy)
                return;
            GameSession.Settings.Save();
            TransitionDirector.Navigate(UiRoutes.NoteColors, TransitionKind.Standard,
                "OPEN CHANNEL", "NOTE COLORS");
        };
        root.AddChild(entry);
        _enterItems.Add(entry);
    }

    private static int ResidualIndex(int ms)
    {
        var index = Array.IndexOf(ResidualValuesMs, ms);
        return index >= 0 ? index : 2;
    }

    // 只列已实现的两种模式；Bleed 预留，见 docs/gameplay-spec.md「游玩模式」。
    internal static string GameplayModeTag(GameplayMode mode) => mode switch
    {
        GameplayMode.Hardcore => "HARD · 硬核判定（窗口 ×0.5）",
        _ => "Normal 判定窗口",
    };

    private void AddVolumeRow(Control root, float centerY, string title, Color accent,
        Func<int> read, Action<int> write)
    {
        var row = CreateRow(root, centerY);
        AddRowLabel(row, title, null);
        AddSlider(row, accent, 0, 100, read, write);
    }

    private void AddFooter(Control root)
    {
        var rule = new ColorRect
        {
            Position = new Vector2(ColumnLeft, FooterLineY),
            Size = new Vector2(ColumnRight - ColumnLeft, 1.5f),
            Color = new Color(UiFonts.Line, 0.6f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(rule);

        var reset = new FlatTextButton
        {
            Position = new Vector2(ColumnLeft, 914f),
            Size = new Vector2(300f, 64f),
            Text = "RESET DEFAULTS",
        };
        reset.Pressed += () =>
        {
            GameSession.Settings.ResetToDefaults();
            Commit();
        };
        root.AddChild(reset);
        _enterItems.Add(reset);
    }

    // ---- 通用行构件 ----

    private void AddGroupHeader(Control parent, string text, float centerY)
    {
        var item = new Control
        {
            Position = new Vector2(ColumnLeft, centerY - 14f),
            Size = new Vector2(ColumnRight - ColumnLeft, 68f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(item);
        AddLabel(item, text, 14, MenuStyle.Secondary, false, 0f, 14f, 600f, 28f);
        item.AddChild(new ColorRect
        {
            Position = new Vector2(0f, 34f),
            Size = new Vector2(ColumnRight - ColumnLeft, 1.5f),
            Color = UiFonts.Line,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _enterItems.Add(item);
    }

    private Control CreateRow(Control parent, float centerY)
    {
        var row = new Control
        {
            Position = new Vector2(ColumnLeft, centerY - RowHeight * 0.5f),
            Size = new Vector2(ColumnRight - ColumnLeft, RowHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(row);
        _enterItems.Add(row);
        return row;
    }

    private static Label? AddRowLabel(Control row, string title, string? subText)
    {
        AddLabel(row, title, 26, MenuStyle.Text, false, 0f, RowHeight * 0.5f - 15f, 700f, 36f);
        if (subText == null)
            return null;
        return AddLabel(row, subText, 15, MenuStyle.Secondary, false, 0f, RowHeight * 0.5f + 17f, 700f, 26f);
    }

    private static Label AddLabel(Control parent, string text, int fontSize, Color color, bool bold,
        float x, float centerY, float width, float height,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Position = new Vector2(x, centerY - height * 0.5f),
            Size = new Vector2(width, height),
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (bold)
            UiLabels.TechBold(label, fontSize, color);
        else
            UiLabels.Tech(label, fontSize, color);
        parent.AddChild(label);
        return label;
    }

    private void AddStepper(Control row, Func<string> valueText, Action<int> adjust)
    {
        var leftX = ColumnRight - ColumnLeft - (TouchButton * 2f + StepperValueWidth + 48f);
        var rightX = ColumnRight - ColumnLeft - TouchButton;

        var minus = new CutButton
        {
            LowBorder = true,
            Position = new Vector2(leftX, 30f),
            Size = new Vector2(TouchButton, TouchButton),
            Cut = 18f,
            Text = "‹",
            FontSize = 40,
            Accent = UiFonts.Cyan,
        };
        minus.Pressed += () =>
        {
            adjust(-1);
            Commit();
        };
        row.AddChild(minus);

        var plus = new CutButton
        {
            LowBorder = true,
            Position = new Vector2(rightX, 30f),
            Size = new Vector2(TouchButton, TouchButton),
            Cut = 18f,
            Text = "›",
            FontSize = 40,
            Accent = UiFonts.Cyan,
        };
        plus.Pressed += () =>
        {
            adjust(1);
            Commit();
        };
        row.AddChild(plus);

        var value = AddLabel(row, valueText(), 34, UiFonts.Cyan, true,
            leftX + TouchButton + 24f, RowHeight * 0.5f, StepperValueWidth, 64f,
            HorizontalAlignment.Center);
        _valueBindings.Add(new ValueBinding(value, valueText, value.Position, value.Text));
    }

    /// <summary>
    /// JUDGE OFFSET 行右侧的「校准 ▶」入口：移到说明文字下面一行（左对齐），
    /// 与同行的步进器错开，不与 ‹ › 抢 96px 触控键的位置。样式对齐该行既有控件。
    /// </summary>
    private void AddCalibrationEntry(Control row)
    {
        var entry = new CutButton
        {
            LowBorder = true,
            Position = new Vector2(0f, RowHeight - 44f),
            Size = new Vector2(240f, 44f),
            Cut = 12f,
            Text = "校准 ▶",
            FontSize = 20,
            Accent = UiFonts.Cyan,
        };
        entry.Pressed += () =>
        {
            if (_localMotionBusy || TransitionDirector.IsBusy)
                return;
            GameSession.Settings.Save();
            TransitionDirector.Navigate(UiRoutes.Calibration, TransitionKind.Standard,
                "OPEN CHANNEL", "CALIBRATION");
        };
        row.AddChild(entry);
        _enterItems.Add(entry);
    }

    private void AddToggle(Control row, Func<bool> read, Action toggle)
    {
        var control = new SettingsToggle
        {
            MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
            Position = new Vector2(ColumnRight - ColumnLeft - ToggleWidth, 48f),
            Size = new Vector2(ToggleWidth, ToggleHeight),
        };
        control.SetOn(read(), animate: false);
        control.Pressed += () =>
        {
            toggle();
            Commit();
        };
        row.AddChild(control);
        _stateRefreshers.Add(() => control.SetOn(read(), animate: true));
    }

    private void AddSlider(Control row, Color accent, int min, int max,
        Func<int> read, Action<int> write)
    {
        var percent = AddLabel(row, $"{read()}%", 36, accent, true,
            ColumnRight - ColumnLeft - 320f, RowHeight * 0.5f, 320f, 64f,
            HorizontalAlignment.Right);

        var control = new SettingsSlider
        {
            Position = new Vector2(TrackX - ColumnLeft - TrackHandle * 0.5f, 0f),
            Size = new Vector2(TrackWidth + TrackHandle, RowHeight),
            Accent = accent,
            Min = min,
            Max = max,
        };
        control.SetValue(read());
        control.Changed += value =>
        {
            percent.Text = $"{value}%";
            write(value);
            Commit();
        };
        row.AddChild(control);
        _stateRefreshers.Add(() =>
        {
            var value = read();
            control.SetValue(value);
            percent.Text = $"{value}%";
        });
    }

    private void AddSegments(Control row, string[] labels, Func<int> read, Action<int> write)
    {
        var total = labels.Length * SegmentWidth + (labels.Length - 1) * SegmentGap;
        var control = new SettingsSegments
        {
            Position = new Vector2(ColumnRight - ColumnLeft - total,
                RowHeight * 0.5f - SegmentHeight * 0.5f),
            Size = new Vector2(total, SegmentHeight),
        };
        control.Configure(labels, read());
        control.Chosen += index =>
        {
            write(index);
            Commit();
        };
        row.AddChild(control);
        _stateRefreshers.Add(() => control.SetSelected(read()));
    }
}

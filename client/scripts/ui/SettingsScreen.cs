using Godot;
using DuxCommunity.Game;

namespace DuxCommunity.Ui;

/// <summary>v0.1 设置页：判定偏移、落速、三类音量和 UI 动效。</summary>
public partial class SettingsScreen : Node2D
{
    private sealed record SettingValueBinding(
        Label Label,
        Func<string> GetText,
        Vector2 BasePosition,
        string CurrentText,
        Tween? Tween = null);

    private readonly List<SettingValueBinding> _valueBindings = new();
    private readonly List<Control> _enterItems = new();
    private readonly Dictionary<Control, Vector2> _enterBasePositions = new();
    private bool _localMotionBusy;

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
        _enterItems.Add(title);

        var backTop = new CutButton
        {
            Position = new Vector2(1640, 76),
            Size = new Vector2(180, 64),
            Text = "返回",
            FontSize = 24,
        };
        backTop.Pressed += Back;
        AddChild(backTop);

        var settingsPanel = new CutPanel
        {
            Position = new Vector2(300, 170),
            Size = new Vector2(1320, 720),
            Fill = new Color(0.035f, 0.05f, 0.11f, 0.88f),
        };
        AddChild(settingsPanel);
        _enterItems.Add(settingsPanel);

        var s = GameSession.Settings;
        AddSettingRow(210, "判定偏移", "TIMING OFFSET",
            () => $"{s.TimingOffsetMs:+0;-0;0} ms",
            direction => s.TimingOffsetMs = Math.Clamp(
                s.TimingOffsetMs + direction * 5, -300, 300));
        AddSettingRow(320, "音符落速", "FALL SPEED",
            () => $"Lv {s.FallSpeedLevel:D2}  ·  {s.FallSpeedMultiplier:F1}x",
            direction => s.FallSpeedLevel = Math.Clamp(
                s.FallSpeedLevel + direction, 1, 20));
        AddSettingRow(430, "音乐音量", "MUSIC",
            () => $"{s.MusicVolume}%",
            direction => s.MusicVolume = Math.Clamp(
                s.MusicVolume + direction * 5, 0, 100));
        AddSettingRow(540, "打击音量", "HIT SFX",
            () => $"{s.HitVolume}%",
            direction => s.HitVolume = Math.Clamp(
                s.HitVolume + direction * 5, 0, 100));
        AddSettingRow(650, "界面音量", "UI SFX",
            () => $"{s.UiVolume}%",
            direction => s.UiVolume = Math.Clamp(
                s.UiVolume + direction * 5, 0, 100));
        AddSettingRow(760, "UI 动效", "UI MOTION",
            () => MotionModeText(s.MotionMode),
            direction => s.MotionMode = CycleMotionMode(s.MotionMode, direction));

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

        RefreshValues(animateChanges: false);
        PrepareEnterAnimation();
        TransitionDirector.ReportSceneReady(PlayEnterAnimation);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_localMotionBusy && !TransitionDirector.IsBusy &&
            e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            Back();
    }

    private void AddSettingRow(float y, string titleText, string tag,
        Func<string> valueText, Action<int> adjust)
    {
        var rowRoot = new Control
        {
            Position = new Vector2(0f, y),
            Size = new Vector2(UiLayout.DesignWidth, 94f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(rowRoot);
        _enterItems.Add(rowRoot);

        rowRoot.AddChild(new ColorRect
        {
            Position = new Vector2(360, 92),
            Size = new Vector2(1200, 1),
            Color = new Color(UiFonts.Line, 0.75f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var title = new Label
        {
            Position = new Vector2(390, 0),
            Size = new Vector2(520, 48),
            Text = titleText,
        };
        title.AddThemeFontOverride("font", UiFonts.Cjk);
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", UiFonts.Text);
        rowRoot.AddChild(title);

        var sub = new Label
        {
            Position = new Vector2(390, 43),
            Size = new Vector2(520, 28),
            Text = tag,
        };
        sub.AddThemeFontOverride("font", UiFonts.Tech);
        sub.AddThemeFontSizeOverride("font_size", 16);
        sub.AddThemeColorOverride("font_color", UiFonts.Dim);
        rowRoot.AddChild(sub);

        var minus = new CutButton
        {
            Position = new Vector2(1030, 8),
            Size = new Vector2(82, 64),
            Text = "-",
            TechFont = true,
            FontSize = 32,
        };
        minus.Pressed += () => { adjust(-1); Commit(); };
        rowRoot.AddChild(minus);

        var value = new Label
        {
            Position = new Vector2(1125, 12),
            Size = new Vector2(250, 56),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.AddThemeFontOverride("font", UiFonts.TechBold);
        value.AddThemeFontSizeOverride("font_size", 24);
        value.AddThemeColorOverride("font_color", UiFonts.Cyan);
        rowRoot.AddChild(value);
        _valueBindings.Add(new SettingValueBinding(value, valueText, value.Position, string.Empty));

        var plus = new CutButton
        {
            Position = new Vector2(1390, 8),
            Size = new Vector2(82, 64),
            Text = "+",
            TechFont = true,
            FontSize = 32,
        };
        plus.Pressed += () => { adjust(1); Commit(); };
        rowRoot.AddChild(plus);
    }

    private static UiMotionMode CycleMotionMode(UiMotionMode mode, int direction)
    {
        var current = mode switch
        {
            UiMotionMode.Reduced => 1,
            UiMotionMode.Off => 2,
            _ => 0,
        };
        var next = (current + (direction < 0 ? 2 : 1)) % 3;
        return next switch
        {
            1 => UiMotionMode.Reduced,
            2 => UiMotionMode.Off,
            _ => UiMotionMode.Full,
        };
    }

    private static string MotionModeText(UiMotionMode mode) => mode switch
    {
        UiMotionMode.Reduced => "REDUCED / 减弱",
        UiMotionMode.Off => "OFF / 关闭",
        _ => "FULL / 完整",
    };

    private void Commit()
    {
        GameSession.Settings.Save();
        GameSession.Settings.ApplyAudioBuses();
        RefreshValues(animateChanges: true);
    }

    private void RefreshValues(bool animateChanges)
    {
        var motion = UiMotionProfile.For(GameSession.Settings.MotionMode);
        for (var i = 0; i < _valueBindings.Count; i++)
        {
            var binding = _valueBindings[i];
            binding.Tween?.Kill();
            binding.Label.Position = binding.BasePosition;
            binding.Label.Modulate = Colors.White;
            var next = binding.GetText();
            if (StringComparer.Ordinal.Equals(binding.CurrentText, next))
            {
                _valueBindings[i] = binding with { Tween = null };
                continue;
            }

            binding.Label.Text = next;
            if (!animateChanges || !motion.IsAnimated)
            {
                _valueBindings[i] = binding with { CurrentText = next, Tween = null };
                continue;
            }

            binding.Label.Position = binding.BasePosition + new Vector2(0f, motion.ValueShift);
            binding.Label.Modulate = new Color(1f, 1f, 1f, 0f);
            var tween = CreateTween().SetParallel(true);
            _valueBindings[i] = binding with { CurrentText = next, Tween = tween };
            tween.TweenMethod(Callable.From<float>(progress =>
                {
                    var eased = UiEase.Standard(progress);
                    binding.Label.Position = (binding.BasePosition +
                            new Vector2(0f, motion.ValueShift)).Lerp(binding.BasePosition, eased);
                    binding.Label.Modulate = new Color(1f, 1f, 1f, eased);
                }), 0f, 1f, motion.ValueDuration)
                .SetTrans(Tween.TransitionType.Linear);
        }
    }

    private void PrepareEnterAnimation()
    {
        var motion = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!motion.IsAnimated)
            return;
        foreach (var item in _enterItems)
        {
            _enterBasePositions[item] = item.Position;
            item.Modulate = new Color(1f, 1f, 1f, 0f);
            if (motion.AllowDirectionalMotion)
                item.Position -= new Vector2(motion.ContentShift, 0f);
        }
    }

    private void PlayEnterAnimation()
    {
        var motion = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!motion.IsAnimated)
        {
            _localMotionBusy = false;
            return;
        }
        _localMotionBusy = true;
        var tween = CreateTween().SetParallel(true);
        for (var i = 0; i < _enterItems.Count; i++)
        {
            var item = _enterItems[i];
            var basePosition = _enterBasePositions.GetValueOrDefault(item, item.Position);
            var startPosition = item.Position;
            var delay = motion.AllowStagger ? i * motion.Stagger : 0.0;
            tween.TweenMethod(Callable.From<float>(progress =>
                {
                    var eased = UiEase.Enter(progress);
                    item.Position = startPosition.Lerp(basePosition, eased);
                    item.Modulate = new Color(1f, 1f, 1f, eased);
                }), 0f, 1f, motion.PanelDuration)
                .SetDelay(delay)
                .SetTrans(Tween.TransitionType.Linear);
        }
        tween.Chain().TweenCallback(Callable.From(() => _localMotionBusy = false));
    }

    private void Back()
    {
        if (!_localMotionBusy)
            TransitionDirector.Navigate(UiRoutes.Main, TransitionKind.Back);
    }
}

using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 设置页 —— 按 Page 2「Game UI」设计稿实现的移动端横屏布局：顶部大返回键 + SETTINGS 标题
/// + 右上版本号，左侧竖排大 tab（GAMEPLAY / AUDIO / DISPLAY），右侧切角内容卡。
/// 几何常量逐条取自 .tmp/penpot_build5.py（1920×1080 设计坐标）；
/// 数值仍来自 GameSettings，保存与音频总线应用路径不变。
/// </summary>
public partial class SettingsScreen : Node2D
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
    private const float SegmentWidth = 168f;
    private const float SegmentHeight = 72f;
    private const float SegmentGap = 12f;
    private const float ToggleWidth = 132f;
    private const float ToggleHeight = 60f;
    private const float ToggleKnob = 48f;
    private const float TrackX = 1048f;
    private const float TrackWidth = 620f;
    private const float TrackHandle = 44f;
    private const float FooterLineY = 876f;
    private const float FooterTextY = 942f;
    private const int TimingStepMs = 5;
    private const int VolumeStepPercent = 5;

    private static readonly string[] TabNames = ["GAMEPLAY", "AUDIO", "DISPLAY"];
    private static readonly string[] MotionNames = ["FULL", "REDUCED", "OFF"];
    // Bleed 为预留模式，暂不出现在设置页（见 docs/gameplay-spec.md「游玩模式」）。
    private static readonly string[] ModeNames = ["STANDARD", "HARDCORE"];

    private enum SettingsTab
    {
        Gameplay,
        Audio,
        Display,
    }

    private sealed record ValueBinding(
        Label Label,
        Func<string> GetText,
        Vector2 BasePosition,
        string CurrentText,
        Tween? Tween = null);

    private readonly List<ValueBinding> _valueBindings = new();
    private readonly List<Action> _stateRefreshers = new();
    private readonly List<Control> _enterItems = new();
    private readonly Dictionary<Control, Vector2> _enterBasePositions = new();
    private readonly Control[] _tabRoots = new Control[3];
    private readonly SettingsTabItem[] _tabItems = new SettingsTabItem[3];

    private SettingsTab _tab = SettingsTab.Gameplay;
    private Tween? _tabTween;
    private bool _localMotionBusy;
    private SceneTreeTimer? _saveTimer;
    private bool _built;

    public override void _Ready()
    {
        GameSession.EnsureInit();
        AddChild(new NeonBackground { Size = UiLayout.DesignSize });

        BuildTopBar();
        BuildTabs();
        BuildContent();
        SelectTab(SettingsTab.Gameplay, animate: false);

        _built = true;
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

    // ---- 顶部栏 ----

    private void BuildTopBar()
    {
        AddChild(new ColorRect
        {
            Position = Vector2.Zero,
            Size = new Vector2(UiLayout.DesignWidth, BarHeight),
            Color = new Color(UiFonts.Bg, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        AddChild(new ColorRect
        {
            Position = new Vector2(0f, BarHeight),
            Size = new Vector2(UiLayout.DesignWidth, 2f),
            Color = UiFonts.Line,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var back = new CutButton
        {
            Position = new Vector2(TabX, 12f),
            Size = new Vector2(TouchButton, TouchButton),
            Cut = 18f,
            Text = "‹",
            FontSize = 44,
            Accent = UiFonts.Cyan,
        };
        back.Pressed += Back;
        AddChild(back);
        _enterItems.Add(back);

        var title = new Label
        {
            Position = new Vector2(176f, 30f),
            Size = new Vector2(900f, 60f),
            Text = "SETTINGS",
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiLabels.TechBold(title, 40, UiFonts.Text);
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
        UiLabels.Tech(version, 13, UiFonts.Dim);
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

    private void SelectTab(SettingsTab tab, bool animate)
    {
        var previous = _tab;
        _tab = tab;
        for (var i = 0; i < _tabItems.Length; i++)
            _tabItems[i].Selected = i == (int)tab;

        for (var i = 0; i < _tabRoots.Length; i++)
            _tabRoots[i].Visible = i == (int)tab;

        _tabTween?.Kill();
        _tabTween = null;
        var root = _tabRoots[(int)tab];
        var motion = UiMotionProfile.For(GameSession.Settings.MotionMode);
        RefreshValues(animateChanges: false);
        if (!animate || !_built || previous == tab || !motion.IsAnimated)
        {
            root.Modulate = Colors.White;
            root.Position = Vector2.Zero;
            return;
        }

        // 切 tab 走控制量级时长：Full 淡入+轻微位移，Reduced 只淡入。
        var shift = motion.AllowDirectionalMotion ? motion.ContentShift * 0.5f : 0f;
        root.Modulate = new Color(1f, 1f, 1f, 0f);
        root.Position = new Vector2(shift, 0f);
        _tabTween = CreateTween().SetParallel(true);
        _tabTween.TweenMethod(Callable.From<float>(progress =>
            {
                var eased = UiEase.Enter(progress);
                root.Modulate = new Color(1f, 1f, 1f, eased);
                root.Position = new Vector2(shift, 0f).Lerp(Vector2.Zero, eased);
            }), 0f, 1f, motion.HoverDuration)
            .SetTrans(Tween.TransitionType.Linear);
    }

    // ---- 内容卡 ----

    private void BuildContent()
    {
        var card = new CutPanel
        {
            Position = new Vector2(CardX, CardY),
            Size = new Vector2(CardWidth, CardHeight),
            Cut = 20f,
            Fill = new Color(UiFonts.Panel, 0.92f),
            Border = UiFonts.Line,
            BorderWidth = 1.5f,
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

        var speedRow = CreateRow(root, 500f);
        var speedSub = AddRowLabel(speedRow, "DROP SPEED", $"{s.FallSpeedMultiplier:F1}x");
        if (speedSub != null)
            _valueBindings.Add(new ValueBinding(speedSub,
                () => $"{s.FallSpeedMultiplier:F1}x", speedSub.Position, speedSub.Text));
        AddStepper(speedRow,
            () => $"Lv {s.FallSpeedLevel:D2}",
            direction => s.FallSpeedLevel = Math.Clamp(s.FallSpeedLevel + direction, 1, 20));

        var effectsRow = CreateRow(root, 656f);
        AddRowLabel(effectsRow, "HIT EFFECTS", null);
        AddToggle(effectsRow,
            () => s.GameplayEffectsEnabled,
            () => s.GameplayEffectsEnabled = !s.GameplayEffectsEnabled);

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
        AddSlider(row, accent, read, write);
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
            Position = new Vector2(ColumnLeft, FooterTextY - 16f),
            Size = new Vector2(420f, 32f),
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
        AddLabel(item, text, 14, UiFonts.Dim, false, 0f, 14f, 600f, 28f);
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
        AddLabel(row, title, 26, UiFonts.Text, false, 0f, RowHeight * 0.5f - 15f, 700f, 36f);
        if (subText == null)
            return null;
        return AddLabel(row, subText, 15, UiFonts.Dim, false, 0f, RowHeight * 0.5f + 17f, 700f, 26f);
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

    private void AddToggle(Control row, Func<bool> read, Action toggle)
    {
        var control = new SettingsToggle
        {
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

    private void AddSlider(Control row, Color accent, Func<int> read, Action<int> write)
    {
        var percent = AddLabel(row, $"{read()}%", 36, accent, true,
            ColumnRight - ColumnLeft - 320f, RowHeight * 0.5f, 320f, 64f,
            HorizontalAlignment.Right);

        var control = new SettingsSlider
        {
            Position = new Vector2(TrackX - ColumnLeft - TrackHandle * 0.5f, 0f),
            Size = new Vector2(TrackWidth + TrackHandle, RowHeight),
            Accent = accent,
        };
        control.SetPercent(read());
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
            control.SetPercent(value);
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

    // ---- 数值刷新与持久化 ----

    private void Commit()
    {
        GameSession.Settings.ApplyAudioBuses();
        RefreshValues(animateChanges: true);
        var timer = GetTree().CreateTimer(0.4);
        _saveTimer = timer;
        timer.Timeout += () =>
        {
            if (!ReferenceEquals(_saveTimer, timer))
                return;
            _saveTimer = null;
            GameSession.Settings.Save();
        };
    }

    public override void _ExitTree()
    {
        if (_saveTimer is null)
            return;
        _saveTimer = null;
        GameSession.Settings.Save();
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

        foreach (var refresh in _stateRefreshers)
            refresh();
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

    // ---- 设计稿专用控件 ----

    /// <summary>竖排 tab：选中 = panel 填充 + 左侧 cyan 竖条，未选中 = 1.5px 描边。</summary>
    private partial class SettingsTabItem : Control
    {
        private bool _selected;
        private bool _hover;

        [Signal]
        public delegate void ChosenEventHandler();

        public string Text { get; set; } = "";

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () =>
            {
                _hover = true;
                QueueRedraw();
            };
            MouseExited += () =>
            {
                _hover = false;
                QueueRedraw();
            };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true })
            {
                EmitSignal(SignalName.Chosen);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var points = UiGeometry.CutCorners(Size, 14f);
            if (_selected)
            {
                DrawColoredPolygon(points, new Color(UiFonts.Panel, 0.95f));
                DrawRect(new Rect2(0f, 14f, 8f, Size.Y - 14f), UiFonts.Cyan);
            }
            else
            {
                if (_hover)
                    DrawColoredPolygon(points, new Color(UiFonts.PanelHover, 0.55f));
                DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 1.5f, true);
            }

            DrawString(_selected ? UiFonts.TechBold : UiFonts.Tech,
                new Vector2(40f, Size.Y * 0.5f + 8.4f), Text,
                HorizontalAlignment.Left, Size.X - 40f, 24,
                _selected ? UiFonts.Text : UiFonts.Dim);
        }
    }

    /// <summary>132×60 开关：cyan 半透明轨 + 48px 切角方块，切换时方块滑动。</summary>
    private partial class SettingsToggle : Control
    {
        private bool _on;
        private float _knob;
        private Tween? _tween;

        [Signal]
        public delegate void PressedEventHandler();

        public override void _Ready() =>
            MouseDefaultCursorShape = CursorShape.PointingHand;

        public void SetOn(bool on, bool animate)
        {
            if (_on == on)
                return;
            _on = on;
            _tween?.Kill();
            _tween = null;
            var motion = MotionProfile;
            if (!animate || !motion.IsAnimated)
            {
                _knob = on ? 1f : 0f;
                QueueRedraw();
                return;
            }

            _tween = CreateTween();
            _tween.TweenMethod(Callable.From<float>(value =>
                {
                    _knob = value;
                    QueueRedraw();
                }), _knob, on ? 1f : 0f, motion.ValueDuration)
                .SetTrans(Tween.TransitionType.Linear);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true })
            {
                SetOn(!_on, animate: true);
                EmitSignal(SignalName.Pressed);
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var track = UiGeometry.CutCorners(Size, 16f);
            if (_on)
            {
                DrawColoredPolygon(track, new Color(UiFonts.Cyan, 0.14f));
                DrawPolyline(UiGeometry.Close(track), UiFonts.Cyan, 2.5f, true);
            }
            else
            {
                DrawPolyline(UiGeometry.Close(track), UiFonts.Line, 2.5f, true);
            }

            var knobX = 12f + _knob * (Size.X - 24f - ToggleKnob);
            var knob = UiGeometry.CutCorners(knobX, (Size.Y - ToggleKnob) * 0.5f,
                ToggleKnob, ToggleKnob, 12f);
            DrawColoredPolygon(knob, _on ? UiFonts.Cyan : new Color(UiFonts.Dim, 0.55f));
        }

        private static UiMotionProfile MotionProfile =>
            UiMotionProfile.For(GameSession.Settings.MotionMode);
    }

    /// <summary>620×6 轨 + 44px 切角手柄；点按/拖动直接改值（按 5% 取整）。</summary>
    private partial class SettingsSlider : Control
    {
        private int _percent;
        private bool _dragging;

        [Signal]
        public delegate void ChangedEventHandler(int value);

        public Color Accent { get; set; } = UiFonts.Cyan;

        public void SetPercent(int percent)
        {
            if (_percent == percent)
                return;
            _percent = percent;
            QueueRedraw();
        }

        public override void _Ready() =>
            MouseDefaultCursorShape = CursorShape.PointingHand;

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                    _dragging = button.Pressed;
                    if (button.Pressed)
                        ApplyAt(button.Position.X);
                    AcceptEvent();
                    break;
                case InputEventMouseMotion motion when _dragging:
                    ApplyAt(motion.Position.X);
                    AcceptEvent();
                    break;
                case InputEventScreenTouch touch:
                    _dragging = touch.Pressed;
                    if (touch.Pressed)
                        ApplyAt(touch.Position.X);
                    AcceptEvent();
                    break;
                case InputEventScreenDrag drag:
                    ApplyAt(drag.Position.X);
                    AcceptEvent();
                    break;
            }
        }

        private void ApplyAt(float localX)
        {
            var ratio = Mathf.Clamp((localX - TrackHandle * 0.5f) / TrackWidth, 0f, 1f);
            var value = Mathf.RoundToInt(ratio * 100f);
            value = Mathf.Clamp(Mathf.RoundToInt((float)value / VolumeStepPercent) * VolumeStepPercent, 0, 100);
            if (value == _percent)
                return;
            _percent = value;
            QueueRedraw();
            EmitSignal(SignalName.Changed, value);
        }

        public override void _Draw()
        {
            var trackLeft = TrackHandle * 0.5f;
            var centerY = Size.Y * 0.5f;
            DrawRect(new Rect2(trackLeft, centerY - 3f, TrackWidth, 6f),
                new Color(UiFonts.Text, 0.14f));
            var fill = TrackWidth * _percent / 100f;
            DrawRect(new Rect2(trackLeft, centerY - 3f, fill, 6f), Accent);
            var handle = UiGeometry.CutCorners(
                trackLeft + fill - TrackHandle * 0.5f, centerY - TrackHandle * 0.5f,
                TrackHandle, TrackHandle, 12f);
            DrawColoredPolygon(handle, UiFonts.Text);
        }
    }

    /// <summary>168×72 分段选择（FULL / REDUCED / OFF）。</summary>
    private partial class SettingsSegments : Control
    {
        private string[] _labels = [];
        private int _selected;
        private int _hover = -1;

        [Signal]
        public delegate void ChosenEventHandler(int index);

        public void Configure(string[] labels, int selected)
        {
            _labels = labels;
            _selected = selected;
            QueueRedraw();
        }

        public void SetSelected(int selected)
        {
            if (_selected == selected)
                return;
            _selected = selected;
            QueueRedraw();
        }

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseExited += () =>
            {
                if (_hover < 0)
                    return;
                _hover = -1;
                QueueRedraw();
            };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseMotion motion)
            {
                var index = IndexAt(motion.Position.X);
                if (index != _hover)
                {
                    _hover = index;
                    QueueRedraw();
                }
                return;
            }

            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } button)
            {
                EmitSignal(SignalName.Chosen, IndexAt(button.Position.X));
                AcceptEvent();
            }
            else if (e is InputEventScreenTouch { Pressed: true } touch)
            {
                EmitSignal(SignalName.Chosen, IndexAt(touch.Position.X));
                AcceptEvent();
            }
        }

        private int IndexAt(float x) =>
            Mathf.Clamp((int)(x / (SegmentWidth + SegmentGap)), 0, _labels.Length - 1);

        public override void _Draw()
        {
            for (var i = 0; i < _labels.Length; i++)
            {
                var origin = new Vector2(i * (SegmentWidth + SegmentGap), 0f);
                var points = UiGeometry.CutCorners(origin.X, origin.Y, SegmentWidth, SegmentHeight, 16f);

                var selected = i == _selected;
                if (selected)
                    DrawColoredPolygon(points, new Color(UiFonts.Cyan, 0.16f));
                else if (i == _hover)
                    DrawColoredPolygon(points, new Color(UiFonts.PanelHover, 0.55f));
                DrawPolyline(UiGeometry.Close(points), selected ? UiFonts.Cyan : UiFonts.Line,
                    2.5f, true);
                DrawString(UiFonts.TechBold,
                    origin + new Vector2(0f, SegmentHeight * 0.5f + 7f), _labels[i],
                    HorizontalAlignment.Center, SegmentWidth, 20,
                    selected ? UiFonts.Cyan : UiFonts.Dim);
            }
        }
    }

    /// <summary>样式稿的无边框文字动作（RESET DEFAULTS）。</summary>
    private partial class FlatTextButton : Control
    {
        private bool _hover;

        [Signal]
        public delegate void PressedEventHandler();

        public string Text { get; set; } = "";

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () =>
            {
                _hover = true;
                QueueRedraw();
            };
            MouseExited += () =>
            {
                _hover = false;
                QueueRedraw();
            };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true })
            {
                EmitSignal(SignalName.Pressed);
                AcceptEvent();
            }
        }

        public override void _Draw() =>
            DrawString(UiFonts.Tech, new Vector2(0f, Size.Y * 0.5f + 5.6f), Text,
                HorizontalAlignment.Left, Size.X, 16,
                _hover ? UiFonts.Cyan : UiFonts.Dim);
    }
}

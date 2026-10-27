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
    private const int TimingStepMs = 5;
    private const int VolumeStepPercent = SettingsSlider.VolumeStepPercent;
    /// <summary>进场错峰总预算：条目再多也在这么久内排完，避免最后一行等一秒多。</summary>
    private const double EnterStaggerBudgetSeconds = 0.30;

    private static readonly string[] TabNames = ["GAMEPLAY", "AUDIO", "DISPLAY", "EFFECTS"];
    private static readonly string[] MotionNames = ["FULL", "REDUCED", "OFF"];
    private static readonly string[] EffectLevelNames = ["FULL", "REDUCED", "OFF"];
    private static readonly string[] ResidualNames = ["OFF", "0.20s", "0.30s", "0.45s"];
    private static readonly int[] ResidualValuesMs = [0, 200, 300, 450];
    // Bleed 为预留模式，暂不出现在设置页（见 docs/gameplay-spec.md「游玩模式」）。
    private static readonly string[] ModeNames = ["STANDARD", "HARDCORE"];

    private enum SettingsTab
    {
        Gameplay,
        Audio,
        Display,
        Effects,
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
    private readonly Control[] _tabRoots = new Control[4];
    private readonly SettingsTabItem[] _tabItems = new SettingsTabItem[4];

    private SettingsTab _tab = SettingsTab.Gameplay;
    // 从配色页返回时仍停在 EFFECTS（场景重载后静态字段仍在）。
    private static SettingsTab _lastTab = SettingsTab.Gameplay;
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
        SelectTab(_lastTab, animate: false);

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

    private void SelectTab(SettingsTab tab, bool animate)
    {
        var previous = _tab;
        _tab = tab;
        _lastTab = tab;
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
            // 隐藏 tab 的内容不参与进场：否则它们会白占错峰序号，
            // 让当前 tab（常是最后构建的 EFFECTS）晚一秒多才浮现。
            if (!item.IsVisibleInTree())
                continue;
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
        var staged = new List<Control>();
        foreach (var item in _enterItems)
            if (_enterBasePositions.ContainsKey(item))
                staged.Add(item);

        // 错峰总预算固定，条目越多步长越短：内容整体在 ~0.3s 内排完，
        // 而不是按 60ms/条线性累加（旧实现 30 条要 1.8s，期间输入还被占着）。
        var step = motion.AllowStagger && staged.Count > 1
            ? Math.Min(motion.Stagger, EnterStaggerBudgetSeconds / (staged.Count - 1))
            : 0.0;

        var tween = CreateTween().SetParallel(true);
        for (var i = 0; i < staged.Count; i++)
        {
            var item = staged[i];
            var basePosition = _enterBasePositions[item];
            var startPosition = item.Position;
            var delay = i * step;
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

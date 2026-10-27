using Godot;
using DynamiteUniverse.Audio;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 延迟校准向导（Penpot 板 <c>Game — Calibration · 01 Live / 02 Result</c>）。
///
/// 几何唯一权威是 <c>design/calibration-v1/build.js</c>：头部（返回键 + CALIBRATION + 120 BPM 标签）、
/// 左列相位块（无面板底板）、中央单轨试打台、右侧统计板（1250,156,622×640 切角板）、
/// 底部偏差条带（48,956,1824×100）。判定线 y 与下落速度不取设计稿的示意值，而是取
/// <see cref="GameplayStageGeometry"/> / <see cref="GameplayVisualMapper"/> 的游玩同款常量，
/// 保证试打台的视觉条件与真实打歌一致。
///
/// 状态机 Ready → Sampling → Complete（相位、目标样本数、热身基准、最后入样 note 与样本统计
/// 都由 <see cref="CalibrationSampling"/> 持有；本页负责读数、动效、保存和导航）：
/// <list type="bullet">
/// <item>Ready：节拍循环在播、note 照常下落但不采样，步进器可用，首次击打进入 Sampling。</item>
/// <item>Sampling：步进器锁定；前 2 个 note 是热身（不入样），之后 |偏差| ≤ 150ms 的击打入样。</item>
/// <item>Complete：轨道冻结压暗、统计板换成确认面板，条带定格并显示 MEDIAN 线。</item>
/// </list>
///
/// 计时口径见 <see cref="SongPositionSeconds"/>：与 <see cref="SongClock"/> 的窗口模式修正链
/// 完全一致，唯一差别是不叠加 <c>UserOffsetMs</c>（这一步正是校准要测的量）；偏差与归属 note
/// 的取整口径见 <see cref="CalibrationSampling"/>。
/// </summary>
public partial class CalibrationScreen : Node2D
{

    // ---- 动效 ----
    /// <summary>Complete 时轨道冻结压暗的过渡时长（0.2s）。</summary>
    private const double FreezeDuration = 0.20;
    /// <summary>相位块文案、统计板内容交叉淡换的时长。</summary>
    private const double ContentSwapDuration = 0.18;
    private const float FrozenLaneOpacity = 0.35f;

    /// <summary>一次击打的分组索引（用于进场动画的错峰）。</summary>
    private enum EnterGroup
    {
        Header,
        PhaseBlock,
        Stage,
        Panel,
        Strip,
    }

    // ---- 运行态 ----
    /// <summary>采样内核：相位、目标样本数、热身基准、最后入样 note 与统计。在 <see cref="_Ready"/> 建。</summary>
    private CalibrationSampling _sampling = null!;
    /// <summary>最近一次击打的带符号偏差（毫秒，正 = 晚），仅用于读数显示。</summary>
    private double _lastHitMs;
    private HitReadout _lastHitKind = HitReadout.None;
    /// <summary>stray tap 的读数保持时长（此后 LAST HIT 回到 —）。</summary>
    private double _strayReadoutRemaining;

    private double _laneDim = 1.0;
    private double _laneDimTarget = 1.0;
    private double _completeElapsed;

    private bool _localTransitionBusy;
    private bool _navigating;
    private SwapPhase _swapPhase = SwapPhase.Idle;
    private double _swapElapsed;
    /// <summary>当前可视状态：淡换期间 <see cref="CalibrationSampling.Phase"/> 已经切到新值，用它找"旧内容那一组"。</summary>
    private SamplingPhase _visualState = SamplingPhase.Ready;

    // ---- 节点 ----
    private Node2D _headerRoot = null!;
    private Node2D _phaseRoot = null!;
    private Node2D _stageRoot = null!;
    private Node2D _panelRoot = null!;
    private Node2D _stripRoot = null!;

    private CalibrationStage _stage = null!;
    private CalibrationHitReadout _hitReadout = null!;
    private CalibrationStrip _strip = null!;
    private CutPanel _statsPanel = null!;
    private CutPanel _resultPanel = null!;
    private Control _statsContent = null!;
    private Control _resultContent = null!;

    private Label _phaseTag = null!;
    private Label _phaseTitle = null!;
    private Label _phaseGuide = null!;
    private readonly ColorRect[] _phaseSegments = new ColorRect[2];
    private Label _currentOffsetValue = null!;

    private Label _lastHitValue = null!;
    private Label _lastHitUnit = null!;
    private Label _frozenTag = null!;
    private Label _earlyLateLegend = null!;
    private Label _pulseInstruction = null!;

    private Label _targetValue = null!;
    private Label _medianValue = null!;
    private Label _spreadValue = null!;
    private Label _progressCount = null!;
    private ColorRect _progressFill = null!;
    private Label _suggestedValue = null!;

    private Label _resultSuggestedValue = null!;
    private Label _resultSuggestedSub = null!;
    private Label _resultDelta = null!;
    private StepperButton _targetMinus = null!;
    private StepperButton _targetPlus = null!;
    private CutButton _applyButton = null!;
    private CutButton _retryButton = null!;
    private HoverLabel _discardLabel = null!;

    private readonly List<(Control Item, EnterGroup Group)> _enterItems = new();
    private readonly Dictionary<Control, Vector2> _enterBasePositions = new();

    public override void _Ready()
    {
        GameSession.EnsureInit();
        AddChild(new NeonBackground { Size = UiLayout.DesignSize });

        // 目标样本数 8–32、每档 4，持久化到 GameSettings.CalibrationTargetSamples。
        _sampling = new CalibrationSampling(BarSeconds, GameSession.Settings.CalibrationTargetSamples);
        GameSession.Settings.CalibrationTargetSamples = _sampling.TargetSamples;

        _headerRoot = AddGroup("CalibrationHeader");
        _phaseRoot = AddGroup("CalibrationPhase");
        _stageRoot = AddGroup("CalibrationStage");
        _panelRoot = AddGroup("CalibrationPanel");
        _stripRoot = AddGroup("CalibrationStrip");

        _strip = new CalibrationStrip
        {
            Position = new Vector2(StripX, StripY),
            Size = new Vector2(StripWidth, StripHeight),
            Cut = StripCut,
            AxisX = AxisX - StripX,
            AxisWidth = AxisWidth,
            AxisCenterY = AxisCenterY,
            LabelX = StripLabelX - StripX,
            LabelY = StripLabelY,
            AxisLabelY = AxisLabelY,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stripRoot.AddChild(_strip);
        RegisterEntrance(_strip, EnterGroup.Strip);

        BuildHeader();
        BuildPhaseBlock();
        BuildStage();
        BuildStatsPanel();
        BuildResultPanel();

        StartBeatLoop();

        ApplyPhase(initial: true);
        RefreshReadouts();

        PrepareEnterAnimation();
        TransitionDirector.ReportSceneReady(PlayEnterAnimation);
    }

    public override void _ExitTree()
    {
        _beatSource.Pause();
    }

    // ---- 输入：触摸按下与鼠标左键 ----

    /// <summary>
    /// 击打统一在 <c>_UnhandledInput</c> 收取：GUI（返回键、步进器、结束态按钮）先拿到事件，
    /// 被消费的点击不会变成击打；试打台上的控件都是 MouseFilter=Ignore，落到这里才算击打。
    /// 键盘不参与击打（用户拍板不要空格），只保留 Esc = 返回。
    /// </summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;

        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            NavigateBack();
            GetViewport().SetInputAsHandled();
            return;
        }

        Vector2 position;
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } touch:
                position = touch.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                position = click.Position;
                break;
            default:
                return;
        }

        if (HandleTap(position))
            GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// 一次击打。返回 true 表示事件被试打台消费（调用方不再往下传）。
    /// 键盘不参与（用户拍板不要空格）；击打区 = 试打台全宽 × 头部之下、底部条带之上，见 <see cref="TapZone"/>。
    /// </summary>
    private bool HandleTap(Vector2 position)
    {
        if (_navigating || !TapZone.HasPoint(position))
            return false;

        RegisterHit(SongPositionSeconds());
        return true;
    }

    /// <summary>
    /// 把一次击打交给采样内核（首击进入采样），再按结果刷新读数；相位变化触发的可视切换由本页负责。
    /// </summary>
    private void RegisterHit(double songSec)
    {
        var phaseBefore = _sampling.Phase;
        var result = _sampling.Hit(songSec);

        // Complete 是冻结帧：击打不再产生任何读数（事件仍被消费，不穿透到别的层）。
        if (result.Outcome == HitOutcome.Ignored)
            return;

        ApplyHitOutcome(result);
        if (_sampling.Phase != phaseBefore)
            ApplyPhase();
        RefreshReadouts();
    }

    /// <summary>读数口径：热身灰显；入样与「同 note 重复」都按 Accepted 彩色显示；其余显示 —。</summary>
    private void ApplyHitOutcome(HitResult result)
    {
        switch (result.Outcome)
        {
            case HitOutcome.Warmup:
                ShowHitReadout(HitReadout.Warmup, result.DeviationMs);
                break;
            case HitOutcome.Accepted:
            case HitOutcome.Duplicate:
                ShowHitReadout(HitReadout.Accepted, result.DeviationMs);
                break;
            default:
                ShowStrayReadout();
                break;
        }
    }

    /// <summary>带符号读数：入样是有色毫秒，热身是同样的数值但灰显。</summary>
    private void ShowHitReadout(HitReadout kind, double deviationMs)
    {
        _lastHitMs = deviationMs;
        _lastHitKind = kind;
        BeginFadeIn(_hitReadout);
    }

    private void ShowStrayReadout()
    {
        _lastHitKind = HitReadout.Stray;
        _strayReadoutRemaining = 1.0;
        _lastHitMs = 0.0;
        BeginFadeIn(_hitReadout);
    }

    // ---- 保存与重测 ----

    /// <summary>离开页面前把目标样本数落盘（与设置页的写盘节奏一致：改动即写）。</summary>
    private void PersistTargetSamples()
    {
        GameSession.Settings.CalibrationTargetSamples = _sampling.TargetSamples;
        GameSession.Settings.Save();
    }

    /// <summary>再测一次：清空样本与读数回到 Ready，保留目标样本数。</summary>
    private void RestartSampling()
    {
        if (_navigating)
            return;
        _sampling.Restart();
        _lastHitKind = HitReadout.None;
        _strayReadoutRemaining = 0.0;
        _completeElapsed = 0.0;
        ApplyPhase();
    }

    /// <summary>应用建议偏移：当前值 − 中位数 → 5ms 步进 → ±300 钳制（<see cref="CalibrationStats.SuggestedOffsetMs"/>），保存后返回设置页。</summary>
    private void ApplySuggestedOffset()
    {
        if (_navigating || !_sampling.IsComplete)
            return;
        if (_sampling.Stats.SuggestedOffset(GameSession.Settings.TimingOffsetMs) is not { } suggested)
            return;

        GameSession.Settings.TimingOffsetMs = suggested;
        GameSession.Settings.Save();

        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        _navigating = true;
        _localTransitionBusy = true;
        _applyButton.CommitPulse(profile.FocusDuration);
        SetResultActionsEnabled(false);

        if (!profile.IsAnimated)
        {
            NavigateToSettings();
            return;
        }

        GetTree().CreateTimer(profile.FocusDuration).Timeout += () =>
        {
            if (IsInsideTree())
                NavigateToSettings();
        };
    }

    private void NavigateToSettings() =>
        TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Back);

    /// <summary>返回/放弃：只离开页面，不写任何设置。</summary>
    private void NavigateBack()
    {
        if (_navigating || _localTransitionBusy || TransitionDirector.IsBusy)
            return;
        _navigating = true;
        TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Back);
    }

    private void SetResultActionsEnabled(bool enabled)
    {
        _applyButton.Disabled = !enabled;
        _retryButton.Disabled = !enabled;
        _discardLabel.Disabled = !enabled;
        _targetMinus.Disabled = !enabled || _sampling.Phase != SamplingPhase.Ready;
        _targetPlus.Disabled = !enabled || _sampling.Phase != SamplingPhase.Ready;
    }

    private void AdjustTargetSamples(int direction)
    {
        // 采样途中步进器锁定（用户拍板：不给中途改目标），只有 Ready 能调。
        if (_navigating || !_sampling.TrySetTargetSamples(
                _sampling.TargetSamples + direction * CalibrationStats.TargetSampleStep))
            return;
        PersistTargetSamples();
        RefreshReadouts();
    }

    /// <summary>内容淡换的两段：先淡出旧内容，落新文案，再淡入。</summary>
    private enum SwapPhase
    {
        Idle,
        FadeOut,
        FadeIn,
    }

    /// <summary>相位块与两块板的内容随状态切换；文案交叉淡换 + 轨道压暗过渡。</summary>
    private void ApplyPhase(bool initial = false)
    {
        var complete = _sampling.IsComplete;
        var ready = _sampling.Phase == SamplingPhase.Ready;
        // 这些量不参与淡换：压暗目标、条带数据、门控状态立即生效。
        _laneDimTarget = complete ? FrozenLaneOpacity : 1.0;
        _completeElapsed = 0.0;
        _medianFade = complete ? 0f : 1f;
        _strip.Frozen = complete;
        _strip.Samples = _sampling.Stats.Samples;
        _strip.MedianMs = _sampling.Stats.MedianMs;
        _strip.MedianAlpha = _medianFade;
        // 文字键（放弃）与步进器的门控：只有"应用"路径需要临时禁用。
        SetResultActionsEnabled(!_navigating);
        // 步进器只有 Ready 能调：Sampling 锁定、Complete 交给确认面板。
        _targetMinus.Disabled = !ready || _navigating;
        _targetPlus.Disabled = !ready || _navigating;

        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (initial || !profile.IsAnimated)
        {
            _swapPhase = SwapPhase.Idle;
            CommitPhaseVisuals();
            SetGroupAlpha(SwapGroup(), 1f);
            SetProcess(true);
            return;
        }

        _swapPhase = SwapPhase.FadeOut;
        _swapElapsed = 0.0;
        SetGroupAlpha(SwapGroup(), 1f);
        SetProcess(true);
    }

    /// <summary>当前可见内容的淡换分组：相位块三行文字 + 当前那一块板的内容。</summary>
    private CanvasItem[] SwapGroup() =>
    [
        _phaseTag,
        _phaseTitle,
        _phaseGuide,
        _visualState == SamplingPhase.Complete ? _resultContent : _statsContent,
    ];

    private static void SetGroupAlpha(IEnumerable<CanvasItem> group, float alpha)
    {
        foreach (var item in group)
            item.Modulate = new Color(1f, 1f, 1f, alpha);
    }

    /// <summary>把当前状态落进可视：相位文案、进度分段、两块板的可见性与轨道读数。</summary>
    private void CommitPhaseVisuals()
    {
        var phase = _sampling.Phase;
        var (tag, title, guide) = phase switch
        {
            SamplingPhase.Complete => ("PHASE 02 / 02", "APPLY · 应用",
                "确认建议偏移后立即生效，也可以再测一次"),
            _ => ("PHASE 01 / 02", "TAP · 跟拍", "音符压线的瞬间，击打屏幕任意位置"),
        };
        _phaseTag.Text = tag;
        _phaseTitle.Text = title;
        _phaseGuide.Text = guide;

        var complete = phase == SamplingPhase.Complete;
        for (var i = 0; i < _phaseSegments.Length; i++)
        {
            var on = i == (complete ? 1 : 0);
            var done = i < (complete ? 1 : 0);
            var segment = _phaseSegments[i];
            segment.Size = new Vector2(on ? PhaseSegCurrentWidth : PhaseSegWidth, 4f);
            segment.Color = on ? UiFonts.Cyan : done ? new Color(UiFonts.Text, 0.7f) : UiFonts.Line;
        }

        _statsPanel.Visible = !complete;
        _statsContent.Visible = !complete;
        _resultPanel.Visible = complete;
        _resultContent.Visible = complete;
        _frozenTag.Visible = complete;
        _hitReadout.Visible = !complete;
        _pulseInstruction.Visible = phase == SamplingPhase.Ready;
        _visualState = phase;
    }

    /// <summary>推进内容淡换；返回 true 表示这一帧还在换内容。</summary>
    private bool AdvanceContentSwap(double delta)
    {
        if (_swapPhase == SwapPhase.Idle)
            return false;

        _swapElapsed += delta;
        var raw = Math.Clamp(_swapElapsed / ContentSwapDuration, 0.0, 1.0);
        var eased = UiEase.Standard((float)raw);

        if (_swapPhase == SwapPhase.FadeOut)
        {
            SetGroupAlpha(SwapGroup(), 1f - eased);
            if (raw >= 1f)
            {
                // 旧内容已降到不透明度 0，这时才落新文案与可见性，再换组淡入。
                CommitPhaseVisuals();
                SetGroupAlpha(SwapGroup(), 0f);
                _swapPhase = SwapPhase.FadeIn;
                _swapElapsed = 0.0;
            }
            return true;
        }

        SetGroupAlpha(SwapGroup(), eased);
        if (raw >= 1f)
        {
            SetGroupAlpha(SwapGroup(), 1f);
            _swapPhase = SwapPhase.Idle;
        }
        return true;
    }

    // ---- 每帧推进：下落进度、脉冲环、冻结压暗、读数淡入 ----

    public override void _Process(double delta)
    {
        if (!double.IsFinite(delta) || delta <= 0.0)
            return;

        var songSec = SongPositionSeconds();
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        var swapping = AdvanceContentSwap(delta);

        // Complete：轨道冻结、note 停在判定线上；冻结后没有节拍可跟，脉冲环也一并停住。
        var frozen = _sampling.IsComplete;
        if (frozen)
            _completeElapsed += delta;

        _stage.Frozen = frozen;
        if (!frozen)
            _stage.NoteProgresses = NoteProgresses(songSec, _stage.FallSeconds);

        if (!frozen)
        {
            // 拍点脉冲：小节头闪一次（视觉上是 1 秒周期的呼吸，小节头相位更强）。
            var beatPhase = songSec / BarSeconds;
            _stage.PulseAmount = (float)(0.5 + 0.5 * Math.Cos(beatPhase * Math.Tau));
        }
        else
        {
            _stage.PulseAmount = 0f;
        }

        _laneDim = profile.IsAnimated
            ? Mathf.MoveToward((float)_laneDim, (float)_laneDimTarget,
                (float)(delta / FreezeDuration))
            : _laneDimTarget;
        _stage.LaneDim = (float)_laneDim;


        if (_hitReadout.AdvanceFade(delta, profile))
            _lastHitValue.Modulate = new Color(1f, 1f, 1f, _hitReadout.Fade);

        if (_strayReadoutRemaining > 0.0)
        {
            _strayReadoutRemaining -= delta;
            if (_strayReadoutRemaining <= 0.0)
            {
                _strayReadoutRemaining = 0.0;
                _lastHitKind = HitReadout.None;
                RefreshReadouts();
            }
        }

        _stage.QueueRedraw();

        if (frozen)
        {
            // 条带定格 + MEDIAN 线 0.18s 淡入（Off 直接落终值）。
            _medianFade = profile.IsAnimated && _completeElapsed < ContentSwapDuration
                ? UiEase.Standard((float)(_completeElapsed / ContentSwapDuration))
                : 1f;
            _strip.MedianAlpha = _medianFade;
            _strip.QueueRedraw();
        }

        if (frozen && Math.Abs(_laneDim - FrozenLaneOpacity) < 0.001 &&
            !swapping && _strayReadoutRemaining <= 0.0 && _hitReadout.Settled &&
            _medianFade >= 1f)
            SetProcess(false);
    }

    /// <summary>MEDIAN 标记线的不透明度（Complete 时淡入）。</summary>
    private float _medianFade = 1f;

    private void BeginFadeIn(CalibrationHitReadout readout)
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        readout.BeginFade(profile);
        _lastHitValue.Modulate = new Color(1f, 1f, 1f, readout.Fade);
    }

    // ---- 读数刷新 ----

    private void RefreshReadouts()
    {
        var stats = _sampling.Stats;
        var currentOffset = GameSession.Settings.TimingOffsetMs;
        _currentOffsetValue.Text = FormatMilliseconds(currentOffset);

        _targetValue.Text = _sampling.TargetSamples.ToString();

        // LAST HIT：入样 = 带符号彩色毫秒（负=早=粉、正=晚=青）；热身 = 同样带符号但灰显；
        // stray tap / 还没击打过 = —。
        switch (_lastHitKind)
        {
            case HitReadout.Accepted:
                _lastHitValue.Text = FormatSignedMilliseconds(_lastHitMs);
                _lastHitValue.AddThemeColorOverride("font_color",
                    _lastHitMs < 0 ? UiFonts.Pink : UiFonts.Cyan);
                break;
            case HitReadout.Warmup:
                _lastHitValue.Text = FormatSignedMilliseconds(_lastHitMs);
                _lastHitValue.AddThemeColorOverride("font_color", UiFonts.Dim);
                break;
            default:
                _lastHitValue.Text = "—";
                _lastHitValue.AddThemeColorOverride("font_color", UiFonts.Dim);
                break;
        }
        _lastHitUnit.Visible = _lastHitKind != HitReadout.None;

        _medianValue.Text = stats.MedianMs is { } median
            ? FormatSignedMilliseconds(median)
            : "—";
        _spreadValue.Text = stats.SpreadMs is { } spread
            ? $"±{Math.Round(spread):0} ms"
            : "—";

        _progressCount.Text = $"{stats.Count} / {_sampling.TargetSamples}";
        var ratio = Math.Clamp(stats.Count / (float)_sampling.TargetSamples, 0f, 1f);
        _progressFill.Size = new Vector2(ProgressTrackWidth * ratio, ProgressTrackHeight);

        _suggestedValue.Text = stats.SuggestedOffset(currentOffset) is { } suggested
            ? FormatMilliseconds(suggested)
            : "—";

        _resultSuggestedValue.Text = _suggestedValue.Text;
        _resultSuggestedSub.Text = stats.Count > 0
            ? $"基于 {stats.Count} 个有效样本 · 离散 {_spreadValue.Text}"
            : "尚无有效样本";
        _resultDelta.Text = stats.SuggestedOffset(currentOffset) is { } finalOffset
            ? $"当前 {FormatMilliseconds(currentOffset)} → 应用后 {FormatMilliseconds(finalOffset)}"
            : "—";

        // 每次读数刷新都重画条带：新样本落 tick、最新一个 tick 放大、中位数标记移动。
        _strip.Samples = stats.Samples;
        _strip.MedianMs = stats.MedianMs;
        _strip.QueueRedraw();
        _medianValue.QueueRedraw();
        _spreadValue.QueueRedraw();

    }

    /// <summary>LAST HIT 读数的三种口径。</summary>
    private enum HitReadout
    {
        None,
        Warmup,
        Accepted,
        Stray,
    }

    private static string FormatMilliseconds(int ms) => $"{ms:+0;-0;0} ms";

    /// <summary>读数用 `+8` / `-12`（符号恒显，带号是数据气质的一部分）；不补零。</summary>
    private static string FormatSignedMilliseconds(double ms)
    {
        var rounded = Math.Round(ms);
        return $"{rounded:+0;-0;0}";
    }

    // ---- 进场动画（学 SongSelect 的分组交错 fade + shift）----

    private void PrepareEnterAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
            return;
        foreach (var (item, _) in _enterItems)
        {
            _enterBasePositions[item] = item.Position;
            item.Modulate = new Color(1f, 1f, 1f, 0f);
            if (profile.AllowDirectionalMotion)
                item.Position -= new Vector2(profile.ContentShift, 0f);
        }
    }

    /// <summary>分组（头部/相位块/试打台/统计板/条带）交错进场，ExpoOut。</summary>
    private void PlayEnterAnimation()
    {
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        if (!profile.IsAnimated)
        {
            ResetEnterState();
            return;
        }

        _localTransitionBusy = true;
        var tween = CreateTween().SetParallel(true);
        foreach (var (item, group) in _enterItems)
        {
            var basePosition = _enterBasePositions.GetValueOrDefault(item, item.Position);
            var startPosition = item.Position;
            var delay = profile.AllowStagger ? (int)group * profile.Stagger : 0.0;
            tween.TweenMethod(Callable.From<float>(progress =>
                {
                    var eased = UiEase.Enter(progress);
                    item.Position = startPosition.Lerp(basePosition, eased);
                    item.Modulate = new Color(1f, 1f, 1f, eased);
                }), 0f, 1f, profile.PanelDuration)
                .SetDelay(delay)
                .SetTrans(Tween.TransitionType.Linear);
        }

        tween.Chain().TweenCallback(Callable.From(() =>
        {
            ResetEnterState();
            _localTransitionBusy = false;
            SetProcess(true);
        }));
    }

    private void ResetEnterState()
    {
        foreach (var (item, _) in _enterItems)
        {
            item.Position = _enterBasePositions.GetValueOrDefault(item, item.Position);
            item.Modulate = Colors.White;
        }
    }

    private void RegisterEntrance(Control item, EnterGroup group) =>
        _enterItems.Add((item, group));
}

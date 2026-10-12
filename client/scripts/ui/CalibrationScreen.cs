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
/// 状态机 Ready → Sampling → Complete：
/// <list type="bullet">
/// <item>Ready：节拍循环在播、note 照常下落但不采样，步进器可用，首次击打进入 Sampling。</item>
/// <item>Sampling：步进器锁定；前 2 个 note 是热身（不入样），之后 |偏差| ≤ 150ms 的击打入样。</item>
/// <item>Complete：轨道冻结压暗、统计板换成确认面板，条带定格并显示 MEDIAN 线。</item>
/// </list>
///
/// 计时口径见 <see cref="SongPositionSeconds"/>：与 <see cref="SongClock"/> 的窗口模式修正链
/// 完全一致，唯一差别是不叠加 <c>UserOffsetMs</c>（这一步正是校准要测的量）。
/// </summary>
public partial class CalibrationScreen : Node2D
{
    // ---- 设计稿几何（build.js 的常量，单位 = 1920×1080 设计坐标）----
    private const float BarHeight = 120f;

    private const float PhaseX = 48f;
    private const float PhaseTagY = 200f;
    private const float PhaseTitleY = 228f;
    private const float PhaseGuideY = 292f;
    private const float PhaseSegY = 372f;
    private const float PhaseSegWidth = 56f;
    private const float PhaseSegCurrentWidth = 88f;
    private const float PhaseSegPitch = 104f;
    private const float CurrentLabelY = 460f;
    private const float CurrentValueY = 486f;
    private const float TipWarmupY = 566f;
    private const float TipScopeY = 594f;
    private const float TipExitY = 884f;

    // 试打台：舞台带 (460,760) 与轨道带 (274 宽) 同轴，判定线取游玩常量 y=861。
    private const float StageX = 460f;
    private const float StageWidth = 760f;
    private const float LaneWidth = 274f;
    private const float LaneTop = 140f;
    private const float LaneBandHeight = 781f;
    private const float Horizon1Y = 300f;
    private const float Horizon2Y = 640f;
    private const float NoteWidth = 240f;
    private const float NoteHeight = 36f;
    private const float NoteCut = 10f;
    private const float PulseInnerRadius = 34f;
    private const float PulseOuterRadius = 52f;
    private const float LastHitLabelX = 1030f;
    private const float LastHitLabelY = 764f;
    private const float LastHitValueX = 1030f;
    private const float LastHitValueY = 790f;
    private const float LastHitUnitX = 1120f;
    private const float LastHitUnitY = 824f;
    private const float EarlyLateLegendY = 916f;
    private const float FrozenTagY = 892f;

    // 右侧统计板 / 确认面板（同一块切角底板）。
    private const float PanelX = 1250f;
    private const float PanelY = 156f;
    private const float PanelWidth = 622f;
    private const float PanelHeight = 640f;
    private const float PanelInnerX = 1286f;
    private const float PanelInnerWidth = 550f;
    private const float PanelCut = 14f;
    private const float TargetRowY = 272f;
    private const float TargetValueX = 1686f;
    private const float TargetMinusX = 1638f;
    private const float TargetPlusX = 1796f;
    private const float TargetControlSize = 40f;
    private const float TargetControlCut = 10f;
    private const float TargetSubY = 328f;
    private const float StatFirstRowY = 396f;
    private const float StatRowPitch = 92f;
    private const float ProgressLabelY = 576f;
    private const float ProgressTrackY = 612f;
    private const float ProgressTrackWidth = 550f;
    private const float ProgressTrackHeight = 6f;
    private const float SuggestedLabelY = 656f;
    private const float SuggestedValueY = 682f;
    private const float SuggestedSubY = 760f;
    // 确认面板（02 Result）的行位
    private const float ResultHeadingY = 196f;
    private const float ResultDividerY = 236f;
    private const float ResultSuggestedLabelY = 272f;
    private const float ResultSuggestedValueY = 300f;
    private const float ResultSuggestedSubY = 404f;
    private const float ResultDivider2Y = 456f;
    private const float ResultDeltaY = 480f;
    private const float ResultApplyY = 552f;
    private const float ResultRetryY = 636f;
    private const float ResultDiscardY = 724f;
    private const float ResultButtonHeight = 68f;
    private const float ResultButtonCut = 16f;

    // 底部偏差条带：±150ms 映射到 ±726px。
    private const float StripX = 48f;
    private const float StripY = 956f;
    private const float StripWidth = 1824f;
    private const float StripHeight = 100f;
    private const float StripCut = 14f;
    private const float StripLabelX = 84f;
    private const float StripLabelY = 14f;
    private const float AxisX = 284f;
    private const float AxisWidth = 1452f;
    private const float AxisCenterY = 50f;
    private const float AxisLabelY = 16f;

    // ---- 动效 ----
    /// <summary>Complete 时轨道冻结压暗的过渡时长（0.2s）。</summary>
    private const double FreezeDuration = 0.20;
    /// <summary>相位块文案、统计板内容交叉淡换的时长。</summary>
    private const double ContentSwapDuration = 0.18;
    private const float FrozenLaneOpacity = 0.35f;
    /// <summary>热身 note 数：前 2 个 note 的击打不入样。</summary>
    private const int WarmupNotes = 2;
    /// <summary>note 落到判定线的迟到回收宽限：超过这个时间还没到线的 note 一定不会被击打。</summary>

    private enum PhaseState
    {
        Ready,
        Sampling,
        Complete,
    }

    /// <summary>一次击打的分组索引（用于进场动画的错峰）。</summary>
    private enum EnterGroup
    {
        Header,
        PhaseBlock,
        Stage,
        Panel,
        Strip,
    }

    // ---- 合成节拍音源常量 ----
    /// <summary>一小节 2.000s（120 BPM 4/4）；2000ms × 44100 = 88200 采样点整，循环无缝。</summary>
    private const double BarSeconds = 2.0;
    /// <summary>小节的 4 个 click 位置（毫秒）。</summary>
    private static readonly double[] ClickOffsetsMs = [0.0, 500.0, 1000.0, 1500.0];

    private const float BeatClickFrequencyHz = 1000f;
    private const float BarClickFrequencyHz = 2000f;
    private const float BeatClickDecaySeconds = 0.040f;
    private const float BeatClickGain = 0.55f;
    private const float BarClickGain = 0.75f;

    // ---- 运行态 ----
    private PhaseState _state = PhaseState.Ready;
    private readonly CalibrationStats _stats = new();
    /// <summary>本页的节拍/采样目标：8–32、每档 4；持久化到 GameSettings.CalibrationTargetSamples。</summary>
    private int _targetSamples = CalibrationStats.DefaultTargetSamples;
    /// <summary>最近一次击打的带符号偏差（毫秒，正 = 晚），仅用于读数显示。</summary>
    private double _lastHitMs;
    private bool _hasLastHit;
    private HitReadout _lastHitKind = HitReadout.None;
    /// <summary>进入 Sampling 时刚过线的那颗 note 的序号：它与其后一颗是热身。</summary>
    private int _samplingBaseNote;
    /// <summary>最近一次入样的归属 note 序号（-1 = 尚未入样）：每颗 note 至多贡献一个样本。</summary>
    private int _lastSampledNote = -1;
    /// <summary>stray tap 的读数保持时长（此后 LAST HIT 回到 —）。</summary>
    private double _strayReadoutRemaining;

    private AudioStreamPlayer _beatPlayer = null!;
    private SongClock _clock = null!;
    private bool _audioReady;
    /// <summary>2s 循环流的回卷展开：GetPlaybackPosition 每圈归零，时间轴必须单调，见 <see cref="LoopPositionUnwrapper"/>。</summary>
    private readonly LoopPositionUnwrapper _loopUnwrap = new(BarSeconds);

    private double _laneDim = 1.0;
    private double _laneDimTarget = 1.0;
    private double _completeElapsed;

    private bool _localTransitionBusy;
    private bool _navigating;
    private SwapPhase _swapPhase = SwapPhase.Idle;
    private double _swapElapsed;
    /// <summary>当前可视状态：淡换期间 _state 已经切到新值，用它找"旧内容那一组"。</summary>
    private PhaseState _visualState = PhaseState.Ready;

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

        _stats.Clear();
        _targetSamples = CalibrationStats.NormalizeTargetSamples(
            GameSession.Settings.CalibrationTargetSamples);
        GameSession.Settings.CalibrationTargetSamples = _targetSamples;

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

    private Node2D AddGroup(string name)
    {
        var node = new Node2D { Name = name };
        AddChild(node);
        return node;
    }

    // ---- 头部 ----

    private void BuildHeader()
    {
        var bar = new ColorRect
        {
            Position = Vector2.Zero,
            Size = new Vector2(UiLayout.DesignWidth, BarHeight),
            Color = new Color(UiFonts.Bg, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _headerRoot.AddChild(bar);
        _headerRoot.AddChild(new ColorRect
        {
            Position = new Vector2(0f, BarHeight),
            Size = new Vector2(UiLayout.DesignWidth, 2f),
            Color = UiFonts.Line,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var back = new CutButton
        {
            Position = new Vector2(48f, 12f),
            Size = new Vector2(96f, 96f),
            Cut = 20f,
            Text = "‹",
            FontSize = 44,
            Accent = UiFonts.Cyan,
        };
        back.Pressed += NavigateBack;
        _headerRoot.AddChild(back);
        RegisterEntrance(back, EnterGroup.Header);

        var title = AddLabel(_headerRoot, 176f, 30f, 520f, 60f, "CALIBRATION", 40,
            UiFonts.Text, bold: true, HorizontalAlignment.Left);
        RegisterEntrance(title, EnterGroup.Header);

        var subtitle = AddLabel(_headerRoot, 178f, 90f, 520f, 24f, "JUDGE OFFSET · 判定偏移校准", 13,
            UiFonts.Dim);
        RegisterEntrance(subtitle, EnterGroup.Header);

        var tempo = AddLabel(_headerRoot, 1330f, 46f, 520f, 40f, "120 BPM · 4/4 · 1 NOTE / BAR", 13,
            UiFonts.Dim, align: HorizontalAlignment.Right);
        RegisterEntrance(tempo, EnterGroup.Header);
    }

    // ---- 左列相位块（无面板底板）----

    private void BuildPhaseBlock()
    {
        _phaseTag = AddLabel(_phaseRoot, PhaseX, PhaseTagY, 340f, 22f, "PHASE 01 / 02", 13, UiFonts.Dim);
        _phaseTitle = AddLabel(_phaseRoot, PhaseX, PhaseTitleY, 380f, 52f, "TAP · 跟拍", 36,
            UiFonts.Cyan, bold: true);
        _phaseGuide = AddLabel(_phaseRoot, PhaseX, PhaseGuideY, 340f, 56f,
            "音符压线的瞬间，击打屏幕任意位置", 15, UiFonts.Dim);

        for (var i = 0; i < _phaseSegments.Length; i++)
        {
            var segment = new ColorRect
            {
                Position = new Vector2(PhaseX + i * PhaseSegPitch, PhaseSegY),
                Size = new Vector2(PhaseSegWidth, 4f),
                Color = UiFonts.Line,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _phaseRoot.AddChild(segment);
            _phaseSegments[i] = segment;
        }

        AddLabel(_phaseRoot, PhaseX, CurrentLabelY, 340f, 22f, "当前偏移 · CURRENT", 13, UiFonts.Dim);
        _currentOffsetValue = AddLabel(_phaseRoot, PhaseX, CurrentValueY, 340f, 44f, "+0 ms", 30,
            UiFonts.Text, bold: true);
        AddLabel(_phaseRoot, PhaseX, TipWarmupY, 340f, 24f, "前 2 拍为热身，不计入统计", 13, UiFonts.Dim);
        AddLabel(_phaseRoot, PhaseX, TipScopeY, 340f, 24f, "校准结果只影响判定时机，不改变谱面", 13, UiFonts.Dim);
        AddLabel(_phaseRoot, PhaseX, TipExitY, 340f, 24f, "左上角返回可随时退出，不写入任何设置", 12, UiFonts.Dim);

        foreach (var child in _phaseRoot.GetChildren())
            if (child is Control control)
                RegisterEntrance(control, EnterGroup.PhaseBlock);
    }

    // ---- 中央单轨试打台 ----

    private void BuildStage()
    {
        _stage = new CalibrationStage
        {
            Position = Vector2.Zero,
            Size = UiLayout.DesignSize,
            LaneX = LaneX,
            LaneWidth = LaneWidth,
            LaneTop = LaneTop,
            LaneBandHeight = LaneBandHeight,
            JudgeY = JudgeLineY,
            Horizon1Y = Horizon1Y,
            Horizon2Y = Horizon2Y,
            StageX = StageX,
            StageWidth = StageWidth,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stageRoot.AddChild(_stage);
        RegisterEntrance(_stage, EnterGroup.Stage);

        _pulseInstruction = AddLabel(_stageRoot, StageX, 404f, StageWidth, 28f,
            "击打屏幕任意位置开始", 18, UiFonts.Cyan, align: HorizontalAlignment.Center);
        RegisterEntrance(_pulseInstruction, EnterGroup.Stage);

        _hitReadout = new CalibrationHitReadout
        {
            Position = Vector2.Zero,
            Size = UiLayout.DesignSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stageRoot.AddChild(_hitReadout);
        RegisterEntrance(_hitReadout, EnterGroup.Stage);

        AddLabel(_hitReadout, LastHitLabelX, LastHitLabelY, 190f, 22f, "LAST HIT", 14, UiFonts.Dim);
        _lastHitValue = AddLabel(_hitReadout, LastHitValueX, LastHitValueY, 130f, 68f, "—", 56,
            UiFonts.Dim, bold: true);
        _lastHitUnit = AddLabel(_hitReadout, LastHitUnitX, LastHitUnitY, 60f, 30f, "ms", 20, UiFonts.Dim);
        _earlyLateLegend = AddLabel(_hitReadout, StageX, EarlyLateLegendY, StageWidth, 20f,
            "EARLY − / LATE +", 12, UiFonts.Dim, align: HorizontalAlignment.Center);

        _frozenTag = AddLabel(_stageRoot, StageX, FrozenTagY, StageWidth, 24f,
            "采样完成 · SIGNAL FROZEN", 13, UiFonts.Dim, align: HorizontalAlignment.Center);
        _frozenTag.Visible = false;
    }


    // ---- 右侧统计板（01 Live）----

    private void BuildStatsPanel()
    {
        _statsPanel = new CutPanel
        {
            Position = new Vector2(PanelX, PanelY),
            Size = new Vector2(PanelWidth, PanelHeight),
            Cut = PanelCut,
            Fill = new Color("0f1429", 0.85f),
            Border = UiFonts.Line,
            BorderWidth = 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panelRoot.AddChild(_statsPanel);
        RegisterEntrance(_statsPanel, EnterGroup.Panel);

        _statsContent = new Control
        {
            Position = Vector2.Zero,
            Size = UiLayout.DesignSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panelRoot.AddChild(_statsContent);
        RegisterEntrance(_statsContent, EnterGroup.Panel);

        AddLabel(_statsContent, PanelInnerX, 196f, PanelInnerWidth, 24f,
            "信号分析 · SIGNAL ANALYSIS", 14, UiFonts.Dim);
        AddDivider(_statsContent, 236f);

        AddLabel(_statsContent, PanelInnerX, 278f, 340f, 32f, "目标样本 · TARGET", 18, UiFonts.Text);
        _targetMinus = new StepperButton
        {
            Position = new Vector2(TargetMinusX, TargetRowY),
            Size = new Vector2(TargetControlSize, TargetControlSize),
            Cut = TargetControlCut,
            Glyph = "−",
        };
        _targetMinus.Pressed += () => AdjustTargetSamples(-1);
        _statsContent.AddChild(_targetMinus);

        _targetValue = AddLabel(_statsContent, TargetValueX, 268f, 102f, 44f, "16", 26,
            UiFonts.Text, bold: true, HorizontalAlignment.Center);

        _targetPlus = new StepperButton
        {
            Position = new Vector2(TargetPlusX, TargetRowY),
            Size = new Vector2(TargetControlSize, TargetControlSize),
            Cut = TargetControlCut,
            Glyph = "+",
        };
        _targetPlus.Pressed += () => AdjustTargetSamples(1);
        _statsContent.AddChild(_targetPlus);
        AddLabel(_statsContent, PanelInnerX, TargetSubY, PanelInnerWidth, 20f,
            "8 – 32 · 每档 4 拍", 12, UiFonts.Dim);

        AddLabel(_statsContent, PanelInnerX, StatFirstRowY, 340f, 32f, "中位数 · MEDIAN", 18, UiFonts.Text);
        _medianValue = AddLabel(_statsContent, 1556f, StatFirstRowY - 8f, 280f, 44f, "—", 28,
            UiFonts.Cyan, bold: true, HorizontalAlignment.Right);

        var spreadY = StatFirstRowY + StatRowPitch;
        AddLabel(_statsContent, PanelInnerX, spreadY, 340f, 32f, "离散度 · SPREAD", 18, UiFonts.Text);
        _spreadValue = AddLabel(_statsContent, 1556f, spreadY - 8f, 280f, 44f, "—", 28,
            UiFonts.Text, bold: true, HorizontalAlignment.Right);

        AddDivider(_statsContent, 556f);
        AddLabel(_statsContent, PanelInnerX, ProgressLabelY, 340f, 22f, "采样进度 · PROGRESS", 13, UiFonts.Dim);
        _progressCount = AddLabel(_statsContent, 1556f, ProgressLabelY - 4f, 280f, 26f, "0 / 16", 15,
            UiFonts.Text, bold: true, HorizontalAlignment.Right);
        _statsContent.AddChild(new ColorRect
        {
            Position = new Vector2(PanelInnerX, ProgressTrackY),
            Size = new Vector2(ProgressTrackWidth, ProgressTrackHeight),
            Color = UiFonts.Line,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _progressFill = new ColorRect
        {
            Position = new Vector2(PanelInnerX, ProgressTrackY),
            Size = new Vector2(0f, ProgressTrackHeight),
            Color = UiFonts.Cyan,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _statsContent.AddChild(_progressFill);

        AddLabel(_statsContent, PanelInnerX, SuggestedLabelY, PanelInnerWidth, 22f,
            "建议偏移 · SUGGESTED OFFSET", 14, UiFonts.Dim);
        _suggestedValue = AddLabel(_statsContent, PanelInnerX, SuggestedValueY, PanelInnerWidth, 72f,
            "—", 64, UiFonts.Cyan, bold: true);
        AddLabel(_statsContent, PanelInnerX, SuggestedSubY, PanelInnerWidth, 22f,
            "随击打样本实时更新 · 打满目标后可应用", 13, UiFonts.Dim);

        foreach (var child in _statsContent.GetChildren())
            if (child is Control control)
                RegisterEntrance(control, EnterGroup.Panel);
    }

    // ---- 右侧确认面板（02 Result）----

    private void BuildResultPanel()
    {
        _resultPanel = new CutPanel
        {
            Position = new Vector2(PanelX, PanelY),
            Size = new Vector2(PanelWidth, PanelHeight + 20f),
            Cut = PanelCut,
            Fill = new Color("0f1429", 0.85f),
            Border = UiFonts.Line,
            BorderWidth = 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _panelRoot.AddChild(_resultPanel);

        _resultContent = new Control
        {
            Position = Vector2.Zero,
            Size = UiLayout.DesignSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _panelRoot.AddChild(_resultContent);

        AddLabel(_resultContent, PanelInnerX, ResultHeadingY, PanelInnerWidth, 24f,
            "校准完成 · CALIBRATION COMPLETE", 14, UiFonts.Dim);
        AddDivider(_resultContent, ResultDividerY);
        AddLabel(_resultContent, PanelInnerX, ResultSuggestedLabelY, PanelInnerWidth, 22f,
            "建议偏移 · SUGGESTED OFFSET", 14, UiFonts.Dim);
        _resultSuggestedValue = AddLabel(_resultContent, PanelInnerX, ResultSuggestedValueY,
            PanelInnerWidth, 96f, "—", 88, UiFonts.Cyan, bold: true);
        _resultSuggestedSub = AddLabel(_resultContent, PanelInnerX, ResultSuggestedSubY,
            PanelInnerWidth, 24f, "—", 15, UiFonts.Dim);
        AddDivider(_resultContent, ResultDivider2Y);
        _resultDelta = AddLabel(_resultContent, PanelInnerX, ResultDeltaY, PanelInnerWidth, 28f,
            "—", 16, UiFonts.Text);

        _applyButton = new CutButton
        {
            Position = new Vector2(PanelInnerX, ResultApplyY),
            Size = new Vector2(PanelInnerWidth, ResultButtonHeight),
            Cut = ResultButtonCut,
            Text = "应用偏移 · APPLY",
            StyleKind = CutButton.ButtonStyle.Solid,
            TechFont = false,
            FontSize = 22,
        };
        _applyButton.Pressed += ApplySuggestedOffset;
        _resultContent.AddChild(_applyButton);

        _retryButton = new CutButton
        {
            Position = new Vector2(PanelInnerX, ResultRetryY),
            Size = new Vector2(PanelInnerWidth, ResultButtonHeight),
            Cut = ResultButtonCut,
            Text = "再测一次 · RETRY",
            TechFont = false,
            FontSize = 22,
            Accent = UiFonts.Cyan,
        };
        _retryButton.Pressed += RestartSampling;
        _resultContent.AddChild(_retryButton);

        // CutButton 自带 0.10s hover；放弃是文字键，用共享的 UiHoverFade 走同一档过渡。
        _discardLabel = new HoverLabel
        {
            Position = new Vector2(PanelInnerX, ResultDiscardY),
            Size = new Vector2(PanelInnerWidth, 44f),
            Text = "放弃并返回 · DISCARD",
            FontSize = 15,
            BaseColor = UiFonts.Dim,
            HoverColor = UiFonts.Text,
            Alignment = HorizontalAlignment.Center,
        };
        _discardLabel.Pressed += NavigateBack;
        _resultContent.AddChild(_discardLabel);
    }

    private static void AddDivider(Control parent, float y)
    {
        parent.AddChild(new ColorRect
        {
            Position = new Vector2(PanelInnerX, y),
            Size = new Vector2(PanelInnerWidth, 1.5f),
            Color = UiFonts.Line,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }

    // ---- 派生几何：全部从游玩常量算，不硬编设计稿的示意值 ----

    /// <summary>判定线 y：与游玩同源（<see cref="GameplayStageGeometry.CenterLineY"/>，设计稿的 861 就是这个值）。</summary>
    private static readonly float JudgeLineY = GameplayStageGeometry.CenterLineY;

    /// <summary>
    /// 轨道带左缘：居中于试打台舞台带（StageX 460..1220，中轴 840，即设计稿的 x=703）。
    /// 注意这里刻意<strong>不</strong>取游玩的中轨轴（<c>CenterToStageX(MirrorAxis)</c> = 963）：
    /// 校准要保真的是时间维度——判定线 y（<see cref="JudgeLineY"/>）、落速、note 尺寸都源于游玩常量；
    /// 横向位置是页面布局，且 963 会让 LAST HIT 读数（x=1030）压上轨道带右缘。
    /// </summary>
    private static readonly float LaneX = StageX + (StageWidth - LaneWidth) * 0.5f;

    /// <summary>
    /// 击打区（事件坐标口径）：试打台全宽 = 设计稿的舞台带矩形 (460, 140, 760×781)。
    /// 它的右缘 1220 &lt; 统计板 x=1250、下缘 921 &lt; 底部条带 y=956、上缘 140 &gt; 头部 120，
    /// 所以"扣除右侧面板、底部条带、头部"是这块矩形自带的性质，不必额外裁剪；
    /// 返回键/步进器/结束态按钮都落在这块矩形之外，输入也不会互相抢。
    /// </summary>
    private static Rect2 TapZone => new(StageX, LaneTop, StageWidth, LaneBandHeight);

    /// <summary>note 顶边停在判定线上方的位置（设计稿的 JUDGE_Y − 36）。</summary>
    private static float NoteAtLineY => JudgeLineY - NoteHeight;

    // ---- 时钟：与 SongClock 同一套修正链，只是不叠加 UserOffsetMs ----

    /// <summary>
    /// 判定用 song position（秒）。窗口播放时取
    /// <c>player.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() − AudioServer.GetOutputLatency()</c>，
    /// 与 <see cref="SongClock.GetSongTime"/> 的窗口分支逐项相同——校准页测的就是「除 userOffset 以外
    /// 的全部修正是否被玩家充分抵消」，因此这里**不能**叠加任何用户偏移。headless（无音频设备）时
    /// 与 SongClock 一样回退到系统计时器，保证逻辑可运行。
    /// </summary>
    private double SongPositionSeconds()
    {
        // headless（Dummy 音频驱动）时 SongClock 自己也在用系统计时器回退，直接取它，
        // 两边不会各自漂移；UserOffsetMs 恒为 0，所以这个值就是"不含用户偏移的 song position"。
        if (_clock.ManualFallback)
            return _clock.GetSongTime();
        // 有音频设备但音源没建起来（理论上只在资源缺失时发生）：退回系统计时器，保证页面仍可玩。
        if (!_audioReady)
            return (Time.GetTicksMsec() - _fallbackStartMsec) / 1000.0;
        // 音源是 2s 循环流：GetPlaybackPosition 每圈回卷到 0，直接当 song position 会让
        // 小节序号永远不前进（热身永不结束、采样计数恒为 0），必须先展开成单调时间轴。
        return _loopUnwrap.Advance(_beatPlayer.GetPlaybackPosition()
               + AudioServer.GetTimeSinceLastMix()
               - AudioServer.GetOutputLatency());
    }

    /// <summary>循环内小节头的最近过线时刻（秒）。每 <see cref="BarSeconds"/> 一个，从 0 起。</summary>
    private static double NearestBarHead(double songSec) =>
        Math.Round(songSec / BarSeconds) * BarSeconds;

    /// <summary>
    /// 击打偏差（毫秒，正 = 晚）：把击打时刻的 song position 与最近的过线时刻相减。
    /// 建议偏移的口径见 <see cref="CalibrationStats.SuggestedOffsetMs"/>：当前值 − 中位数。
    /// </summary>
    private static double DeviationMs(double songSec) =>
        (songSec - NearestBarHead(songSec)) * 1000.0;

    /// <summary>已过线的 note 数：小节头的序号 + 1。</summary>
    private int PassedNoteCount(double songSec)
    {
        var count = (int)Math.Floor(songSec / BarSeconds) + 1;
        return Math.Max(0, count);
    }

    /// <summary>
    /// 当前所有 in-flight note 的下落进度（0 = 刚从轨道顶生成，1 = 顶边压在判定线上）。
    /// 第 N 个小节头（songSec = N · 2s）是一颗 note 的过线时刻，它从
    /// <c>N·2s − fallSeconds</c> 起下落。可见窗口是 head ∈ [songSec, songSec + fall]
    /// （raw ∈ [0,1]），落速慢时窗口跨过多个小节头，在轨 note 因此可能不止一颗。
    /// 返回列表按小节顺序（最老的在最前），绘制顺序即由上到下。
    /// </summary>
    private static IReadOnlyList<float> NoteProgresses(double songSec, double fallSeconds)
    {
        var fall = Math.Max(0.05, fallSeconds);
        var progress = new List<float>();
        var firstBar = Math.Max(0, (int)Math.Ceiling(songSec / BarSeconds));
        var lastBar = (int)Math.Floor((songSec + fall) / BarSeconds);
        for (var bar = firstBar; bar <= lastBar; bar++)
        {
            var head = bar * BarSeconds;
            var raw = (songSec - (head - fall)) / fall;
            if (raw is < 0.0 or > 1.0)
                continue;
            progress.Add((float)raw);
        }

        return progress;
    }

    // ---- 合成节拍音源 ----

    private ulong _fallbackStartMsec;

    /// <summary>
    /// 程序合成 2 秒单声道循环（44100Hz / 16bit）：一小节 4 个 click（0/500/1000/1500ms），
    /// 小节头的 2000Hz 更重。2000ms × 44100 = 88200 采样点整，循环点无接缝。
    /// </summary>
    private void StartBeatLoop()
    {
        _beatPlayer = new AudioStreamPlayer
        {
            Name = "CalibrationBeat",
            Bus = "Music",
            Stream = BuildBeatStream(),
        };
        AddChild(_beatPlayer);

        _clock = new SongClock { Name = "CalibrationClock" };
        AddChild(_clock);
        _clock.Attach(_beatPlayer);
        // 校准必须测出玩家侧的偏差，时钟本身不能再叠加任何用户偏移。
        _clock.UserOffsetMs = 0.0;

        _clock.Play(0.0);
        _audioReady = _beatPlayer.Stream is not null && !_clock.ManualFallback;
        _fallbackStartMsec = Time.GetTicksMsec();

        if (_clock.ManualFallback)
            GD.PushWarning("CalibrationScreen: 无音频设备，节拍与计时改用系统计时器回退时钟。");
    }

    private static AudioStreamWav BuildBeatStream()
    {
        const int mixRate = 44100;
        var barFrames = (int)Math.Round(BarSeconds * mixRate); // 88200
        // 数据末尾多垫 1 个静音帧：引擎混音在循环边界的分块会读到 loop_end 这一帧
        // （aux = (limit − offset) + 1 含端点），loop_end == 帧数时越界读 2 字节——
        // 桌面靠分配器冗余掩盖，Android 上越过页边界直接 SIGSEGV（真机已复现，
        // 崩溃时机 = 首个循环回卷 = 第一颗 note 过线）。
        var frameCount = barFrames + 1;
        var data = new byte[frameCount * 2];

        foreach (var offsetMs in ClickOffsetsMs)
        {
            var start = (int)Math.Round(offsetMs / 1000.0 * mixRate);
            var isBarHead = offsetMs == 0.0;
            var frequency = isBarHead ? BarClickFrequencyHz : BeatClickFrequencyHz;
            var gain = isBarHead ? BarClickGain : BeatClickGain;
            var decaySamples = ClickDecaySamples(mixRate);
            for (var i = 0; i < decaySamples && start + i < frameCount; i++)
            {
                var t = (double)i / mixRate;
                var envelope = Math.Exp(-t / BeatClickDecaySeconds);
                var value = Math.Sin(2.0 * Math.PI * frequency * t) * envelope * gain;
                var sample = (short)Math.Clamp(Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
                var byteIndex = (start + i) * 2;
                data[byteIndex] = (byte)(sample & 0xFF);
                data[byteIndex + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = mixRate,
            Stereo = false,
            Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            // 循环内容 = [0, barFrames)，垫帧不属于循环；周期仍是整 88200 采样点。
            LoopEnd = barFrames,
        };
    }

    /// <summary>指数衰减到 −60dB 所需的采样数（再长就听不见了，不必写满整段）。</summary>
    private static int ClickDecaySamples(int mixRate) =>
        (int)Math.Ceiling(BeatClickDecaySeconds * 6.0 * mixRate);

    public override void _ExitTree()
    {
        if (IsInstanceValid(_clock))
            _clock.Pause();
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

        var songSec = SongPositionSeconds();
        if (_state == PhaseState.Ready)
            EnterSampling(songSec);

        RegisterHit(songSec);
        return true;
    }

    private void RegisterHit(double songSec)
    {
        // Complete 是冻结帧：击打不再产生任何读数（事件仍被消费，不穿透到别的层）。
        if (_state == PhaseState.Complete)
            return;

        var deviation = DeviationMs(songSec);
        // 热身判定用偏差**归属**的那颗 note（最近小节头），而不是「当前小节」——
        // 提前 ≤150ms 的击打属于下一颗 note，否则提前量会把它误记成前一颗的热身。
        // 热身 = 进入 Sampling 后最先过线的 2 颗 note（进入当下那颗算第 1 颗）。
        var attributedNote = (int)Math.Round(songSec / BarSeconds) + 1;
        var warmup = attributedNote <= _samplingBaseNote + WarmupNotes - 1;

        if (_state != PhaseState.Sampling || warmup)
        {
            // 热身/等待态：读数灰显，不入样。|偏差| 超窗也只显示 —（stray tap）。
            if (Math.Abs(deviation) > CalibrationStats.MaxAcceptedDeviationMs)
            {
                ShowStrayReadout();
                return;
            }
            _lastHitMs = deviation;
            _hasLastHit = true;
            _lastHitKind = HitReadout.Warmup;
            BeginFadeIn(_hitReadout);
            RefreshReadouts();
            return;
        }

        // 每颗 note 至多贡献一个样本：对同一颗 note 连击（±150ms 内的第二次击打）不算独立观测，
        // 不入样但按 Accepted 口径刷新 LAST HIT 读数（读数反映真实偏差，只是不计入统计）。
        if (attributedNote == _lastSampledNote)
        {
            _lastHitMs = deviation;
            _hasLastHit = true;
            _lastHitKind = HitReadout.Accepted;
            BeginFadeIn(_hitReadout);
        }
        else if (_stats.TryAdd(deviation))
        {
            _lastSampledNote = attributedNote;
            _lastHitMs = deviation;
            _hasLastHit = true;
            _lastHitKind = HitReadout.Accepted;
            BeginFadeIn(_hitReadout);
            if (_stats.Count >= _targetSamples)
                CompleteSampling();
        }
        else
        {
            ShowStrayReadout();
        }

        RefreshReadouts();
    }

    private void ShowStrayReadout()
    {
        _hasLastHit = true;
        _lastHitKind = HitReadout.Stray;
        _strayReadoutRemaining = 1.0;
        _lastHitMs = 0.0;
        BeginFadeIn(_hitReadout);
        RefreshReadouts();
    }

    // ---- 状态机 ----

    private void EnterSampling(double songSec)
    {
        if (_state != PhaseState.Ready)
            return;
        // 基准取"已经过线"的那颗：它与其后一颗是热身，采样从第 3 颗 note 起。
        _samplingBaseNote = PassedNoteCount(songSec);
        _lastSampledNote = -1;
        _state = PhaseState.Sampling;
        ApplyPhase();
    }

    private void CompleteSampling()
    {
        if (_state == PhaseState.Complete)
            return;
        _state = PhaseState.Complete;
        ApplyPhase();
    }

    /// <summary>离开页面前把目标样本数落盘（与设置页的写盘节奏一致：改动即写）。</summary>
    private void PersistTargetSamples()
    {
        GameSession.Settings.CalibrationTargetSamples = _targetSamples;
        GameSession.Settings.Save();
    }

    /// <summary>再测一次：清空样本与读数回到 Ready，保留目标样本数。</summary>
    private void RestartSampling()
    {
        if (_navigating)
            return;
        _stats.Clear();
        _samplingBaseNote = 0;
        _lastSampledNote = -1;
        _hasLastHit = false;
        _lastHitKind = HitReadout.None;
        _strayReadoutRemaining = 0.0;
        _completeElapsed = 0.0;
        _state = PhaseState.Ready;
        ApplyPhase();
    }

    /// <summary>应用建议偏移：当前值 − 中位数 → 5ms 步进 → ±300 钳制（<see cref="CalibrationStats.SuggestedOffsetMs"/>），保存后返回设置页。</summary>
    private void ApplySuggestedOffset()
    {
        if (_navigating || _state != PhaseState.Complete)
            return;
        if (_stats.SuggestedOffset(GameSession.Settings.TimingOffsetMs) is not { } suggested)
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
        _targetMinus.Disabled = !enabled || _state != PhaseState.Ready;
        _targetPlus.Disabled = !enabled || _state != PhaseState.Ready;
    }

    private void AdjustTargetSamples(int direction)
    {
        // 采样途中步进器锁定（用户拍板：不给中途改目标），只有 Ready 能调。
        if (_state != PhaseState.Ready || _navigating)
            return;
        var next = CalibrationStats.NormalizeTargetSamples(
            _targetSamples + direction * CalibrationStats.TargetSampleStep);
        if (next == _targetSamples)
            return;
        _targetSamples = next;
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
        var complete = _state == PhaseState.Complete;
        // 这些量不参与淡换：压暗目标、条带数据、门控状态立即生效。
        _laneDimTarget = complete ? FrozenLaneOpacity : 1.0;
        _completeElapsed = 0.0;
        _medianFade = complete ? 0f : 1f;
        _strip.Frozen = complete;
        _strip.Samples = _stats.Samples;
        _strip.MedianMs = _stats.MedianMs;
        _strip.MedianAlpha = _medianFade;
        // 文字键（放弃）与步进器的门控：只有"应用"路径需要临时禁用。
        SetResultActionsEnabled(!_navigating);
        // 步进器只有 Ready 能调：Sampling 锁定、Complete 交给确认面板。
        _targetMinus.Disabled = _state != PhaseState.Ready || _navigating;
        _targetPlus.Disabled = _state != PhaseState.Ready || _navigating;

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
        _visualState == PhaseState.Complete ? _resultContent : _statsContent,
    ];

    private static void SetGroupAlpha(IEnumerable<CanvasItem> group, float alpha)
    {
        foreach (var item in group)
            item.Modulate = new Color(1f, 1f, 1f, alpha);
    }

    /// <summary>把当前状态落进可视：相位文案、进度分段、两块板的可见性与轨道读数。</summary>
    private void CommitPhaseVisuals()
    {
        var (tag, title, guide) = _state switch
        {
            PhaseState.Complete => ("PHASE 02 / 02", "APPLY · 应用",
                "确认建议偏移后立即生效，也可以再测一次"),
            _ => ("PHASE 01 / 02", "TAP · 跟拍", "音符压线的瞬间，击打屏幕任意位置"),
        };
        _phaseTag.Text = tag;
        _phaseTitle.Text = title;
        _phaseGuide.Text = guide;

        var complete = _state == PhaseState.Complete;
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
        _pulseInstruction.Visible = _state == PhaseState.Ready;
        _visualState = _state;
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
        var frozen = _state == PhaseState.Complete;
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
                _hasLastHit = false;
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
        var currentOffset = GameSession.Settings.TimingOffsetMs;
        _currentOffsetValue.Text = FormatMilliseconds(currentOffset);

        _targetValue.Text = _targetSamples.ToString();

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

        _medianValue.Text = _stats.MedianMs is { } median
            ? FormatSignedMilliseconds(median)
            : "—";
        _spreadValue.Text = _stats.SpreadMs is { } spread
            ? $"±{Math.Round(spread):0} ms"
            : "—";

        _progressCount.Text = $"{_stats.Count} / {_targetSamples}";
        var ratio = Math.Clamp(_stats.Count / (float)_targetSamples, 0f, 1f);
        _progressFill.Size = new Vector2(ProgressTrackWidth * ratio, ProgressTrackHeight);

        _suggestedValue.Text = _stats.SuggestedOffset(currentOffset) is { } suggested
            ? FormatMilliseconds(suggested)
            : "—";

        _resultSuggestedValue.Text = _suggestedValue.Text;
        _resultSuggestedSub.Text = _stats.Count > 0
            ? $"基于 {_stats.Count} 个有效样本 · 离散 {_spreadValue.Text}"
            : "尚无有效样本";
        _resultDelta.Text = _stats.SuggestedOffset(currentOffset) is { } finalOffset
            ? $"当前 {FormatMilliseconds(currentOffset)} → 应用后 {FormatMilliseconds(finalOffset)}"
            : "—";

        // 每次读数刷新都重画条带：新样本落 tick、最新一个 tick 放大、中位数标记移动。
        _strip.Samples = _stats.Samples;
        _strip.MedianMs = _stats.MedianMs;
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

    // ---- 小工具 ----

    private static Label AddLabel(Node parent, float x, float y, float width, float height,
        string text, int fontSize, Color color, bool bold = false,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Position = new Vector2(x, y),
            Size = new Vector2(width, height),
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // 混排中英的标签统一走中文回退字体（与 ResultScreen 同一判据）。
        if (text.Any(c => c > 0x2E80))
            label.AddThemeFontOverride("font", UiFonts.Cjk);
        else if (bold)
            label.AddThemeFontOverride("font", UiFonts.TechBold);
        else
            label.AddThemeFontOverride("font", UiFonts.Tech);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>
    /// 单轨试打台：轨道带 + 两条水平参考线 + 判定线 + 下落 note + 拍点脉冲环。
    /// 判定线 y 与轨道中轴都来自游玩常量；下落时长由玩家落速与游玩落速公式推导，
    /// 保证试打台的视觉条件与真实打歌一致。
    /// </summary>
    private partial class CalibrationStage : Control
    {
        public float LaneX { get; init; }
        public float LaneWidth { get; init; }
        public float LaneTop { get; init; }
        public float LaneBandHeight { get; init; }
        public float JudgeY { get; init; }
        public float Horizon1Y { get; init; }
        public float Horizon2Y { get; init; }
        public float StageX { get; init; }
        public float StageWidth { get; init; }

        /// <summary>
        /// 当前在下落的 note 们的压线进度（0 = 刚从轨道顶生成，1 = 顶边压在判定线上）。
        /// 每 <see cref="BarSeconds"/> 一颗；落速慢时下落时长会超过一小节，因此同时在轨的
        /// note 可能不止一颗，这里把所有 in-flight 的都算出来（设计稿的静态帧只有一颗，
        /// 那是"下落沿途三帧站位"，运动版按真实几何来）。
        /// </summary>
        public IReadOnlyList<float> NoteProgresses { get; set; } = [];

        /// <summary>Complete 后的冻结帧：只有压线那一颗，压暗显示。</summary>
        public bool Frozen { get; set; }

        /// <summary>1 = 无压暗，0.35 = Complete 冻结压暗。</summary>
        public float LaneDim { get; set; } = 1f;

        /// <summary>拍点脉冲强度 0..1（小节头最强）。</summary>
        public float PulseAmount { get; set; }

        /// <summary>
        /// note 从轨道顶部下落到判定线的时长（秒）。取游玩同款公式：
        /// <c>距离 = 音频秒数 × 流速 × 玩家落速 × BaseFallSpeedPx</c>，其中流速取 1.0
        /// （校准页没有谱面变速），玩家落速 = <c>FallSpeedLevel / 10</c>。
        /// 所以时长 = 行程 / (BaseFallSpeedPx × level/10)；行程取游玩中轨的 790px。
        /// </summary>
        public float FallSeconds
        {
            get
            {
                var player = Math.Max(0.1, GameSession.Settings.FallSpeedMultiplier);
                var speed = GameplayVisualMapper.BaseFallSpeedPx * player;
                return (float)(GameplayStageGeometry.CenterPreviewTravelPx / speed);
            }
        }

        public Rect2 LaneRect => new(LaneX, LaneTop, LaneWidth, LaneBandHeight);

        public override void _Draw()
        {
            var dim = LaneDim;

            // 轨道带 + 两侧发光线
            DrawRect(new Rect2(LaneX, LaneTop, LaneWidth, LaneBandHeight),
                new Color(UiFonts.Cyan, 0.07f * dim));
            DrawRect(new Rect2(LaneX, LaneTop, 1f, LaneBandHeight),
                new Color(UiFonts.Cyan, 0.25f * dim));
            DrawRect(new Rect2(LaneX + LaneWidth - 1f, LaneTop, 1f, LaneBandHeight),
                new Color(UiFonts.Cyan, 0.25f * dim));

            // 两条水平参考线（不含判定线本身）
            DrawRect(new Rect2(StageX, Horizon1Y, StageWidth, 2f),
                new Color(UiFonts.Line, 0.5f * dim));
            DrawRect(new Rect2(StageX, Horizon2Y, StageWidth, 2f),
                new Color(UiFonts.Line, 0.5f * dim));

            // 拍点脉冲环：判定线中心，内环强、外环弱
            if (PulseAmount > 0.001f)
            {
                var center = new Vector2(LaneX + LaneWidth * 0.5f, JudgeY);
                var inner = new Color(UiFonts.Cyan, 0.55f * PulseAmount);
                var outer = new Color(UiFonts.Cyan, 0.22f * PulseAmount);
                DrawArc(center, PulseInnerRadius, 0f, Mathf.Tau, 48, inner, 2f, true);
                DrawArc(center, PulseOuterRadius, 0f, Mathf.Tau, 48, outer, 1.5f, true);
            }

            DrawNotes();

            // 判定线：全宽细线 + 轨道内亮芯
            DrawRect(new Rect2(StageX, JudgeY, StageWidth, 3f), UiFonts.Line);
            DrawRect(new Rect2(LaneX, JudgeY - 2f, LaneWidth, 6f),
                new Color(UiFonts.Cyan, 0.9f * dim));
        }

        private void DrawNotes()
        {
            foreach (var progress in Frozen ? (IReadOnlyList<float>)[1f] : NoteProgresses)
                DrawNote(progress);
        }

        private void DrawNote(float progress)
        {
            var laneCenter = LaneX + LaneWidth * 0.5f;
            var startY = LaneTop - NoteHeight * 0.5f;
            var endY = JudgeY - NoteHeight;
            var y = Mathf.Lerp(startY, endY, progress);

            // 压线帧加辉光：用两圈由外到内的高透明度描边近似设计稿的 drop-shadow。
            var glow = progress > 0.72f ? Mathf.InverseLerp(0.72f, 1f, progress) : 0f;
            var points = UiGeometry.CutCorners(
                laneCenter - NoteWidth * 0.5f, y, NoteWidth, NoteHeight, NoteCut);
            if (glow > 0.01f)
            {
                var haloColor = new Color(UiFonts.Cyan, 0.22f * glow * LaneDim);
                DrawPolyline(UiGeometry.Close(points), haloColor, 14f, true);
                DrawPolyline(UiGeometry.Close(points), new Color(UiFonts.Cyan, 0.16f * glow * LaneDim),
                    26f, true);
            }

            DrawColoredPolygon(points, new Color(UiFonts.Cyan, (0.5f + 0.5f * glow) * LaneDim));
        }
    }

    /// <summary>
    /// 偏差条带：±150ms 映射到 ±726px，早粉晚青；最新一个 tick 放大（Complete 后定格），
    /// Complete 时再叠一条 MEDIAN 标记线。
    /// </summary>
    private partial class CalibrationStrip : Control
    {
        public float Cut { get; init; } = 14f;
        public float AxisX { get; init; }
        public float AxisWidth { get; init; }
        public float AxisCenterY { get; init; }
        public float LabelX { get; init; }
        public float LabelY { get; init; }
        public float AxisLabelY { get; init; }
        public IReadOnlyList<double> Samples { get; set; } = [];
        public double? MedianMs { get; set; }
        public bool Frozen { get; set; }
        /// <summary>0 = 完全透明，1 = 完整显示（MEDIAN 线淡入用）。</summary>
        public float MedianAlpha { get; set; } = 1f;

        private static float MsToPx => 726f / (float)CalibrationStats.MaxAcceptedDeviationMs;

        /// <summary>Complete 后 MEDIAN 标记线的高度（设计稿 44px）。</summary>
        private const float MedianMarkerHeight = 44f;

        public override void _Draw()
        {
            var points = UiGeometry.CutCorners(Size, Cut);
            DrawColoredPolygon(points, new Color(new Color("0f1429"), 0.85f));
            DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 2f, true);

            DrawString(UiFonts.Cjk, new Vector2(LabelX, LabelY + 16f), "DEVIATION MAP · 偏差分布",
                HorizontalAlignment.Left, -1, 13, UiFonts.Dim);

            var mid = AxisX + AxisWidth * 0.5f;
            DrawRect(new Rect2(AxisX, AxisCenterY, AxisWidth, 1.5f), UiFonts.Line);
            DrawRect(new Rect2(mid - 1f, AxisCenterY - 14f, 2f, 28f), UiFonts.Cyan);
            DrawString(UiFonts.Tech, new Vector2(AxisX, AxisCenterY + AxisLabelY + 14f), "-150",
                HorizontalAlignment.Left, -1, 12, UiFonts.Dim);
            DrawString(UiFonts.Tech, new Vector2(mid - 20f, AxisCenterY + AxisLabelY + 14f), "0",
                HorizontalAlignment.Center, 40f, 12, UiFonts.Dim);
            DrawString(UiFonts.Tech, new Vector2(AxisX + AxisWidth - 80f, AxisCenterY + AxisLabelY + 14f),
                "+150", HorizontalAlignment.Right, 80f, 12, UiFonts.Dim);

            for (var i = 0; i < Samples.Count; i++)
            {
                var value = Samples[i];
                var latest = !Frozen && i == Samples.Count - 1;
                var width = latest ? 6f : 4f;
                var height = latest ? 32f : 24f;
                var color = value < 0 ? UiFonts.Pink : UiFonts.Cyan;
                var x = mid + (float)value * MsToPx - width * 0.5f;
                DrawRect(new Rect2(x, AxisCenterY - height * 0.5f, width, height),
                    new Color(color, latest ? 1f : 0.8f));
            }

            if (Frozen && MedianMs is { } median && MedianAlpha > 0.001f)
            {
                var mx = mid + (float)median * MsToPx;
                var color = new Color(UiFonts.Cyan, MedianAlpha);
                DrawRect(new Rect2(mx - 1f, AxisCenterY - MedianMarkerHeight * 0.5f,
                    2f, MedianMarkerHeight), color);
                var label = $"MEDIAN {(median < 0 ? "-" : "+")}{Math.Round(Math.Abs(median)):0} ms";
                DrawString(UiFonts.Tech, new Vector2(mx + 10f, AxisCenterY + AxisLabelY + 14f),
                    label, HorizontalAlignment.Left, 200f, 12, color);
            }
        }

    }

    /// <summary>
    /// LAST HIT 毫秒读数的淡入载体：只承载透明度推进，文字由页面持有（灰显/彩色由页面决定）。
    /// 沿用 <see cref="UiHoverFade"/> 的两个独立条件：值变了才重绘、到终值才停表。
    /// </summary>
    private partial class CalibrationHitReadout : Control
    {
        private float _fade;
        private double _elapsed;
        private bool _animating;

        public float Fade => _fade;
        public bool Settled => !_animating;

        public override void _Ready()
        {
            _fade = 1f;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public void BeginFade(UiMotionProfile profile)
        {
            if (!profile.IsAnimated)
            {
                _fade = 1f;
                _animating = false;
                return;
            }

            _fade = 0f;
            _elapsed = 0.0;
            _animating = true;
        }

        /// <summary>推进淡入；返回 true 表示本帧值有变化（调用方据此重画文字 alpha）。</summary>
        public bool AdvanceFade(double delta, UiMotionProfile profile)
        {
            if (!_animating)
                return false;
            if (!profile.IsAnimated)
            {
                _fade = 1f;
                _animating = false;
                return true;
            }

            _elapsed += delta;
            var raw = Math.Clamp(_elapsed / profile.HoverDuration, 0.0, 1.0);
            var next = UiEase.Standard((float)raw);
            var changed = next != _fade;
            _fade = next;
            if (raw >= 1.0)
            {
                _animating = false;
                _fade = 1f;
            }
            return changed;
        }
    }

    /// <summary>统计板里的 ± 步进器：40×40 切角块，hover 走共享的 0.10s 过渡。</summary>
    private partial class StepperButton : Control
    {
        private bool _disabled;
        private bool _hover;
        private UiHoverFade _hoverFade;

        [Signal]
        public delegate void PressedEventHandler();

        public float Cut { get; init; } = 10f;
        public string Glyph { get; set; } = "+";

        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value)
                    return;
                _disabled = value;
                if (!MotionProfile.IsAnimated)
                    _hoverFade.Snap(false);
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => SetHover(true);
            MouseExited += () => SetHover(false);
            SetProcess(false);
        }

        public override void _Process(double delta)
        {
            if (_hoverFade.Update(_hover && !_disabled, delta))
                QueueRedraw();
            if (_hoverFade.Settled(_hover && !_disabled))
                SetProcess(false);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_disabled)
                return;
            var pressed = e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true };
            if (!pressed)
                return;
            EmitSignal(SignalName.Pressed);
            AcceptEvent();
        }

        private static UiMotionProfile MotionProfile =>
            UiMotionProfile.For(GameSession.Settings.MotionMode);

        private void SetHover(bool hover)
        {
            if (_disabled)
                hover = false;
            if (_hover == hover)
                return;
            _hover = hover;
            if (!MotionProfile.IsAnimated)
                _hoverFade.Snap(hover);
            SetProcess(true);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var hover = UiEase.Standard(MotionProfile.IsAnimated
                ? _hoverFade.Value
                : (_hover && !_disabled ? 1f : 0f));
            var border = UiFonts.Line.Lerp(UiFonts.Cyan, hover);
            var fill = new Color(new Color("0f1429"), 0.85f)
                .Lerp(UiFonts.PanelHover, hover);
            var glyphColor = (_disabled ? UiFonts.Dim : UiFonts.Cyan).Lerp(Colors.White, hover * 0.4f);

            var points = UiGeometry.CutCorners(Size, Cut);
            DrawColoredPolygon(points, fill);
            DrawPolyline(UiGeometry.Close(points), border, 2f, true);
            DrawString(UiFonts.Tech, new Vector2(0f, Size.Y * 0.5f + 8f), Glyph,
                HorizontalAlignment.Center, Size.X, 22, glyphColor);
        }
    }

    /// <summary>hover 文字键（放弃并返回）：共享 <see cref="UiHoverFade"/> 的 0.10s 过渡。</summary>
    private partial class HoverLabel : Control
    {
        private bool _hover;
        private bool _disabled;
        private UiHoverFade _hoverFade;

        [Signal]
        public delegate void PressedEventHandler();

        public string Text { get; set; } = "";
        public int FontSize { get; init; } = 15;
        public Color BaseColor { get; init; } = UiFonts.Dim;
        public Color HoverColor { get; init; } = UiFonts.Text;
        public HorizontalAlignment Alignment { get; init; } = HorizontalAlignment.Center;

        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value)
                    return;
                _disabled = value;
                if (!MotionProfile.IsAnimated)
                    _hoverFade.Snap(false);
                QueueRedraw();
            }
        }

        public override void _Ready()
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += () => SetHover(true);
            MouseExited += () => SetHover(false);
            SetProcess(false);
        }

        public override void _Process(double delta)
        {
            if (_hoverFade.Update(_hover && !_disabled, delta))
                QueueRedraw();
            if (_hoverFade.Settled(_hover && !_disabled))
                SetProcess(false);
        }

        public override void _GuiInput(InputEvent e)
        {
            if (_disabled)
                return;
            var pressed = e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or
                InputEventScreenTouch { Pressed: true };
            if (!pressed)
                return;
            EmitSignal(SignalName.Pressed);
            AcceptEvent();
        }

        private static UiMotionProfile MotionProfile =>
            UiMotionProfile.For(GameSession.Settings.MotionMode);

        private void SetHover(bool hover)
        {
            if (_disabled)
                hover = false;
            if (_hover == hover)
                return;
            _hover = hover;
            if (!MotionProfile.IsAnimated)
                _hoverFade.Snap(hover);
            SetProcess(true);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var amount = UiEase.Standard(MotionProfile.IsAnimated
                ? _hoverFade.Value
                : (_hover && !_disabled ? 1f : 0f));
            var color = BaseColor.Lerp(_disabled ? UiFonts.Dim : HoverColor, amount);
            DrawString(UiFonts.Cjk, new Vector2(0f, Size.Y * 0.5f + FontSize * 0.35f), Text,
                Alignment, Size.X, FontSize, color);
        }
    }
}

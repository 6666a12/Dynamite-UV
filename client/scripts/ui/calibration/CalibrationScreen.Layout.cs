using Godot;
using DynamiteUniverse.Audio;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;

public partial class CalibrationScreen
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
    private const float NoteHeight = CalibrationStage.NoteHeight;
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

    private Node2D AddGroup(string name)
    {
        var node = new Node2D { Name = name };
        AddChild(node);
        return node;
    }

    // ---- 头部 ----

    private void BuildHeader()
    {
        var back = new CutButton
        {
            LowBorder = true,
            Position = new Vector2(48f, 28f),
            Size = new Vector2(76f, 76f),
            Cut = 12f,
            Text = "‹",
            BackChevron = true,
            FontSize = 44,
            Accent = UiFonts.Cyan,
        };
        back.Pressed += NavigateBack;
        _headerRoot.AddChild(back);
        RegisterEntrance(back, EnterGroup.Header);

        var title = AddLabel(_headerRoot, 152f, 30f, 520f, 44f, "CALIBRATION", 36,
            MenuStyle.Text, bold: true, HorizontalAlignment.Left);
        MenuStyle.Heading(title);
        RegisterEntrance(title, EnterGroup.Header);

        var subtitle = AddLabel(_headerRoot, 154f, 80f, 700f, 26f, "JUDGE OFFSET · 判定偏移校准", 16,
            MenuStyle.Secondary);
        RegisterEntrance(subtitle, EnterGroup.Header);

        var tempo = AddLabel(_headerRoot, 1330f, 46f, 520f, 40f, "120 BPM · 4/4 · 1 NOTE / BAR", 13,
            MenuStyle.Secondary, align: HorizontalAlignment.Right);
        RegisterEntrance(tempo, EnterGroup.Header);
    }

    // ---- 左列相位块（无面板底板）----

    private void BuildPhaseBlock()
    {
        _phaseTag = AddLabel(_phaseRoot, PhaseX, PhaseTagY, 340f, 22f, "PHASE 01 / 02", 13, MenuStyle.Secondary);
        _phaseTitle = AddLabel(_phaseRoot, PhaseX, PhaseTitleY, 380f, 52f, "TAP · 跟拍", 36,
            UiFonts.Cyan, bold: true);
        _phaseGuide = AddLabel(_phaseRoot, PhaseX, PhaseGuideY, 340f, 56f,
            "音符压线的瞬间，击打屏幕任意位置", 15, MenuStyle.Secondary);

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

        AddLabel(_phaseRoot, PhaseX, CurrentLabelY, 340f, 22f, "当前偏移 · CURRENT", 13, MenuStyle.Secondary);
        _currentOffsetValue = AddLabel(_phaseRoot, PhaseX, CurrentValueY, 340f, 44f, "+0 ms", 30,
            MenuStyle.Text, bold: true);
        AddLabel(_phaseRoot, PhaseX, TipWarmupY, 340f, 24f, "前 2 拍为热身，不计入统计", 13, MenuStyle.Secondary);
        AddLabel(_phaseRoot, PhaseX, TipScopeY, 340f, 24f, "校准结果只影响判定时机，不改变谱面", 13, MenuStyle.Secondary);
        AddLabel(_phaseRoot, PhaseX, TipExitY, 340f, 24f, "左上角返回可随时退出，不写入任何设置", 12, MenuStyle.Secondary);

        foreach (var child in _phaseRoot.GetChildren())
            if (child is Control control)
                RegisterEntrance(control, EnterGroup.PhaseBlock);
    }

    // ---- 中央单轨试打台 ----

    private void BuildStage()
    {
        _stage = new CalibrationStage
        {
            FallSpeedMultiplier = () => GameSession.Settings.FallSpeedMultiplier,
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

        AddLabel(_hitReadout, LastHitLabelX, LastHitLabelY, 190f, 22f, "LAST HIT", 14, MenuStyle.Secondary);
        _lastHitValue = AddLabel(_hitReadout, LastHitValueX, LastHitValueY, 130f, 68f, "—", 56,
            MenuStyle.Secondary, bold: true);
        _lastHitUnit = AddLabel(_hitReadout, LastHitUnitX, LastHitUnitY, 60f, 30f, "ms", 20, MenuStyle.Secondary);
        _earlyLateLegend = AddLabel(_hitReadout, StageX, EarlyLateLegendY, StageWidth, 20f,
            "EARLY − / LATE +", 12, MenuStyle.Secondary, align: HorizontalAlignment.Center);

        _frozenTag = AddLabel(_stageRoot, StageX, FrozenTagY, StageWidth, 24f,
            "采样完成 · SIGNAL FROZEN", 13, MenuStyle.Secondary, align: HorizontalAlignment.Center);
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
            Fill = Colors.Transparent,
            Border = UiFonts.Line,
            BorderWidth = 0f,
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
            "信号分析 · SIGNAL ANALYSIS", 14, MenuStyle.Secondary);
        AddDivider(_statsContent, 236f);

        AddLabel(_statsContent, PanelInnerX, 278f, 340f, 32f, "目标样本 · TARGET", 18, MenuStyle.Text);
        _targetMinus = new StepperButton
        {
            MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
            Position = new Vector2(TargetMinusX, TargetRowY),
            Size = new Vector2(TargetControlSize, TargetControlSize),
            Cut = TargetControlCut,
            Glyph = "−",
        };
        _targetMinus.Pressed += () => AdjustTargetSamples(-1);
        _statsContent.AddChild(_targetMinus);

        _targetValue = AddLabel(_statsContent, TargetValueX, 268f, 102f, 44f, "16", 26,
            MenuStyle.Text, bold: true, HorizontalAlignment.Center);

        _targetPlus = new StepperButton
        {
            MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
            Position = new Vector2(TargetPlusX, TargetRowY),
            Size = new Vector2(TargetControlSize, TargetControlSize),
            Cut = TargetControlCut,
            Glyph = "+",
        };
        _targetPlus.Pressed += () => AdjustTargetSamples(1);
        _statsContent.AddChild(_targetPlus);
        AddLabel(_statsContent, PanelInnerX, TargetSubY, PanelInnerWidth, 20f,
            "8 – 32 · 每档 4 拍", 12, MenuStyle.Secondary);

        AddLabel(_statsContent, PanelInnerX, StatFirstRowY, 340f, 32f, "中位数 · MEDIAN", 18, MenuStyle.Text);
        _medianValue = AddLabel(_statsContent, 1556f, StatFirstRowY - 8f, 280f, 44f, "—", 28,
            UiFonts.Cyan, bold: true, HorizontalAlignment.Right);

        var spreadY = StatFirstRowY + StatRowPitch;
        AddLabel(_statsContent, PanelInnerX, spreadY, 340f, 32f, "离散度 · SPREAD", 18, MenuStyle.Text);
        _spreadValue = AddLabel(_statsContent, 1556f, spreadY - 8f, 280f, 44f, "—", 28,
            MenuStyle.Text, bold: true, HorizontalAlignment.Right);

        AddDivider(_statsContent, 556f);
        AddLabel(_statsContent, PanelInnerX, ProgressLabelY, 340f, 22f, "采样进度 · PROGRESS", 13, MenuStyle.Secondary);
        _progressCount = AddLabel(_statsContent, 1556f, ProgressLabelY - 4f, 280f, 26f, "0 / 16", 15,
            MenuStyle.Text, bold: true, HorizontalAlignment.Right);
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
            "建议偏移 · SUGGESTED OFFSET", 14, MenuStyle.Secondary);
        _suggestedValue = AddLabel(_statsContent, PanelInnerX, SuggestedValueY, PanelInnerWidth, 72f,
            "—", 64, UiFonts.Cyan, bold: true);
        AddLabel(_statsContent, PanelInnerX, SuggestedSubY, PanelInnerWidth, 22f,
            "随击打样本实时更新 · 打满目标后可应用", 13, MenuStyle.Secondary);

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
            Fill = Colors.Transparent,
            Border = UiFonts.Line,
            BorderWidth = 0f,
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
            "校准完成 · CALIBRATION COMPLETE", 14, MenuStyle.Secondary);
        AddDivider(_resultContent, ResultDividerY);
        AddLabel(_resultContent, PanelInnerX, ResultSuggestedLabelY, PanelInnerWidth, 22f,
            "建议偏移 · SUGGESTED OFFSET", 14, MenuStyle.Secondary);
        _resultSuggestedValue = AddLabel(_resultContent, PanelInnerX, ResultSuggestedValueY,
            PanelInnerWidth, 96f, "—", 88, UiFonts.Cyan, bold: true);
        _resultSuggestedSub = AddLabel(_resultContent, PanelInnerX, ResultSuggestedSubY,
            PanelInnerWidth, 24f, "—", 15, MenuStyle.Secondary);
        AddDivider(_resultContent, ResultDivider2Y);
        _resultDelta = AddLabel(_resultContent, PanelInnerX, ResultDeltaY, PanelInnerWidth, 28f,
            "—", 16, MenuStyle.Text);

        _applyButton = new CutButton
        {
            LowBorder = true,
            Position = new Vector2(PanelInnerX, ResultApplyY),
            Size = new Vector2(PanelInnerWidth, ResultButtonHeight),
            Cut = ResultButtonCut,
            Text = "应用偏移 · APPLY",
            StyleKind = CutButton.ButtonStyle.Solid,
            Accent = MenuStyle.Accent,
            TechFont = false,
            FontSize = 22,
        };
        _applyButton.Pressed += ApplySuggestedOffset;
        _resultContent.AddChild(_applyButton);

        _retryButton = new CutButton
        {
            LowBorder = true,
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
            MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
            Position = new Vector2(PanelInnerX, ResultDiscardY),
            Size = new Vector2(PanelInnerWidth, 44f),
            Text = "放弃并返回 · DISCARD",
            FontSize = 15,
            BaseColor = MenuStyle.Secondary,
            HoverColor = MenuStyle.Text,
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

    /// <summary>判定线 y：使用游玩共享基准 861；客户端游玩舞台另下移 40px，校准页布局不跟随。</summary>
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
}

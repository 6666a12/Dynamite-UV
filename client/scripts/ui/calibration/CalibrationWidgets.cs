using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;


/// <summary>
/// 单轨试打台：轨道带 + 两条水平参考线 + 判定线 + 下落 note + 拍点脉冲环。
/// 判定线 y 与轨道中轴都来自游玩常量；下落时长由玩家落速与游玩落速公式推导，
/// 保证试打台的视觉条件与真实打歌一致。
/// </summary>
internal partial class CalibrationStage : Control
{
    internal const float NoteWidth = 240f, NoteHeight = 36f, NoteCut = 10f;
    internal const float PulseInnerRadius = 34f, PulseOuterRadius = 52f;
    public required Func<double> FallSpeedMultiplier { private get; init; }

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
    /// 每 <see cref="CalibrationBeatSource.BarSeconds"/> 一颗；落速慢时下落时长会超过一小节，因此同时在轨的
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
    /// 所以时长 = 实际绘制行程 / (BaseFallSpeedPx × level/10)。
    /// </summary>
    public float FallSeconds
    {
        get
        {
            var player = Math.Max(0.1, FallSpeedMultiplier());
            var speed = GameplayVisualMapper.BaseFallSpeedPx * player;
            return (float)((JudgeY - LaneTop - NoteHeight * 0.5f) / speed);
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
internal partial class CalibrationStrip : Control
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
internal partial class CalibrationHitReadout : Control
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
internal partial class StepperButton : Control
{
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }

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

    private UiMotionProfile MotionProfile => MotionProfileProvider();

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
        var fill = _disabled ? new Color("0F1429", 0.85f) : MenuStyle.Idle.Lerp(MenuStyle.Active, hover);
        var glyphColor = (_disabled ? UiFonts.Dim : UiFonts.Cyan).Lerp(Colors.White, hover * 0.4f);

        var points = UiGeometry.CutCorners(Size, Cut);
        DrawColoredPolygon(points, fill);
        DrawString(UiFonts.Tech, new Vector2(0f, MenuStyle.Baseline(UiFonts.Tech, 22, Size.Y)), Glyph,
            HorizontalAlignment.Center, Size.X, 22, glyphColor);
    }
}

/// <summary>hover 文字键（放弃并返回）：共享 <see cref="UiHoverFade"/> 的 0.10s 过渡。</summary>
internal partial class HoverLabel : Control
{
    public required Func<UiMotionProfile> MotionProfileProvider { private get; init; }

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

    private UiMotionProfile MotionProfile => MotionProfileProvider();

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

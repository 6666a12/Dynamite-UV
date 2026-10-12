using Godot;
using System;
using System.Globalization;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>Editor — Settings 板（设计稿数值见 `.tmp/penpot_build4.py` 的 `settings_board()` 与
/// `seg_js` / `toggle_js` / `stepper_js` / `slider_js`）的自绘页。切角面板、分段器、步进器、
/// 开关与滑条都不是控件树能表达的形状，所以整页自绘，坐标与设计稿 1:1（切角只切左上 + 右下）。
///
/// 本层不推断能力：每行在 <see cref="Rows"/> 里声明是否已接入，未接入的行画 55% 且不进命中表。
/// 本层也不持有设置真源——值由 shell 以快照传入，交互发出事件，由 shell 落进 `EditorSettings`。</summary>
internal sealed partial class SettingsPageOverlay : Control
{
    /// <summary>三个真实滑条（GRID OPACITY / BACKGROUND DIM / SCROLL INERTIA）。占位滑条
    /// （PREVIEW VOLUME）不进命中表，所以不会发出事件。</summary>
    public const int GridOpacitySlider = 0, BackgroundDimSlider = 1, ScrollInertiaSlider = 2;

    private const float DisabledAlpha = .55f, HoverLift = .12f;

    // ---------------------------------------------------------------- 设计稿几何

    private const float TitleX = 112f, TitleCenterY = 88f;
    private static int TitleSize => EditorFonts.Pt(40);
    private const float UnderlineY = 122f, UnderlineWidth = 64f, UnderlineHeight = 3f;

    private const float NavX = 112f, NavTop = 210f, NavPitch = 56f;
    private const float NavWidth = 300f, NavHeight = 50f, NavCut = 8f, NavAccentWidth = 4f;
    private const float NavTextX = 136f;
    private static int NavTextSize => EditorFonts.Pt(15);

    private const float PanelX = 460f, PanelY = 210f, PanelWidth = 1348f, PanelCut = 16f, PanelStroke = 1.5f;
    private const float GroupLeft = 496f, GroupRight = 1772f;
    private const float HeaderOffsetY = 12f, RuleOffsetY = 26f, FirstRowOffsetY = 52f;
    /// <summary>行距：稿上的 52。曾经为「三组累进堆叠」的布局收过 48，2026-10-08 改成
    /// 「每组一块从头开始的独立面板」（<see cref="GroupTop"/>）后，最重的组也只有 5 行，
    /// 不再逼近底部限位，行距回到稿上原值。</summary>
    private const float RowPitch = 52f, RowHalfPitch = 26f;
    /// <summary>行标签的可读宽度上限：设计稿控件列最左缘是 1440，标签按真实字宽截断，不侵占控件列。</summary>
    private const float RowLabelWidth = 420f;
    private static int RowLabelSize => EditorFonts.Pt(20);
    /// <summary>面板高度按组累进的常数偏置：稿上是
    /// `底 = 组顶 + 行距×行数 + 56 − 204`，这里只保留 148（56 + 204 之外的常数项）。</summary>
    private const float PanelHeightBias = 148f;

    private const float SegmentWidth = 92f, SegmentHeight = 34f, SegmentGap = 4f, SegmentCut = 7f;
    /// <summary>分段块的最左缘：稿上三档行 x0 = 1772 − (3×92 + 2×4) = 1488。
    /// 段宽按段数从这块宽度反算、上限仍是稿上的 92（<see cref="SegmentWidthFor"/>），
    /// 所以 MOTION / DEFAULT GRID / EFFECTS 三行与稿上逐值一致，四档的 UI SCALE 行也只是
    /// 段变窄（68）而不是把整块往标签方向顶出 96px。</summary>
    private const float SegmentLeft = 1488f;
    private static float SegmentWidthFor(int count) =>
        Math.Min(SegmentWidth, (GroupRight - SegmentLeft - (count - 1) * SegmentGap) / count);
    private static int SegmentTextSize => EditorFonts.Pt(13);

    private const float ToggleWidth = 64f, ToggleHeight = 28f, ToggleCut = 8f;
    private const float KnobSize = 20f, KnobCut = 5f, KnobLeftInset = 6f, KnobRightInset = 26f, KnobInsetY = 4f;

    private const float StepperWidth = 134f, StepButtonSize = 30f, StepGap = 8f, StepValueWidth = 58f, StepCut = 6f;
    private static int StepValueSize => EditorFonts.Pt(16);
    private static int StepGlyphSize => EditorFonts.Pt(14);
    /// <summary>AUTO SAVE 行的开关右缘（左边给秒步进器留位，见 `settings_board`）。</summary>
    private const float AutoSaveToggleRight = 1630f;

    private const float SliderTrackWidth = 260f, SliderTrackHeight = 3f;
    private const float SliderValueWidth = 54f, SliderValueGap = 18f;
    private const float SliderHandleSize = 14f, SliderHandleCut = 4f;
    private static int SliderValueSize => EditorFonts.Pt(13);
    private const int SliderStepPercent = 5;

    private const float ResetRightX = 1612f, ResetCenterY = 992f;
    private static int ResetTextSize => EditorFonts.Pt(12);
    private const float ResetHitWidth = 170f, ResetHitHeight = 32f;

    private const float BackX = 1652f, BackY = 966f, BackWidth = 156f, BackHeight = 52f, BackCut = 10f;
    private static int BackTextSize => EditorFonts.Pt(15);

    // 占位行的示例值取设计稿样本（EFFECTS FULL、AUTO SAVE ON · 60 s、PREVIEW VOLUME 80%、
    // METRONOME OFF、HITSOUND ON），它们不读写设置模型。
    private const string AutoSaveSecondsText = "60 s";
    private const int PlaceholderPreviewVolume = 80;

    // 设计稿字距（ls1..ls6）在 Godot 侧用 FontVariation 表达，与 Start 板/HUD 同一做法。
    private static readonly Font Bold6 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 6 };
    private static readonly Font Bold2 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 2 };
    private static readonly Font Bold1 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 1 };
    private static readonly Font Tech3 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 3 };
    private static readonly Font Tech2 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };

    // ---------------------------------------------------------------- 行表

    private enum ControlKind { Segment, Stepper, ToggleStepper, Toggle, Slider }

    // 行下标是**绝对行号**（跨组连续），Groups 的 FirstRow/RowCount 与各类命中/绘制分支都按它取行。
    // 加行只改这三处常量与下面 Rows 的顺序，不再散落魔法数字。
    private const int MotionRow = 0, UiScaleRow = 1, EffectsRow = 2, AutoSaveRow = 3;
    private const int DefaultGridRow = 4, SnapRow = 5, GridOpacityRow = 6, BackgroundDimRow = 7, ScrollInertiaRow = 8;
    private const int PreviewVolumeRow = 9, MetronomeRow = 10, HitsoundRow = 11;

    /// <summary>一行：标签 + 控件形状 + 是否已接入（未接入画 55% 且不响应）。</summary>
    private readonly record struct RowPlan(string Label, ControlKind Kind, bool Enabled);

    private static readonly RowPlan[] Rows =
    [
        new("MOTION", ControlKind.Segment, true),
        new("UI SCALE", ControlKind.Segment, true),
        new("EFFECTS", ControlKind.Segment, false),
        new("AUTO SAVE", ControlKind.ToggleStepper, false),
        new("DEFAULT GRID", ControlKind.Segment, true),
        new("SNAP", ControlKind.Stepper, true),
        new("GRID OPACITY", ControlKind.Slider, true),
        new("BACKGROUND DIM", ControlKind.Slider, true),
        new("SCROLL INERTIA", ControlKind.Slider, true),
        new("PREVIEW VOLUME", ControlKind.Slider, false),
        new("METRONOME", ControlKind.Toggle, false),
        new("HITSOUND", ControlKind.Toggle, false),
    ];

    private static readonly string[] GroupTitles = ["GENERAL", "EDITOR", "AUDIO"];
    private static readonly int[] GroupFirstRow = [MotionRow, DefaultGridRow, PreviewVolumeRow];
    private static readonly int[] GroupRowCount = [4, 5, 3];
    /// <summary>每组面板的组顶（恒定）：三组各自从 240 起排（<see cref="GroupTop"/>）。</summary>
    private const float GroupTopStart = 240f;
    /// <summary>面板底不可越过的下缘：底部控制带（BACK 顶 966）上面留 6px。</summary>
    private const float PanelBottomLimit = BackY - 6f;

    private static readonly string[] MotionOptions = ["FULL", "REDUCED", "OFF"];
    private static readonly string[] EffectsOptions = ["FULL", "REDUCED"];
    private static readonly string[] GridOptions = ["1/8", "1/16", "1/32"];
    /// <summary>UI SCALE 的四档读数（与 <see cref="EditorSettings.UiScaleChoices"/> 同序）。</summary>
    private static readonly string[] UiScaleOptions = ["90%", "100%", "110%", "125%"];

    // ---------------------------------------------------------------- 数据

    /// <summary>页面快照：只带已接入项的值，占位行由本层按设计稿示例值绘制。</summary>
    public sealed class Snapshot
    {
        public int MotionIndex { get; init; }
        /// <summary>UI SCALE 段选中的下标（<see cref="EditorSettings.UiScaleChoices"/> 的顺序），
        /// 默认落在 <see cref="EditorSettings.DefaultUiScalePercent"/> 那一档上。</summary>
        public int UiScaleIndex { get; init; } =
            Math.Max(0, Array.IndexOf(EditorSettings.UiScaleChoices, EditorSettings.DefaultUiScalePercent));
        public int GridIndex { get; init; }
        public string SnapText { get; init; } = string.Empty;
        public int GridOpacityPercent { get; init; } = 100;
        public int BackgroundDimPercent { get; init; }
        public int ScrollInertiaPercent { get; init; } = EditorSettings.DefaultScrollInertiaPercent;
    }

    public event Action<int>? MotionSelected;
    /// <summary>UI SCALE 段选（0..3）。与 <see cref="GridSelected"/> 同款：离散档位，
    /// 收到即落盘、立即生效，不走滑条那种「松手才提交」的通道。</summary>
    public event Action<int>? UiScaleSelected;
    public event Action<int>? GridSelected;
    public event Action<int>? SnapStepped;
    /// <summary>滑条拖动中的实时值（第一个参数是 <see cref="GridOpacitySlider"/> /
    /// <see cref="BackgroundDimSlider"/> / <see cref="ScrollInertiaSlider"/>）。</summary>
    public event Action<int, int>? SliderChanged;
    /// <summary>一次连续编辑结束：写盘时机。</summary>
    public event Action? EditCommitted;
    public event Action? ResetRequested;
    public event Action? BackRequested;

    private enum Hotspot { None, Nav, Reset, Back, Segment, StepButton, Slider }

    private readonly record struct Hit(Hotspot Kind, int Index, int Sub);

    private Snapshot _state = new();
    private int _active;
    private Hit _hover = new(Hotspot.None, -1, 0);
    private int _dragging = -1;
    // hover 统一过渡（docs/editor-ui-design.md §3）：热点多且动态（按组/行/子下标变化），
    // 用键值表而不是逐个字段；键 = 行 100 + 子 10 + 组/索引 的页面自编码（见 HoverKey）。
    private readonly HoverFadeMap _hoverFade = new();

    public SettingsPageOverlay()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // 没有焦点/hover 活动时不空转：hover 过渡全部在终值，_Process 关着，hover 变化才开。
        SetProcess(false);
    }

    public void Apply(Snapshot state)
    {
        _state = state;
        QueueRedraw();
    }

    // ---------------------------------------------------------------- 布局

    /// <summary>每组是一块**从头开始的独立面板**（2026-10-08 用户拍板）：左导航切的是三块板，
    /// 不是一块长板的不同段——任何组的第一行都从 240 起排，面板高度随行数变化
    /// （<see cref="PanelHeight"/>）。之前的累进堆叠（组顶按前面组的行数下移）会让切到后面的组时
    /// 面板上半截是一大段空白，看起来像把一块板拆成了三块。</summary>
    private static float GroupTop(int group) => GroupTopStart;

    /// <summary>面板高度 = 组顶（恒定 240）+ 行距×行数 − 148（返回值是**相对 PanelY** 的高度，
    /// 最终底 = PanelY + 高度）。每组都从 240 起排，所以面板底随行数自然变化：GENERAL(4 行) = 510、
    /// EDITOR(5 行) = 562、AUDIO(3 行) = 458，都离 <see cref="PanelBottomLimit"/> 很远。
    /// 钳制保留为第二道保险：以后再加行，先被压住的是框，不是底部控制带。</summary>
    private static float PanelHeight(int group)
    {
        var height = GroupTop(group) + GroupRowCount[group] * RowPitch - PanelHeightBias;
        var limit = PanelBottomLimit - PanelY;
        return Math.Min(height, limit);
    }

    private static float RowCenter(int group, int localRow) => GroupTop(group) + FirstRowOffsetY + localRow * RowPitch;

    private static int RowIndex(int group, int localRow) => GroupFirstRow[group] + localRow;

    private static Rect2 NavRect(int group) => new(NavX, NavTop + group * NavPitch, NavWidth, NavHeight);
    private Rect2 PanelRect => new(PanelX, PanelY, PanelWidth, PanelHeight(_active));
    private static Rect2 BackRect => new(BackX, BackY, BackWidth, BackHeight);

    private static Rect2 ResetRect => new(ResetRightX - ResetHitWidth, ResetCenterY - ResetHitHeight * .5f,
        ResetHitWidth, ResetHitHeight);

    /// <summary>某一行分段器的档位读数。命中、绘制与选中下标共用这一处：段数不同的行
    /// （MOTION 3 档 / UI SCALE 4 档 / DEFAULT GRID 3 档 / 占位 EFFECTS 2 档）靠它对齐。</summary>
    private static string[] SegmentOptions(int row) => row switch
    {
        EffectsRow => EffectsOptions,
        MotionRow => MotionOptions,
        UiScaleRow => UiScaleOptions,
        _ => GridOptions,
    };

    /// <summary>分段器几何：左缘固定 <see cref="SegmentLeft"/>、段宽按段数反算（三档 = 稿上 92，
    /// 四档 = 68），整块因此始终右对齐在 <see cref="GroupRight"/>。</summary>
    private static Rect2 SegmentRect(float cy, int index, int count)
    {
        var width = SegmentWidthFor(count);
        var x0 = GroupRight - (count * width + (count - 1) * SegmentGap);
        return new Rect2(x0 + index * (width + SegmentGap), cy - SegmentHeight * .5f, width, SegmentHeight);
    }

    private static (Rect2 Left, Rect2 Right) StepButtons(float cy, float right = GroupRight)
    {
        var x0 = right - StepperWidth;
        return (new Rect2(x0, cy - StepButtonSize * .5f, StepButtonSize, StepButtonSize),
            new Rect2(x0 + StepButtonSize + StepGap + StepValueWidth, cy - StepButtonSize * .5f, StepButtonSize, StepButtonSize));
    }

    private static Vector2 StepValueCenter(float cy, float right = GroupRight) =>
        new(right - StepperWidth + StepButtonSize + StepGap + StepValueWidth * .5f, cy);

    private static Rect2 ToggleRect(float cy, float right = GroupRight) =>
        new(right - ToggleWidth, cy - ToggleHeight * .5f, ToggleWidth, ToggleHeight);

    private static float SliderTrackRight => GroupRight - SliderValueWidth - SliderValueGap;
    private static float SliderTrackLeft => SliderTrackRight - SliderTrackWidth;

    private static Rect2 SliderHitRect(float cy) => new(SliderTrackLeft - 8f, cy - 14f, SliderTrackWidth + 16f, 28f);

    // ---------------------------------------------------------------- 输入

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            if (_dragging >= 0) { ApplySlider(_dragging, PercentAt(motion.Position.X)); return; }
            Highlight(HitAt(motion.Position));
            return;
        }
        if (@event is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left) return;
        if (!button.Pressed)
        {
            if (_dragging < 0) return;
            _dragging = -1;
            EditCommitted?.Invoke();
            AcceptEvent();
            return;
        }
        var hit = HitAt(button.Position);
        switch (hit.Kind)
        {
            case Hotspot.Nav:
                if (_active == hit.Index) return;
                _active = hit.Index;
                QueueRedraw();
                break;
            case Hotspot.Reset: ResetRequested?.Invoke(); break;
            case Hotspot.Back: BackRequested?.Invoke(); break;
            case Hotspot.Segment: SelectSegment(hit.Index, hit.Sub); break;
            case Hotspot.StepButton: SnapStepped?.Invoke(hit.Sub); break;
            case Hotspot.Slider: _dragging = hit.Index; ApplySlider(hit.Index, hit.Sub); break;
            default: return;
        }
        AcceptEvent();
    }

    private void SelectSegment(int row, int index)
    {
        switch (row)
        {
            case MotionRow:
                if (index == _state.MotionIndex) return;
                MotionSelected?.Invoke(index);
                break;
            case UiScaleRow:
                if (index == _state.UiScaleIndex) return;
                UiScaleSelected?.Invoke(index);
                break;
            case DefaultGridRow:
                if (index == _state.GridIndex) return;
                GridSelected?.Invoke(index);
                break;
        }
    }

    private void ApplySlider(int row, int percent)
    {
        var clamped = Math.Clamp(percent / SliderStepPercent * SliderStepPercent, 0, 100);
        var slider = row switch
        {
            GridOpacityRow => GridOpacitySlider,
            BackgroundDimRow => BackgroundDimSlider,
            ScrollInertiaRow => ScrollInertiaSlider,
            _ => -1,
        };
        if (slider < 0) return;
        SliderChanged?.Invoke(slider, clamped);
    }

    private static int PercentAt(float x) =>
        Mathf.RoundToInt(Mathf.Clamp((x - SliderTrackLeft) / SliderTrackWidth, 0f, 1f) * 100f);

    private Hit HitAt(Vector2 point)
    {
        for (var group = 0; group < GroupTitles.Length; group++)
            if (NavRect(group).HasPoint(point)) return new Hit(Hotspot.Nav, group, 0);
        if (ResetRect.HasPoint(point)) return new Hit(Hotspot.Reset, -1, 0);
        if (BackRect.HasPoint(point)) return new Hit(Hotspot.Back, -1, 0);
        for (var local = 0; local < GroupRowCount[_active]; local++)
        {
            var row = RowIndex(_active, local);
            var plan = Rows[row];
            if (!plan.Enabled) continue;
            var cy = RowCenter(_active, local);
            switch (plan.Kind)
            {
                case ControlKind.Segment:
                {
                    var options = SegmentOptions(row);
                    for (var i = 0; i < options.Length; i++)
                        if (SegmentRect(cy, i, options.Length).HasPoint(point)) return new Hit(Hotspot.Segment, row, i);
                    break;
                }
                case ControlKind.Stepper:
                {
                    var (minus, plus) = StepButtons(cy);
                    if (minus.HasPoint(point)) return new Hit(Hotspot.StepButton, row, -1);
                    if (plus.HasPoint(point)) return new Hit(Hotspot.StepButton, row, 1);
                    break;
                }
                case ControlKind.Slider:
                    if (SliderHitRect(cy).HasPoint(point)) return new Hit(Hotspot.Slider, row, PercentAt(point.X));
                    break;
            }
        }
        return new Hit(Hotspot.None, -1, 0);
    }

    private void Highlight(Hit hit)
    {
        var next = hit.Kind == Hotspot.Slider ? new Hit(Hotspot.Slider, hit.Index, 0) : hit;
        if (next == _hover) return;
        _hover = next;
        // hover 目标变了才开 _Process：过渡值推进 + 重绘，全部到终值后自关（不空转）。
        SetProcess(true);
        QueueRedraw();
    }

    /// <summary>hover 热点 → 过渡值表的键。导航 0..2、RESET/BACK 固定值；行控件 = 行号×100 +
    /// 段/步进子下标（步进 −1/+1 传 0/1 保正）。None 编码为 -1（全部键向 0 回落）。</summary>
    private static long HoverKey(Hit hit) => hit.Kind switch
    {
        Hotspot.Nav => hit.Index,
        Hotspot.Reset => 900,
        Hotspot.Back => 901,
        Hotspot.Segment => hit.Index * 100 + hit.Sub,
        Hotspot.StepButton => hit.Index * 100 + (hit.Sub < 0 ? 0 : 1),
        Hotspot.Slider => hit.Index * 100 + 2,
        _ => -1,
    };

    /// <summary>绘制处取某热点的 hover 过渡值（0..1）：绘制色 = 常态色.Lerp(hover 色, 值)。</summary>
    private float HoverT(Hotspot kind, int index = 0, int sub = 0) => _hoverFade[HoverKey(new Hit(kind, index, sub))];

    public override void _Process(double delta)
    {
        // hover 统一过渡：键推进、值绘出，全部到终值即关 _Process 不空转。
        if (_hoverFade.Update(HoverKey(_hover), delta)) return;
        SetProcess(false);
    }

    // ---------------------------------------------------------------- 绘制

    public override void _Draw()
    {
        EditorOverlayDraw.LeftText(this, Bold6, new Vector2(TitleX, TitleCenterY), "SETTINGS", TitleSize, UiFonts.Text);
        DrawRect(new Rect2(TitleX, UnderlineY, UnderlineWidth, UnderlineHeight), UiFonts.Cyan, true);
        DrawNav();
        DrawRows();
        DrawBottom();
    }

    private void DrawNav()
    {
        for (var group = 0; group < GroupTitles.Length; group++)
        {
            var rect = NavRect(group);
            var selected = group == _active;
            if (selected)
            {
                EditorOverlayDraw.CutPanel(this, rect, NavCut, new Color(UiFonts.Panel, .95f), new Color(0, 0, 0, 0), 0f);
                DrawRect(new Rect2(rect.Position.X, rect.Position.Y, NavAccentWidth, rect.Size.Y), UiFonts.Cyan, true);
            }
            var hovered = HoverT(Hotspot.Nav, group);
            var color = selected ? UiFonts.Text : UiFonts.Dim.Lerp(UiFonts.Text, hovered);
            EditorOverlayDraw.LeftText(this, selected ? Bold2 : Tech2,
                new Vector2(NavTextX, rect.Position.Y + rect.Size.Y * .5f), GroupTitles[group], NavTextSize, color);
        }
    }

    private void DrawRows()
    {
        var top = GroupTop(_active);
        EditorOverlayDraw.CutPanel(this, PanelRect, PanelCut, new Color(UiFonts.Panel, .9f), UiFonts.Line, PanelStroke);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(GroupLeft, top + HeaderOffsetY), GroupTitles[_active], EditorFonts.Pt(12), UiFonts.Dim);
        DrawRect(new Rect2(GroupLeft, top + RuleOffsetY, GroupRight - GroupLeft, 1f), UiFonts.Line, true);
        var rows = GroupRowCount[_active];
        for (var local = 0; local < rows; local++)
        {
            var row = RowIndex(_active, local);
            var plan = Rows[row];
            var cy = RowCenter(_active, local);
            var alpha = plan.Enabled ? 1f : DisabledAlpha;
            EditorOverlayDraw.LeftText(this, EditorFonts.Medium, new Vector2(GroupLeft, cy),
                Fitted(plan.Label, RowLabelSize, RowLabelWidth), RowLabelSize, Alpha(UiFonts.Text, alpha));
            DrawControl(row, plan, cy, alpha);
            if (local < rows - 1)
                DrawRect(new Rect2(GroupLeft, cy + RowHalfPitch, GroupRight - GroupLeft, 1f), new Color(UiFonts.Line, .5f), true);
        }
    }

    /// <summary>行标签宽了会顶到控件列，按真实字宽截断。</summary>
    private static string Fitted(string text, int size, float maxWidth)
    {
        if (EditorFonts.Medium.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth) return text;
        var trimmed = text;
        while (trimmed.Length > 1 &&
               EditorFonts.Medium.GetStringSize(trimmed + "…", HorizontalAlignment.Left, -1, size).X > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed + "…";
    }

    private void DrawControl(int row, RowPlan plan, float cy, float alpha)
    {
        switch (plan.Kind)
        {
            case ControlKind.Segment:
                DrawSegment(plan, row, cy, alpha);
                break;
            case ControlKind.Stepper:
                DrawStepper(cy, alpha, _state.SnapText, plan.Enabled, row);
                break;
            case ControlKind.ToggleStepper:
                DrawToggle(cy, AutoSaveToggleRight, on: true, alpha);
                DrawStepper(cy, alpha, AutoSaveSecondsText);
                break;
            case ControlKind.Toggle:
                DrawToggle(cy, GroupRight, on: row == HitsoundRow, alpha);
                break;
            case ControlKind.Slider:
                DrawSlider(row, cy, alpha);
                break;
        }
    }

    private void DrawSegment(RowPlan plan, int row, float cy, float alpha)
    {
        var options = SegmentOptions(row);
        var selected = row switch
        {
            EffectsRow => 0,
            MotionRow => _state.MotionIndex,
            UiScaleRow => _state.UiScaleIndex,
            _ => _state.GridIndex,
        };
        for (var i = 0; i < options.Length; i++)
        {
            var rect = SegmentRect(cy, i, options.Length);
            var on = i == selected;
            var hovered = plan.Enabled ? HoverT(Hotspot.Segment, row, i) : 0f;
            if (on)
                EditorOverlayDraw.CutPanel(this, rect, SegmentCut, Alpha(UiFonts.Cyan, .16f * alpha), Alpha(UiFonts.Cyan, alpha), 2f);
            else
            {
                // hover 统一过渡：hover 态只有填充 0 → .08（描边保持 Line，与原 hover 视觉一致），
                // 按过渡值 lerp；值为 0 时走纯描边分支，与常态绘制逐值一致。
                var fill = Alpha(UiFonts.Cyan, .08f * hovered * alpha);
                if (fill.A > 0f)
                    EditorOverlayDraw.CutPanel(this, rect, SegmentCut, fill, Alpha(UiFonts.Line, alpha), 2f);
                else
                    EditorOverlayDraw.CutStroke(this, rect, SegmentCut, Alpha(UiFonts.Line, alpha), 2f);
            }
            EditorOverlayDraw.CenteredText(this, Bold1, rect.Position + rect.Size * .5f, options[i], SegmentTextSize,
                on ? Alpha(UiFonts.Cyan, alpha) : Alpha(UiFonts.Dim, alpha));
        }
    }

    private void DrawStepper(float cy, float alpha, string value, bool enabled = true, int row = -1)
    {
        var (minus, plus) = StepButtons(cy);
        foreach (var (rect, glyph, sub) in new[] { (minus, "‹", 0), (plus, "›", 1) })
        {
            // hover 统一过渡：描边 Line → Cyan 按值 lerp；占位（55%）行不参与 hover 提亮。
            var hovered = enabled && row >= 0 ? HoverT(Hotspot.StepButton, row, sub) : 0f;
            EditorOverlayDraw.CutStroke(this, rect, StepCut, Alpha(UiFonts.Line.Lerp(UiFonts.Cyan, hovered), alpha), 2f);
            EditorOverlayDraw.CenteredText(this, EditorFonts.Medium, rect.Position + rect.Size * .5f, glyph, StepGlyphSize,
                Alpha(UiFonts.Text, alpha));
        }
        EditorOverlayDraw.CenteredText(this, UiFonts.TechBold, StepValueCenter(cy), value, StepValueSize, Alpha(UiFonts.Cyan, alpha));
    }

    /// <summary>开关：ON = cyan 12% 填充 + cyan 描边 + cyan 方块停右；OFF = line 描边 + dim 方块停左。</summary>
    private void DrawToggle(float cy, float right, bool on, float alpha)
    {
        var rect = ToggleRect(cy, right);
        if (on)
            EditorOverlayDraw.CutPanel(this, rect, ToggleCut, Alpha(UiFonts.Cyan, .12f * alpha), Alpha(UiFonts.Cyan, alpha), 1.5f);
        else
            EditorOverlayDraw.CutStroke(this, rect, ToggleCut, Alpha(UiFonts.Line, alpha), 1.5f);
        var knobX = rect.Position.X + (on ? ToggleWidth - KnobRightInset : KnobLeftInset);
        var knob = new Rect2(knobX, rect.Position.Y + KnobInsetY, KnobSize, KnobSize);
        var color = on ? Alpha(UiFonts.Cyan, alpha) : Alpha(UiFonts.Dim, .5f * alpha);
        EditorOverlayDraw.CutPanel(this, knob, KnobCut, color, new Color(0, 0, 0, 0), 0f);
    }

    private void DrawSlider(int row, float cy, float alpha)
    {
        var percent = row switch
        {
            GridOpacityRow => _state.GridOpacityPercent,
            BackgroundDimRow => _state.BackgroundDimPercent,
            ScrollInertiaRow => _state.ScrollInertiaPercent,
            _ => PlaceholderPreviewVolume,
        };
        var accent = row == PreviewVolumeRow ? UiFonts.Pink : UiFonts.Cyan;
        // 稿上轨道是 rect(x0, cy − 2, 260, 3)、手柄是 cut(x0 + fw − 7, cy − 7, 14, 14)。
        var track = new Rect2(SliderTrackLeft, cy - 2f, SliderTrackWidth, SliderTrackHeight);
        // hover 统一过渡：轨道白 .12 → .20 按过渡值 lerp（只做不透明度变化，不动填充/手柄/读数）。
        var trackAlpha = Mathf.Lerp(.12f, .2f, HoverT(Hotspot.Slider, row));
        DrawRect(track, Alpha(UiFonts.Text, trackAlpha * alpha), true);
        var filled = Mathf.RoundToInt(SliderTrackWidth * percent / 100f);
        if (filled > 0)
            DrawRect(new Rect2(track.Position.X, track.Position.Y, filled, SliderTrackHeight), Alpha(accent, alpha), true);
        var handle = new Rect2(track.Position.X + filled - SliderHandleSize * .5f, cy - SliderHandleSize * .5f,
            SliderHandleSize, SliderHandleSize);
        EditorOverlayDraw.CutPanel(this, handle, SliderHandleCut, Alpha(UiFonts.Text, alpha), new Color(0, 0, 0, 0), 0f);
        EditorOverlayDraw.RightText(this, UiFonts.TechBold, GroupRight, cy, $"{percent}%", SliderValueSize, Alpha(accent, alpha));
    }

    private void DrawBottom()
    {
        // RESET 是文字键：hover 只把 Dim 文字提亮到 Cyan，用过渡值 lerp（自绘文字无 theme 可挂）。
        var resetHovered = HoverT(Hotspot.Reset);
        EditorOverlayDraw.RightText(this, Tech2, ResetRightX, ResetCenterY, "RESET DEFAULTS", ResetTextSize,
            UiFonts.Dim.Lerp(UiFonts.Cyan, resetHovered));

        var back = BackRect;
        EditorOverlayDraw.CutPanel(this, back, BackCut, new Color(0, 0, 0, 0),
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Back)), 2f);
        EditorOverlayDraw.CenteredText(this, Bold2, back.Position + back.Size * .5f, "BACK", BackTextSize, UiFonts.Cyan);
    }

    private static Color Alpha(Color color, float alpha) => new(color.R, color.G, color.B, color.A * alpha);

    /// <summary>吸附步长的显示格式与 HUD 的 SNAP 读数一致。</summary>
    public static string SnapText(double step) => step.ToString("0.00", CultureInfo.InvariantCulture);
}

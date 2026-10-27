using Godot;
using DynamiteUniverse.Game;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;


/// <summary>右键菜单面板（设计稿 Editor — Context Menu / Editor — Node Menu）。面板几何、
/// 行距、字号与色值取设计稿；每行的可交互性来自画布的真实能力，未实现的行 55% 压暗占位、
/// 点击无效果。锚在光标处、近屏幕边缘翻转，只做整体 120ms 淡入。</summary>
internal sealed partial class EditorContextMenu : Control
{
    // 设计稿面板：480 宽、切角 14、内边距 16，行宽 448（736..1184）。
    private const float PanelWidth = 480f, PanelCut = 14f, PanelPad = 16f, RowWidth = 448f;
    private const float TrackWidth = 108f, TrackHeight = 40f, TrackPitch = 116f, TrackCut = 8f;
    private const float ToolWidth = 218f, ToolHeight = 40f, ToolRowPitch = 44f, ToolColumnPitch = 224f;
    private const float StepRowHeight = 38f, StepValueX = 274f, StepButtonSize = 30f, StepButtonX = 364f, StepButtonPitch = 38f, StepButtonCut = 6f;
    private const float CurveWidth = 68f, CurveHeight = 40f, CurvePitch = 76f, CurveCut = 8f;
    private const float JudgeToggleX = 374f, JudgeToggleWidth = 58f, JudgeToggleHeight = 32f;
    private const float ActionRowHeight = 36f, NodeActionRowHeight = 38f;
    private const float CommandRowHeight = 40f, CommandTextRow = 36f;
    // 设计稿滑条轨道从面板局部 x=130 起（板面绝对 850 = 面板 720 + 130）。
    private const float SliderTrackX = 130f, SliderTrackWidth = 260f, SliderRowHeight = 36f;
    private const float FadeSeconds = .12f, AnchorOffset = 12f, DisabledAlpha = .55f;
    private const float NotePanelBottomPad = 40f, NodePanelBottomPad = 24f;

    // 动作 id：菜单只发 id，命令与状态都由 shell 分发。
    public const long ActSideBase = 1;      // +0 ← +1 ↓ +2 → +3 #
    public const long ActToolBase = 10;     // +1..4 画布真实工具
    public const long ActTimeMinus = 20, ActTimePlus = 21;
    public const long ActCenterMinus = 22, ActCenterPlus = 23;
    public const long ActWidthMinus = 24, ActWidthPlus = 25;
    public const long ActDelete = 30;
    public const long ActCurveBase = 40;    // +0..5 六段曲线
    public const long ActJudge = 46, ActDeleteNode = 47, ActAddNode = 48;
    public const long ActMarkHere = 50, ActPlayFromMark = 51, ActReplay = 52;
    public const long ActSave = 60, ActSaveAs = 61, ActSettings = 62;
    public const int CurveCount = 6;

    private static readonly Font MenuBold = UiFonts.TechBold;
    private static readonly Font MenuText = EditorFonts.Medium;
    /// <summary>设计稿的 ls1 / ls1.5 / ls2 字距用 FontVariation 表达（与 HUD 同一做法）。</summary>
    private static readonly Font MenuTech1 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 1 };
    private static readonly Font MenuTech2 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };
    private static readonly Font MenuBold1 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 1 };
    private static readonly Font MenuBold2 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 2 };

    private enum ElementKind
    {
        Track, Tool, StepRow, StepButton, Divider, Header, HeaderRight,
        CurveLabel, Curve, JudgeToggle, JudgeCaption, Row, Command, Slider,
    }

    private readonly record struct Element(
        ElementKind Kind, Rect2 Rect, string Text, string Glyph, Color Accent, bool Enabled, bool Selected)
    {
        /// <summary>>= 0 才可点击；占位行保持 -1，因此点击无效果。</summary>
        public long Action { get; init; } = -1;
        /// <summary>加粗行（设计稿的节点动作行与工具标签）。</summary>
        public bool Strong { get; init; }
        /// <summary>曲线图标序号。</summary>
        public int Index { get; init; }
        /// <summary>滑条比例（0..1）。</summary>
        public float Value { get; init; }
        public float Cut { get; init; }
        /// <summary>行首 glyph 的**设计稿**字号：13（动作行）或 15（节点动作行）。
        /// 存设计稿值而不是换算结果——菜单元素每次右键重建，但绘制才该取现值（见 EditorFonts）。</summary>
        public int GlyphSize { get; init; } = ActionGlyphSize;
    }

    /// <summary>设计稿动作行的 glyph 字号（MENU `M`/`R` 13px、NMENU `+` 15px）。</summary>
    private const int ActionGlyphSize = 13, NodeActionGlyphSize = 15;

    /// <summary>面板数据快照（每次右键由 shell 从画布与 shell 状态生成；选中相关字段在
    /// 建立快照后再补齐，所以是可写属性）。</summary>
    public sealed class MenuState
    {
        public int Side { get; init; }
        public int Tool { get; init; }
        public bool HasSelection { get; init; }
        public int SelectionCount { get; init; }
        public bool NodeMode { get; set; }
        public string Header { get; set; } = string.Empty;
        public string CurveName { get; set; } = string.Empty;
        public string TimeText { get; set; } = "--";
        public string CenterText { get; set; } = "--";
        public string WidthText { get; set; } = "--";
        public bool StepsEnabled { get; set; }
        public int CurveIndex { get; set; } = -1;
        public bool CurveEnabled { get; set; }
        public bool JudgeRow { get; set; }
        public bool JudgeOn { get; set; }
        public bool JudgeLocked { get; set; }
        public bool AddNodeEnabled { get; set; }
        public int NodeCount { get; set; }
        public string CursorBar { get; init; } = "0";
    }

    private readonly List<Element> _elements = new();
    private double _fade = 1d;
    private int _hover = -1;
    // hover 统一过渡：菜单元素数量随内容变化，用键值表；键 = 元素下标（列表每次 Open 重建，
    // 生命周期与过渡时长同量级，直接用下标即可）。
    private readonly HoverFadeMap _hoverFade = new();
    public event Action<long>? MenuClicked;

    public EditorContextMenu()
    {
        // Input is routed explicitly by the root so the canvas cannot see a
        // menu click as an editor click.
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 20;
        SetProcess(true);
        Hide();
    }

    public override void _Process(double delta)
    {
        // 淡入与 hover 过渡共用 _Process：两者都到终值才自关；Open 在 Show 之后会重新
        // SetProcess(true)，所以下一轮打开仍能推进淡入与 hover 过渡（见 Open 尾部注释）。
        if (Visible)
        {
            // hover 统一过渡：hover 目标（元素下标 / -1）驱动键值表，到全部终值即停推进。
            if (_hoverFade.Update(_hover, delta))
            {
                QueueRedraw();
            }
            else if (_fade >= 1d)
            {
                SetProcess(false);
                return;
            }
        }
        if (!Visible || _fade >= 1d) return;
        _fade = Math.Min(1d, _fade + delta / FadeSeconds);
        Modulate = new Color(1f, 1f, 1f, (float)_fade);
    }

    public void Open(Vector2 pointer, MenuState state)
    {
        _elements.Clear();
        var y = PanelPad;
        BuildTrack(state, ref y);
        if (state.NodeMode)
        {
            BuildNodeHeader(state, ref y);
            BuildSteppers(state, ref y);
            BuildNodeBlock(state, ref y);
        }
        else
        {
            BuildTools(state, ref y);
            BuildSteppers(state, ref y);
        }
        BuildActions(state, ref y);
        BuildCommands(ref y);
        BuildSliders(ref y);
        var height = y + (state.NodeMode ? NodePanelBottomPad : NotePanelBottomPad);

        // 锚在光标右下 12px（设计稿光标 708/164 → 面板 720/176），近边缘时翻到另一侧。
        var viewport = GetViewportRect().Size;
        var x = pointer.X + AnchorOffset;
        if (x + PanelWidth > viewport.X) x = pointer.X - AnchorOffset - PanelWidth;
        var top = pointer.Y + AnchorOffset;
        if (top + height > viewport.Y) top = pointer.Y - AnchorOffset - height;
        Position = new Vector2(Mathf.Clamp(x, 0f, Mathf.Max(0f, viewport.X - PanelWidth)),
            Mathf.Clamp(top, 0f, Mathf.Max(0f, viewport.Y - height)));
        Size = new Vector2(PanelWidth, height);
        // 菜单重建后元素下标全变：hover 过渡表直接清空（菜单本就带 120ms 整体淡入，无需余晖）。
        _hoverFade.Clear();
        _hover = -1;
        // GENERAL / MOTION = OFF 时菜单不淡入，直接定格在终值（编辑器当前只有菜单与页面切换两种淡入）。
        _fade = EditorSettings.Current.MotionMode == UiMotionMode.Off ? 1d : 0d;
        Modulate = new Color(1f, 1f, 1f, (float)_fade);
        Show();
        // 重新开 _Process：上次打开到终值自关（_Process 尾部 SetProcess(false)）后，这里必须
        // 重新启用才能推进本次淡入与 hover 过渡。OFF 模式 _fade=1 且 hover 已落定，
        // _Process 首帧就会自关，无副作用。
        SetProcess(true);
        QueueRedraw();
    }

    public void HandleGlobalMouse(Vector2 viewportPosition, MouseButton button, bool pressed)
    {
        var local = GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        if (button == MouseButton.None)
        {
            SetHover(HoverAt(local));
            return;
        }
        if (!pressed) return;
        if (button is MouseButton.Right or MouseButton.Middle) { Hide(); return; }
        if (button != MouseButton.Left) return;
        var id = ActionAt(local);
        if (id >= 0) MenuClicked?.Invoke(id);
        Hide();
    }

    public bool ContainsGlobalPoint(Vector2 viewportPosition)
    {
        var local = GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        return new Rect2(Vector2.Zero, Size).HasPoint(local);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion) { SetHover(HoverAt(motion.Position)); return; }            if (@event is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.Right) { Hide(); AcceptEvent(); return; }
            if (button.ButtonIndex == MouseButton.Left)
            {
                var id = ActionAt(button.Position);
                if (id >= 0) MenuClicked?.Invoke(id);
                Hide(); AcceptEvent();
            }
        }
    }

    private void SetHover(int index)
    {
        if (index == _hover) return;
        _hover = index;
        // hover 目标变了才开 _Process：过渡值推进 + 重绘，全部到终值后自关（不空转）。
        SetProcess(true);
        QueueRedraw();
    }

    /// <summary>可点击命中：只在 Enabled 且有 Action 的元素上生效，占位行返回 -1。</summary>
    private long ActionAt(Vector2 p)
    {
        for (var i = 0; i < _elements.Count; i++)
        {
            var element = _elements[i];
            if (element.Action >= 0 && element.Enabled && element.Rect.HasPoint(p)) return element.Action;
        }
        return -1;
    }

    private int HoverAt(Vector2 p)
    {
        for (var i = 0; i < _elements.Count; i++)
        {
            var element = _elements[i];
            if (element.Action >= 0 && element.Enabled && element.Rect.HasPoint(p)) return i;
        }
        return -1;
    }

    public override void _Draw()
    {
        EditorOverlayDraw.CutPanel(this, new Rect2(Vector2.Zero, Size), PanelCut,
            Alpha(new Color("040a12"), .95f), Alpha(new Color("33d9ff"), .9f), 2f);
        // hover 统一过渡：按下标取过渡值传给绘制（元素列表 Open 时重建，下标即键）。
        for (var i = 0; i < _elements.Count; i++) DrawElement(_elements[i], _hoverFade[i]);
    }
    // ------------------------------------------------------------ layout

    private void BuildTrack(MenuState state, ref float y)
    {
        // 轨道段 [← ↓ → #]：接现有「切换编辑侧」能力（↓ 一格是 Center）。
        var sides = new[] { 1, 0, 2, 3 };
        var glyphs = new[] { "←", "↓", "→", "#" };
        for (var i = 0; i < 4; i++)
        {
            _elements.Add(new Element(ElementKind.Track,
                new Rect2(PanelPad + i * TrackPitch, y, TrackWidth, TrackHeight),
                string.Empty, glyphs[i], UiFonts.Cyan, true, state.Side == sides[i])
            { Action = ActSideBase + i, Cut = TrackCut });
        }
        y += 56f;
    }

    /// <summary>工具格：只有画布真实存在的四个工具可点，其余按设计稿位置 55% 压暗占位。
    /// Events 侧第一格是真实的 BPM 事件模式（该侧 tool 1 添加 BPM 事件）。</summary>
    private static (string Label, Color Accent, int Tool, bool Enabled)[] ToolCells(MenuState state) =>
    [
        state.Side == 3
            ? ("BPM EVENT", new Color("ff6bb4"), 1, true)
            : ("TAP", NoteVisualSpec.Tap, 1, true),
        ("DRAG", NoteVisualSpec.Drag, 2, state.Side != 3),
        ("HOLD", NoteVisualSpec.Hold, 3, state.Side != 3),
        ("SELECT", UiFonts.Cyan, 4, true),
        ("EX-TAP", NoteVisualSpec.ExTap, 0, false),
        ("MIXER", NoteVisualSpec.Mixer, 0, false),
        ("MINE", NoteVisualSpec.Mine, 0, false),
        ("BARLINE", NoteVisualSpec.BarLine, 0, false),
    ];

    private void BuildTools(MenuState state, ref float y)
    {
        var cells = ToolCells(state);
        for (var i = 0; i < cells.Length; i++)
        {
            var (label, accent, tool, enabled) = cells[i];
            var rect = new Rect2(PanelPad - 6f + (i % 2) * ToolColumnPitch, y + (i / 2) * ToolRowPitch,
                ToolWidth, ToolHeight);
            _elements.Add(new Element(ElementKind.Tool, rect, label, string.Empty, accent, enabled,
                enabled && state.Tool == tool)
            { Action = enabled ? ActToolBase + tool : -1, Strong = true });
        }
        y += 4f * ToolRowPitch + 14f;
        AddDivider(ref y);
    }

    private void BuildSteppers(MenuState state, ref float y)
    {
        AddStepRow("TIME", state.TimeText, ActTimeMinus, ActTimePlus, state.StepsEnabled, ref y);
        AddStepRow("CENTER", state.CenterText, ActCenterMinus, ActCenterPlus, state.StepsEnabled, ref y);
        AddStepRow("WIDTH", state.WidthText, ActWidthMinus, ActWidthPlus, state.StepsEnabled, ref y);
        y += 8f;
        AddDivider(ref y);
    }

    private void AddStepRow(string label, string value, long minus, long plus, bool enabled, ref float y)
    {
        _elements.Add(new Element(ElementKind.StepRow, new Rect2(PanelPad, y, RowWidth, StepButtonSize),
            label, value, UiFonts.Cyan, enabled, false));
        _elements.Add(new Element(ElementKind.StepButton,
            new Rect2(PanelPad + StepButtonX, y, StepButtonSize, StepButtonSize),
            string.Empty, "‹", UiFonts.Text, enabled, false)
        { Action = enabled ? minus : -1, Cut = StepButtonCut });
        _elements.Add(new Element(ElementKind.StepButton,
            new Rect2(PanelPad + StepButtonX + StepButtonPitch, y, StepButtonSize, StepButtonSize),
            string.Empty, "›", UiFonts.Text, enabled, false)
        { Action = enabled ? plus : -1, Cut = StepButtonCut });
        y += StepRowHeight;
    }

    private void BuildNodeHeader(MenuState state, ref float y)
    {
        _elements.Add(new Element(ElementKind.Header, new Rect2(PanelPad, y, RowWidth, 28f),
            state.Header, string.Empty, new Color("ffcc59"), true, false));
        _elements.Add(new Element(ElementKind.HeaderRight, new Rect2(PanelPad, y, RowWidth, 28f),
            $"{state.CurveName} PATH", string.Empty, UiFonts.Dim, true, false));
        y += 28f;
    }

    private void BuildNodeBlock(MenuState state, ref float y)
    {
        _elements.Add(new Element(ElementKind.CurveLabel, new Rect2(PanelPad, y, RowWidth, 26f),
            "CURVE TO NEXT", string.Empty, UiFonts.Dim, true, false));
        y += 26f;
        for (var i = 0; i < CurveCount; i++)
        {
            _elements.Add(new Element(ElementKind.Curve,
                new Rect2(PanelPad + i * CurvePitch, y, CurveWidth, CurveHeight),
                string.Empty, string.Empty, UiFonts.Cyan, state.CurveEnabled,
                state.CurveEnabled && state.CurveIndex == i)
            { Action = state.CurveEnabled ? ActCurveBase + i : -1, Index = i, Cut = CurveCut });
        }
        y += 56f;
        // Hold 专属 JUDGE 拨杆；Mixer 没有这一行，Hold 尾节点锁定 ON。
        if (state.JudgeRow)
        {
            _elements.Add(new Element(ElementKind.JudgeToggle,
                new Rect2(PanelPad + JudgeToggleX, y, JudgeToggleWidth, JudgeToggleHeight),
                string.Empty, string.Empty, UiFonts.Casual, !state.JudgeLocked, state.JudgeOn)
            { Action = state.JudgeLocked ? -1 : ActJudge });
            _elements.Add(new Element(ElementKind.JudgeCaption,
                new Rect2(PanelPad, y, RowWidth, JudgeToggleHeight),
                "MAIN JUDGE AT THIS NODE", "JUDGE", UiFonts.Dim, true, false));
            y += 48f;
        }
        AddDivider(ref y);
        // ADD NODE 接真实命令：在游标时刻插入控制节点（双击路径身体是同一入口的手势形式），
        // 游标不在路径内部时禁用；DELETE NODE 只剩一个节点时禁用。
        var canAdd = state.AddNodeEnabled;
        _elements.Add(new Element(ElementKind.Row, new Rect2(PanelPad, y, RowWidth, NodeActionRowHeight),
            "ADD NODE · AT CURSOR", "+", UiFonts.Cyan, canAdd, false)
        { Action = canAdd ? ActAddNode : -1, Strong = true, GlyphSize = NodeActionGlyphSize });
        y += NodeActionRowHeight;
        var canDelete = state.NodeCount > 1;
        _elements.Add(new Element(ElementKind.Row, new Rect2(PanelPad, y, RowWidth, NodeActionRowHeight),
            "DELETE NODE", string.Empty, UiFonts.Giga, canDelete, false)
        { Action = canDelete ? ActDeleteNode : -1, Strong = true });
        y += NodeActionRowHeight + 6f;
        AddDivider(ref y);
    }

    private void BuildActions(MenuState state, ref float y)
    {
        // 旧的框选非空选区删除菜单并进这一行；节点态已有 DELETE NODE，不再重复。
        if (!state.NodeMode && state.HasSelection)
        {
            var label = state.SelectionCount > 1 ? $"DELETE · {state.SelectionCount} NOTES" : "DELETE";
            _elements.Add(new Element(ElementKind.Row, new Rect2(PanelPad, y, RowWidth, ActionRowHeight),
                label, string.Empty, UiFonts.Giga, true, false) { Action = ActDelete });
            y += ActionRowHeight;
        }
        // 动作行（设计稿 MENU / NMENU）：`M` 在 MARK HERE 行、`R` 在 TEST PLAY FROM MARK 行，
        // 两行都接现有的游标标记与「从标记重播」能力；REPLAY 只在普通面板 —— 节点态的 NMENU
        // 只有这两行。
        _elements.Add(new Element(ElementKind.Row, new Rect2(PanelPad, y, RowWidth, ActionRowHeight),
            $"MARK HERE · {state.CursorBar}", "M", UiFonts.Text, true, false)
        { Action = ActMarkHere });
        y += ActionRowHeight;
        _elements.Add(new Element(ElementKind.Row, new Rect2(PanelPad, y, RowWidth, ActionRowHeight),
            "TEST PLAY FROM MARK", "R", UiFonts.Text, true, false)
        { Action = ActPlayFromMark });
        y += ActionRowHeight;
        if (!state.NodeMode)
        {
            _elements.Add(new Element(ElementKind.Row, new Rect2(PanelPad, y, RowWidth, ActionRowHeight),
                "REPLAY", string.Empty, UiFonts.Text, true, false) { Action = ActReplay });
            y += ActionRowHeight;
        }
        y += 6f;
        AddDivider(ref y);
    }

    private void BuildCommands(ref float y)
    {
        _elements.Add(new Element(ElementKind.Command, new Rect2(PanelPad, y, RowWidth, CommandTextRow),
            "SAVE · CTRL+S", string.Empty, UiFonts.Cyan, true, false) { Action = ActSave });
        y += CommandTextRow;
        _elements.Add(new Element(ElementKind.Command, new Rect2(PanelPad, y, RowWidth, CommandTextRow),
            "SAVE AS · CTRL+SHIFT+S", string.Empty, UiFonts.Cyan, true, false) { Action = ActSaveAs });
        y += CommandTextRow;
        // SETTINGS 是真实动作：进 Settings 页，BACK 回 Edit（设计稿 §4.7 的编辑器内入口）。
        _elements.Add(new Element(ElementKind.Command, new Rect2(PanelPad, y, RowWidth, CommandTextRow),
            "SETTINGS", string.Empty, UiFonts.Cyan, true, false) { Action = ActSettings });
        y += CommandTextRow;
        // EXPORT 尚未实现 → 55% 占位且不进入命中表。
        _elements.Add(new Element(ElementKind.Command, new Rect2(PanelPad, y, RowWidth, CommandRowHeight),
            "EXPORT", string.Empty, UiFonts.Cyan, false, false));
        y += CommandRowHeight;
    }

    private void BuildSliders(ref float y)
    {
        // HITSOUND/MUSIC 滑条：没有音频链路 → 55% 占位，不进入命中表（不响应拖动）。
        AddSlider("HITSOUND", .4f, UiFonts.Cyan, ref y);
        AddSlider("MUSIC", .8f, UiFonts.Pink, ref y);
    }

    private void AddSlider(string label, float value, Color accent, ref float y)
    {
        _elements.Add(new Element(ElementKind.Slider, new Rect2(PanelPad, y, RowWidth, SliderRowHeight),
            label, string.Empty, accent, false, false) { Value = value });
        y += SliderRowHeight;
    }

    private void AddDivider(ref float y)
    {
        _elements.Add(new Element(ElementKind.Divider, new Rect2(PanelPad, y, RowWidth, 1f),
            string.Empty, string.Empty, UiFonts.Line, false, false));
        y += 14f;
    }

    // ------------------------------------------------------------ paint

    /// <summary>hover 是 0..1 过渡值（见 _Draw 的调用点）：高亮 alpha、文字提亮都按值插值，
    /// MOTION OFF 时 Update 直接跳终值，行为与原瞬时换色一致。</summary>
    private void DrawElement(Element element, float hover)
    {
        switch (element.Kind)
        {
            case ElementKind.Track:
                EditorOverlayDraw.CutPanel(this, element.Rect, element.Cut,
                    element.Selected ? Alpha(UiFonts.Cyan, .16f) : new Color(0, 0, 0, 0),
                    element.Selected ? UiFonts.Cyan : UiFonts.Line, 2f);
                EditorOverlayDraw.CenteredText(this, MenuBold,
                    element.Rect.Position + new Vector2(TrackWidth * .5f, TrackHeight * .5f),
                    element.Glyph, EditorFonts.Pt(15), element.Selected ? UiFonts.Cyan : UiFonts.Dim);
                break;
            case ElementKind.Tool:
            {
                var alpha = element.Enabled ? 1f : DisabledAlpha;
                if (element.Selected) DrawRect(element.Rect, Alpha(UiFonts.Cyan, .12f), true);
                else if (hover > 0f) DrawRect(element.Rect, Alpha(UiFonts.Cyan, .10f * hover), true);
                DrawRect(new Rect2(element.Rect.Position + new Vector2(6f, 15f), new Vector2(10f, 10f)),
                    Alpha(element.Accent, alpha), true);
                EditorOverlayDraw.LeftText(this, MenuBold1, element.Rect.Position + new Vector2(28f, 20f),
                    element.Text, EditorFonts.Pt(13), Alpha(element.Selected ? UiFonts.Text : element.Accent, alpha));
                break;
            }
            case ElementKind.StepRow:
            {
                var alpha = element.Enabled ? 1f : DisabledAlpha;
                EditorOverlayDraw.LeftText(this, MenuTech2, element.Rect.Position + new Vector2(0f, 15f),
                    element.Text, EditorFonts.Pt(11), Alpha(UiFonts.Dim, alpha));
                EditorOverlayDraw.CenteredText(this, MenuBold,
                    element.Rect.Position + new Vector2(StepValueX, 15f), element.Glyph, EditorFonts.Pt(15),
                    Alpha(UiFonts.Cyan, alpha));
                break;
            }
            case ElementKind.StepButton:
                EditorOverlayDraw.CutStroke(this, element.Rect, element.Cut,
                    Alpha(UiFonts.Line, element.Enabled ? 1f : DisabledAlpha), 2f);
                EditorOverlayDraw.CenteredText(this, MenuBold,
                    element.Rect.Position + element.Rect.Size * .5f, element.Glyph, EditorFonts.Pt(14),
                    Alpha(UiFonts.Text, element.Enabled ? 1f : DisabledAlpha));
                break;
            case ElementKind.Divider:
                DrawRect(element.Rect, UiFonts.Line, true);
                break;
            case ElementKind.Header:
                // 设计稿：上下文行居中在 y+8，之后整块前进 28。
                EditorOverlayDraw.LeftText(this, MenuBold2, element.Rect.Position + new Vector2(0f, 8f),
                    element.Text, EditorFonts.Pt(14), element.Accent);
                break;
            case ElementKind.HeaderRight:
                EditorOverlayDraw.RightText(this, MenuTech1, PanelPad + RowWidth,
                    element.Rect.Position.Y + 8f, element.Text, 11, element.Accent);
                break;
            case ElementKind.CurveLabel:
                EditorOverlayDraw.LeftText(this, MenuTech2, element.Rect.Position + new Vector2(0f, 10f),
                    element.Text, 11, element.Accent);
                break;
            case ElementKind.Curve:
                EditorOverlayDraw.CutPanel(this, element.Rect, element.Cut,
                    element.Selected ? Alpha(UiFonts.Cyan, .14f) : new Color(0, 0, 0, 0),
                    element.Enabled ? (element.Selected ? UiFonts.Cyan : UiFonts.Line) : Alpha(UiFonts.Line, DisabledAlpha), 2f);
                DrawCurveIcon(element.Index, element.Rect.Position + new Vector2(22f, 12f),
                    element.Enabled
                        ? (element.Selected ? UiFonts.Cyan : UiFonts.Dim)
                        : Alpha(UiFonts.Dim, DisabledAlpha));
                break;
            case ElementKind.JudgeToggle:
            {
                var radius = JudgeToggleHeight * .5f;
                if (element.Selected)
                {
                    EditorOverlayDraw.CutPanel(this, element.Rect, radius,
                        Alpha(element.Accent, element.Enabled ? .9f : DisabledAlpha), new Color(0, 0, 0, 0), 0f);
                    DrawCircle(element.Rect.Position + new Vector2(JudgeToggleWidth - 16f, radius), 11f,
                        new Color("04121a"));
                }
                else
                {
                    var stroke = Alpha(UiFonts.Line, element.Enabled ? 1f : DisabledAlpha);
                    EditorOverlayDraw.CutStroke(this, element.Rect, radius, stroke, 2f);
                    DrawCircle(element.Rect.Position + new Vector2(16f, radius), 11f, stroke);
                }
                break;
            }
            case ElementKind.JudgeCaption:
                EditorOverlayDraw.LeftText(this, MenuTech2, element.Rect.Position + new Vector2(0f, 18f),
                    element.Glyph, EditorFonts.Pt(11), element.Accent);
                EditorOverlayDraw.LeftText(this, MenuTech1, element.Rect.Position + new Vector2(124f, 18f),
                    element.Text, EditorFonts.Pt(12), UiFonts.Text);
                break;
            case ElementKind.Row:
            {
                var alpha = element.Enabled ? 1f : DisabledAlpha;
                if (element.Enabled && hover > 0f) DrawRect(element.Rect, Alpha(UiFonts.Cyan, .10f * hover), true);
                var centerY = element.Rect.Position.Y + element.Rect.Size.Y * .5f;
                if (element.Glyph.Length > 0)
                    EditorOverlayDraw.LeftText(this, MenuBold1, new Vector2(element.Rect.Position.X, centerY),
                        element.Glyph, EditorFonts.Pt(element.GlyphSize),
                        Alpha(element.Strong ? element.Accent : UiFonts.Dim, alpha));
                var textX = element.Rect.Position.X + (element.Glyph.Length > 0 ? 32f : 0f);
                EditorOverlayDraw.LeftText(this, element.Strong ? MenuBold1 : MenuTech1,
                    new Vector2(textX, centerY), element.Text, EditorFonts.Pt(13), Alpha(element.Accent, alpha));
                break;
            }
            case ElementKind.Command:
                if (element.Enabled && hover > 0f) DrawRect(element.Rect, Alpha(UiFonts.Cyan, .10f * hover), true);
                EditorOverlayDraw.LeftText(this, MenuBold1,
                    new Vector2(element.Rect.Position.X, element.Rect.Position.Y + CommandTextRow * .5f),
                    element.Text, EditorFonts.Pt(13), Alpha(element.Accent, element.Enabled ? 1f : DisabledAlpha));
                break;
            case ElementKind.Slider:
            {
                // 设计稿的滑条行中心是行顶 +12（行距 36 只是下一行的步距）。
                var centerY = element.Rect.Position.Y + 12f;
                EditorOverlayDraw.LeftText(this, MenuTech1, new Vector2(element.Rect.Position.X, centerY),
                    element.Text, EditorFonts.Pt(11), Alpha(UiFonts.Text, DisabledAlpha));
                var track = new Rect2(SliderTrackX, centerY - 2f, SliderTrackWidth, 4f);
                DrawRect(track, Alpha(UiFonts.Text, .12f * DisabledAlpha), true);
                DrawRect(new Rect2(track.Position, new Vector2(SliderTrackWidth * element.Value, 4f)),
                    Alpha(element.Accent, DisabledAlpha), true);
                DrawRect(new Rect2(track.Position.X + SliderTrackWidth * element.Value, centerY - 7f, 3f, 14f),
                    Alpha(UiFonts.Text, DisabledAlpha), true);
                EditorOverlayDraw.RightText(this, MenuBold, PanelPad + RowWidth, centerY,
                    $"{Mathf.RoundToInt(element.Value * 100f)}%", EditorFonts.Pt(12), Alpha(element.Accent, DisabledAlpha));
                break;
            }
        }
    }

    /// <summary>设计稿的六段曲线图标（24×16 的线性/台阶/三种缓动/平滑）。</summary>
    private void DrawCurveIcon(int index, Vector2 origin, Color color)
    {
        const float stroke = 2f;
        switch (index)
        {
            case 1:
                DrawPolyline([
                    origin + new Vector2(2f, 14f), origin + new Vector2(13f, 14f),
                    origin + new Vector2(13f, 2f), origin + new Vector2(22f, 2f),
                ], color, stroke, true);
                break;
            case 2:
                DrawPolyline(Quadratic(origin + new Vector2(2f, 14f), origin + new Vector2(18f, 14f),
                    origin + new Vector2(22f, 2f)), color, stroke, true);
                break;
            case 3:
                DrawPolyline(Quadratic(origin + new Vector2(2f, 14f), origin + new Vector2(2f, 2f),
                    origin + new Vector2(22f, 2f)), color, stroke, true);
                break;
            case 4:
                DrawPolyline(Cubic(origin + new Vector2(2f, 14f), origin + new Vector2(10f, 14f),
                    origin + new Vector2(14f, 2f), origin + new Vector2(22f, 2f)), color, stroke, true);
                break;
            case 5:
                DrawPolyline(Quadratic(origin + new Vector2(2f, 10f), origin + new Vector2(7f, 2f),
                    origin + new Vector2(12f, 10f)), color, stroke, true);
                DrawPolyline(Quadratic(origin + new Vector2(12f, 10f), origin + new Vector2(17f, 18f),
                    origin + new Vector2(22f, 10f)), color, stroke, true);
                break;
            default:
                DrawLine(origin + new Vector2(2f, 14f), origin + new Vector2(22f, 2f), color, stroke);
                break;
        }
    }

    private static Vector2[] Quadratic(Vector2 start, Vector2 control, Vector2 end, int steps = 12)
    {
        var points = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1f - t;
            points[i] = u * u * start + 2f * u * t * control + t * t * end;
        }
        return points;
    }

    private static Vector2[] Cubic(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, int steps = 16)
    {
        var points = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1f - t;
            points[i] = u * u * u * start + 3f * u * u * t * control1 +
                3f * u * t * t * control2 + t * t * t * end;
        }
        return points;
    }

    private static Color Alpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);
}

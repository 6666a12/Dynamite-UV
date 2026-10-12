using Godot;
using System;
using System.Collections.Generic;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>Small Godot shell for the desktop DynaMaker interaction model.</summary>
public partial class StandaloneEditorMain : Control
{
    private AuthoringCanvas _canvas = null!;
    private ContextMenuOverlay _menu = null!;
    /// <summary>视图时间：HUD 读数、进度线、note 位置都消费它。滚轮只推 <see cref="_timeTarget"/>，
    /// 这里是被惯性趋近后的**显示值**（见 docs/editor-ui-design.md §4 第 14 条）。写入文档的时间逻辑
    /// 走文档自己的时间字段，不经这里。</summary>
    private double _time;
    /// <summary>滚轮滚动的目标时间。播放中每帧跟随 <see cref="_time"/>（播放是音频/视觉时钟，
    /// 惯性完全旁路，不能拖尾）。</summary>
    private double _timeTarget;
    private double? _mark;
    private bool _playing;
    private int _side;
    private int _lastPlayableSide;
    private double _hispeed = 1.0;
    private double _audioRate = 1.0;
    private bool _rollReverse = true;

    // 正文字体统一走 Medium（窗口 0.833 折 + Regular 偏细，见 EditorFonts），EditorFlow 同用这一份。
    private static readonly Font TechFont = EditorFonts.Medium;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildShell();
        _canvas.ContextMenuRequested += ShowCanvasMenu;
        _canvas.SetSide(0);
        _canvas.SetTool(4);
        InitializeEditorFlow();
    }

    public override void _Process(double delta)
    {
        // 惯性推进放在 HUD 刷新之前：HUD 的时间读数、进度线、note 位置这一帧就要读到趋近后的显示值。
        StepViewTime(delta);
        RefreshSaveState();
        RefreshEditHud();
        // Start 页 SETTINGS 文字键的 hover 过渡：值未到终值才每帧写颜色覆盖，到终值后零成本。
        if (_settingsKey is { } settingsKey)
        {
            var before = _settingsHover.Value;
            _ = _settingsHover.Update(settingsKey.IsHovered(), delta);
            if (_settingsHover.Value != before)
                settingsKey.AddThemeColorOverride("font_color", FlowMuted.Lerp(FlowCyan, _settingsHover.Value));
        }
    }

    /// <summary>视图时间的每帧推进。播放时显示值与目标值一起走（播放的时间是音频/视觉时钟，
    /// 惯性完全旁路，否则 HUD 与进度线会在播放中拖尾）；否则按惯性趋近滚轮推出来的目标值。</summary>
    private void StepViewTime(double delta)
    {
        if (_playing)
        {
            _time += delta * _hispeed * _audioRate;
            _timeTarget = _time;
            _canvas.PreviewTime = _time;
            return;
        }
        if (_time == _timeTarget) return;
        _time = EditorScrollInertia.Step(_time, _timeTarget, EditorSettings.Current.ScrollInertiaTauSeconds, delta);
        _canvas.PreviewTime = _time;
    }

    /// <summary>把视图时间直接落到某处：键盘/按钮跳转、程序性刷新（打开文档）都走这条，
    /// 显示值与目标值一起写、不播惯性。</summary>
    private void JumpTime(double seconds)
    {
        _time = Math.Clamp(seconds, 0d, _canvas.DurationSeconds);
        _timeTarget = _time;
        _canvas.PreviewTime = _time;
    }

    /// <summary>滚轮增量：只推目标值，显示值交给 <see cref="StepViewTime"/> 的指数趋近。
    /// 惯性关掉时（滑条最左，或 MOTION = OFF）立刻落目标，与改动前的瞬时滚轮一致；
    /// 播放中同样即时——播放时间由时钟推进，给视图时间留拖尾会让 HUD 与进度线滞后于音频。</summary>
    private void NudgeTime(double seconds)
    {
        if (_playing)
        {
            JumpTime(_time + seconds);
            return;
        }
        _timeTarget = Math.Clamp(_timeTarget + seconds, 0d, _canvas.DurationSeconds);
        if (EditorSettings.Current.ScrollInertiaTauSeconds <= 0d) JumpTime(_timeTarget);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton wheel && wheel.Pressed &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown &&
            _canvas is not null && _flowPage == EditorFlowPage.Edit &&
            _canvas.GetGlobalRect().HasPoint(wheel.Position))
        {
            if (_menu is null || !_menu.Visible)
            {
                var up = wheel.ButtonIndex == MouseButton.WheelUp;
                var direction = (_rollReverse == up) ? 1d : -1d;
                var step = 100d / (AuthoringCanvas.PixelsPerSecond * Math.Max(.1d, _hispeed));
                NudgeTime(direction * step);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (_menu is null || !_menu.Visible) return;
        if (@event is InputEventMouseMotion motion)
        {
            _menu.HandleGlobalMouse(motion.Position, MouseButton.None, false);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventMouseButton button || !button.Pressed) return;
        if (_menu.ContainsGlobalPoint(button.Position))
        {
            _menu.HandleGlobalMouse(button.Position, button.ButtonIndex, true);
            if (!_menu.Visible) CallDeferred(nameof(ReleaseMenuInput));
            GetViewport().SetInputAsHandled();
        }
        else
        {
            _menu.Hide();
            CallDeferred(nameof(ReleaseMenuInput));
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildShell()
    {
        // ChartCanvas and its SharedGameplayStage are scene-owned. Keeping the
        // stage in the scene makes the editor and gameplay use the same visible
        // hierarchy instead of hiding the migration inside runtime construction.
        _canvas = GetNodeOrNull<AuthoringCanvas>("ChartCanvas")
            ?? throw new InvalidOperationException("editor_main.tscn is missing ChartCanvas");

        // 裸舞台不带 shell 自诊断文字：Edit 主态只有 EditorFlow 叠加的四角 HUD（+ 路径编辑态的
        // 顶中 `PATH EDIT`），保存/错误回报走 Edit 页的保存状态行，其余页走各自的消息通道。

        _menu = new ContextMenuOverlay();
        _menu.MenuClicked += HandleMenu;
        AddChild(_menu);
        // 窗口级拖放统一在 shell 接一次（Godot 4 的 FilesDropped 挂在根 Window 上），
        // 按当前页面路由：Start/Create 各自回填，Pack/Edit/Settings 不响应（见 HandleFilesDropped）。
        GetWindow().FilesDropped += HandleFilesDropped;
    }

    /// <summary>窗口拖放路由。坐标换算：FilesDropped 不带位置，用页面 Control 的
    /// <see cref="Control.GetGlobalMousePosition"/>（画布变换后的视口坐标，页面 rect 都是
    /// 1920×1080 视口系）判落点，比手动换算 GetMousePosition 稳。只有 Start 与 Create 收文件。</summary>
    private void HandleFilesDropped(string[] files)
    {
        if (files is not { Length: > 0 }) return;
        switch (_flowPage)
        {
            case EditorFlowPage.Start:
                TryAcceptStartDrop(files);
                break;
            case EditorFlowPage.Create:
                _createOverlay.TryAcceptDrop(_createOverlay.GetGlobalMousePosition(), files);
                break;
        }
        // Pack / Edit / Settings 拖入不响应：这些页面没有外部文件的回填位，静默忽略。
    }

    /// <summary>Start 页拖放：目录回填路径行（不直接打开，与 BROWSE 卡行为一致）；文件给提示不收
    /// （Start 页没有音频/封面的回填位，避免把外部资源引到错误的页面语义上）。</summary>
    private void TryAcceptStartDrop(string[] files)
    {
        var first = files[0];
        if (Directory.Exists(first))
        {
            _packagePath.Text = first;
            SetFlowMessage("已选择目录 · 按 OPEN 或回车打开。", false);
            return;
        }
        SetFlowMessage("Start 页只收目录（V2 包目录）；文件请到 Create 页拖入音频/封面。", true);
    }

    /// <summary>右键菜单只按画布当前的选中状态取内容：选中路径节点 → Node Menu；
    /// 其余（含框选非空、空选区）→ 同一块面板，只有选区相关行随选择出现。</summary>
    private void ShowCanvasMenu(Vector2 position)
    {
        _side = _canvas.CurrentSide;
        _canvas.InputSuspended = true;
        _menu.Open(position, BuildMenuState());
    }

    /// <summary>菜单面板的数据快照：读数取真实选中对象，可交互性取命令层真实能力。</summary>
    private ContextMenuOverlay.MenuState BuildMenuState()
    {
        var state = new ContextMenuOverlay.MenuState
        {
            Side = _canvas.CurrentSide,
            Tool = _canvas.Tool,
            HasSelection = _canvas.HasSelection,
            SelectionCount = _canvas.SelectedCount,
            CursorBar = _canvas.CursorBarText,
        };
        var target = _canvas.SingleSelection;
        if (target is null) return state;
        state.NodeMode = target.IsNode;
        state.Header = $"{AuthoringCanvas.KindLabel(target.Kind)} · NODE {target.NodeIndex + 1}/{target.NodeCount}";
        state.CurveName = AuthoringCanvas.CurveLabel(target.CurveToNext).ToUpperInvariant();
        state.TimeText = ExactBarTimeText.Format(target.NodeTime);
        state.CenterText = target.NodeCenter.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        state.WidthText = target.NodeWidth.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        state.StepsEnabled = true;
        state.CurveIndex = (int)target.CurveToNext;
        state.CurveEnabled = target.IsNode && !target.IsTail;
        state.JudgeRow = target.IsNode && target.Kind == AuthoringCanvas.AuthoringNoteKind.Hold;
        state.JudgeOn = target.Judge;
        state.JudgeLocked = target.IsTail;
        state.NodeCount = target.NodeCount;
        state.AddNodeEnabled = _canvas.CanInsertPathNodeAtCursor;
        return state;
    }

    private void HandleMenu(long id)
    {
        switch (id)
        {
            case ContextMenuOverlay.ActSideBase + 0: SetPlayableSide(1); break;
            case ContextMenuOverlay.ActSideBase + 1: SetPlayableSide(0); break;
            case ContextMenuOverlay.ActSideBase + 2: SetPlayableSide(2); break;
            case ContextMenuOverlay.ActSideBase + 3: _side = 3; _canvas.SetSide(3); break;
            case ContextMenuOverlay.ActToolBase + 1: _canvas.SetTool(1); break;
            case ContextMenuOverlay.ActToolBase + 2: _canvas.SetTool(2); break;
            case ContextMenuOverlay.ActToolBase + 3: _canvas.SetTool(3); break;
            case ContextMenuOverlay.ActToolBase + 4: _canvas.SetTool(4); break;
            case ContextMenuOverlay.ActTimeMinus: _canvas.StepSelection(EditorStepProperty.Time, -1); break;
            case ContextMenuOverlay.ActTimePlus: _canvas.StepSelection(EditorStepProperty.Time, 1); break;
            case ContextMenuOverlay.ActCenterMinus: _canvas.StepSelection(EditorStepProperty.Center, -1); break;
            case ContextMenuOverlay.ActCenterPlus: _canvas.StepSelection(EditorStepProperty.Center, 1); break;
            case ContextMenuOverlay.ActWidthMinus: _canvas.StepSelection(EditorStepProperty.Width, -1); break;
            case ContextMenuOverlay.ActWidthPlus: _canvas.StepSelection(EditorStepProperty.Width, 1); break;
            case ContextMenuOverlay.ActDelete: _canvas.DeleteSelection(); break;
            case ContextMenuOverlay.ActJudge: _canvas.ToggleSelectedJudge(); break;
            case ContextMenuOverlay.ActDeleteNode: _canvas.DeleteSelectedPathNode(); break;
            case ContextMenuOverlay.ActAddNode: _canvas.InsertPathNodeAtCursor(); break;
            case ContextMenuOverlay.ActMarkHere: _mark = _time; break;
            // 「从标记/从头重播」是按钮跳转：显示值与目标值一起落，惯性不参与。
            case ContextMenuOverlay.ActPlayFromMark: JumpTime(_mark ?? 0); _playing = true; break;
            case ContextMenuOverlay.ActReplay: JumpTime(0); _playing = true; break;
            case ContextMenuOverlay.ActSave: RequestSave(false); break;
            case ContextMenuOverlay.ActSaveAs: RequestSave(true); break;
            case ContextMenuOverlay.ActSettings: OpenSettings(EditorFlowPage.Edit); break;
            default:
                if (id >= ContextMenuOverlay.ActCurveBase &&
                    id < ContextMenuOverlay.ActCurveBase + ContextMenuOverlay.CurveCount)
                    _canvas.SetSelectedCurve((V2PathCurve)(id - ContextMenuOverlay.ActCurveBase));
                break;
        }
        CallDeferred(nameof(ReleaseMenuInput));
    }

    private void ReleaseMenuInput()
    {
        // 菜单之外还有页面流：只有回到 Edit 主态才把画布交还输入，
        // 否则从菜单进 Settings 时这次 deferred 释放会把设置页的点击穿透到舞台。
        _canvas.InputSuspended = _flowPage != EditorFlowPage.Edit;
        _canvas.ClearPointerHover();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || key.Echo) return;
        if (_menu is not null && _menu.Visible)
        {
            if (key.Pressed && key.Keycode == Key.Escape)
            {
                _menu.Hide();
                CallDeferred(nameof(ReleaseMenuInput));
            }
            return;
        }
        // 菜单之外：文本框持有焦点时编辑器快捷键一律不触发（打字不能被画布命令吞掉）。
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;
        // 页面门控：所有画布/播放快捷键只在 Edit 主态生效（Z/X snap override、Ctrl+S 保存、
        // 数字工具键/Delete/Space/F1/M/R/方向键等一律不落到隐藏画布上）。门控放在派发之前，
        // 按住 Z/X 期间切页，松开事件同样被挡。
        if (_flowPage != EditorFlowPage.Edit) return;
        if (key.Keycode == Key.Z) { _canvas.TimeSnapOverride = !key.Pressed; return; }
        if (key.Keycode == Key.X) { _canvas.SpaceSnapOverride = !key.Pressed; return; }
        if (!key.Pressed) return;
        if (key.Keycode == Key.S && key.CtrlPressed) { RequestSave(key.ShiftPressed); return; }
        if (key.Keycode is Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4)
        {
            if (_side == 3)
            {
                if (key.Keycode == Key.Key1) _canvas.SetTool(4);
                return;
            }
            _canvas.SetTool((int)key.Keycode - (int)Key.Key1 + 1);
            return;
        }
        if (key.Keycode is Key.Delete or Key.Backspace) { _canvas.DeleteSelection(); return; }
        if (key.Keycode == Key.Space) { SetAuto(!_playing); return; }
        if (key.Keycode == Key.F1) { SetAuto(!_auto); return; }
        // 以下时间跳转（M/R/Enter/A/D）都是键盘动作，直接落目标、不播惯性。
        if (key.Keycode == Key.M) { JumpTime(_mark ?? 0); _playing = true; return; }
        if (key.Keycode == Key.R)
        {
            if (key.ShiftPressed)
            {
                _canvas.ResetGridDivisor();
                _hispeed = 1.0;
                _audioRate = 1.0;
            }
            JumpTime(0);
            _playing = true;
            return;
        }
        if (key.Keycode == Key.Enter) { JumpTime(0); _canvas.SetTool(4); return; }
        if (key.Keycode == Key.B) { _rollReverse = !_rollReverse; return; }
        if (key.Keycode is Key.Left or Key.Right)
        {
            if (key.ShiftPressed)
            {
                if (key.Keycode == Key.Left) _canvas.Undo(); else _canvas.Redo();
                return;
            }
            _canvas.CycleTrackVisibility(key.Keycode == Key.Left ? 1 : 2);
            return;
        }
        if (key.Keycode == Key.Up) { SetPlayableSide((_side + 1) % 3); return; }
        if (key.Keycode == Key.Down) { _canvas.CycleTrackVisibility(0); return; }
        if (key.Keycode is Key.A or Key.D) { JumpTime(_time + (key.Keycode == Key.A ? -1 : 1) * (key.ShiftPressed ? .01 : 1)); return; }
        if (key.Keycode is Key.Q or Key.E) { _hispeed = Math.Clamp(_hispeed + (key.Keycode == Key.Q ? -.1 : .1), .1, 8); return; }
        if (key.Keycode is Key.S or Key.W) { _audioRate = Math.Clamp(_audioRate + (key.Keycode == Key.S ? -.01 : .01), .25, 2); return; }
        if (key.Keycode == Key.C) { _canvas.AdjustGridDivisor(false, key.ShiftPressed); return; }
        if (key.Keycode == Key.V) { _canvas.AdjustGridDivisor(true, key.ShiftPressed); return; }
    }

    private void SetPlayableSide(int side)
    {
        _lastPlayableSide = Math.Clamp(side, 0, 2);
        _side = _lastPlayableSide;
        _canvas.SetSide(_side);
    }

    /// <summary>页面的普通文字标签。<paramref name="designSize"/> 是设计稿字号，登记进
    /// <see cref="EditorFonts"/> 的注册表，UI SCALE 换档时统一重放。</summary>
    private static Label LabelAt(float x, float y, float w, float h, string text, int designSize, Color color)
    {
        var label = new Label
        { Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", TechFont);
        EditorFonts.ApplySize(label, designSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>右键菜单面板（设计稿 Editor — Context Menu / Editor — Node Menu）。面板几何、
    /// 行距、字号与色值取设计稿；每行的可交互性来自画布的真实能力，未实现的行 55% 压暗占位、
    /// 点击无效果。锚在光标处、近屏幕边缘翻转，只做整体 120ms 淡入。</summary>
    private sealed partial class ContextMenuOverlay : Control
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

        public ContextMenuOverlay()
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

}

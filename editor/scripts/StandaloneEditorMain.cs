using Godot;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain : Control
{
    private AuthoringCanvas _canvas = null!;
    private EditorContextMenu _menu = null!;
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
        _startView?.Tick(delta);
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

        _menu = new EditorContextMenu();
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
            _startView.PackagePath = first;
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
    private EditorContextMenu.MenuState BuildMenuState()
    {
        var state = new EditorContextMenu.MenuState
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
        state.JudgeRow = target.IsNode && target.Kind == AuthoringNoteKind.Hold;
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
            case EditorContextMenu.ActSideBase + 0: SetPlayableSide(1); break;
            case EditorContextMenu.ActSideBase + 1: SetPlayableSide(0); break;
            case EditorContextMenu.ActSideBase + 2: SetPlayableSide(2); break;
            case EditorContextMenu.ActSideBase + 3: _side = 3; _canvas.SetSide(3); break;
            case EditorContextMenu.ActToolBase + 1: _canvas.SetTool(1); break;
            case EditorContextMenu.ActToolBase + 2: _canvas.SetTool(2); break;
            case EditorContextMenu.ActToolBase + 3: _canvas.SetTool(3); break;
            case EditorContextMenu.ActToolBase + 4: _canvas.SetTool(4); break;
            case EditorContextMenu.ActTimeMinus: _canvas.StepSelection(EditorStepProperty.Time, -1); break;
            case EditorContextMenu.ActTimePlus: _canvas.StepSelection(EditorStepProperty.Time, 1); break;
            case EditorContextMenu.ActCenterMinus: _canvas.StepSelection(EditorStepProperty.Center, -1); break;
            case EditorContextMenu.ActCenterPlus: _canvas.StepSelection(EditorStepProperty.Center, 1); break;
            case EditorContextMenu.ActWidthMinus: _canvas.StepSelection(EditorStepProperty.Width, -1); break;
            case EditorContextMenu.ActWidthPlus: _canvas.StepSelection(EditorStepProperty.Width, 1); break;
            case EditorContextMenu.ActDelete: _canvas.DeleteSelection(); break;
            case EditorContextMenu.ActJudge: _canvas.ToggleSelectedJudge(); break;
            case EditorContextMenu.ActDeleteNode: _canvas.DeleteSelectedPathNode(); break;
            case EditorContextMenu.ActAddNode: _canvas.InsertPathNodeAtCursor(); break;
            case EditorContextMenu.ActMarkHere: _mark = _time; break;
            // 「从标记/从头重播」是按钮跳转：显示值与目标值一起落，惯性不参与。
            case EditorContextMenu.ActPlayFromMark: JumpTime(_mark ?? 0); _playing = true; break;
            case EditorContextMenu.ActReplay: JumpTime(0); _playing = true; break;
            case EditorContextMenu.ActSave: RequestSave(false); break;
            case EditorContextMenu.ActSaveAs: RequestSave(true); break;
            case EditorContextMenu.ActSettings: OpenSettings(EditorFlowPage.Edit); break;
            default:
                if (id >= EditorContextMenu.ActCurveBase &&
                    id < EditorContextMenu.ActCurveBase + EditorContextMenu.CurveCount)
                    _canvas.SetSelectedCurve((V2PathCurve)(id - EditorContextMenu.ActCurveBase));
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
}

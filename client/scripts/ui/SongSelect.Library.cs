using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.IO;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

public partial class SongSelect
{

    private const float TouchScrollDeadzone = 12f;
    private readonly List<SongRow> _rows = new();
    private IReadOnlyList<int> _visibleOrder => _browse.VisibleIndices;
    private ScrollContainer _songScroll = null!;
    private VBoxContainer _rowBox = null!;
    private int? _scrollTouchId;
    private float _scrollTouchStartY;
    private int _scrollTouchStartOffset;
    private bool _scrollTouchMoved;
    /// <summary>惯性滚动状态（见 <see cref="ScrollInertia"/>）：滚轮与触摸板只推 <see cref="_scrollTarget"/>，
    /// <see cref="_scrollDisplay"/> 是逐帧趋近后的显示值。<see cref="_scrollWritten"/> 记录上一次写进
    /// <see cref="ScrollContainer.ScrollVertical"/> 的整数，用来识别"这个值被别人改了"（选中行
    /// EnsureControlVisible、滚动条拖动、触摸拖动、键盘翻页），那些都是直接跳、惯性要让位。</summary>
    private bool _scrollAnimating;
    private float _scrollDisplay;
    private float _scrollTarget;
    private int _scrollWritten;

    /// <summary>卡的展开态由 <see cref="SongDetailCard"/> 持有（收拢落点才转 false），
    /// 页面只读它决定行的隐藏与试听/详情请求是否仍有效。</summary>
    private bool _expanded => _detailCard.IsPresenting;

    /// <summary>本次滚动的有效时间常数：client 没有惯性强弱设置项，用固定常量；但
    /// GENERAL / MOTION = OFF 时按"无动效"语义强制为 0（与编辑器同一选择，见
    /// docs/editor-ui-design.md §4 第 14 条）。</summary>
    private static double ScrollTau =>
        GameSession.Settings.MotionMode == UiMotionMode.Off ? 0d : ScrollInertia.TauSeconds;

    /// <summary>惯性推进列表滚动。与编辑器的滚轮处理同一模型：推力只改
    /// <see cref="_scrollTarget"/>，显示值按帧率无关的指数趋近。
    ///
    /// <see cref="_scrollDisplay"/> 是浮点累加器，只在写进 <see cref="ScrollContainer.ScrollVertical"/> 时取整：
    /// 若把取整后的值当状态，尾巴上不足 1px 的步进会被反复抹掉，动画永远收敛不了。
    ///
    /// 检测"别人改了滚动位置"是必要的：<see cref="ScrollContainer.EnsureControlVisible"/>（选中行/展开卡）、
    /// 拖动滚动条、触摸拖动都会直接写 <c>ScrollVertical</c>。这些是直接跳转，必须以它们为准并中止惯性，
    /// 否则惯性会在下一帧把它们拽回去。</summary>
    private void StepListScroll(double delta)
    {
        var actual = _songScroll.ScrollVertical;
        if (actual != _scrollWritten)
        {
            _scrollWritten = actual;
            _scrollDisplay = actual;
            _scrollTarget = actual;
            _scrollAnimating = false;
            return;
        }
        if (!_scrollAnimating) return;

        var reached = Mathf.Abs(_scrollTarget - _scrollDisplay) <= ScrollInertia.SnapPixels;
        if (reached) _scrollDisplay = _scrollTarget;
        else _scrollDisplay = ScrollInertia.Step(_scrollDisplay, _scrollTarget, ScrollTau, delta);
        _scrollAnimating = !reached;
        WriteListScroll();
    }

    /// <summary>可滚动的最大偏移。取内部 VScrollBar 的 max−page（Godot 把 page 当作最小 max），
    /// 内容不超高时为 0。</summary>
    private float MaxListScroll()
    {
        var bar = _songScroll.GetVScrollBar();
        return Mathf.Max(0f, (float)(bar.MaxValue - bar.Page));
    }

    /// <summary>把显示值写进容器并回读真实落点：ScrollContainer 会把越界值钳到可滚范围之内。
    /// 回读不一致说明这个位置够不到（内容高度或页高这一帧变了），累加器与目标一起拉回来，
    /// 否则惯性会每帧重走却前进不了。</summary>
    private void WriteListScroll()
    {
        var wanted = Mathf.RoundToInt(_scrollDisplay);
        _songScroll.ScrollVertical = wanted;
        var landed = _songScroll.ScrollVertical;
        if (landed != wanted)
        {
            _scrollDisplay = landed;
            _scrollTarget = landed;
            _scrollAnimating = false;
        }
        _scrollWritten = landed;
    }

    /// <summary>推一次列表滚动增量（滚轮一格 / 触摸板一段）。惯性途中继续加在目标上，
    /// 所以快速连滚会累积而不是每一格从头开始。惯性为 0（MOTION = OFF）时直接落目标，
    /// 与改动前的瞬时逐格滚动一致。</summary>
    private void PushListScroll(float pixels)
    {
        if (!_scrollAnimating)
        {
            // 新手势以真实位置起步，不沿用上一次外部改动留下的旧显示值。
            var actual = _songScroll.ScrollVertical;
            _scrollDisplay = actual;
            _scrollTarget = actual;
            _scrollWritten = actual;
        }
        _scrollTarget = Mathf.Clamp(_scrollTarget + pixels, 0f, MaxListScroll());
        if (ScrollTau <= 0d)
        {
            _scrollDisplay = _scrollTarget;
            WriteListScroll();
            _scrollAnimating = false;
            return;
        }
        _scrollAnimating = true;
    }

    /// <summary>滚轮一格的距离沿用 ScrollContainer 原生口径（页高 / 8），逐格距离与改动前一致，
    /// 差别只在它是滑过去而不是瞬移。</summary>
    private float WheelScrollPixels(float factor) =>
        (float)(_songScroll.GetVScrollBar().Page / 8.0) * factor;

    public override void _Input(InputEvent e)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;

        // 滚轮/触摸板滚动先接管（见 HandleListScrollInput 的注释）：必须在 GUI 拿到事件之前，
        // 否则 ScrollContainer 会先把位置瞬移掉。事件已被消费就直接返回，不再走后面的触摸分支。
        if (HandleListScrollInput(e))
            return;

        // 搜索框聚焦时 Esc 先清空并交出焦点，避免直接吃掉整页的返回语义。
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } &&
            _search.HasFocus())
        {
            _search.Text = "";
            ApplySearchFilter();
            _search.ReleaseFocus();
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (e)
        {
            case InputEventScreenTouch touch when touch.Pressed &&
                _scrollTouchId is null && ListRect.HasPoint(touch.Position):
                _scrollTouchId = touch.Index;
                _scrollTouchStartY = touch.Position.Y;
                _scrollTouchStartOffset = _songScroll.ScrollVertical;
                _scrollTouchMoved = false;
                // 手指落下就交出手指跟随：惯性拖尾会顶住手指下的内容。
                // 触摸拖动本身是直接操纵（内容跟手），保持 1:1，不吃惯性。
                StopListInertia();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenDrag drag when _scrollTouchId == drag.Index:
            {
                var delta = drag.Position.Y - _scrollTouchStartY;
                if (Math.Abs(delta) >= TouchScrollDeadzone)
                    _scrollTouchMoved = true;
                if (_scrollTouchMoved)
                    _songScroll.ScrollVertical = _scrollTouchStartOffset - Mathf.RoundToInt(delta);
                GetViewport().SetInputAsHandled();
                break;
            }
            case InputEventScreenTouch touch when !touch.Pressed &&
                _scrollTouchId == touch.Index:
                if (!_scrollTouchMoved && ListRect.HasPoint(touch.Position))
                    SelectVisibleRowAt(touch.Position);
                _scrollTouchId = null;
                _scrollTouchMoved = false;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    /// <summary>滚轮与触摸板滚动在 <c>_Input</c> 里截住（它先于 GUI 派发，见 Godot 的输入顺序）：
    /// 交给 ScrollContainer 自己处理就是瞬移，接管后才谈得上惯性。返回 true 表示本次事件已被
    /// 消费（已 SetInputAsHandled）；指针不在列表上时不拦截，让事件照常往下走。</summary>
    private bool HandleListScrollInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel
                when ListRect.HasPoint(wheel.Position):
            {
                var up = wheel.ButtonIndex == MouseButton.WheelUp;
                PushListScroll(WheelScrollPixels((up ? -1f : 1f) * wheel.Factor));
                GetViewport().SetInputAsHandled();
                return true;
            }
            // 触摸板两指滚动走 PanGesture，位移口径与 ScrollContainer 原生一致（页高 / 8）。
            case InputEventPanGesture pan when ListRect.HasPoint(pan.Position):
                PushListScroll(WheelScrollPixels(pan.Delta.Y));
                GetViewport().SetInputAsHandled();
                return true;
        }
        return false;
    }

    /// <summary>中止惯性：显示值与目标值停在当前位置（滚轮连滚中被外部改动打断时用）。</summary>
    private void StopListInertia()
    {
        _scrollAnimating = false;
        _scrollDisplay = _songScroll.ScrollVertical;
        _scrollTarget = _scrollDisplay;
        _scrollWritten = _songScroll.ScrollVertical;
    }

    private static Rect2 ListRect => new(ListX, ListY, ListWidth, ListHeight);

    // ---- 列表交互：选择 / 展开 / 搜索 ----

    private void OnRowPressed(int index)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        if (_expanded && index == _packIdx)
        {
            CollapseCard();
            return;
        }
        var changedPack = index != _packIdx;
        if (changedPack)
        {
            _browse.SelectPack(index);
            GameSession.ClearPreload();
        }
        ExpandCard();
        BeginRefreshAll();
    }

    private void SelectVisibleRowAt(Vector2 position)
    {
        var contentY = position.Y - ListY + _songScroll.ScrollVertical;
        var stride = RowHeight + RowGap;
        var index = Mathf.FloorToInt(contentY / stride);
        var withinRow = contentY - index * stride;
        if (withinRow < 0 || withinRow > RowHeight)
            return;
        if (index < 0 || index >= _visibleOrder.Count)
            return;
        OnRowPressed(_visibleOrder[index]);
    }

    private void ExpandCard()
    {
        if (_packIdx < 0 || _packIdx >= _rows.Count)
            return;
        // 可见性与高度归组件（Full 从行高占位撑开，Reduced/Off 当场贴满）。
        _detailCard.Expand();
        // 统一走过滤器刷新行可见性：既藏掉本次展开的那一行，也把**上一次**展开的行恢复成
        // 普通行。只写 `_rows[_packIdx].Visible = false` 会让旧行永久留在隐藏态，
        // 连续切歌时列表会一行行消失。
        ApplySearchFilter();
        _rowBox.MoveChild(_detailCard, _rows[_packIdx].GetIndex());
        CallDeferred(nameof(EnsureSelectedRowVisible));
        // 试听在 CompleteRefreshAll（谱面就绪）里启动。
    }

    private void CollapseCard()
    {
        if (!_expanded)
            return;
        StopPreview();
        _detailRequest.Invalidate();
        // 收拢动画期间展开态仍为 true：行维持隐藏、卡占位逐帧收缩，收完才换回普通行，
        // 避免收缩途中"行 + 半截卡"双重占高。途中再点行由 ExpandCard 干净反向。
        // 落点那一帧由 StepCardAnimations 拿到返回值后恢复普通行。
        if (_detailCard.Collapse())
            ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        var query = _search.Text.Trim();
        _browse.Filter(query);
        for (var i = 0; i < _rows.Count; i++)
        {
            var match = _browse.IsVisible(i);
            _rows[i].Visible = match && !(_expanded && i == _packIdx);
        }

        if (_rows.Count > 0)
        {
            _emptyState.Visible = _visibleOrder.Count == 0 && !_expanded;
            _emptyState.Text = "没有匹配的谱面";
        }

        // 选中项保持合法：被过滤掉就选第一个可见项（没有可见项时清空选择）。
        if (_visibleOrder.Count == 0)
        {
            if (_expanded)
                CollapseCard();
            _browse.ClearSelection();
            ApplyEmptySelection();
            return;
        }

        if (_packIdx < 0 || !_visibleOrder.Contains(_packIdx))
        {
            if (_expanded)
                CollapseCard();
            _browse.SelectPack(_visibleOrder[0]);
            GameSession.ClearPreload();
            BeginRefreshAll();
        }
    }

    private void EnsureSelectedRowVisible()
    {
        if (_expanded && IsInstanceValid(_detailCard))
        {
            _songScroll.EnsureControlVisible(_detailCard);
            return;
        }
        if (_packIdx >= 0 && _packIdx < _rows.Count && _rows[_packIdx].Visible)
            _songScroll.EnsureControlVisible(_rows[_packIdx]);
    }
}

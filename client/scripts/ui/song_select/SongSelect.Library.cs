using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

public partial class SongSelect
{

    private readonly List<SongRow> _rows = new();
    private IReadOnlyList<int> _visibleOrder => _browse.VisibleIndices;
    private ScrollContainer _songScroll = null!;
    private SongListViewport _listViewport = null!;

    private bool _expanded => _detailCard.IsPresenting;

    private static double ScrollTau =>
        GameSession.Settings.MotionMode == UiMotionMode.Off ? 0d : ScrollInertia.TauSeconds;

    public override void _Input(InputEvent e)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        if (_listViewport.HandleInput(e))
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
    }

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
        // Container geometry includes the animated detail card, filtering and row gaps.
        if (_detailCard.Visible && _detailCard.GetGlobalRect().HasPoint(position))
        {
            _detailCard.HandleListTap(position);
            return;
        }
        foreach (var index in _visibleOrder)
            if (_rows[index].Visible && _rows[index].GetGlobalRect().HasPoint(position))
            {
                OnRowPressed(index);
                return;
            }
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

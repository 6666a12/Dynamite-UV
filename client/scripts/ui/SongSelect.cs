using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.IO;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Ui;

/// <summary>
/// 选曲界面 v2（样式稿「Game — Song Select v2」）：
/// 左列 QUICK SETTINGS（MODE 两段互斥 + BLEED/MIRROR/AUTO 三个同规格开关 + 全部设置入口）、
/// 右列曲库（表头 + 搜索 + 筛选占位 + 就地展开的详情卡 + 预览播放）、底部操作条。
/// 几何与交互口径见 docs/editor-ui-design.md §2.1。
/// </summary>
public partial class SongSelect : Node2D
{

    private SongBrowseState _browse = null!;
    private int _packIdx => _browse.PackIndex;
    private int _diffIdx => _browse.DifficultyIndex;
    private bool _localTransitionBusy;
    private Node2D _topRoot = null!;
    private Node2D _settingsRoot = null!;
    private Node2D _listRoot = null!;
    private Node2D _bottomRoot = null!;

    public override void _Ready()
    {
        GameSession.EnsureInit();
        _browse = new SongBrowseState(GameSession.Packs.Select(pack => new SongBrowseEntry(
            pack.Id, pack.Title, pack.Artist, pack.Charts.Select(chart =>
                new SongBrowseChart(chart.ChartId, chart.CharterDisplay)).ToArray())).ToArray());

        UiLayout.AddBackground(this);

        _topRoot = new Node2D { Name = "SongSelectTop" };
        _settingsRoot = new Node2D { Name = "SongSelectQuickSettings" };
        _listRoot = new Node2D { Name = "SongSelectLibrary" };
        _bottomRoot = new Node2D { Name = "SongSelectBottom" };
        AddChild(_topRoot);
        AddChild(_settingsRoot);
        AddChild(_listRoot);
        AddChild(_bottomRoot);

        _previewPlayer = new SongPreviewPlayer(this, Res.LoadAudio);

        BuildTopBar();
        BuildQuickSettings();
        BuildLibrary();
        BuildBottomBar();
        RefreshQuickSettings();
        ApplySearchFilter();

        RestoreCommittedSelection();
        BeginRefreshAll();
        SetProcess(true);
        CallDeferred(MethodName.EnsureSelectedRowVisible);
        PrepareEnterAnimation();
        TransitionDirector.ReportSceneReady(PlayEnterAnimation);
    }

    public override void _Process(double delta)
    {
        // 列表滚动的惯性推进先做：滚轮的显示值要每帧追上目标（见 ScrollInertia）。
        StepListScroll(delta);
        if (_cardAnimating)
            StepCardAnimations(delta);
        CompleteChartDetailsLoad();
        CompletePreviewLoad();
        if (!_previewPlayer.Playing)
            return;
        // 播放器位置是流内位置（含 Play(from) 的起点），减去起点即本次试听已播时长。
        UpdatePreviewReadout(_previewPlayer.Elapsed);
    }

    public override void _ExitTree()
    {
        _detailRequest.Dispose();
        StopPreview();
        _previewPlayer.Dispose();
    }

    // ---- 选择 / 加载 / 结算联动（沿用既有流程） ----

    private void RestoreCommittedSelection()
    {
        var selection = GameSession.CurrentSelection;
        if (selection is null)
            return;

        _browse.Restore(selection.Pack.Id, selection.Diff.ChartId);
    }

    private void BrowseNextDifficulty()
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;
        _browse.NextDifficulty(pack.Charts.Count);
        BeginRefreshAll();
    }

    private LoadedChart? GetOrCacheChart(ChartPack pack, ChartDiff diff)
    {
        if (GameSession.GetPreload(pack, diff) is { } cached)
            return cached;
        if (!pack.TryLoadChart(diff, out var loaded) || loaded is null)
            return null;
        GameSession.CachePreload(pack, diff, loaded);
        return loaded;
    }

    private void CommitAndStart()
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;

        _browse.ClampDifficulty(pack.Charts.Count);
        var diff = pack.Charts[_diffIdx];
        var loaded = GetOrCacheChart(pack, diff);
        if (loaded is null)
            return;

        StopPreview();
        GameSession.CommitSelection(pack, diff, loaded);
        var profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        var texture = _detailCard.CoverTexture;
        CoverTextureCache.Remember(pack.CoverPath, texture);
        var level = diff.Level is { } value ? $"Lv {value}" : "UNRATED";
        var relay = new TrackRelayPresentation(
            pack.Title,
            $"{diff.Display.ToUpperInvariant()} · {level}",
            texture,
            RelaySource.Selection);

        _localTransitionBusy = true;
        _startBtn.CommitPulse(profile.FocusDuration);
        if (_packIdx >= 0 && _packIdx < _rows.Count)
            _rows[_packIdx].CommitPulse(profile.FocusDuration);
        SetSelectionActionsEnabled(false);

        if (!profile.IsAnimated)
        {
            BeginGameplayNavigation(relay);
            return;
        }

        GetTree().CreateTimer(profile.FocusDuration).Timeout += () =>
        {
            if (IsInsideTree())
                BeginGameplayNavigation(relay);
        };

        void BeginGameplayNavigation(TrackRelayPresentation presentation)
        {
            var started = TransitionDirector.Navigate(
                UiRoutes.Gameplay,
                TransitionKind.Gameplay,
                "TRACK HANDOFF",
                $"{pack.Title} · {diff.Display.ToUpperInvariant()} · {level}",
                relay: presentation);
            if (!started)
            {
                _localTransitionBusy = false;
                SetSelectionActionsEnabled(true);
            }
        }
    }

    private void SetSelectionActionsEnabled(bool enabled)
    {
        _diffBtn.Disabled = !enabled;
        _startBtn.Disabled = !enabled ||
            (CurrentPack is not { } pack || pack.Charts.Count == 0 ||
             GameSession.GetPreload(pack, pack.Charts[Mathf.Clamp(
                 _diffIdx, 0, pack.Charts.Count - 1)]) is null);
        foreach (var row in _rows)
            row.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
    }

    private ChartPack? CurrentPack =>
        _packIdx >= 0 && _packIdx < GameSession.Packs.Count
            ? GameSession.Packs[_packIdx]
            : null;

    private void NavigateBack()
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy)
            return;
        StopPreview();
        TransitionDirector.Navigate(UiRoutes.Main, TransitionKind.Back);
    }
}

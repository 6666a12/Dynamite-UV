using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.IO;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;
namespace DynamiteUniverse.Ui;

public partial class SongSelect
{
    private SongPreviewPlayer _previewPlayer = null!;
    private readonly SongDetailRequest<LoadedChart> _detailRequest = new();

    private void ApplyEmptySelection()
    {
        // 清空选择时掐掉进行中的卡内动画：否则旧淡出的落零换文（代际未变时）或封面淡入
        // 会把陈旧内容写回空卡，"—" 也可能停在半截 alpha 上。
        _detailCard.ShowEmpty();
        _diffBtn.Text = "无可用谱面";
        _diffBtn.Disabled = true;
        _startBtn.Disabled = true;
        _bestLabel.Text = "";
        _bestGrade.Text = "";
        StopPreview();
    }


    // ---- 预览播放 ----

    private void StartPreview()
    {
        StopPreview();
        var pack = CurrentPack;
        if (pack == null || pack.Charts.Count == 0)
            return;
        var diff = pack.Charts[Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1)];
        var loaded = GameSession.GetPreload(pack, diff);
        if (loaded == null)
            return;

        _detailCard.ShowPreviewLoading();
        _previewPlayer.Load(new SongPreviewKey(_detailRequest.CurrentGeneration, pack.Id, diff.ChartId,
            loaded.ResolvedAudioPath), pack.PreviewFor(diff));
    }

    private void CompletePreviewLoad()
    {
        var completion = _previewPlayer.CompleteLoad(key =>
            IsCurrentPreviewLoad(key.Generation, key.PackId, key.ChartId));
        if (completion is not { } result) return;
        _detailCard.SetTransportPlaying(result.Success);
        if (!result.Success) _detailCard.ShowPreviewUnavailable();
        UpdatePreviewReadout(0);
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
            GD.PushWarning($"SongSelect: preview loading {result.Key.PackId}/{result.Key.ChartId} failed: {result.Error}");
    }

    private bool IsCurrentPreviewLoad(int generation, string packId, string chartId)
    {
        if (!IsInsideTree() || !_expanded || CurrentPack is not { } pack || pack.Charts.Count == 0)
            return false;
        // 当前身份由页面解析（代际/包/谱的比对在详情请求对象里）。
        var chart = pack.Charts[Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1)];
        return _detailRequest.IsCurrent(new SongDetailKey(generation, packId, chartId),
            pack.Id, chart.ChartId);
    }

    private void StopPreview()
    {
        _previewPlayer.Stop();
        _detailCard.SetTransportPlaying(false);
        UpdatePreviewReadout(0);
    }

    private void TogglePreviewPause()
    {
        if (_expanded && _previewPlayer.TogglePause())
            _detailCard.SetTransportPlaying(_previewPlayer.Playing);
    }

    /// <summary>Read-only progress; playback stops at the preview end without looping.</summary>
    private void UpdatePreviewReadout(double elapsed)
    {
        var readout = _previewPlayer.Readout(elapsed);
        _detailCard.ShowPreviewProgress(readout.Fraction, readout.Elapsed, readout.Duration);
        if (_previewPlayer.StopAtEnd(readout.Elapsed))
            _detailCard.SetTransportPlaying(false);
    }

    private void BeginRefreshAll()
    {
        var generation = _detailRequest.Begin();
        var pack = CurrentPack;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Selected = i == _packIdx;

        _libTitle.Text = $"SONG LIBRARY · {GameSession.Packs.Sum(p => p.Charts.Count)} CHARTS";

        if (pack == null || pack.Charts.Count == 0)
        {
            ApplyEmptySelection();
            return;
        }

        _browse.ClampDifficulty(pack.Charts.Count);
        var diff = pack.Charts[_diffIdx];
        // 文本淡出→换文→淡入：换文延后到淡出落零那一刻，代际快照当场比对，
        // 快速连切时只有最新一次请求能把新文本写上卡。
        _detailCard.ShowText(
            new CardTextContent(pack.Title, pack.Artist, $"谱师 {diff.CharterDisplay}"), generation);
        // 封面：动画模式瞬时压到 0 再等 Complete 淡回（旧封面不留在卡上）；Off 保持旧封面显示。
        _detailCard.HideCover();
        _detailCard.SetTransportEnabled(false);
        _detailCard.ShowChartLoading();
        _diffBtn.Disabled = false;
        _diffBtn.Text = diff.Level is { } level
            ? $"{diff.Display.ToUpperInvariant()} · Lv {level}"
            : $"{diff.Display.ToUpperInvariant()} · UNRATED";
        _startBtn.Disabled = true;
        _bestLabel.Text = "BEST —   PREPARING";
        _bestGrade.Text = "";

        CallDeferred(nameof(CompleteRefreshAll), generation, pack.Id, diff.ChartId);
    }

    private void CompleteRefreshAll(int generation, string packId, string chartId)
    {
        if (!_detailRequest.IsCurrentGeneration(generation) || !IsInsideTree())
            return;
        var pack = CurrentPack;
        if (pack == null || !StringComparer.Ordinal.Equals(pack.Id, packId) ||
            pack.Charts.Count == 0)
            return;
        _browse.ClampDifficulty(pack.Charts.Count);
        var diff = pack.Charts[_diffIdx];
        if (!StringComparer.Ordinal.Equals(diff.ChartId, chartId))
            return;

        if (GameSession.GetPreload(pack, diff) is { } cached)
        {
            CompleteChartDetails(generation, pack, diff, cached);
            return;
        }

        _detailCard.ShowChartLoading();
        _ = _detailRequest.Start(new SongDetailKey(generation, pack.Id, diff.ChartId),
            () => pack.LoadChart(diff));
    }

    private void CompleteChartDetailsLoad()
    {
        if (!_detailRequest.TryTakeCompleted(out var result))
            return;

        var generation = result.Key.Generation;
        var packId = result.Key.PackId;
        var chartId = result.Key.ChartId;
        var loaded = result.Value;
        if (!_detailRequest.IsCurrentGeneration(generation))
            return;
        if (result.Error is { } error)
            GD.PushWarning($"SongSelect: detail loading {packId}/{chartId} failed: {error.Message}");
        if (loaded is not null &&
            CurrentPack is { } loadedPack &&
            StringComparer.Ordinal.Equals(loadedPack.Id, packId) &&
            loadedPack.ChartOf(chartId) is { } loadedDiff)
            GameSession.CachePreload(loadedPack, loadedDiff, loaded);

        // 缓存先写、身份后判：即使当前选择已经换了，刚加载到的谱面也已进缓存。
        if (!IsInsideTree() || !_expanded ||
            CurrentPack is not { } pack || pack.Charts.Count == 0)
            return;
        var currentChartId = pack.Charts[Mathf.Clamp(_diffIdx, 0, pack.Charts.Count - 1)].ChartId;
        if (!_detailRequest.IsCurrent(new SongDetailKey(generation, packId, chartId),
                pack.Id, currentChartId))
            return;

        var diff = pack.Charts[_diffIdx];
        CompleteChartDetails(generation, pack, diff, loaded);
    }

    private void CompleteChartDetails(int generation, ChartPack pack, ChartDiff diff,
        LoadedChart? loaded)
    {
        if (!_detailRequest.IsCurrentGeneration(generation))
            return;
        if (loaded is not null)
        {
            _startBtn.Disabled = false;
            _detailCard.SetTransportEnabled(true);
        }
        else
        {
            _startBtn.Disabled = true;
            _detailCard.SetTransportEnabled(false);
            _detailCard.ShowChartUnavailable();
            GameSession.ClearPreload();
        }

        // 加载完成后封面从 0 淡入（Full/Reduced），Off 瞬时；代际守卫保证只有当前请求走到这里。
        _detailCard.ShowCover(CoverTextureCache.Load(pack.CoverPath));
        // Complete 在 Begin 后一帧就到，文字淡出多半还在半途：不打断它，
        // 让"淡出落零→代际守卫换文→淡入"自然走完；提前落 alpha 反而会把旧文本定格在卡上。
        _detailCard.SettleTextDim();
        if (_expanded && loaded is not null)
            StartPreview();

        ScoreRecord? rec = null;
        if (loaded != null && loaded.TryGetScoreIdentity(out var identity))
            rec = GameSession.Scores.Get(identity);
        else if (loaded != null)
            rec = GameSession.Scores.Get(pack.Id, diff.LegacyScoreKey(pack.PackageFormat));
        if (rec != null)
        {
            _bestLabel.Text = $"BEST {rec.Score:N0}   CLEAR {rec.Acc:F2}%";
            _bestGrade.Text = DynamiteUniverse.Shared.Score.ScoreStoreCore.GradeLabel(rec.Grade);
            _bestGrade.AddThemeColorOverride("font_color", UiFonts.GradeColor(rec.Grade));
        }
        else
        {
            _bestLabel.Text = "BEST —   尚无成绩";
            _bestGrade.Text = "";
        }
    }
}

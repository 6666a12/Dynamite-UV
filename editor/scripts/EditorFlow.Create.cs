using static DynamiteUniverse.Editor.EditorPageUi;
using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BigInteger = System.Numerics.BigInteger;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain
{
    private enum CreateMode { NewPackage, AddDifficulty }
    private CreatePageOverlay _createOverlay = null!;
    private CreateMode _createMode;
    /// <summary>Create 板的初始时间线默认值：BPM 与音频偏移在 TIMING 面板可改，默认沿用既有 150 / 0。</summary>
    private const double DefaultInitialBpm = 150d, DefaultAudioOffsetSec = 0d;

    /// <summary>Create 板（设计稿 Create Project）：整页自绘 + 真实输入控件，几何与行为都在
    /// <see cref="CreatePageOverlay"/>。创建失败的回报与实时校验都走 overlay 的中缝状态行，
    /// shell 只提供包级上下文（模式、已打开包的元数据）与前置校验规则。</summary>
    private Control BuildCreatePage()
    {
        var page = NewPage(rule: false);
        _createOverlay = new CreatePageOverlay();
        _createOverlay.BrowseAudioRequested += () => _audioDialog.PopupCenteredRatio(.82f);
        _createOverlay.PickCoverRequested += () => _coverDialog.PopupCenteredRatio(.82f);
        _createOverlay.Confirmed += ConfirmCreate;
        _createOverlay.Cancelled += () => ShowFlowPage(
            _createMode == CreateMode.NewPackage ? EditorFlowPage.Start : EditorFlowPage.Pack);
        _createOverlay.Validate = PreflightCreate;
        page.AddChild(_createOverlay);
        return page;
    }

    /// <summary>新包的预填值：包元数据 + 差分级默认（HARD 10）与既有默认时间线。
    /// BPM/offset 从这一轮起可在 TIMING 面板改，默认仍是 150 / 0。</summary>
    private void PrepareNewPackageForm()
    {
        _createOverlay.Apply(new CreatePageOverlay.Snapshot
        {
            PackId = "community-pack",
            Title = "New Community Pack",
            DraftLabel = "MEMORY DRAFT",
            DraftDetail = "NOTHING ON DISK YET",
        });
        _createOverlay.PrefillChart("chart-01", 2, string.Empty, 10, false);
        _createOverlay.PrefillTiming(DefaultInitialBpm, DefaultAudioOffsetSec);
    }

    /// <summary>AddDifficulty：包元数据/音频/封面属于已打开的包，只读展示；本轮只编辑新差分字段，
    /// 时间线沿用该差分创建时的默认值（见 <see cref="ConfirmCreate"/> 的 AddChartCommand 分支）。</summary>
    private void PrepareAddDifficultyForm()
    {
        if (_document is null) return;
        var persisted = _document.PackageDirectory is not null;
        _createOverlay.Apply(new CreatePageOverlay.Snapshot
        {
            AddDifficulty = true,
            PackId = _document.PackId,
            Title = _document.Title,
            Artist = _document.Artist,
            AudioPath = _document.Audio ?? string.Empty,
            PackCharter = string.Join(", ", _document.Charts.SelectMany(chart => chart.Charters).Distinct()),
            CoverName = _document.Cover ?? string.Empty,
            DraftLabel = persisted ? "ADDS TO OPEN PACKAGE" : "MEMORY DRAFT",
            DraftDetail = persisted ? "NOTHING WRITTEN YET" : "NOTHING ON DISK YET",
            DraftWarn = !persisted,
        });
        _createOverlay.PrefillChart(NextChartId(), 2, string.Empty, 10, false);
    }

    private string NextChartId()
    {
        if (_document is null) return "chart-01";
        var i = 1; while (_document.Charts.Any(chart => chart.Id == $"chart-{i:00}")) i++; return $"chart-{i:00}";
    }

    /// <summary>创建语义与校验保持原样：这里只把输入源从旧面板换成
    /// <see cref="CreatePageOverlay"/>，并把新接入的初始 BPM / 音频偏移 / 可选封面写进草稿请求
    /// （BPM 与 offset 的默认值仍是 150 / 0）。</summary>
    private void ConfirmCreate()
    {
        try
        {
            var form = _createOverlay.ReadForm();
            var id = form.ChartId;
            if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")) throw new InvalidOperationException("Chart ID 只能包含字母、数字、点、下划线和短横线。");
            var difficulty = (V2Difficulty)form.DifficultyIndex;
            var key = difficulty == V2Difficulty.Custom ? form.CustomKey : null;
            if (difficulty == V2Difficulty.Custom && string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("CUSTOM 难度必须填写 difficulty key。");
            var unrated = form.Unrated;
            int? level = null;
            if (!unrated)
            {
                if (form.Level is not { } parsed || parsed is < 1 or > 99) throw new InvalidOperationException("等级必须是 1–99，或勾选 UNRATED。");
                level = parsed;
            }
            var charter = form.Charter;
            if (charter.Length == 0) throw new InvalidOperationException("请填写 Charter。");

            if (_createMode == CreateMode.NewPackage)
            {
                var request = new EditorProjectDraftRequest(form.PackId, form.Title, form.Artist, id, difficulty, key, level, unrated, charter,
                    form.Bpm, form.OffsetSec, EditorSettings.Current.DefaultGridDivisor, form.AudioPath, form.CoverPath);
                _documentSession.CreateDraft(request);
            }
            else
            {
                if (_document is null) throw new InvalidOperationException("没有打开的包。");
                // A new difficulty joins the already opened package, so its resources stay in-package.
                if (_document.Charts.Any(chart => chart.Id == id)) throw new InvalidOperationException($"Chart ID '{id}' 已存在。");
                var chart = new EditableChart { Id = id, Difficulty = difficulty, DifficultyKey = key, Level = level, Unrated = unrated, File = $"charts/{id}.json", Audio = null, Preview = null, AudioOffsetSec = 0 };
                chart.Charters.Add(charter); chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
                _documentSession.AddChart(chart);
            }
            _saveError = null;
            ShowFlowPage(EditorFlowPage.Pack);
        }
        catch (Exception exception)
        {
            SetFlowMessage($"创建失败：{exception.Message}", true);
        }
    }

    /// <summary>Create 板中缝的前置校验：把已知非法输入挡在 CREATE 之前（按钮压暗 + 可读原因）。
    /// 它不是第二套规则——这里每一条（chart ID 形状、CUSTOM key 必填/≤24/包内唯一、level 与 unrated
    /// 恰有其二、音频与封面文件可用）都与 draft factory 和 v2 validator 的既有约束一致，只是提前报出来；
    /// 真正的创建仍由 <see cref="ConfirmCreate"/> 与 core 决定，本函数不参与写盘。</summary>
    private string? PreflightCreate(CreatePageOverlay.Form form)
    {
        if (!Regex.IsMatch(form.ChartId, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"))
            return "Chart ID 只能包含字母、数字、点、下划线和短横线。";
        if (form.DifficultyIndex == CreatePageOverlay.CustomIndex)
        {
            if (form.CustomKey.Length == 0) return "CUSTOM 难度必须填写 difficulty key。";
            if (form.CustomKey.Length > 24) return "difficulty key 最多 24 个字符。";
        }
        if (!form.Unrated && form.Level is null) return "等级必须是 1–99，或勾选 UNRATED。";
        if (form.Charter.Length == 0) return "请填写 Charter。";

        if (_createMode == CreateMode.NewPackage)
        {
            if (form.PackId.Length == 0) return "请填写 Pack ID。";
            if (form.Title.Length == 0) return "请填写 Title。";
            if (form.AudioPath.Length == 0) return "新建包需要一个可访问的外部音频文件。";
            if (!File.Exists(form.AudioPath)) return "音频文件不存在或不可访问。";
            if (!EditorProjectDraftFactory.IsSupportedAudioPath(form.AudioPath))
                return "音频扩展名不被支持：需要 .wav/.mp3/.flac/.ogg/.opus/.m4a/.aac。";
            if (form.CoverPath is not null && !File.Exists(form.CoverPath)) return "封面文件不存在或不可访问。";
            return null;
        }

        if (_document is null) return "没有打开的包。";
        if (_document.Charts.Any(chart => chart.Id == form.ChartId)) return $"Chart ID '{form.ChartId}' 已存在。";
        if (form.DifficultyIndex == CreatePageOverlay.CustomIndex && CustomKeyTaken(form.CustomKey))
            return $"difficulty key '{form.CustomKey}' 在本包内已存在。";
        return null;
    }

    /// <summary>是否有已存在的自定义难度占用了同一个 key（NFC + 大小写折叠后比较）。</summary>
    private bool CustomKeyTaken(string key)
    {
        var folded = FoldDifficultyKey(key);
        return _document!.Charts.Any(chart => chart.Difficulty == V2Difficulty.Custom &&
            chart.DifficultyKey is { } existing && FoldDifficultyKey(existing) == folded);
    }

    /// <summary>包内自定义难度 key 的唯一性，与 v2 validator 同一口径：NFC + 大小写折叠后比较。
    /// 未配对代理项无法 NFC 归一化（`Normalize` 会抛 ArgumentException），此时退回纯大小写折叠，
    /// 不因为一个按键把页面打断。</summary>
    private static string FoldDifficultyKey(string key)
    {
        try
        {
            return key.Normalize(NormalizationForm.FormC).ToUpperInvariant().ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return key.ToUpperInvariant().ToLowerInvariant();
        }
    }
}

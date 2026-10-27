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
    private Label _packMessage = null!;
    private PackPageOverlay _packOverlay = null!;
    private Texture2D? _packCover;
    private string? _packCoverKey;
    /// <summary>Pack 页底部状态行（设计稿 13px ls2）。</summary>
    private static readonly Font PackStatusFont = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };

    private Control BuildPackPage()
    {
        var page = NewPage(rule: false);
        _packOverlay = new PackPageOverlay();
        _packOverlay.BackRequested += () => ShowFlowPage(EditorFlowPage.Start);
        _packOverlay.ImportRequested += () => _importDialog.PopupCenteredRatio(.82f);
        _packOverlay.NewChartRequested += () =>
        {
            _createMode = CreateMode.AddDifficulty;
            PrepareAddDifficultyForm();
            ShowFlowPage(EditorFlowPage.Create);
        };
        _packOverlay.ChartSelected += SelectPackChart;
        _packOverlay.OpenInEditorRequested += () => OpenChart(_document!.SelectedChartId);
        page.AddChild(_packOverlay);

        // 设计稿的底部中缝是只读状态行；本页没有时长来源，所以这一行只放真实数据
        // （难度 + 等级 + 主判定数）与保存/打开回报，两者共用同一条。
        _packMessage = new Label
        {
            Position = new Vector2(540, 972), Size = new Vector2(840, 52),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _packMessage.AddThemeFontOverride("font", PackStatusFont);
        EditorFonts.ApplySize(_packMessage, 13);
        _packMessage.AddThemeColorOverride("font_color", FlowMuted);
        page.AddChild(_packMessage);
        return page;
    }

    /// <summary>Start 页路径行与 Pack 页 IMPORT 行共用这一条打开流程：目录先过
    /// <see cref="EditorPackageRepository.Open"/> 的严格校验，失败不换页，并保留当前已打开的文档。</summary>
    private void TryOpenPackage() => TryOpenPackage(null);

    private void TryOpenPackage(string? directory)
    {
        var path = (directory ?? _startView.PackagePath).Trim();
        if (string.IsNullOrWhiteSpace(path)) { SetFlowMessage("请选择一个目录。", true); return; }
        try
        {
            var document = _documentSession.Open(path);
            _saveError = null;
            _startView.PackagePath = path;
            InvalidateDigestAudioHash();
            SetFlowMessage($"VALID V2 PACKAGE · {document.Charts.Count} CHARTS", false);
            ShowFlowPage(EditorFlowPage.Pack);
        }
        catch (Exception exception)
        {
            SetFlowMessage($"无法打开此路径：{exception.Message}", true);
        }
    }

    private void RefreshPackPage()
    {
        if (_document is null) return;
        var rows = OrderedCharts().Select(BuildChartRow).ToArray();
        var selectedId = _document.SelectedChartId;
        _packOverlay.Apply(new PackPageOverlay.Snapshot
        {
            PackName = _document.Title.Length > 0 ? _document.Title : _document.PackId,
            PackMeta = PackMetaText(),
            PackTime = PackTimeText(),
            Cover = LoadPackCover(),
            DigestOk = TryDigest(),
            Charts = rows,
        });
        var selected = rows.FirstOrDefault(row => row.Id == selectedId);
        SetPackStatus(selected is null ? string.Empty : ChartStatus(selected));
    }

    /// <summary>设计稿 §4.4 的排序：casual→normal→hard→mega→giga→tech 垫底，custom 排在其后按添加顺序
    /// （OrderBy 稳定，同难度保持包内顺序）。</summary>
    private IEnumerable<EditableChart> OrderedCharts()
    {
        var standard = new[]
        {
            V2Difficulty.Casual, V2Difficulty.Normal, V2Difficulty.Hard,
            V2Difficulty.Mega, V2Difficulty.Giga, V2Difficulty.Tech,
        };
        return _document!.Charts.Where(chart => chart.Difficulty != V2Difficulty.Custom)
            .OrderBy(chart => Array.IndexOf(standard, chart.Difficulty))
            .Concat(_document.Charts.Where(chart => chart.Difficulty == V2Difficulty.Custom));
    }

    private PackPageOverlay.ChartRow BuildChartRow(EditableChart chart) => new(
        chart.Id,
        DifficultyName(chart).ToUpperInvariant(),
        chart.Difficulty == V2Difficulty.Custom,
        DifficultyColor(chart.Difficulty),
        chart.Unrated || chart.Level is null ? "--" : chart.Level.Value.ToString(CultureInfo.InvariantCulture),
        MainJudgementCount(chart).ToString(CultureInfo.InvariantCulture),
        DensityFor(chart),
        chart.Id == _document!.SelectedChartId);

    /// <summary>note 数取 shared 的主判定数（`V2SemanticValidator.CountMainJudgements`，与客户端计数同源），
    /// 不是把三种 Note 类型简单相加。</summary>
    private BigInteger MainJudgementCount(EditableChart chart) =>
        V2SemanticValidator.CountMainJudgements(_document!.BuildChartSnapshot(chart.Id));

    private const int DensityBuckets = 72;

    /// <summary>密度条 = 真实 note 起始时间分布：整首 note 时间轴（末 note/路径尾）均分 72 桶，
    /// 按 BPM 时间线换算成秒后分桶，桶高按桶内 note 数归一化。没有 note 时返回 null（不画条）。</summary>
    private static float[]? DensityFor(EditableChart chart)
    {
        if (chart.Notes.Count == 0) return null;
        var span = 0d;
        foreach (var note in chart.Notes)
        {
            var tail = note.Nodes.Count > 0 ? note.Nodes[^1].Time : note.Time;
            span = Math.Max(span, EditorTime.BarToSeconds(tail, chart.Bpms));
        }
        if (span <= 0d) return null;
        var counts = new int[DensityBuckets];
        foreach (var note in chart.Notes)
        {
            var second = EditorTime.BarToSeconds(note.Time, chart.Bpms);
            counts[Math.Clamp((int)(second / span * DensityBuckets), 0, DensityBuckets - 1)]++;
        }
        var max = counts.Max();
        var density = new float[DensityBuckets];
        for (var i = 0; i < DensityBuckets; i++) density[i] = counts[i] / (float)max;
        return density;
    }

    private string PackMetaText()
    {
        var charts = $"{_document!.Charts.Count} {(_document.Charts.Count == 1 ? "CHART" : "CHARTS")}";
        return _document.Artist.Length > 0 ? $"{_document.Artist} · {charts}" : charts;
    }

    /// <summary>左列右侧的时间列取包内 meta.json 的真实修改时间；内存草稿没有落盘时间。</summary>
    private string PackTimeText()
    {
        if (_document!.PackageDirectory is null) return "MEMORY DRAFT";
        try
        {
            var meta = V2PackageWriter.ResolveRegularFile(_document.PackageDirectory, "meta.json");
            var stamp = File.GetLastWriteTime(meta);
            var days = (DateTime.Now.Date - stamp.Date).Days;
            if (days <= 0) return stamp.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (days == 1) return "YESTERDAY";
            if (days < 30) return $"{days} DAYS AGO";
            return stamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return "—";
        }
    }

    private static string ChartStatus(PackPageOverlay.ChartRow row)
    {
        var level = row.LevelText == "--" ? "UNRATED" : row.LevelText;
        return $"{row.Label} {level} · {row.NotesText} NOTES";
    }

    /// <summary>包内封面：相对路径交给 writer 的严格解析（不允许逃逸/链接），解码失败或无引用时
    /// 返回 null，页面回落到客户端的 NO COVER 占位。同一封面只解码一次。</summary>
    private Texture2D? LoadPackCover()
    {
        var relative = _document!.Cover;
        var root = _document.PackageDirectory;
        if (string.IsNullOrEmpty(relative) || root is null)
        {
            _packCoverKey = null;
            return _packCover = null;
        }
        var key = $"{root}\n{relative}";
        if (string.Equals(_packCoverKey, key, StringComparison.Ordinal) && _packCover is not null) return _packCover;
        _packCover = null;
        try
        {
            var image = Image.LoadFromFile(V2PackageWriter.ResolveRegularFile(root, relative));
            if (image is not null && !image.IsEmpty()) _packCover = ImageTexture.CreateFromImage(image);
        }
        catch (Exception)
        {
            _packCover = null;
        }
        _packCoverKey = key;
        return _packCover;
    }

    private void SelectPackChart(string chartId)
    {
        if (_document is null) return;
        try
        {
            _document.SelectChart(chartId);
        }
        catch (Exception exception)
        {
            SetFlowMessage($"无法选中差分：{exception.Message}", true);
            return;
        }
        RefreshPackPage();
    }

    /// <summary>底部中缝状态行：默认是选中差分的只读读数，保存/打开回报会临时占据同一条。</summary>
    private void SetPackStatus(string text)
    {
        SetLabelText(_packMessage, text);
        _packMessage.AddThemeColorOverride("font_color", FlowMuted);
    }
}

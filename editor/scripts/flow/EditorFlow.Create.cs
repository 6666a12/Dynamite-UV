using static DynamiteUniverse.Editor.EditorPageUi;
using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    private const double DefaultInitialBpm = 150d, DefaultAudioOffsetSec = 0d;

    private static EditorCreateMode CoreCreateMode(CreateMode mode) => mode == CreateMode.NewPackage
        ? EditorCreateMode.NewPackage : EditorCreateMode.AddDifficulty;

    private static EditorCreateForm CoreCreateForm(CreatePageOverlay.Form form) => new(
        form.PackId, form.Title, form.Artist, form.AudioPath, form.ChartId, form.DifficultyIndex,
        form.CustomKey, form.Level, form.Unrated, form.Charter, form.Bpm, form.OffsetSec, form.CoverPath);

    private EditorCreatePackageSummary? CreatePackageSummary() => _document is null ? null : new(
        _document.Charts.Select(chart => chart.Id).ToArray(),
        _document.Charts.Select(chart => new EditorCreateChartSummary(chart.Difficulty, chart.DifficultyKey)).ToArray());

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

    private void ConfirmCreate()
    {
        try
        {
            var result = EditorCreateRequest.Build(CoreCreateMode(_createMode),
                CoreCreateForm(_createOverlay.ReadForm()), EditorSettings.Current.DefaultGridDivisor);
            if (_createMode == CreateMode.NewPackage)
            {
                _documentSession.CreateDraft(result.DraftRequest!);
            }
            else
            {
                if (_document is null) throw new InvalidOperationException("没有打开的包。");
                var chart = result.Chart!;
                if (_document.Charts.Any(existing => existing.Id == chart.Id))
                    throw new InvalidOperationException($"Chart ID '{chart.Id}' 已存在。");
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

    private string? PreflightCreate(CreatePageOverlay.Form form)
    {
        var newPackage = _createMode == CreateMode.NewPackage;
        return EditorCreatePreflight.Validate(CoreCreateMode(_createMode), CoreCreateForm(form),
            newPackage ? null : CreatePackageSummary(),
            newPackage && File.Exists(form.AudioPath),
            newPackage && form.CoverPath is not null && File.Exists(form.CoverPath));
    }
}

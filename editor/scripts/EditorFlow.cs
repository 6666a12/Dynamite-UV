using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>Page flow around the fixed authoring stage. The page model deliberately keeps
/// package validation at the v2 repository boundary: a path is never promoted to the pack
/// page unless the complete directory can be decoded and validated.</summary>
public partial class StandaloneEditorMain
{
    private enum EditorFlowPage { Start, Pack, Create, Edit }
    private enum CreateMode { NewPackage, AddDifficulty }

    private Control _flowRoot = null!;
    private Control _startPage = null!;
    private Control _packPage = null!;
    private Control _createPage = null!;
    private Control _editChrome = null!;
    private LineEdit _packagePath = null!;
    private LineEdit _createPackId = null!;
    private LineEdit _createPackTitle = null!;
    private LineEdit _createPackArtist = null!;
    private LineEdit _createAudio = null!;
    private Button _createAudioBrowse = null!;
    private LineEdit _createChartId = null!;
    private LineEdit _createCustomDifficulty = null!;
    private LineEdit _createLevel = null!;
    private LineEdit _createCharter = null!;
    private OptionButton _createDifficulty = null!;
    private CheckButton _createUnrated = null!;
    private CheckButton _autoButton = null!;
    private Label _startMessage = null!;
    private Label _createMessage = null!;
    private Label _packHeading = null!;
    private Label _packPathLabel = null!;
    private VBoxContainer _difficultyList = null!;
    private CutPanel _editTagPanel = null!;
    private ColorRect _editTagAccent = null!;
    private Label _editSongTitle = null!;
    private Label _editDifficulty = null!;
    private FileDialog _pathDialog = null!;
    private FileDialog _audioDialog = null!;
    private EditorDocument? _document;
    private EditorFlowPage _flowPage;
    private CreateMode _createMode;
    private bool _memoryDraft;
    private bool _auto = true;

    // Keep page chrome on the same palette as the game HUD. UiFonts is linked
    // from the client project in the editor csproj so both surfaces share the
    // same font, difficulty colors, panel fill and line colors.
    private static readonly Color FlowBackground = UiFonts.Bg;
    private static readonly Color FlowPanel = UiFonts.Panel;
    private static readonly Color FlowPanelRaised = UiFonts.PanelHover;
    private static readonly Color FlowCyan = UiFonts.Cyan;
    private static readonly Color FlowMuted = UiFonts.Dim;
    private static readonly Color FlowPink = UiFonts.Pink;
    private static readonly Color FlowAmber = UiFonts.Hard;

    private void InitializeEditorFlow()
    {
        _flowRoot = new Control { Name = "EditorPageFlow", ZIndex = 40, MouseFilter = Control.MouseFilterEnum.Ignore };
        _flowRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_flowRoot);

        _startPage = BuildStartPage();
        _packPage = BuildPackPage();
        _createPage = BuildCreatePage();
        _editChrome = BuildEditChrome();
        _flowRoot.AddChild(_startPage);
        _flowRoot.AddChild(_packPage);
        _flowRoot.AddChild(_createPage);
        _flowRoot.AddChild(_editChrome);

        _pathDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "SELECT V2 PACKAGE DIRECTORY",
            OkButtonText = "USE DIRECTORY",
            CancelButtonText = "CANCEL"
        };
        _pathDialog.DirSelected += path => _packagePath.Text = path;
        _flowRoot.AddChild(_pathDialog);
        _audioDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "SELECT EXTERNAL AUDIO FILE",
            OkButtonText = "USE AUDIO",
            CancelButtonText = "CANCEL"
        };
        _audioDialog.FileSelected += path => _createAudio.Text = path;
        _flowRoot.AddChild(_audioDialog);
        ShowFlowPage(EditorFlowPage.Start);
    }

    private Control BuildStartPage()
    {
        var page = NewPage();
        AddFlowLabel(page, 110, 126, 1200, 72, "DYNAMAKER UV", 44, FlowCyan);
        AddFlowLabel(page, 114, 206, 700, 36, "START", 18, FlowMuted);

        var pathPanel = new PanelContainer
        {
            Position = new Vector2(90, 278), Size = new Vector2(1545, 500),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        pathPanel.AddThemeStyleboxOverride("panel", FlowBox(new Color(FlowPanel, .82f), new Color(FlowCyan, .46f), 14));
        page.AddChild(pathPanel);
        AddFlowLabel(page, 114, 316, 700, 36, "PACKAGE PATH", 22, FlowAmber);
        AddFlowLabel(page, 114, 360, 700, 28, "V2 DIRECTORY", 14, FlowMuted);
        var decoration = new StartDecoration
        { Position = new Vector2(1125, 308), Size = new Vector2(440, 150), MouseFilter = Control.MouseFilterEnum.Ignore };
        page.AddChild(decoration);

        _packagePath = NewLineEdit("Package directory path", 114, 486, 1010, 64);
        page.AddChild(_packagePath);
        var browse = NewButton("BROWSE", 1144, 486, 250, 64, FlowPanelRaised);
        browse.Pressed += () => _pathDialog.PopupCenteredRatio(.82f);
        page.AddChild(browse);
        var open = NewButton("OPEN PACKAGE", 114, 588, 340, 68, FlowCyan);
        open.Pressed += TryOpenPackage;
        page.AddChild(open);
        var create = NewButton("NEW PACKAGE", 474, 588, 300, 68, FlowPanelRaised);
        create.Pressed += () => { _createMode = CreateMode.NewPackage; PrepareNewPackageForm(); ShowFlowPage(EditorFlowPage.Create); };
        page.AddChild(create);

        _startMessage = AddFlowLabel(page, 114, 688, 960, 46, "READY · V2 PACKAGES ONLY", 16, FlowMuted);
        return page;
    }

    private Control BuildPackPage()
    {
        var page = NewPage();
        AddFlowLabel(page, 90, 72, 1500, 54, "PACKAGE", 38, FlowCyan);
        _packHeading = AddFlowLabel(page, 90, 138, 1500, 48, "", 30, Colors.White);
        _packPathLabel = AddFlowLabel(page, 90, 194, 1500, 30, "", 15, FlowMuted);
        var back = NewButton("‹ START", 90, 934, 190, 56, FlowPanelRaised);
        back.Pressed += () => ShowFlowPage(EditorFlowPage.Start);
        page.AddChild(back);
        var create = NewButton("＋ CREATE DIFFICULTY", 1280, 934, 360, 56, FlowPink);
        create.Pressed += () => { _createMode = CreateMode.AddDifficulty; PrepareAddDifficultyForm(); ShowFlowPage(EditorFlowPage.Create); };
        page.AddChild(create);
        AddFlowLabel(page, 90, 266, 1000, 32, "DIFFICULTY LIST · SELECT A CHART TO OPEN", 15, FlowAmber);
        var listPanel = new PanelContainer { Position = new Vector2(72, 300), Size = new Vector2(1580, 610), MouseFilter = Control.MouseFilterEnum.Ignore };
        listPanel.AddThemeStyleboxOverride("panel", FlowBox(new Color(FlowPanel, .64f), new Color(FlowCyan, .28f), 12));
        page.AddChild(listPanel);
        var scroll = new ScrollContainer { Position = new Vector2(90, 324), Size = new Vector2(1545, 570), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _difficultyList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _difficultyList.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_difficultyList);
        page.AddChild(scroll);
        return page;
    }

    private Control BuildCreatePage()
    {
        var page = NewPage();
        AddFlowLabel(page, 90, 72, 1500, 48, "CREATE", 30, FlowPink);
        AddFlowLabel(page, 90, 138, 1500, 34, "A small, explicit form keeps the package metadata valid before the canvas opens.", 15, FlowMuted);

        AddFlowLabel(page, 90, 236, 650, 28, "PACKAGE METADATA", 13, FlowAmber);
        _createPackId = NewLineEdit("Pack ID", 90, 276, 500, 50); page.AddChild(_createPackId);
        _createPackTitle = NewLineEdit("Title", 610, 276, 500, 50); page.AddChild(_createPackTitle);
        _createPackArtist = NewLineEdit("Artist", 1130, 276, 500, 50); page.AddChild(_createPackArtist);
        _createAudio = NewLineEdit("External audio file (.wav/.mp3/…)", 90, 338, 1320, 50); page.AddChild(_createAudio);
        _createAudioBrowse = NewButton("BROWSE", 1430, 338, 200, 50, FlowPanelRaised);
        _createAudioBrowse.Pressed += () => _audioDialog.PopupCenteredRatio(.82f);
        page.AddChild(_createAudioBrowse);

        AddFlowLabel(page, 90, 438, 650, 28, "FIRST DIFFICULTY", 13, FlowAmber);
        _createChartId = NewLineEdit("Chart ID", 90, 478, 500, 50); page.AddChild(_createChartId);
        _createDifficulty = new OptionButton { Position = new Vector2(610, 478), Size = new Vector2(300, 50) };
        foreach (var item in new[] { "CASUAL", "NORMAL", "HARD", "MEGA", "GIGA", "TECH", "CUSTOM" }) _createDifficulty.AddItem(item);
        _createDifficulty.Selected = 2; page.AddChild(_createDifficulty);
        _createCustomDifficulty = NewLineEdit("Custom difficulty key (CUSTOM only)", 930, 478, 340, 50); page.AddChild(_createCustomDifficulty);
        _createLevel = NewLineEdit("Level 1–99", 1290, 478, 170, 50); page.AddChild(_createLevel);
        _createUnrated = new CheckButton { Position = new Vector2(1480, 478), Size = new Vector2(150, 50), Text = "UNRATED" };
        _createUnrated.AddThemeFontOverride("font", TechFont); _createUnrated.AddThemeFontSizeOverride("font_size", 14); page.AddChild(_createUnrated);
        _createCharter = NewLineEdit("Charter name", 90, 540, 720, 50); page.AddChild(_createCharter);

        _createUnrated.Toggled += value => { _createLevel.Editable = !value; if (value) _createLevel.Text = ""; };
        var cancel = NewButton("CANCEL", 90, 870, 220, 58, FlowPanelRaised);
        cancel.Pressed += () => ShowFlowPage(_createMode == CreateMode.NewPackage ? EditorFlowPage.Start : EditorFlowPage.Pack);
        page.AddChild(cancel);
        var confirm = NewButton("CREATE", 1330, 870, 300, 58, FlowPink); confirm.Pressed += ConfirmCreate; page.AddChild(confirm);
        _createMessage = AddFlowLabel(page, 90, 650, 1500, 72, "", 15, FlowMuted);
        return page;
    }

    private Control BuildEditChrome()
    {
        var chrome = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var back = NewButton("‹ PACK", 32, 986, 170, 52, FlowPanelRaised);
        back.MouseFilter = Control.MouseFilterEnum.Stop; back.Pressed += ReturnToPack; chrome.AddChild(back);
        // Reuse the gameplay HUD left-bottom title plate so the editor and
        // play surface read as one series.
        _editTagPanel = new CutPanel
        {
            Position = new Vector2(52, 884), Size = new Vector2(612, 104), Cut = 16,
            Fill = new Color(0.018f, 0.035f, 0.082f, .88f), Border = new Color(UiFonts.Line, .82f),
            BorderWidth = 1.5f, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        chrome.AddChild(_editTagPanel);
        _editTagAccent = new ColorRect { Position = new Vector2(52, 884), Size = new Vector2(6, 104), Color = UiFonts.Cyan, MouseFilter = Control.MouseFilterEnum.Ignore };
        chrome.AddChild(_editTagAccent);
        _editSongTitle = AddFlowLabel(chrome, 78, 896, 556, 42, "", 27, UiFonts.Text);
        _editSongTitle.AddThemeFontOverride("font", UiFonts.Cjk);
        _editDifficulty = AddFlowLabel(chrome, 78, 944, 556, 28, "", 17, FlowAmber);
        _editDifficulty.AddThemeFontOverride("font", UiFonts.TechBold);
        _autoButton = new CheckButton { Position = new Vector2(1510, 985), Size = new Vector2(220, 54), Text = "AUTO" , ButtonPressed = true, MouseFilter = Control.MouseFilterEnum.Stop };
        _autoButton.AddThemeFontOverride("font", TechFont); _autoButton.AddThemeFontSizeOverride("font_size", 18); _autoButton.AddThemeColorOverride("font_color", FlowCyan);
        _autoButton.Toggled += value => { _auto = value; _playing = value; RefreshEditTag(); _status.Text = value ? "AUTO ON · PREVIEW RUNNING" : "AUTO OFF · EDIT MODE"; };
        chrome.AddChild(_autoButton);
        return chrome;
    }

    private Control NewPage()
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = FlowBackground, MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddChild(background);
        var rule = new ColorRect { Position = new Vector2(90, 118), Size = new Vector2(1545, 2), Color = new Color(FlowCyan, .45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        page.AddChild(rule);
        return page;
    }

    private Label AddFlowLabel(Node parent, float x, float y, float w, float h, string text, int size, Color color)
    {
        var label = LabelAt(x, y, w, h, text, size, color);
        parent.AddChild(label); return label;
    }

    private LineEdit NewLineEdit(string placeholder, float x, float y, float w, float h)
    {
        var edit = new LineEdit { Position = new Vector2(x, y), Size = new Vector2(w, h), PlaceholderText = placeholder };
        edit.AddThemeFontOverride("font", TechFont); edit.AddThemeFontSizeOverride("font_size", 18);
        edit.AddThemeStyleboxOverride("normal", FlowBox(FlowPanel, new Color("24516a"), 6));
        edit.AddThemeColorOverride("font_color", Colors.White); edit.AddThemeColorOverride("caret_color", FlowCyan);
        return edit;
    }

    private Button NewButton(string text, float x, float y, float w, float h, Color accent)
    {
        var button = new Button { Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text, MouseFilter = Control.MouseFilterEnum.Stop };
        button.AddThemeFontOverride("font", TechFont); button.AddThemeFontSizeOverride("font_size", 17);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeStyleboxOverride("normal", FlowBox(accent.A > .7f && accent.R < .4f ? new Color(accent, .14f) : accent, accent, 6));
        button.AddThemeStyleboxOverride("hover", FlowBox(new Color(accent, .28f), accent, 6));
        button.AddThemeStyleboxOverride("pressed", FlowBox(new Color(accent, .4f), Colors.White, 6));
        return button;
    }

    private static StyleBoxFlat FlowBox(Color background, Color border, int radius) => new()
    { BgColor = background, BorderColor = border, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, ContentMarginLeft = 14, ContentMarginRight = 14 };

    private void ShowFlowPage(EditorFlowPage page)
    {
        _flowPage = page;
        _startPage.Visible = page == EditorFlowPage.Start;
        _packPage.Visible = page == EditorFlowPage.Pack;
        _createPage.Visible = page == EditorFlowPage.Create;
        _editChrome.Visible = page == EditorFlowPage.Edit;
        var editing = page == EditorFlowPage.Edit;
        var activePage = page switch
        {
            EditorFlowPage.Start => _startPage,
            EditorFlowPage.Pack => _packPage,
            EditorFlowPage.Create => _createPage,
            _ => _editChrome,
        };
        activePage.Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(activePage, "modulate", Colors.White, .22f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _canvas.Visible = editing;
        _status.Visible = editing; _inspector.Visible = editing; _transport.Visible = editing;
        if (_menu is not null) _menu.Hide();
        if (!editing) { _playing = false; _canvas.InputSuspended = true; } else { _canvas.InputSuspended = false; }
        if (page == EditorFlowPage.Pack) RefreshPackPage();
    }

    private void TryOpenPackage()
    {
        var path = _packagePath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path)) { SetFlowMessage("请选择一个目录。", true); return; }
        try
        {
            var document = EditorPackageRepository.Open(path);
            _document = document; _memoryDraft = false;
            SetFlowMessage($"VALID V2 PACKAGE · {document.Charts.Count} CHARTS", false);
            ShowFlowPage(EditorFlowPage.Pack);
        }
        catch (Exception exception)
        {
            _document = null;
            SetFlowMessage($"无法打开此路径：{exception.Message}", true);
        }
    }

    private void RefreshPackPage()
    {
        if (_document is null) return;
        _packHeading.Text = $"{_document.Title}  /  {_document.Artist}";
        _packPathLabel.Text = $"{(_memoryDraft ? "MEMORY DRAFT" : _document.PackageDirectory)}  ·  PACK ID {_document.PackId}";
        foreach (var child in _difficultyList.GetChildren()) child.QueueFree();
        foreach (var chart in _document.Charts)
        {
            var row = new PanelContainer { CustomMinimumSize = new Vector2(1520, 86) };
            row.AddThemeStyleboxOverride("panel", FlowBox(FlowPanel, DifficultyColor(chart.Difficulty), 6));
            var button = new Button { Text = DifficultyText(chart), Alignment = HorizontalAlignment.Left, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(1500, 84) };
            button.AddThemeFontOverride("font", TechFont); button.AddThemeFontSizeOverride("font_size", 18); button.AddThemeColorOverride("font_color", Colors.White);
            button.AddThemeStyleboxOverride("normal", FlowBox(new Color(FlowPanel, .2f), DifficultyColor(chart.Difficulty), 6));
            button.AddThemeStyleboxOverride("hover", FlowBox(new Color(DifficultyColor(chart.Difficulty), .2f), DifficultyColor(chart.Difficulty), 6));
            var chartId = chart.Id; button.Pressed += () => OpenChart(chartId);
            row.AddChild(button); _difficultyList.AddChild(row);
        }
    }

    private void PrepareNewPackageForm()
    {
        _createPackId.Text = "community-pack"; _createPackTitle.Text = "New Community Pack"; _createPackArtist.Text = ""; _createAudio.Text = "";
        _createChartId.Text = "chart-01"; _createDifficulty.Selected = 2; _createCustomDifficulty.Text = ""; _createLevel.Text = "10"; _createUnrated.ButtonPressed = false; _createCharter.Text = "";
        _createMessage.Text = "新建包需要一个可访问的音频文件；创建后仍会先停留在 MEMORY DRAFT。";
        foreach (var control in new Control[] { _createPackId, _createPackTitle, _createPackArtist, _createAudio, _createAudioBrowse }) control.Visible = true;
    }

    private void PrepareAddDifficultyForm()
    {
        if (_document is null) return;
        _createPackId.Text = _document.PackId; _createPackTitle.Text = _document.Title; _createPackArtist.Text = _document.Artist; _createAudio.Text = _document.Audio ?? "";
        _createChartId.Text = NextChartId(); _createDifficulty.Selected = 2; _createCustomDifficulty.Text = ""; _createLevel.Text = "10"; _createUnrated.ButtonPressed = false; _createCharter.Text = "";
        _createMessage.Text = "差分会加入当前包的内存文档；返回包页后从列表打开。";
        foreach (var control in new Control[] { _createPackId, _createPackTitle, _createPackArtist, _createAudio, _createAudioBrowse }) control.Visible = false;
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
            var id = _createChartId.Text.Trim();
            if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")) throw new InvalidOperationException("Chart ID 只能包含字母、数字、点、下划线和短横线。");
            var difficulty = (V2Difficulty)_createDifficulty.Selected;
            var key = difficulty == V2Difficulty.Custom ? _createCustomDifficulty.Text.Trim() : null;
            if (difficulty == V2Difficulty.Custom && string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("CUSTOM 难度必须填写 difficulty key。");
            var unrated = _createUnrated.ButtonPressed;
            int? level = null;
            var parsed = 0;
            if (!unrated && (!int.TryParse(_createLevel.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed is < 1 or > 99)) throw new InvalidOperationException("等级必须是 1–99，或勾选 UNRATED。");
            if (!unrated) level = parsed;
            var charter = _createCharter.Text.Trim();
            if (charter.Length == 0) throw new InvalidOperationException("请填写 Charter。");

            if (_createMode == CreateMode.NewPackage)
            {
                var request = new EditorProjectDraftRequest(_createPackId.Text.Trim(), _createPackTitle.Text.Trim(), _createPackArtist.Text.Trim(), id, difficulty, key, level, unrated, charter, 150, 0, 16, _createAudio.Text.Trim(), null);
                var draft = EditorProjectDraftFactory.Create(request);
                _document = draft.Document; _memoryDraft = true;
            }
            else
            {
                if (_document is null) throw new InvalidOperationException("没有打开的包。");
                if (_document.Charts.Any(chart => chart.Id == id)) throw new InvalidOperationException($"Chart ID '{id}' 已存在。");
                var chart = new EditableChart { Id = id, Difficulty = difficulty, DifficultyKey = key, Level = level, Unrated = unrated, File = $"charts/{id}.json", Audio = null, Preview = null, AudioOffsetSec = 0 };
                chart.Charters.Add(charter); chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
                _document.Execute(new AddChartCommand(chart));
            }
            ShowFlowPage(EditorFlowPage.Pack);
        }
        catch (Exception exception)
        {
            SetFlowMessage($"创建失败：{exception.Message}", true);
        }
    }

    private void OpenChart(string chartId)
    {
        if (_document is null) return;
        try
        {
            _document.SelectChart(chartId);
            _canvas.AttachDocument(_document);
            _canvas.SetSide(0); _canvas.SetTool(4);
            _editSongTitle.Text = _document.Title;
            _time = 0;
            _canvas.PreviewTime = 0;
            _auto = true; _autoButton.SetPressedNoSignal(true); _playing = true;
            _status.Text = "AUTO ON · PREVIEW RUNNING";
            RefreshEditTag();
            ShowFlowPage(EditorFlowPage.Edit);
        }
        catch (Exception exception)
        {
            SetFlowMessage($"无法打开差分：{exception.Message}", true);
        }
    }

    private void ReturnToPack()
    {
        _playing = false; ShowFlowPage(EditorFlowPage.Pack);
    }

    private void SetFlowMessage(string message, bool error)
    {
        var label = _flowPage == EditorFlowPage.Create ? _createMessage : _startMessage;
        if (label is null) return;
        label.Text = message; label.AddThemeColorOverride("font_color", error ? new Color("ff879f") : FlowMuted);
    }

    private static string DifficultyText(EditableChart chart)
    {
        var name = chart.Difficulty == V2Difficulty.Custom ? chart.DifficultyKey ?? "CUSTOM" : chart.Difficulty.ToString().ToUpperInvariant();
        var level = chart.Unrated ? "UNRATED" : chart.Level is null ? "—" : $"LV {chart.Level}";
        var charter = chart.Charters.Count == 0 ? "UNKNOWN" : string.Join(", ", chart.Charters);
        return $"{name}   /   {level}   /   {charter}   ·   {chart.Id}";
    }

    private static string DifficultyTagText(EditableChart chart)
    {
        var name = chart.Difficulty == V2Difficulty.Custom ? chart.DifficultyKey ?? "CUSTOM" : chart.Difficulty.ToString().ToUpperInvariant();
        return chart.Unrated ? $"{name} · UNRATED" : $"{name} · Lv {chart.Level ?? 0}";
    }

    private void RefreshEditTag()
    {
        if (_document is null || _editDifficulty is null) return;
        var chart = _document.SelectedChart;
        var difficultyColor = DifficultyColor(chart.Difficulty);
        _editDifficulty.Text = _auto
            ? $"{DifficultyTagText(chart)}   /   AUTO (F1)"
            : DifficultyTagText(chart);
        _editDifficulty.AddThemeColorOverride("font_color", difficultyColor);
        _editTagAccent.Color = difficultyColor;
        _editTagPanel.Border = new Color(difficultyColor, .56f);
    }

    private static Color DifficultyColor(V2Difficulty difficulty) => difficulty switch
    {
        V2Difficulty.Casual => UiFonts.Casual,
        V2Difficulty.Normal => UiFonts.Normal,
        V2Difficulty.Hard => UiFonts.Hard,
        V2Difficulty.Mega => UiFonts.Mega,
        V2Difficulty.Giga => UiFonts.Giga,
        V2Difficulty.Tech => UiFonts.Giga,
        _ => UiFonts.Dim,
    };

    /// <summary>Small procedural landing-page ornament: no copied artwork or text,
    /// just the same cyan/pink geometry language used by the shared stage.</summary>
    private sealed partial class StartDecoration : Control
    {
        private double _phase;

        public override void _Process(double delta)
        {
            _phase = (_phase + delta * .55) % (Math.PI * 2d);
            QueueRedraw();
        }

        public override void _Draw()
        {
            // The ornament lives in its own shallow strip above the controls. All
            // geometry is derived from Size so a future window scale cannot make
            // an arc spill into the path field or the BROWSE button.
            var center = new Vector2(Size.X * .5f, Size.Y * .5f);
            var extent = Mathf.Min(Size.X, Size.Y);
            var radius = extent * .34f;
            var innerRadius = radius * .70f;
            var outerRadius = radius * 1.34f;
            var diamondRadius = radius * .88f;
            var cyan = new Color(FlowCyan, .70f);
            var pink = new Color(FlowPink, .58f);
            var dim = new Color(FlowCyan, .13f);
            var hairline = new Color(FlowCyan, .065f);

            // A low-contrast technical grid keeps the strip visually active without
            // competing with the form below it.
            var gridStepX = Mathf.Max(36f, Size.X / 9f);
            for (var x = gridStepX * .5f; x < Size.X; x += gridStepX)
                DrawLine(new Vector2(x, 8), new Vector2(x, Size.Y - 8), hairline, 1f);
            var gridStepY = Mathf.Max(28f, Size.Y / 4f);
            for (var y = gridStepY; y < Size.Y; y += gridStepY)
                DrawLine(new Vector2(8, y), new Vector2(Size.X - 8, y), hairline, 1f);

            // Ring system: the arcs rotate at different speeds, like a compact
            // signal-lock indicator, while the center remains quiet and legible.
            DrawCircle(center, outerRadius, new Color(FlowCyan, .018f));
            DrawArc(center, outerRadius, (float)-_phase * .42f, (float)-_phase * .42f + 2.05f, 56, new Color(FlowCyan, .42f), 1.4f, true);
            DrawArc(center, radius, 0, Mathf.Tau, 72, cyan, 1.35f, true);
            DrawArc(center, innerRadius, (float)_phase * 1.15f, (float)_phase * 1.15f + 4.2f, 56, pink, 2f, true);

            // Moving scan beam and its two guide ticks.
            var scanAngle = (float)(_phase * .8d - .8d);
            var scanEnd = center + new Vector2(Mathf.Cos(scanAngle), Mathf.Sin(scanAngle)) * outerRadius;
            DrawLine(center, scanEnd, new Color(FlowPink, .25f), 1.2f);
            var tickAngle = scanAngle + Mathf.Pi * .5f;
            var tickDirection = new Vector2(Mathf.Cos(tickAngle), Mathf.Sin(tickAngle));
            DrawLine(center - tickDirection * (radius * .92f), center + tickDirection * (radius * .92f), new Color(FlowCyan, .13f), 1f);

            // Four orbiting nodes and a slowly breathing core provide motion at a
            // glance without introducing an image asset or extra UI copy.
            var pulse = (float)(.5 + .5 * Math.Sin(_phase * 2d));
            for (var i = 0; i < 4; i++)
            {
                var angle = (float)(_phase * (i % 2 == 0 ? .55d : -.38d) + i * Mathf.Pi * .5d);
                var node = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius * 1.16f);
                DrawCircle(node, 2.2f + pulse * (i % 2 == 0 ? 1.8f : 1f), i % 2 == 0 ? pink : cyan);
                DrawLine(center + (node - center).Normalized() * radius, node, new Color(i % 2 == 0 ? FlowPink : FlowCyan, .16f), 1f);
            }

            var diamond = new[]
            {
                center + new Vector2(0, -diamondRadius),
                center + new Vector2(diamondRadius, 0),
                center + new Vector2(0, diamondRadius),
                center + new Vector2(-diamondRadius, 0)
            };
            DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, new Color(FlowPink, .42f), 1.1f, true);

            // Tiny signal bars anchor the effect to the lower edge of the strip.
            var barWidth = Mathf.Max(3f, extent * .035f);
            var barGap = barWidth * .8f;
            var barStart = Size.X * .74f;
            for (var i = 0; i < 6; i++)
            {
                var barPhase = (float)(_phase * 1.5d + i * .9d);
                var barHeight = 7f + (Mathf.Sin(barPhase) * .5f + .5f) * 18f;
                var bar = new Rect2(barStart + i * (barWidth + barGap), Size.Y - 10f - barHeight, barWidth, barHeight);
                DrawRect(bar, new Color(i % 2 == 0 ? FlowCyan : FlowPink, .30f), true);
            }

            DrawCircle(center, 17f + pulse * 8f, new Color(FlowCyan, .075f));
            DrawCircle(center, 4.5f + pulse * 2.5f, FlowCyan);
            DrawLine(new Vector2(8, center.Y), new Vector2(Size.X - 8, center.Y), dim, 1f);
            DrawLine(new Vector2(center.X, 8), new Vector2(center.X, Size.Y - 8), dim, 1f);
        }
    }
}

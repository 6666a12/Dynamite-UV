using Godot;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain
{
    private enum EditorFlowPage { Start, Pack, Create, Edit, Settings }

    private Control _flowRoot = null!;
    private Control _startPage = null!;
    private Control _packPage = null!;
    private Control _createPage = null!;
    private Control _editChrome = null!;
    private Control _settingsPage = null!;
    private FileDialog _pathDialog = null!;
    private FileDialog _audioDialog = null!;
    private FileDialog _coverDialog = null!;
    private FileDialog _saveDialog = null!;
    private FileDialog _importDialog = null!;
    private readonly EditorDocumentSession _documentSession = new();
    private EditorDocument? _document => _documentSession.Document;
    private EditorFlowPage _flowPage;
    private bool _auto = true;

    private void InitializeEditorFlow()
    {
        // 编辑器设置先于页面构建加载：画布与菜单在首次绘制时就读到持久化值。
        EditorSettings.Current.Load();
        // 画布 _Ready 早于本方法（子节点先就绪），所以背景压暗要在这里按加载后的值补一次。
        _canvas.ApplyBackgroundDim();
        _flowRoot = new Control { Name = "EditorPageFlow", ZIndex = 40, MouseFilter = Control.MouseFilterEnum.Ignore };
        _flowRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_flowRoot);

        _startPage = BuildStartPage();
        _packPage = BuildPackPage();
        _createPage = BuildCreatePage();
        _editChrome = BuildEditChrome();
        _settingsPage = BuildSettingsPage();
        _flowRoot.AddChild(_startPage);
        _flowRoot.AddChild(_packPage);
        _flowRoot.AddChild(_createPage);
        _flowRoot.AddChild(_editChrome);
        _flowRoot.AddChild(_settingsPage);

        _pathDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "SELECT V2 PACKAGE DIRECTORY",
            OkButtonText = "USE DIRECTORY",
            CancelButtonText = "CANCEL",
            UseNativeDialog = true, // Windows 走系统资源管理器对话框
        };
        // OPEN PACKAGE 大卡只负责选目录：回填路径后由路径行的 OPEN / 回车真正解析，
        // 这样点错卡不会直接把页面切走。
        _pathDialog.DirSelected += path =>
        {
            _startView.PackagePath = path;
            SetFlowMessage("已选择目录 · 按 OPEN 或回车打开。", false);
        };
        _flowRoot.AddChild(_pathDialog);
        // Pack 页的 IMPORT PACKAGE 行是同一套打开流程，但选完目录直接解析（不再回填路径行），
        // 成功/失败都留在 Pack 页回报。
        _importDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "IMPORT V2 PACKAGE DIRECTORY",
            OkButtonText = "IMPORT PACKAGE",
            CancelButtonText = "CANCEL",
            UseNativeDialog = true, // Windows 走系统资源管理器对话框
        };
        _importDialog.DirSelected += path => TryOpenPackage(path);
        _flowRoot.AddChild(_importDialog);
        _audioDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "SELECT EXTERNAL AUDIO FILE",
            OkButtonText = "USE AUDIO",
            CancelButtonText = "CANCEL",
            UseNativeDialog = true, // Windows 走系统资源管理器对话框
        };
        _audioDialog.FileSelected += path =>
        {
            _createOverlay.SetAudioPath(path);
            // The shell supplies no host audio probe, so the writer's built-in probe accepts
            // RIFF/WAVE only; warn before an unmutable draft hits that wall on its first save.
            _createOverlay.Report(string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)
                ? "已选择外部音频；创建后仍是内存草稿，直到第一次 SAVE 才落盘。"
                : "当前落盘只支持 .wav（PCM/IEEE-float）音频；其他格式会在首次保存时被 writer 拒绝。", false);
        };
        _flowRoot.AddChild(_audioDialog);
        // 封面是可选的外部图片：镜像源与音频一致（writer 会把 PNG/JPG 复制进包，写入的是实际字节）。
        _coverDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "SELECT COVER IMAGE",
            OkButtonText = "USE IMAGE",
            CancelButtonText = "CANCEL",
            UseNativeDialog = true, // Windows 走系统资源管理器对话框
            Filters = ["*.png, *.jpg, *.jpeg ; PNG / JPG"],
        };
        _coverDialog.FileSelected += path => _createOverlay.SetCoverPath(path);
        _flowRoot.AddChild(_coverDialog);
        // Godot only reports directories that exist, so Save/Save As targets the picked folder
        // itself; an existing but empty folder stays valid through AllowEmptyDestination.
        _saveDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "SELECT SAVE DESTINATION DIRECTORY",
            OkButtonText = "SAVE HERE",
            CancelButtonText = "CANCEL",
            UseNativeDialog = true, // Windows 走系统资源管理器对话框
        };
        _saveDialog.DirSelected += SaveToDirectory;
        _flowRoot.AddChild(_saveDialog);
        ShowFlowPage(EditorFlowPage.Start);
    }

    private void ShowFlowPage(EditorFlowPage page)
    {
        _flowPage = page;
        _startPage.Visible = page == EditorFlowPage.Start;
        _packPage.Visible = page == EditorFlowPage.Pack;
        _createPage.Visible = page == EditorFlowPage.Create;
        _editChrome.Visible = page == EditorFlowPage.Edit;
        _settingsPage.Visible = page == EditorFlowPage.Settings;
        var editing = page == EditorFlowPage.Edit;
        var activePage = page switch
        {
            EditorFlowPage.Start => _startPage,
            EditorFlowPage.Pack => _packPage,
            EditorFlowPage.Create => _createPage,
            EditorFlowPage.Settings => _settingsPage,
            _ => _editChrome,
        };
        // GENERAL / MOTION 是编辑器动效的真接点：OFF 时页面切换与右键菜单都不再淡入，
        // 直接落到终值；REDUCED 与 FULL 目前相同（编辑器只有这两种淡入，见 docs/dyna-maker-uv.md）。
        if (EditorSettings.Current.MotionMode == UiMotionMode.Off)
        {
            activePage.Modulate = Colors.White;
        }
        else
        {
            activePage.Modulate = new Color(1, 1, 1, 0);
            CreateTween().TweenProperty(activePage, "modulate", Colors.White, .22f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        _canvas.Visible = editing;
        if (_menu is not null) _menu.Hide();
        // 离开 Edit 时打断播放，同时把惯性目标拉回显示值：下次进 Edit 不会先"漂"到上次滚轮的目标。
        if (!editing) { _playing = false; _timeTarget = _time; _canvas.InputSuspended = true; } else { _canvas.InputSuspended = false; }
        if (page == EditorFlowPage.Pack) RefreshPackPage();
    }

    private void OpenChart(string chartId)
    {
        if (_document is null) return;
        try
        {
            _document.SelectChart(chartId);
            _canvas.AttachDocument(_document);
            _canvas.SetSide(0); _canvas.SetTool(4);
            // 打开文档时把设置的默认网格写进当前文档：v2 不存编辑期网格分母，
            // 它只属于本次会话的设置（EditorDocument 加载时是常量默认值）。
            _canvas.ApplyDefaultGridDivisor();
            // 程序性刷新（打开文档即进 AUTO 播放）：视图时间直接归零，显示值与目标值一起落。
            JumpTime(0);
            SetAuto(true);
            RefreshEditHud();
            RefreshSaveState();
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

    /// <summary>Create 页没有独立的消息 Label：回报与前置校验共用设计稿的中缝状态行。</summary>
    private void SetFlowMessage(string message, bool error)
    {
        if (_flowPage == EditorFlowPage.Create)
        {
            _createOverlay.Report(message, error);
            return;
        }
        if (_flowPage == EditorFlowPage.Pack)
        {
            if (_packMessage is null) return;
            _packMessage.Text = message;
            _packMessage.AddThemeColorOverride("font_color", error ? new Color("ff879f") : FlowMuted);
        }
        else _startView?.Report(message, error);
    }
}

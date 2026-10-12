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

/// <summary>Page flow around the fixed authoring stage. The page model deliberately keeps
/// package validation at the v2 repository boundary: a path is never promoted to the pack
/// page unless the complete directory can be decoded and validated.</summary>
public partial class StandaloneEditorMain
{
    private enum EditorFlowPage { Start, Pack, Create, Edit, Settings }
    private enum CreateMode { NewPackage, AddDifficulty }

    private Control _flowRoot = null!;
    private Control _startPage = null!;
    private Control _packPage = null!;
    private Control _createPage = null!;
    private Control _editChrome = null!;
    private Control _settingsPage = null!;
    private SettingsPageOverlay _settingsOverlay = null!;
    private EditorFlowPage _settingsReturn;
    private LineEdit _packagePath = null!;
    private Tween? _pathPlateTween;
    private Label _startMessage = null!;
    private Label _packMessage = null!;
    private Label _saveState = null!;
    private PackPageOverlay _packOverlay = null!;
    private CreatePageOverlay _createOverlay = null!;
    private Texture2D? _packCover;
    private string? _packCoverKey;
    private string? _digestAudioPath;
    private string? _digestAudioSha;
    // DIGEST 音频哈希的后台任务状态：任务只算字节哈希，写回 _digestAudioSha/_digestAudioPath
    // 一律在主线程 CallDeferred 里做。_digestPending 是「是否有进行中的任务」+ 代际守卫用的目标
    // 期望（任务启动时记录，完成时与之一致才落结果）；_digestLastMeta 是上次算过哈希时的文件
    // 元数据，路径相同但元数据变了就重算（同路径换内容不吞过期哈希）。
    private bool _digestPending;
    private string? _digestExpectedPath;
    private string? _digestAudioMetaKey;
    // Edit 主态的裸舞台只有四角纯文字 HUD（docs/editor-ui-design.md §4.1）：
    // 右上 GRID/SNAP(/AUTO)、左下曲名 + 难度色名 + LV、右下时间 + BAR/BPM。
    private Label _hudGrid = null!;
    private Label _hudTitle = null!;
    private Label _hudDifficulty = null!;
    private Label _hudLevel = null!;
    private Label _hudTime = null!;
    private Label _hudBarBpm = null!;
    /// <summary>路径节点编辑态的顶中标签（设计稿 PATH_FIELD 的 `PATH EDIT`）。</summary>
    private Label _hudPathEdit = null!;
    private FileDialog _pathDialog = null!;
    private FileDialog _audioDialog = null!;
    private FileDialog _coverDialog = null!;
    private FileDialog _saveDialog = null!;
    private FileDialog _importDialog = null!;
    private EditorDocument? _document;
    private EditorProjectDraft? _draft;
    private string? _saveError;
    private EditorFlowPage _flowPage;
    private CreateMode _createMode;
    private bool _memoryDraft;
    private bool _auto = true;

    // Keep page chrome on the same palette as the game HUD. UiFonts is linked
    // from the client project in the editor csproj so both surfaces share the
    // same font, difficulty colors, panel fill and line colors.
    private static readonly Color FlowBackground = UiFonts.Bg;
    private static readonly Color FlowPanel = UiFonts.Panel;
    private static readonly Color FlowCyan = UiFonts.Cyan;
    private static readonly Color FlowMuted = UiFonts.Dim;
    private static readonly Color FlowAmber = UiFonts.Hard;
    /// <summary>Create 板的初始时间线默认值：BPM 与音频偏移在 TIMING 面板可改，默认沿用既有 150 / 0。</summary>
    private const double DefaultInitialBpm = 150d, DefaultAudioOffsetSec = 0d;
    // 设计稿的 HUD 字号带字距（GRID/SNAP 1px、BAR/BPM 1.5px），Godot 侧用 FontVariation 的
    // 字形间距表达；字距为 0 的那几行直接吃原始字体。
    // 正文一律 Medium（窗口 0.833 折 + Regular 偏细，见 EditorFonts）；TechBold 是强调字，不动。
    private static readonly Font HudFont = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 1 };
    // 设计稿的 ls1.5 用整数字形间距（FontVariation 只有整数像素）按仓库既有约定编码为 2：
    // HUD 右下的 BAR·BPM 行与路径编辑态的顶中标签都是这一档。
    private static readonly Font HudFontWide = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };
    private static readonly Font PathEditFont = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 3 };
    // Start 板同理：eyebrow ls8、wordmark ls10、水印 ls12、卡面 ls2。
    private static readonly Font StartEyebrow = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 8 };
    private static readonly Font StartWordmark = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 10 };
    private static readonly Font StartWatermark = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 12 };
    private static readonly Font StartCaption = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 2 };
    /// <summary>Pack 页底部状态行（设计稿 13px ls2）。</summary>
    private static readonly Font PackStatusFont = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };

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
            _packagePath.Text = path;
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

    /// <summary>Start 板（设计稿 Start）。背景是两行重复水印 + 椭圆轨道系统，居中标题组下面是
    /// 两张 400×150 大卡：OPEN PACKAGE 承担选目录（BROWSE），NEW PACKAGE 进 Create 页。
    /// 设计板没有路径输入行，所以路径能力压成卡下一条 slim 行——手输/粘贴 + 回车或 OPEN
    /// 打开，卡选完目录只回填路径，不会因为误点就开始解析包。</summary>
    private Control BuildStartPage()
    {
        var page = NewPage(rule: false);

        var backdrop = new StartBackdrop { MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddChild(backdrop);
        // 水印两行是刻意的出血位（x=-20 / -260），所以 Label 给足宽度且不裁切。
        // 字号一律传**设计稿值**，由 EditorFonts.ApplySize 登记表统一换算与重放（UI SCALE 立即生效）。
        AddStartText(page, -20, 60, 6000, 80, Watermark, 64, new Color(FlowCyan, .10f), StartWatermark);
        AddStartText(page, -260, 975, 6000, 80, Watermark, 64, new Color(UiFonts.Text, .07f), StartWatermark);

        // 居中标题组：eyebrow 中心 (960,246)、wordmark 中心 (960,340)。
        AddStartText(page, 0, 234, 1920, 24, "DYNAMITE UNIVERSE", 16, FlowMuted, StartEyebrow, HorizontalAlignment.Center);
        AddStartText(page, 0, 266, 1920, 148, "DYNAMAKER UV", 118, FlowCyan, StartWordmark, HorizontalAlignment.Center);

        var openCard = new StartCard
        {
            Position = new Vector2(540, 680), Size = new Vector2(400, 150),
            Fill = FlowPanel, Border = UiFonts.Line, Glyph = StartIcon.Folder, GlyphColor = FlowCyan
        };
        openCard.Pressed += () => _pathDialog.PopupCenteredRatio(.82f);
        page.AddChild(openCard);
        AddStartText(openCard, 100, 57, 280, 36, "OPEN PACKAGE", 26, FlowCyan, StartCaption);

        var newCard = new StartCard
        {
            Position = new Vector2(980, 680), Size = new Vector2(400, 150),
            Fill = FlowCyan, Glyph = StartIcon.Plus, GlyphColor = UiFonts.InkText
        };
        newCard.Pressed += () => { _createMode = CreateMode.NewPackage; PrepareNewPackageForm(); ShowFlowPage(EditorFlowPage.Create); };
        page.AddChild(newCard);
        AddStartText(newCard, 100, 57, 280, 36, "NEW PROJECT", 26, UiFonts.InkText, StartCaption);

        _packagePath = NewStartPathField(page, 540, 862, 640, 52);
        var openChip = new StartCard
        {
            Position = new Vector2(1192, 862), Size = new Vector2(188, 52), Cut = 10f, Fill = FlowCyan
        };
        openChip.Pressed += TryOpenPackage;
        page.AddChild(openChip);
        AddStartText(openChip, 0, 0, 188, 52, "OPEN", 15, UiFonts.InkText, StartCaption, HorizontalAlignment.Center);

        _startMessage = AddFlowLabel(page, 540, 918, 840, 48, string.Empty, 15, FlowMuted);
        // 设计板没有 Settings 入口（§4.7 只写「Start 页进入」），按同一套语言补一枚低调文字键在右下角：
        // dim 13px ls2，hover 转 cyan，与页面底部同一 y 带（Settings 板的 RESET/BACK 行）。
        var settings = new Button
        {
            Position = new Vector2(1620, 976), Size = new Vector2(260, 32), Text = "SETTINGS",
            Alignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Stop,
            // 不吃键盘焦点：编辑器里 SPACE / ENTER 是预览快捷键，按钮抢焦点会把它们吃掉。
            FocusMode = Control.FocusModeEnum.None
        };
        var flat = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
            settings.AddThemeStyleboxOverride(state, flat);
        settings.AddThemeFontOverride("font", StartCaption);
        EditorFonts.ApplySize(settings, 13);
        // SETTINGS 文字键：stylebox 全空、无自绘形状，hover 的 dim → cyan 过渡按共享 hover 档
        // lerp——font_color 覆盖由每帧的过渡值重写（hover/pressed 静态覆盖移除，颜色统一由值决定）。
        // 推进挂在 shell 自己的 _Process（它本就为 HUD 每帧在跑），到终值即停写，无空转成本。
        settings.AddThemeColorOverride("font_color", FlowMuted);
        _settingsKey = settings;
        settings.Pressed += () => OpenSettings(EditorFlowPage.Start);
        page.AddChild(settings);
        return page;
    }

    /// <summary>Start 页 SETTINGS 文字键与其 hover 过渡值：壳层 _Process 每帧推进，
    /// MOTION OFF 首帧即落终值；到终值后不再写颜色覆盖（值已到位，无逐帧成本）。</summary>
    private Button? _settingsKey;
    private HoverFade _settingsHover;

    private const string Watermark =
        "DESIGN · CHART · SHIP · DESIGN · CHART · SHIP · DESIGN · CHART · SHIP · DESIGN · CHART · SHIP · DESIGN · CHART · SHIP";

    /// <summary>Start 板的文字：字距走 FontVariation，垂直居中——放置一律给框中心，
    /// 与设计稿的 ctxt/vtxt 锚点同一套语义。<paramref name="designSize"/> 是设计稿字号，
    /// 登记进 <see cref="EditorFonts"/> 的注册表，UI SCALE 换档时随之重放。</summary>
    private static Label AddStartText(Node parent, float x, float y, float w, float h, string text,
        int designSize, Color color, Font font, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", font);
        EditorFonts.ApplySize(label, designSize);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>路径行：切角底板 + 无样式 LineEdit（样式全给底板，与客户端搜索框同一做法）。</summary>
    private LineEdit NewStartPathField(Control page, float x, float y, float w, float h)
    {
        var plate = new CutPanel
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Cut = 10f,
            Fill = new Color("0f1429", .85f), Border = UiFonts.Line, BorderWidth = 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        page.AddChild(plate);
        var field = new LineEdit
        {
            Position = new Vector2(x + 14f, y + 5f), Size = new Vector2(w - 28f, h - 10f),
            PlaceholderText = "PACKAGE DIRECTORY",
            CaretBlink = true,
        };
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "focus", "read_only" }) field.AddThemeStyleboxOverride(state, empty);
        field.AddThemeFontOverride("font", EditorFonts.Medium);
        EditorFonts.ApplySize(field, 17);
        field.AddThemeColorOverride("font_color", UiFonts.Text);
        field.AddThemeColorOverride("font_placeholder_color", FlowMuted);
        field.AddThemeColorOverride("caret_color", FlowCyan);
        // 样式全给底板，LineEdit 三个 stylebox 都是空的，焦点反馈只能画在底板描边上：
        // 聚焦时 Border 平滑过渡到 cyan（.12s），失焦过渡回去；快速进出焦点要 kill 上一个
        // Tween，避免两个动画打架把描边留在中间色。MOTION OFF 尊重设置真源，直接跳终值。
        field.FocusEntered += () => TweenPathPlateBorder(plate, UiFonts.Cyan);
        field.FocusExited += () => TweenPathPlateBorder(plate, UiFonts.Line);
        field.TextSubmitted += _ => TryOpenPackage();
        page.AddChild(field);
        return field;
    }

    private void TweenPathPlateBorder(CutPanel plate, Color target)
    {
        _pathPlateTween?.Kill();
        _pathPlateTween = null;
        if (EditorSettings.Current.MotionMode == UiMotionMode.Off)
        {
            plate.Border = target;
            return;
        }
        var from = plate.Border;
        _pathPlateTween = CreateTween();
        _pathPlateTween.TweenMethod(Callable.From<float>(t => plate.Border = from.Lerp(target, t)),
            0f, 1f, .12f);
    }

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

    /// <summary>Editor — Settings 板：整页自绘（见 <see cref="SettingsPageOverlay"/>），
    /// shell 只负责把设置真源以快照传入、把页面事件落进 <see cref="EditorSettings"/> 并落盘。
    /// 两个入口（Start 页的 SETTINGS 文字键、编辑态右键菜单的 SETTINGS 行）进同一页，
    /// BACK 回到进入前的那一页（<see cref="_settingsReturn"/>）。</summary>
    private Control BuildSettingsPage()
    {
        var page = NewPage(rule: false);
        _settingsOverlay = new SettingsPageOverlay();
        _settingsOverlay.BackRequested += () => ShowFlowPage(_settingsReturn);
        _settingsOverlay.ResetRequested += ResetSettings;
        _settingsOverlay.MotionSelected += index =>
        {
            EditorSettings.Current.MotionMode = (UiMotionMode)index;
            CommitSettings();
        };
        _settingsOverlay.UiScaleSelected += index =>
        {
            EditorSettings.Current.UiScalePercent = EditorSettings.UiScaleChoices[index];
            ApplyUiScale();
            CommitSettings();
        };
        _settingsOverlay.GridSelected += index =>
        {
            EditorSettings.Current.DefaultGridDivisor = EditorSettings.GridDivisorChoices[index];
            _canvas.ApplyDefaultGridDivisor();
            CommitSettings();
        };
        _settingsOverlay.SnapStepped += StepSnapStep;
        _settingsOverlay.SliderChanged += ApplySettingsSlider;
        _settingsOverlay.EditCommitted += () => EditorSettings.Current.Save();
        page.AddChild(_settingsOverlay);
        return page;
    }

    /// <summary>Start 页与编辑态右键菜单共用这一条：记录来源页，BACK 各回其处。</summary>
    private void OpenSettings(EditorFlowPage from)
    {
        _settingsReturn = from;
        RefreshSettingsPage();
        ShowFlowPage(EditorFlowPage.Settings);
    }

    private void RefreshSettingsPage()
    {
        if (_settingsOverlay is null) return;
        var settings = EditorSettings.Current;
        _settingsOverlay.Apply(new SettingsPageOverlay.Snapshot
        {
            MotionIndex = (int)settings.MotionMode,
            UiScaleIndex = settings.UiScaleChoiceIndex,
            GridIndex = settings.GridChoiceIndex,
            SnapText = SettingsPageOverlay.SnapText(settings.SnapStep),
            GridOpacityPercent = settings.GridOpacityPercent,
            BackgroundDimPercent = settings.BackgroundDimPercent,
            ScrollInertiaPercent = settings.ScrollInertiaPercent,
        });
    }

    /// <summary>离散改动（档位/复位）：落盘后重推快照，页面读数与设置真源保持同一份。</summary>
    private void CommitSettings()
    {
        EditorSettings.Current.Save();
        RefreshSettingsPage();
    }

    /// <summary>UI SCALE 的立即生效：重放字号注册表（Label / LineEdit / Button 靠它），再把
    /// 自绘页面重绘一次——画布、Pack / Create / Settings overlay 的字号都在 <c>_Draw</c> 里现算
    /// <see cref="EditorFonts.Pt"/>，所以**重绘就是应用**，不需要重建任何页面。
    /// 三个 overlay 都刷而不是只刷当前页：从 Edit 右键菜单进 Settings 时画布文字仍然可见，
    /// 少刷一个会留下旧字号的残影。Start 页的文字全是注册表管的 Label，没有自绘字号。</summary>
    private void ApplyUiScale()
    {
        EditorFonts.ReapplySizes();
        _canvas.RedrawForUiScale();
        _packOverlay?.QueueRedraw();
        _createOverlay?.QueueRedraw();
        _settingsOverlay?.QueueRedraw();
        _menu?.QueueRedraw();
    }

    private void StepSnapStep(int direction)
    {
        var choices = EditorSettings.SnapStepChoices;
        var index = EditorSettings.Current.SnapChoiceIndex;
        EditorSettings.Current.SnapStep = choices[Math.Clamp(index + direction, 0, choices.Length - 1)];
        CommitSettings();
    }

    /// <summary>滑条只改内存与画面，松手（<c>EditCommitted</c>）才写盘，避免拖动时反复写文件。
    /// 拖动中把新值推回页面快照，滑条与百分比读数才会跟着走。
    /// SCROLL INERTIA 没有额外接点：画布滚轮每帧读设置真源（`ScrollInertiaTauSeconds`），
    /// 所以拖动中正在进行的滑动立刻换手感，不需要重推。</summary>
    private void ApplySettingsSlider(int slider, int percent)
    {
        switch (slider)
        {
            case SettingsPageOverlay.GridOpacitySlider:
                EditorSettings.Current.GridOpacityPercent = percent;
                _canvas.QueueRedraw();
                break;
            case SettingsPageOverlay.BackgroundDimSlider:
                EditorSettings.Current.BackgroundDimPercent = percent;
                _canvas.ApplyBackgroundDim();
                break;
            case SettingsPageOverlay.ScrollInertiaSlider:
                EditorSettings.Current.ScrollInertiaPercent = percent;
                break;
            default:
                return;
        }
        RefreshSettingsPage();
    }

    /// <summary>RESET DEFAULTS 是真实动作：回默认值、落盘，并把当前文档的网格与画面接点一起拉回默认。
    /// UI SCALE 也一并回到 100%，所以这里同样要重放字号。</summary>
    private void ResetSettings()
    {
        EditorSettings.Current.ResetToDefaults();
        EditorSettings.Current.Save();
        _canvas.ApplyDefaultGridDivisor();
        _canvas.ApplyBackgroundDim();
        ApplyUiScale();
        RefreshSettingsPage();
    }

    /// <summary>Edit 主态的 HUD 层。裸舞台是 GameplayStageRenderer 的事，这一层只有四角纯文字：
    /// 右上 GRID/SNAP(/AUTO)、左下曲名 + 难度色名 + LV、右下游标时间 + BAR/BPM，顶中是路径节点
    /// 编辑态的 `PATH EDIT`；坐标取设计稿 Editor — Main / PATH_FIELD。
    /// 返回包页与保存状态压到左下角曲名行上方，不占舞台。</summary>
    private Control BuildEditChrome()
    {
        var chrome = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var back = NewCutButton("‹ PACK", 32, 922, 170, 48);
        back.MouseFilter = Control.MouseFilterEnum.Stop; back.Pressed += ReturnToPack; chrome.AddChild(back);
        _saveState = HudLabel(chrome, 220, 930, 1652, 32, TechFont, 15, FlowMuted, HorizontalAlignment.Right);

        _hudGrid = HudLabel(chrome, 672, 23, 1200, 26, HudFont, 12, FlowMuted, HorizontalAlignment.Right);
        // 设计稿 PATH_FIELD 的顶中标签：13px bold cyan ls3，中心 (960,36)，只在路径节点编辑态出现。
        _hudPathEdit = HudLabel(chrome, 360, 23, 1200, 26, PathEditFont, 13, FlowCyan, HorizontalAlignment.Center);
        _hudPathEdit.Text = "PATH EDIT";
        _hudPathEdit.Visible = false;

        var songRow = new HBoxContainer
        {
            Position = new Vector2(48, 1001), Size = new Vector2(1300, 30),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        songRow.AddThemeConstantOverride("separation", 8);
        chrome.AddChild(songRow);
        _hudTitle = HudRowLabel(songRow, UiFonts.TechBold, 18, UiFonts.Text);
        _hudDifficulty = HudRowLabel(songRow, UiFonts.TechBold, 13, FlowAmber);
        _hudLevel = HudRowLabel(songRow, EditorFonts.Medium, 13, FlowMuted);

        _hudTime = HudLabel(chrome, 672, 992, 1200, 28, UiFonts.TechBold, 18, UiFonts.Text, HorizontalAlignment.Right);
        _hudBarBpm = HudLabel(chrome, 672, 1022, 1200, 22, HudFontWide, 11, FlowMuted, HorizontalAlignment.Right);
        return chrome;
    }

    /// <summary>HUD 文字一律单行、不做自动换行：设计稿的四角标签宽度固定，换行会把角标顶出画布。
    /// <paramref name="designSize"/> 是设计稿字号，登记进注册表以便 UI SCALE 重放。</summary>
    private static Label HudLabel(Node parent, float x, float y, float w, float h, Font font, int designSize, Color color, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Text = string.Empty,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", font);
        EditorFonts.ApplySize(label, designSize);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>曲名行里的标签：字号不同，靠容器高度 + 垂直居中保持同一基线。</summary>
    private static Label HudRowLabel(Node parent, Font font, int designSize, Color color) =>
        HudLabel(parent, 0, 0, 0, 0, font, designSize, color, HorizontalAlignment.Left);

    /// <summary>页底：背景 + 可选标题分隔线。Start 板按设计稿没有那条线。</summary>
    private Control NewPage(bool rule = true)
    {
        var page = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = FlowBackground, MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddChild(background);
        if (!rule) return page;
        var hairline = new ColorRect { Position = new Vector2(90, 118), Size = new Vector2(1545, 2), Color = new Color(FlowCyan, .45f), MouseFilter = Control.MouseFilterEnum.Ignore };
        page.AddChild(hairline);
        return page;
    }

    private Label AddFlowLabel(Node parent, float x, float y, float w, float h, string text, int designSize, Color color)
    {
        var label = LabelAt(x, y, w, h, text, designSize, color);
        parent.AddChild(label); return label;
    }

    /// <summary>编辑态的描边动作键：切角形状自绘（全编辑器统一的「只切左上 + 右下」语言，
    /// StyleBoxFlat 的圆角表达不了切角，Start 板的卡也是同一做法）。</summary>
    private EditCutButton NewCutButton(string text, float x, float y, float w, float h)
    {
        var button = new EditCutButton
        {
            Position = new Vector2(x, y), Size = new Vector2(w, h), Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.AddThemeFontOverride("font", EditorFonts.Medium);
        EditorFonts.ApplySize(button, 17);
        // 自绘按钮的四个状态色都显式给，避免默认主题的 hover/pressed 色把文字变白。
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, UiFonts.Text);
        return button;
    }

    /// <summary>Edit 主态的切角动作键（`‹ PACK`）：形状与 hover/pressed 自绘，
    /// 切角 10 = 设计稿底部操作条那一档。hover 提亮走统一过渡（0..1 lerp）。</summary>
    private sealed partial class EditCutButton : Button
    {
        private const float Cut = 10f;
        private const float HoverLift = .12f;
        // hover 统一过渡：accent 提亮按 0..1 值 lerp；hover 变化才开 _Process，到终值自关。
        private HoverFade _hoverFade;

        public override void _Ready()
        {
            var empty = new StyleBoxEmpty();
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
                AddThemeStyleboxOverride(state, empty);
            // Button 的 hover 状态变了 Godot 会自行重绘文字态，但形状画在本层 _Draw：
            // hover 值要靠 _Process 推进，进/出 hover 都开，到终值自关（不空转）。
            MouseEntered += () => SetProcess(true);
            MouseExited += () => SetProcess(true);
            SetProcess(false);
        }

        public override void _Process(double delta)
        {
            // MOTION OFF 时 Update 直接跳终值，同一帧 SetProcess(false) 生效，无过渡残留。
            QueueRedraw();
            if (_hoverFade.Update(IsHovered(), delta)) return;
            SetProcess(false);
        }

        public override void _Draw()
        {
            // 按钮的文字与状态色全由这里决定，所以字体颜色覆盖也要齐全（默认主题的 hover/pressed 色不同）。
            var mode = GetDrawMode();
            var pressed = mode is DrawMode.Pressed or DrawMode.HoverPressed;
            var accent = UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), _hoverFade.Value);
            EditorOverlayDraw.CutPanel(this, new Rect2(Vector2.Zero, Size), Cut,
                pressed ? new Color(UiFonts.Cyan, .28f) : new Color(UiFonts.PanelHover, .85f), accent, 2f);
        }
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

    /// <summary>Start 页路径行与 Pack 页 IMPORT 行共用这一条打开流程：目录先过
    /// <see cref="EditorPackageRepository.Open"/> 的严格校验，失败不换页，并保留当前已打开的文档。</summary>
    private void TryOpenPackage() => TryOpenPackage(null);

    private void TryOpenPackage(string? directory)
    {
        var path = (directory ?? _packagePath.Text).Trim();
        if (string.IsNullOrWhiteSpace(path)) { SetFlowMessage("请选择一个目录。", true); return; }
        try
        {
            var document = EditorPackageRepository.Open(path);
            _document = document; _draft = null; _memoryDraft = false; _saveError = null;
            _packagePath.Text = path;
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

    /// <summary>DIGEST 盾标：用当前编辑状态真算一次 gameplay-v1 digest（内存谱面投影 + 音频字节的
    /// SHA-256），与保存时 writer 写入的身份同一算法。音频哈希按「路径 + 文件元数据（最后写入时间 +
    /// 长度）」缓存，换包/保存/文件被替换后才重算；哈希计算在后台线程做，主线程只读元数据判断
    /// 失效与派发任务（首次进 Pack 页不整读音频）。
    /// 取不到音频（无音频引用的包）或谱面当前校验不过（例如还没有主判定）时返回 false，
    /// 页面画压暗的 `DIGEST —`，不假装通过。异步化后第一次调用哈希未就绪同样返回 false，
    /// 哈希就绪后由 <see cref="ApplyDigestAudioSha"/> 的回调再刷一次盾标。</summary>
    private bool TryDigest()
    {
        if (_document is null) return false;
        try
        {
            var audio = ResolveDigestAudio();
            if (audio is null)
            {
                // 没有音频引用：清掉缓存与进行中任务的期望，盾标维持压暗。
                InvalidateDigestAudioHash();
                return false;
            }
            EnsureDigestAudioHash(audio);
            if (!string.Equals(_digestAudioPath, audio, StringComparison.OrdinalIgnoreCase) ||
                _digestAudioSha is null)
                return false; // 哈希未就绪（后台任务进行中/失败），盾标先画压暗的 `DIGEST —`。
            var entry = _document.BuildPackSnapshot().Charts.Single(item => item.Id == _document.SelectedChartId);
            _ = V2GameplayDigest.Compute(_document.BuildChartSnapshot(_document.SelectedChartId),
                entry.JudgePreset, _digestAudioSha);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>保证 <paramref name="audio"/> 的字节 SHA-256 已缓存且不过期：元数据（最后写入时间 +
    /// 长度）与缓存键一致就直接用；路径或元数据变了就在后台线程重算，完成时 CallDeferred 回主线程
    /// 落 <see cref="_digestAudioSha"/>/<see cref="_digestAudioPath"/> 并刷新盾标。
    /// 元数据读取很轻，留在主线程做失效判断；同一路径已有任务在跑时不重复起任务。</summary>
    private void EnsureDigestAudioHash(string audio)
    {
        var metaKey = DigestAudioMetaKey(audio);
        if (!_digestPending &&
            string.Equals(_digestAudioPath, audio, StringComparison.OrdinalIgnoreCase) &&
            _digestAudioSha is not null &&
            string.Equals(_digestAudioMetaKey, metaKey, StringComparison.Ordinal))
            return; // 缓存命中：路径与元数据都没变，直接沿用。
        if (_digestPending && string.Equals(_digestExpectedPath, audio, StringComparison.OrdinalIgnoreCase))
            return; // 同一路径的任务已在跑，等它回来即可。
        // 派发新任务（可能是首次、路径变了或元数据变了）：记下期望与元数据键。
        _digestPending = true;
        _digestExpectedPath = audio;
        _digestAudioMetaKey = metaKey;
        var expected = audio;
        _ = Task.Run(() =>
        {
            // CallDeferred 走 Godot 消息队列，可跨线程调用，回调在主线程执行；
            // 不装 SynchronizationContext 的 Godot 主线程上 FromCurrentSynchronizationContext 会直接抛。
            var sha = DigestAudioHashOnBackground(expected);
            CallDeferred(nameof(ApplyDigestAudioSha), expected,
                sha is null ? "" : Convert.ToHexStringLower(sha));
        });
    }

    /// <summary>后台线程只读文件字节并算 SHA-256，绝不碰 Godot 对象；读文件失败（被删等）时
    /// 返回 null，由主线程静默放弃，不落缓存，盾标维持压暗的 `DIGEST —`。</summary>
    private static byte[]? DigestAudioHashOnBackground(string path)
    {
        try
        {
            return SHA256.HashData(File.ReadAllBytes(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>后台任务的回调：只在主线程执行（任务里 CallDeferred 回到这里），
    /// 用代际守卫丢弃过期结果——期望路径已变（换包、保存后又触发）或节点已释放时一律不落缓存。
    /// 哈希就绪后触发一次盾标重绘（Pack 页重画 <c>DIGEST OK</c>）。</summary>
    private void ApplyDigestAudioSha(string path, string sha256Hex)
    {
        // CallDeferred 到已释放的节点会被 Godot 静默丢弃，但这里显式守一下更稳。
        if (!IsInstanceValid(this)) return;
        // 期望已被清掉（换包/保存的失效点），或回来的不是当前期望那份：丢弃过期结果。
        if (!_digestPending ||
            !string.Equals(_digestExpectedPath, path, StringComparison.OrdinalIgnoreCase))
            return;
        _digestPending = false;
        _digestExpectedPath = null;
        if (sha256Hex.Length == 0 ||
            !string.Equals(path, ResolveDigestAudio(), StringComparison.OrdinalIgnoreCase))
            return; // 读文件失败，或期望路径已不是当前需要的音频：静默放弃，不落缓存。
        if (!string.Equals(_digestAudioMetaKey, DigestAudioMetaKey(path), StringComparison.Ordinal))
        {
            // 哈希途中文件被替换（元数据已变）：这份结果是旧内容的，重派一次拿新内容的。
            EnsureDigestAudioHash(path);
            return;
        }
        _digestAudioSha = sha256Hex;
        _digestAudioPath = path;
        // 哈希就绪：盾标从压暗的 `DIGEST —` 变 `DIGEST OK`，Pack 页重绘一次。
        if (_flowPage == EditorFlowPage.Pack) RefreshPackPage();
    }

    /// <summary>让 digest 音频哈希缓存与进行中的后台任务一起失效（开包/保存/音频引用消失时调用）：
    /// 进行中的任务完成时会因 _digestPending 已清而丢弃结果。</summary>
    private void InvalidateDigestAudioHash()
    {
        _digestAudioSha = null;
        _digestAudioPath = null;
        _digestAudioMetaKey = null;
        _digestExpectedPath = null;
        _digestPending = false;
    }

    /// <summary>digest 音频缓存的元数据键：最后写入时间（UTC）+ 文件长度。路径相同但元数据变了就
    /// 重算，同路径换内容不会用过期哈希；元数据读取很轻，留在主线程做失效判断。</summary>
    private static string? DigestAudioMetaKey(string audio)
    {
        try
        {
            var info = new FileInfo(audio);
            return $"{File.GetLastWriteTimeUtc(audio):O}|{info.Length}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>digest 用的音频源：已落盘包走 writer 的严格包内解析（差分自身音频优先），
    /// 内存草稿用 draft 的外部音频——两者都是保存时会写进包的那份字节。</summary>
    private string? ResolveDigestAudio()
    {
        if (_document!.PackageDirectory is { } root)
        {
            var relative = _document.SelectedChart.Audio ?? _document.Audio;
            return relative is null ? null : V2PackageWriter.ResolveRegularFile(root, relative);
        }
        return _draft is null ? null : Path.GetFullPath(_draft.ExternalAudioSource);
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
                var draft = EditorProjectDraftFactory.Create(request);
                _draft = draft;
                _document = draft.Document; _memoryDraft = true;
            }
            else
            {
                if (_document is null) throw new InvalidOperationException("没有打开的包。");
                // A new difficulty joins the already opened package, so its resources stay in-package.
                _draft = null;
                if (_document.Charts.Any(chart => chart.Id == id)) throw new InvalidOperationException($"Chart ID '{id}' 已存在。");
                var chart = new EditableChart { Id = id, Difficulty = difficulty, DifficultyKey = key, Level = level, Unrated = unrated, File = $"charts/{id}.json", Audio = null, Preview = null, AudioOffsetSec = 0 };
                chart.Charters.Add(charter); chart.Bpms.Add(new EditableBpm { Time = ExactBarTime.Zero, Bpm = 150 });
                _document.Execute(new AddChartCommand(chart));
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

    /// <summary>SAVE writes the open document; SAVE AS and the first save of an in-memory draft
    /// ask for a destination directory first.</summary>
    private void RequestSave(bool saveAs)
    {
        if (_document is null) { ReportSave("没有可保存的文档。", true); return; }
        if (!saveAs && !_document.IsUnpersisted)
        {
            SaveToDirectory(_document.PackageDirectory!);
            return;
        }
        if (_document.PackageDirectory is not null) _saveDialog.CurrentDir = _document.PackageDirectory.Replace('\\', '/');
        _saveDialog.PopupCenteredRatio(.82f);
    }

    private void SaveToDirectory(string destinationDirectory)
    {
        if (_document is null) { ReportSave("没有可保存的文档。", true); return; }
        try
        {
            string action;
            string directory;
            if (_document.IsUnpersisted)
            {
                if (_draft is null) throw new InvalidOperationException("内存草稿缺少外部资源，无法创建包。");
                var created = V2PackageWriter.Create(
                    _draft.BuildPackageCreateRequest(destinationDirectory, allowEmptyDestination: true));
                _draft = null; _memoryDraft = false;
                action = "CREATED"; directory = created.DestinationDirectory;
            }
            else
            {
                var written = V2PackageWriter.Write(new V2PackageWriteRequest
                {
                    SourceDirectory = _document.PackageDirectory!,
                    DestinationDirectory = destinationDirectory,
                    Pack = _document.BuildPackSnapshot(),
                    Charts = _document.BuildAllChartSnapshots(),
                    ExternalResources = _document.PendingResourceReplacements,
                    AllowEmptyDestination = true,
                });
                action = "SAVED"; directory = written.DestinationDirectory;
            }
            _document.MarkSaved(directory);
            InvalidateDigestAudioHash();
            // 状态行是 Pack 页唯一的回报位，刷新会重置它，所以先刷新页面再写回报。
            if (_flowPage == EditorFlowPage.Pack) RefreshPackPage();
            ReportSave($"{action} · {_document.Charts.Count} CHARTS · {directory}", false);
        }
        catch (Exception exception)
        {
            // Nothing was published: the writer stages and rolls back, so the draft stays dirty.
            ReportSave($"保存失败：{exception.Message}", true);
        }
    }

    /// <summary>保存结果的回报位：Edit 主态是 HUD 的保存状态行（<see cref="RefreshSaveState"/> 会把
    /// <see cref="_saveError"/> 显示出来），其余页面复用各自的消息通道；shell 不再有常驻调试文字行。</summary>
    private void ReportSave(string message, bool error)
    {
        _saveError = error ? message : null;
        if (_flowPage != EditorFlowPage.Edit) SetFlowMessage(message, error);
        RefreshSaveState();
    }

    private void RefreshSaveState()
    {
        if (_saveState is null) return;
        string text;
        var color = FlowMuted;
        if (_document is null) text = "";
        else if (_saveError is not null) { text = _saveError; color = new Color("ff879f"); }
        else if (_document.IsUnpersisted) text = "MEMORY DRAFT · UNSAVED";
        else text = (_document.HasUnsavedChanges ? "UNSAVED CHANGES · " : "SAVED · ") + _document.PackageDirectory;
        if (_saveState.Text == text) return;
        _saveState.Text = text;
        _saveState.AddThemeColorOverride("font_color", color);
    }

    /// <summary>Create 页没有独立的消息 Label：回报与前置校验共用设计稿的中缝状态行。</summary>
    private void SetFlowMessage(string message, bool error)
    {
        if (_flowPage == EditorFlowPage.Create)
        {
            _createOverlay.Report(message, error);
            return;
        }
        var label = _flowPage == EditorFlowPage.Pack ? _packMessage : _startMessage;
        if (label is null) return;
        label.Text = message; label.AddThemeColorOverride("font_color", error ? new Color("ff879f") : FlowMuted);
    }

    private static string DifficultyName(EditableChart chart) => chart.Difficulty == V2Difficulty.Custom
        ? chart.DifficultyKey ?? "CUSTOM"
        : chart.Difficulty.ToString().ToUpperInvariant();

    private static string LevelTagText(EditableChart chart) => chart.Unrated || chart.Level is null
        ? "· LV --"
        : $"· LV {chart.Level.Value.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>AUTO 是视觉预览的运行态。CheckButton 被移除后，开关入口是右键菜单的播放行、
    /// `Space` 与 `F1`（F1 不再顺带改动工具态）；三处都走这里，HUD 的 GRID/SNAP 行随之刷新。</summary>
    private void SetAuto(bool enabled)
    {
        _auto = enabled;
        _playing = enabled;
    }

    /// <summary>四角 HUD 的实时数据。所有数值都取运行时状态，设计稿上的 1/32、0.10、02:00.000
    /// 只是展示值：网格分母来自文档，吸附来自画布，BPM/Bar 来自当前差分的时间线。</summary>
    private void RefreshEditHud()
    {
        if (_hudGrid is null || _canvas is null) return;
        var grid = $"GRID 1/{_canvas.GridDivisor} · SNAP {SnapStepText()}";
        if (_auto) grid += " · AUTO";
        SetLabelText(_hudGrid, grid);
        // 进入路径节点编辑态（单选 Hold/Mixer，或双击插入的宽度拖拽）出标签，退出即消失。
        _hudPathEdit.Visible = _canvas.PathEditActive;

        if (_document is null)
        {
            SetLabelText(_hudTime, CursorTimeText(_time));
            SetLabelText(_hudBarBpm, string.Empty);
            return;
        }

        var chart = _document.SelectedChart;
        var tagChanged = SetLabelText(_hudTitle, _document.Title);
        tagChanged |= SetLabelText(_hudDifficulty, DifficultyName(chart));
        tagChanged |= SetLabelText(_hudLevel, LevelTagText(chart));
        if (tagChanged) _hudDifficulty.AddThemeColorOverride("font_color", DifficultyColor(chart.Difficulty));

        SetLabelText(_hudTime, CursorTimeText(_time));
        SetLabelText(_hudBarBpm, $"BAR {(long)Math.Floor(Math.Max(0d, _canvas.CursorBar))} · {_canvas.CursorBpm:0.0} BPM");
    }

    /// <summary>吸附被 Z（时间吸附）临时关闭时显示 FREE；X 把空间吸附降到细步长，显示细步长。
    /// 常规步长取设置真源（Settings / EDITOR / SNAP），细步长仍是编辑器常量 0.01。</summary>
    private string SnapStepText() => !_canvas.SnapEnabled || !_canvas.TimeSnapOverride
        ? "FREE"
        : (_canvas.SpaceSnapOverride ? EditorSettings.Current.SnapStep : EditorGeometry.FineCenterSnap)
            .ToString("0.00", CultureInfo.InvariantCulture);

    private static string CursorTimeText(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static bool SetLabelText(Label label, string text)
    {
        if (label.Text == text) return false;
        label.Text = text;
        return true;
    }

    private static Color DifficultyColor(V2Difficulty difficulty) => difficulty switch
    {
        V2Difficulty.Casual => UiFonts.Casual,
        V2Difficulty.Normal => UiFonts.Normal,
        V2Difficulty.Hard => UiFonts.Hard,
        V2Difficulty.Mega => UiFonts.Mega,
        V2Difficulty.Giga => UiFonts.Giga,
        V2Difficulty.Tech => EditorOverlayDraw.Tech,
        _ => UiFonts.Dim,
    };

    /// <summary>Start 板的背景装饰层（设计稿 START 段的轨道系统）：两个椭圆描边、轨道上
    /// 六个 18×18 切角方块、中心 70×70 切角方块与一条 640×2 横线。静态帧，不带动画。</summary>
    private sealed partial class StartBackdrop : Control
    {
        private const float CenterX = 960f, CenterY = 555f;
        private const float NodeSize = 18f, NodeCut = 5f, CoreSize = 70f, CoreCut = 18f;
        /// <summary>轨道方块：极坐标 (角度, rx, ry)，取设计稿同一组参数。</summary>
        private static readonly (float Angle, float Rx, float Ry)[] OrbitNodes =
        [
            (25f, 280f, 95f), (140f, 280f, 95f), (75f, 210f, 150f),
            (250f, 210f, 150f), (200f, 280f, 95f), (320f, 210f, 150f),
        ];

        public override void _Draw()
        {
            DrawPolyline(UiGeometry.Close(Orbit(280f, 95f)), new Color(FlowCyan, .35f), 2f, true);
            DrawPolyline(UiGeometry.Close(Orbit(210f, 150f)), new Color(FlowCyan, .22f), 2f, true);
            foreach (var node in OrbitNodes)
            {
                var angle = Mathf.DegToRad(node.Angle);
                var x = Mathf.Round(CenterX + node.Rx * Mathf.Cos(angle) - NodeSize * .5f);
                var y = Mathf.Round(CenterY + node.Ry * Mathf.Sin(angle) - NodeSize * .5f);
                DrawColoredPolygon(UiGeometry.CutCorners(x, y, NodeSize, NodeSize, NodeCut), FlowCyan);
            }
            DrawColoredPolygon(
                UiGeometry.CutCorners(CenterX - CoreSize * .5f, CenterY - CoreSize * .5f, CoreSize, CoreSize, CoreCut), FlowCyan);
            DrawRect(new Rect2(640f, 470f, 640f, 2f), new Color(FlowCyan, .6f), true);
        }

        /// <summary>椭圆按 96 段采点：Godot 只有圆弧，设计稿两个轨道都是椭圆描边。</summary>
        private static Vector2[] Orbit(float rx, float ry)
        {
            const int steps = 96;
            var points = new Vector2[steps];
            for (var i = 0; i < steps; i++)
            {
                var angle = i * Mathf.Tau / steps;
                points[i] = new Vector2(CenterX + rx * Mathf.Cos(angle), CenterY + ry * Mathf.Sin(angle));
            }
            return points;
        }
    }

    /// <summary>Start 板的切角卡与动作键：Button 只承载点击与 hover，形状和 44px 图标自绘
    /// （StyleBox 表达不了「只切左上 + 右下」），26px 标题按设计稿字距另挂 Label。</summary>
    private sealed partial class StartCard : Button
    {
        private static readonly Vector2 GlyphAt = new(34f, 53f);
        // hover 统一过渡：卡面提亮（+8%）按 0..1 值 lerp；hover 变化才开 _Process，到终值自关。
        private HoverFade _hoverFade;

        public float Cut = 28f;
        public Color Fill = Colors.Transparent;
        public Color Border = Colors.Transparent;
        public float BorderWidth = 2f;
        public Color GlyphColor = Colors.White;
        public StartIcon Glyph = StartIcon.None;

        public override void _Ready()
        {
            var empty = new StyleBoxEmpty();
            foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" }) AddThemeStyleboxOverride(state, empty);
            MouseEntered += () => { SetProcess(true); };
            MouseExited += () => { SetProcess(true); };
            SetProcess(false);
        }

        public override void _Process(double delta)
        {
            // hover 统一过渡：MOTION OFF 时 Update 直接跳终值，SetProcess(false) 也在同帧生效。
            QueueRedraw();
            if (_hoverFade.Update(IsHovered(), delta)) return;
            SetProcess(false);
        }

        public override void _Draw()
        {
            EditorOverlayDraw.CutPanel(this, new Rect2(Vector2.Zero, Size), Cut,
                Fill.Lerp(Fill.Lightened(.08f), _hoverFade.Value), Border, BorderWidth);
            if (Glyph == StartIcon.Plus)
            {
                // 设计稿 ICON_PLUS_BIG：44 框里的两条 3.5px 实心笔。
                DrawLine(GlyphAt + new Vector2(22f, 8f), GlyphAt + new Vector2(22f, 36f), GlyphColor, 3.5f, true);
                DrawLine(GlyphAt + new Vector2(8f, 22f), GlyphAt + new Vector2(36f, 22f), GlyphColor, 3.5f, true);
            }
            else if (Glyph == StartIcon.Folder)
            {
                // 设计稿 ICON_FOLDER：M4 8h11l3 4h22v24H4z 的 2px 描边。
                DrawPolyline(UiGeometry.Close(new[]
                {
                    GlyphAt + new Vector2(4f, 8f), GlyphAt + new Vector2(15f, 8f), GlyphAt + new Vector2(18f, 12f),
                    GlyphAt + new Vector2(40f, 12f), GlyphAt + new Vector2(40f, 36f), GlyphAt + new Vector2(4f, 36f),
                }), GlyphColor, 2f, true);
            }
        }
    }

    private enum StartIcon { None, Folder, Plus }
}

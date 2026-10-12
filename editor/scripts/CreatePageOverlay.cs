using Godot;
using System;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>Create Project 板（设计稿 Create Project；施工值见 `.tmp/penpot_build4.py` 的
/// `create_board()`，等级输入见同文件的 Widget — Level States `lvl_row/lvl_blocks`）的自绘层：
/// 面板切角、字段框、难度 chips、等级十进制块编码、封面区与底部操作条都不是控件树能表达的形状，
/// 所以整页自绘，坐标与设计稿 1:1（切角只切左上 + 右下，见 `docs/editor-ui-design.md` §3）。
///
/// 文本输入仍是真实控件：字段框里放 LineEdit 子节点，BPM/offset 与等级数字也是 LineEdit，
/// ‹ › 步进、难度 chips、UNRATED 框与封面区是本层自绘的命中矩形。本层只管几何、状态与表单读取，
/// **不复制创建语义**：最终校验与草稿构建仍在 shell 的 `ConfirmCreate` 与 core 的 draft factory，
/// 这里只做前置检查（<see cref="Validate"/>）把已知非法值挡在 CREATE 之前。
///
/// 右列支持「折叠重排」：难度非 CUSTOM 时 DIFF KEY 收起，LEVEL 行与 ChartPanel 之下的右列
/// 统一上移 64px（0.18s CubicOut），由单值 <see cref="_collapseT"/> 驱动（展开态 t=0 坐标与
/// 设计稿常量逐值一致）；进入页面直接落终值不播动画，页内点 chips 才过渡。</summary>
internal sealed partial class CreatePageOverlay : Control
{
    /// <summary>CUSTOM chip 的下标（`V2Difficulty` 的 casual..tech 之后一枚）。</summary>
    public const int CustomIndex = 6;

    private const float DisabledAlpha = .55f, HoverLift = .12f, DashedDash = 6f;

    /// <summary>表单快照：读壳读回的全部用户输入。unrated 时 <see cref="Level"/> 为 null。</summary>
    public sealed record Form(
        string PackId, string Title, string Artist, string AudioPath,
        string ChartId, int DifficultyIndex, string CustomKey, int? Level, bool Unrated,
        string Charter, double Bpm, double OffsetSec, string? CoverPath);

    /// <summary>页面上下文：模式与只读数据。AddDifficulty 下包元数据/音频/封面属于已打开的包，
    /// 只作只读展示；TIMING 面板隐藏（新差分的时间线参数本轮仍走既有默认值，见 `ConfirmCreate`）。</summary>
    public sealed class Snapshot
    {
        public bool AddDifficulty { get; init; }
        public string PackId { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string AudioPath { get; init; } = string.Empty;
        /// <summary>已打开包记录的谱师（charts[].charters 去重），只读展示。</summary>
        public string PackCharter { get; init; } = string.Empty;
        /// <summary>已打开包的封面引用（包内相对路径），只读展示。</summary>
        public string CoverName { get; init; } = string.Empty;
        public string DraftLabel { get; init; } = string.Empty;
        public string DraftDetail { get; init; } = string.Empty;
        /// <summary>草稿状态方块/标题用 hard 色（尚未落盘）还是 cyan（已并入磁盘上的包）。</summary>
        public bool DraftWarn { get; init; } = true;
    }

    public event Action? BrowseAudioRequested;
    public event Action? PickCoverRequested;
    public event Action? Confirmed;
    public event Action? Cancelled;

    /// <summary>前置校验：返回可读原因表示当前表单不能创建。由 shell 提供——包级规则（chart ID 冲突、
    /// 难度 key 包内唯一…）只有它有上下文。</summary>
    public Func<Form, string?>? Validate { get; set; }

    // ---------------------------------------------------------------- 设计稿几何

    private const float BarHeight = 76f, BarLineHeight = 2f;
    private const float BarWordX = 32f, BarCenterY = 38f, BarPageX = 330f;

    private const float PanelCut = 16f, HeaderX = 56f, ChartHeaderX = 1008f;
    private const float HeaderCenterYOffset = 26f;
    private static readonly Rect2 AudioPanel = new(32f, 108f, 920f, 172f);
    private static readonly Rect2 MetaPanel = new(32f, 304f, 920f, 240f);
    private static readonly Rect2 TimingPanel = new(32f, 568f, 920f, 170f);
    private static readonly Rect2 ChartPanel = new(984f, 108f, 904f, 340f);
    private static readonly Rect2 CoverPanel = new(984f, 472f, 904f, 160f);
    private static readonly Rect2 DraftPanel = new(984f, 656f, 904f, 54f);

    private static readonly Rect2 AudioPlay = new(56f, 154f, 40f, 40f);
    private static readonly Vector2 AudioPlayGlyph = new(66f, 164f);
    private static readonly Rect2 AudioPathFrame = new(56f, 214f, 700f, 44f);
    private static readonly Rect2 AudioBrowse = new(770f, 214f, 130f, 44f);
    private const float AudioStatusRight = 920f, AudioStatusCenterY = 136f;
    private const float AudioFieldCut = 8f, AudioStatusMaxWidth = 420f;

    private const float FieldCut = 8f, FieldValueInset = 14f;
    /// <summary>设计稿字号（不预先换算）：字号的换算与重放统一走 <see cref="EditorFonts.ApplySize"/>，
    /// 所以这里的常量保持设计稿原值，UI SCALE 换档时注册表才能重放出正确倍数。</summary>
    private const int FieldLabelSize = 11, FieldValueSize = 16;
    private const float FieldLabelOffsetY = -14.5f;
    private static readonly Rect2 TitleFrame = new(56f, 372f, 420f, 48f);
    private static readonly Rect2 ArtistFrame = new(500f, 372f, 420f, 48f);
    private static readonly Rect2 PackIdFrame = new(56f, 462f, 420f, 48f);
    private static readonly Rect2 PackCharterFrame = new(500f, 462f, 420f, 48f);
    private static readonly Rect2 ChartIdFrame = new(1008f, 176f, 420f, 48f);
    private static readonly Rect2 ChartCharterFrame = new(1452f, 176f, 400f, 48f);
    private static readonly Rect2 DiffKeyFrame = new(1008f, 320f, 300f, 48f);
    private const float DiffKeyHintX = 1324f, DiffKeyHintCenterY = 344f;

    private static readonly Rect2 BpmFrame = new(55f, 616f, 164f, 50f);
    private static readonly Rect2 OffsetFrame = new(699f, 616f, 172f, 50f);
    private const float BpmLabelX = 60f, OffsetLabelX = 704f, TimingLabelCenterY = 673.5f;
    private const float TicksX = 220f, TicksY = 632f, TicksWidth = 420f, TicksHeight = 34f, TickCount = 32;

    private const float ChipX = 1008f, ChipY = 240f, ChipWidth = 92f, ChipHeight = 34f;
    private const float ChipCut = 6f, ChipPitch = 102f;

    private static readonly Vector2 LevelOrigin = new(1008f, 384f);
    private static readonly Vector2 LevelSize = new(886f, 54f);

    private static readonly Rect2 CoverZone = new(1008f, 518f, 856f, 90f);
    private const float CoverZoneCut = 12f, CoverIconX = 1360f, CoverIconY = 546f;
    private const float CoverTextX = 1410f, CoverTextCenterY = 563f, CoverTextMaxWidth = 400f;
    private static readonly Rect2 CoverClear = new(1824f, 530f, 24f, 24f);

    private static readonly Vector2 DraftSquare = new(1008f, 678f);
    // 设计稿行心 y=683（方块 678 + 10/2），两段文本共用这一条中心线。
    private const float DraftLabelX = 1030f, DraftDetailX = 1200f, DraftCenterY = 683f;

    private const float BottomY = 972f, BottomHeight = 52f, BottomCut = 10f, BottomCenterY = 998f;
    private static readonly Rect2 CancelRect = new(32f, BottomY, 180f, 52f);
    private static readonly Rect2 CreateRect = new(1588f, BottomY, 300f, 52f);
    private const float CancelCenterX = 122f, CreateCenterX = 1738f, StatusCenterX = 960f;
    private const float StatusMaxWidth = 1200f;

    // 设计稿字距（ls1..ls4）在 Godot 侧用 FontVariation 表达，与 Start/Pack 板同一做法。
    private static readonly Font Tech1 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 1 };
    private static readonly Font Tech2 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };
    private static readonly Font Tech3 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 3 };
    private static readonly Font Bold1 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 1 };
    private static readonly Font Bold2 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 2 };
    private static readonly Font Bold4 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 4 };

    private static readonly string[] DifficultyLabels =
        ["CASUAL", "NORMAL", "HARD", "MEGA", "GIGA", "TECH", "CUSTOM"];

    // ---------------------------------------------------------------- 状态

    private enum Hotspot { None, Browse, Chip, Cover, CoverClear, Cancel, Create }

    private readonly LineEdit _audioPath;
    private readonly LineEdit _title, _artist, _packId, _packCharter;
    private readonly LineEdit _chartId, _chartCharter, _diffKey;
    private readonly NumericField _bpm, _offset;
    private readonly LevelField _level;
    private Snapshot _snapshot = new();
    private Hotspot _hover = Hotspot.None;
    // hover 统一过渡的当前值表：每个「热点+下标」一枚 0..1 值，绘制处按值 lerp 颜色；
    // 全部到终值后 _Process 自关（见 _Process 尾部），页面空闲不逐帧重绘。
    private readonly HoverFadeMap _hoverFade = new();
    private int _difficulty = 2;
    private int _hoverChip = -1;
    private string? _coverPath;
    private string? _report;
    private bool _reportError;
    private string _statusText = "READY TO CREATE";
    private bool _statusError;
    private bool _createEnabled;
    // 焦点 glow 的当前值表（0..1）：底板是 _Draw 自绘的，没有独立控件可挂样式，只能按
    // 每个输入框的 glow 值在重绘时插值描边色；_Process 里逐帧向目标（HasFocus ? 1 : 0）靠拢。
    private readonly Dictionary<Control, float> _fieldGlow = new();
    /// <summary>焦点 glow 的过渡时长（约 0.12s），与 Start 板路径行的 Tween 同一档。</summary>
    private const float GlowSeconds = .12f;
    // hover 统一过渡（docs/editor-ui-design.md §3）：瞬时换色改为 lerp，时长取共享档位
    // HoverMotion.Seconds；颜色插值在绘制处按值做，不复制第二套动效参数。
    private const float HoverSeconds = HoverMotion.Seconds;

    /// <summary>折叠量（0=展开 1=折叠）与它的动画参数：难度非 CUSTOM 时 DIFF KEY 行收起，
    /// LEVEL 行与其下方的右列内容整体上移 64px，靠这一个量统一驱动（布局取值见
    /// <see cref="Shift"/>/<see cref="ChartPanelRect"/>），保证展开态 t=0 与设计稿常量逐值一致、
    /// 不会复制出第二套坐标。过渡时长 0.18s、CubicOut，与既有动效档位一致。
    /// 进入页面（Apply/PrefillChart）直接落终值不播动画——页面本身有整页淡入，再叠重排会打架。</summary>
    private const float CollapseSeconds = .18f, CollapseShift = -64f;
    private float _collapseT = 1f, _collapseFrom, _collapseTo, _collapseElapsed;
    private bool _collapseAnimating;

    public CreatePageOverlay()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // 没有焦点活动时不空转：glow 全部在终值，_Process 关着，焦点变化才开。
        SetProcess(false);

        _audioPath = NewField(FieldRect(AudioPathFrame), "EXTERNAL AUDIO FILE", 14, "AudioPath");
        _title = NewField(FieldRect(TitleFrame), "TITLE", FieldValueSize, "Title");
        _artist = NewField(FieldRect(ArtistFrame), "ARTIST", FieldValueSize, "Artist");
        _packId = NewField(FieldRect(PackIdFrame), "PACK ID", FieldValueSize, "PackId");
        // v2 的谱师是 chart 级（meta.json 的 charts[].charters），没有包级字段：新建时 METADATA 的
        // CHARTER 与 FIRST CHART 的 CHARTER 是同一个值，两边都可编辑、互为镜像（只读展示会把
        // 「看起来能输入却打不了字」变成 bug 体验）；加差分时包级框只读展示已打开包记录的谱师
        // （Apply 处理），差分级框可编辑。
        _packCharter = NewField(FieldRect(PackCharterFrame), "CHARTER", FieldValueSize, "PackCharter");
        _chartCharter = NewField(FieldRect(ChartCharterFrame), "CHARTER", FieldValueSize, "Charter");
        _packCharter.TextChanged += _ => MirrorCharter(_packCharter, _chartCharter);
        _chartCharter.TextChanged += _ => MirrorCharter(_chartCharter, _packCharter);
        _chartId = NewField(FieldRect(ChartIdFrame), "CHART ID", FieldValueSize, "ChartId");
        _diffKey = NewField(FieldRect(DiffKeyFrame), "DIFF KEY", FieldValueSize, "DiffKey");

        _bpm = new NumericField(NumericField.Kind.Bpm, BpmFrame);
        _bpm.Committed += OnFormChanged;
        AddChild(_bpm);
        _offset = new NumericField(NumericField.Kind.Offset, OffsetFrame);
        _offset.Committed += OnFormChanged;
        AddChild(_offset);

        _level = new LevelField { Position = LevelOrigin, Size = LevelSize };
        _level.ValueChanged += OnFormChanged;
        AddChild(_level);
        _level.DifficultyColor = ChipColor(_difficulty);
        // 初始布局按默认难度（HARD=折叠）直接落终值；首次显示前 shell 总会先 Apply 定难度的终态。
        _collapseTo = CollapseTarget;
        SnapCollapse();
    }

    /// <summary>字段框 → LineEdit 矩形：设计稿的值是「框内左缩 14、垂直居中」，LineEdit 自带文本
    /// 内边距，所以这里再收 2px，让字形落在同一处。</summary>
    private static Rect2 FieldRect(Rect2 frame) => new(
        frame.Position.X + FieldValueInset - 2f, frame.Position.Y + 2f,
        frame.Size.X - (FieldValueInset - 2f) * 2f, frame.Size.Y - 4f);

    // ---------------------------------------------------------------- 折叠布局

    /// <summary>当前折叠位移（px，折叠态为 -64）：所有随 DIFF KEY 收起而上移的元素统一取这一个
    /// 值，绘制坐标、子控件 Position 与命中矩形共用，展开态 t=0 时恒为 0（设计稿 1:1）。</summary>
    private float Shift => CollapseShift * _collapseT;

    /// <summary>FIRST CHART 面板随折叠收缩（高度 340→276），底边贴着上移后的 LEVEL 行。</summary>
    private Rect2 ChartPanelRect => new(ChartPanel.Position, new Vector2(ChartPanel.Size.X, ChartPanel.Size.Y + Shift));

    /// <summary>CoverPanel/DraftPanel 原样上移，尺寸不变。</summary>
    private Vector2 CoverPanelPos => CoverPanel.Position + new Vector2(0f, Shift);
    private Vector2 DraftPanelPos => DraftPanel.Position + new Vector2(0f, Shift);
    private Rect2 CoverZoneRect => new(CoverZone.Position + new Vector2(0f, Shift), CoverZone.Size);
    private Rect2 CoverClearRect => new(CoverClear.Position + new Vector2(0f, Shift), CoverClear.Size);
    private Vector2 DraftSquarePos => DraftSquare + new Vector2(0f, Shift);

    /// <summary>把折叠布局落到位：控件 Position/交互态按 t 设终值并重绘。动画中也用它逐帧推进，
    /// 因为命中矩形与 LineEdit 位置必须跟当前动画值走（过渡中可交互元素仍然点得到）。</summary>
    private void ApplyCollapseLayout(float t)
    {
        _collapseT = t;
        var shift = Shift;
        _level.Position = LevelOrigin + new Vector2(0f, shift);
        _diffKey.Position = FieldRect(DiffKeyFrame).Position + new Vector2(0f, shift);
        // DIFF KEY 随过渡淡出/淡入（折叠 t=1 全透明，展开 t=0 不透明）。
        _diffKey.Modulate = new Color(1f, 1f, 1f, 1f - t);
        // 折叠终值的 DIFF KEY 完全不可交互；过渡中保持可交互，让淡出期间点得到、焦点能自然移出。
        var collapsed = _collapseTo > 0f && t >= 1f;
        _diffKey.MouseFilter = collapsed ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        _diffKey.FocusMode = collapsed ? FocusModeEnum.None : FocusModeEnum.All;
        // 收起终点若焦点还留在框里，先还给页面，避免隐藏字段持有焦点。
        if (collapsed && _diffKey.HasFocus()) ReleaseFocus();
        QueueRedraw();
    }

    /// <summary>目标折叠量：CUSTOM 展开、其余折叠；MOTION OFF 时一切直接跳终值不播动画。</summary>
    private float CollapseTarget => _difficulty == CustomIndex ? 0f : 1f;

    /// <summary>难度切换后的布局过渡入口。进入页面走 <see cref="SnapCollapse"/>（不动画），
    /// 页内点 chips 走这里播 0.18s CubicOut。</summary>
    private void AnimateCollapse()
    {
        var target = CollapseTarget;
        if (EditorSettings.Current.MotionMode == UiMotionMode.Off || target == _collapseT)
        {
            _collapseAnimating = false;
            _collapseTo = target;
            SnapCollapse();
            return;
        }
        // 展开方向先把 DIFF KEY 立即可见（透明度由 ApplyCollapseLayout 按 t 淡入）；
        // 收起方向保持可见，淡出到终点后由 RefreshDiffKey 隐藏。
        if (target == 0f) _diffKey.Visible = true;
        _collapseFrom = _collapseT;
        _collapseTo = target;
        _collapseElapsed = 0f;
        _collapseAnimating = true;
        SetProcess(true);
    }

    /// <summary>不做过渡、直接落终值：进入页面与 MOTION OFF 都走这里。不关 _Process——
    /// 若焦点 glow 还在过渡，让它自己结算后按既有逻辑关闭。</summary>
    private void SnapCollapse()
    {
        _collapseAnimating = false;
        _collapseT = _collapseTo;
        ApplyCollapseLayout(_collapseT);
        RefreshDiffKey();
    }

    /// <summary><paramref name="designSize"/> 是设计稿字号：theme override 与注册表都交给
    /// <see cref="EditorFonts.ApplySize"/>，UI SCALE 换档时这一批 LineEdit 一起换字号。</summary>
    private LineEdit NewField(Rect2 rect, string placeholder, int designSize, string name)
    {
        var edit = new LineEdit
        {
            Position = rect.Position, Size = rect.Size, Name = name, PlaceholderText = placeholder,
        };
        var empty = new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "focus", "read_only" }) edit.AddThemeStyleboxOverride(state, empty);
        edit.AddThemeFontOverride("font", EditorFonts.Medium);
        EditorFonts.ApplySize(edit, designSize);
        edit.AddThemeColorOverride("font_color", UiFonts.Text);
        edit.AddThemeColorOverride("font_uneditable_color", UiFonts.Dim);
        edit.AddThemeColorOverride("font_placeholder_color", new Color(UiFonts.Dim, .7f));
        edit.AddThemeColorOverride("caret_color", UiFonts.Cyan);
        edit.CaretBlink = true;
        edit.TextChanged += _ => OnFormChanged();
        // 焦点反馈画在自绘底板上（stylebox 全空，LineEdit 自身没有视觉态），注册进 glow 表。
        _fieldGlow[edit] = 0f;
        edit.FocusEntered += () => { _fieldGlow[edit] = EditorSettings.Current.MotionMode == UiMotionMode.Off ? 1f : _fieldGlow[edit]; WakeGlow(); };
        edit.FocusExited += () => WakeGlow();
        AddChild(edit);
        return edit;
    }

    /// <summary>焦点变化后开 _Process：每帧把各 glow 值 lerp 向目标（HasFocus ? 1 : 0）并重绘，
    /// 全部到终值后关掉，避免页面空闲时也每帧重绘。MOTION OFF 的字段在 FocusEntered 里已跳 1，
    /// 失焦走普通 lerp 也会立即结算，行为一致。</summary>
    private void WakeGlow() => SetProcess(true);

    public override void _Process(double delta)
    {
        var settled = true;
        if (_collapseAnimating)
        {
            // 折叠过渡：CubicOut（1-(1-t)^3），0.18s；布局与淡出同值推进，终点顺带结算交互态。
            _collapseElapsed += (float)delta;
            var linear = Mathf.Min(_collapseElapsed / CollapseSeconds, 1f);
            var t = Mathf.Lerp(_collapseFrom, _collapseTo, 1f - Mathf.Pow(1f - linear, 3f));
            ApplyCollapseLayout(t);
            if (linear >= 1f)
            {
                _collapseAnimating = false;
                RefreshDiffKey();
            }
            else settled = false;
        }
        var step = (float)delta / GlowSeconds;
        foreach (var field in _fieldGlow.Keys)
        {
            var target = field.HasFocus() ? 1f : 0f;
            var value = Mathf.MoveToward(_fieldGlow[field], target, step);
            _fieldGlow[field] = value;
            if (value != target) settled = false;
        }
        // hover 统一过渡：hotspot 用「类型 + 下标」编码成一个键，hover 离开（None 传 -1）时
        // 所有键向 0 回落，余晖渐出而不是硬切。MOTION OFF 时 Update 内部直接跳终值。
        if (_hoverFade.Update(HoverKey(_hover, _hoverChip), delta)) settled = false;
        QueueRedraw();
        if (settled) SetProcess(false);
    }

    /// <summary>hover 热点 → 过渡值表的键（0..1 各一枚）。None 编码为 -1（表里不存在）。</summary>
    private static long HoverKey(Hotspot hotspot, int chipIndex) => hotspot switch
    {
        Hotspot.Chip => chipIndex, // chips 在键空间最前，下标直接当键
        Hotspot.Browse => 100,
        Hotspot.Cover => 101,
        Hotspot.CoverClear => 102,
        Hotspot.Cancel => 103,
        Hotspot.Create => 104,
        _ => -1,
    };

    /// <summary>绘制处取某热点的 hover 过渡值（0..1）：绘制色 = 常态色.Lerp(hover 色, 值)。</summary>
    private float HoverT(Hotspot hotspot, int chipIndex = 0) => _hoverFade[HoverKey(hotspot, chipIndex)];

    // ---------------------------------------------------------------- 对外接口

    /// <summary>进入页面时套用上下文：模式、只读的包数据、草稿状态行。上一次会话的回报与封面
    /// 在这里清掉，避免把旧页面的错误带到新的表单上。</summary>
    public void Apply(Snapshot snapshot)
    {
        _snapshot = snapshot;
        _report = null;
        SetText(_packId, snapshot.PackId);
        SetText(_title, snapshot.Title);
        SetText(_artist, snapshot.Artist);
        SetText(_audioPath, snapshot.AudioPath);
        SetText(_packCharter, snapshot.AddDifficulty ? snapshot.PackCharter : _chartCharter.Text);
        _coverPath = null;
        var readOnly = snapshot.AddDifficulty;
        foreach (var edit in new[] { _packId, _title, _artist }) SetReadOnly(edit, readOnly);
        // 包级 CHARTER：加差分时只读展示包内谱师；新建时与差分级框互为镜像、可编辑。
        SetReadOnly(_packCharter, readOnly);
        // 设计稿的路径行是 14px dim（次要信息），所以它的可编辑态也不跟 METADATA 的亮色值同色。
        SetReadOnly(_audioPath, readOnly, UiFonts.Dim);
        _bpm.Visible = !readOnly;
        _offset.Visible = !readOnly;
        // 进入页面不播重排动画（页面本身有整页淡入）：难度是进入前定好的，直接落终值。
        _collapseTo = CollapseTarget;
        SnapCollapse();
        RefreshStatus();
    }

    /// <summary>差分级预填：Create 页里属于新差分的那几个字段。</summary>
    public void PrefillChart(string chartId, int difficultyIndex, string charter, int level, bool unrated)
    {
        _difficulty = Math.Clamp(difficultyIndex, 0, DifficultyLabels.Length - 1);
        SetText(_chartId, chartId);
        SetText(_chartCharter, charter);
        SetText(_diffKey, string.Empty);
        _level.SetValue(level, unrated);
        _level.DifficultyColor = ChipColor(_difficulty);
        // 与 Apply 同理：预填的难度直接落终值，不做重排动画。
        _collapseTo = CollapseTarget;
        SnapCollapse();
        RefreshStatus();
    }

    /// <summary>新建包时写进草稿的初始 BPM 与音频偏移（draft factory 的 initialBpm / audioOffsetSec）。</summary>
    public void PrefillTiming(double bpm, double offsetSeconds)
    {
        _bpm.SetValue(bpm);
        _offset.SetValue(offsetSeconds);
    }

    public void SetCoverPath(string? path)
    {
        _coverPath = string.IsNullOrWhiteSpace(path) ? null : path;
        RefreshStatus();
    }

    /// <summary>BROWSE 选完音频后回填路径行（值、状态行与中缝校验一起刷新）。</summary>
    public void SetAudioPath(string? path)
    {
        SetText(_audioPath, path?.Trim() ?? string.Empty);
        OnFormChanged();
    }

    /// <summary>AUDIO 面板的 BROWSE 键矩形：拖放路由用它判定「落在音频区」（与路径行 AudioPathFrame
    /// 同一面板，暴露出来避免 shell 复制第二套坐标）。</summary>
    public Rect2 AudioBrowseRect => AudioBrowse;

    /// <summary>拖放命中与校验（shell 从窗口 FilesDropped 按当前页面路由到这里）。命中自己判：
    /// 音频区 = AudioPathFrame ∪ AudioBrowse，封面区 = CoverZoneRect（走现有动态矩形，折叠重排的
    /// Shift 偏移自动生效）。同一份文件多目标命中时音频区优先；多文件取第一个合法的，其余忽略；
    /// 不合法走既有 <see cref="Report"/> 中缝报错。AddDifficulty 下音频/封面只读，直接拒收。
    /// 返回 true 表示这次拖放被本页受理（含报错），shell 不再提示。</summary>
    public bool TryAcceptDrop(Vector2 viewPos, string[] files)
    {
        var audioZone = AudioPathFrame.HasPoint(viewPos) || AudioBrowse.HasPoint(viewPos);
        var coverZone = CoverZoneRect.HasPoint(viewPos);
        if (!audioZone && !coverZone) return false;
        if (_snapshot.AddDifficulty)
        {
            // 音频/封面属于已打开的包（只读展示）：拖放会误导用户以为换掉了包资源，拒收并说明。
            Report(audioZone ? "加差分模式下音频属于已打开的包，不能替换。" : "加差分模式下封面属于已打开的包，不能替换。", true);
            return true;
        }
        foreach (var file in files)
        {
            if (audioZone && EditorProjectDraftFactory.IsSupportedAudioPath(file))
            {
                SetAudioPath(file);
                // 与 BROWSE 同口径的落盘提示：writer 只吃 WAV，其他格式首次保存会被拒。
                Report(string.Equals(System.IO.Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase)
                    ? "已选择外部音频；创建后仍是内存草稿，直到第一次 SAVE 才落盘。"
                    : "当前落盘只支持 .wav（PCM/IEEE-float）音频；其他格式会在首次保存时被 writer 拒绝。", false);
                return true;
            }
            if (!coverZone) continue;
            var extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                SetCoverPath(file);
                return true;
            }
            // 非法封面不在这里报：多文件拖入要先把后面的合法文件看完，统一在循环后报。
        }
        // 没有一个文件过校验：按第一个文件名报原因，多文件拖入只报这一个（其余同类被忽略）。
        if (files.Length > 0)
        {
            var sample = files[0];
            Report(audioZone
                ? $"不支持的音频格式：{System.IO.Path.GetFileName(sample)}（需要 .wav/.mp3/.flac/.ogg/.opus/.m4a/.aac）。"
                : $"不支持的封面格式：{System.IO.Path.GetFileName(sample)}（只收 .png/.jpg/.jpeg）。", true);
        }
        return true;
    }

    public Form ReadForm() => new(
        _packId.Text.Trim(), _title.Text.Trim(), _artist.Text.Trim(), _audioPath.Text.Trim(),
        _chartId.Text.Trim(), _difficulty, _diffKey.Text.Trim(), _level.Level, _level.Unrated,
        _chartCharter.Text.Trim(), _bpm.Value, _offset.Value, _coverPath);

    /// <summary>shell 的回报（创建失败、选音频提示…）占据设计中缝，直到用户下一次改动表单。</summary>
    public void Report(string? message, bool error)
    {
        _report = message;
        _reportError = error;
        RefreshStatus();
    }

    private void OnFormChanged()
    {
        _report = null;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var error = Validate?.Invoke(ReadForm());
        _createEnabled = error is null;
        var reason = _report ?? error;
        _statusText = reason ?? "READY TO CREATE";
        _statusError = reason is not null;
        QueueRedraw();
    }

    /// <summary>DIFF KEY 的可见性由折叠过渡接管（页内切难度时不在这里切，收起淡出完毕才隐藏、
    /// 展开在过渡开始前就恢复可见）；进入页面/预填与动画终点直接落到当前难度的终态。</summary>
    private void RefreshDiffKey()
    {
        _diffKey.Visible = _difficulty == CustomIndex;
        _diffKey.Modulate = Colors.White;
        QueueRedraw();
    }

    private static Color ChipColor(int index) => index switch
    {
        0 => UiFonts.Casual,
        1 => UiFonts.Normal,
        2 => UiFonts.Hard,
        3 => UiFonts.Mega,
        4 => UiFonts.Giga,
        5 => EditorOverlayDraw.Tech,
        _ => UiFonts.Cyan,
    };

    private static void SetText(LineEdit edit, string text)
    {
        if (edit.Text == text) return;
        edit.Text = text;
    }

    /// <summary>新建模式下两个 CHARTER 框互为镜像：v2 只有 chart 级谱师，包级框与差分级框是
    /// 同一个值，在任一边输入都同步到另一边。SetText 等值即返回，同步不会来回触发。
    /// AddDifficulty 下包级框只读展示（不同步）。</summary>
    private void MirrorCharter(LineEdit from, LineEdit to)
    {
        if (_snapshot.AddDifficulty) return;
        SetText(to, from.Text);
    }

    /// <summary>只读展示：不吃焦点、不响应点击，值转 dim（视觉上仍是真数据）。
    /// <paramref name="editableColor"/> 是可编辑态的字色（路径行按设计稿用 dim）。</summary>
    private static void SetReadOnly(LineEdit edit, bool readOnly, Color? editableColor = null)
    {
        edit.Editable = !readOnly;
        edit.MouseFilter = readOnly ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        edit.FocusMode = readOnly ? FocusModeEnum.None : FocusModeEnum.All;
        edit.AddThemeColorOverride("font_color", readOnly ? UiFonts.Dim : editableColor ?? UiFonts.Text);
    }

    // ---------------------------------------------------------------- 输入

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            Highlight(HotspotAt(motion.Position, out var chip), chip);
            return;
        }
        if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left) return;
        switch (HotspotAt(button.Position, out var chipIndex))
        {
            case Hotspot.Browse: if (_snapshot.AddDifficulty) return; BrowseAudioRequested?.Invoke(); break;
            case Hotspot.Chip: SelectDifficulty(chipIndex); break;
            case Hotspot.Cover: if (_snapshot.AddDifficulty) return; PickCoverRequested?.Invoke(); break;
            case Hotspot.CoverClear: if (_snapshot.AddDifficulty) return; _coverPath = null; OnFormChanged(); break;
            case Hotspot.Cancel: Cancelled?.Invoke(); break;
            case Hotspot.Create: if (!_createEnabled) return; Confirmed?.Invoke(); break;
            default: return;
        }
        AcceptEvent();
    }

    private void SelectDifficulty(int index)
    {
        if (_difficulty == index) return;
        _difficulty = index;
        _level.DifficultyColor = ChipColor(index);
        // 页内切换难度才播折叠/展开动画；可见性交给过渡（非 CUSTOM 折叠到终点才隐藏）。
        AnimateCollapse();
        OnFormChanged();
    }

    private void Highlight(Hotspot hotspot, int chipIndex)
    {
        if (hotspot == _hover && chipIndex == _hoverChip) return;
        _hover = hotspot;
        _hoverChip = chipIndex;
        // hover 目标变了才开 _Process：过渡值推进 + 重绘，全部到终值后自关（不空转）。
        SetProcess(true);
        QueueRedraw();
    }

    private Hotspot HotspotAt(Vector2 point, out int chipIndex)
    {
        chipIndex = -1;
        if (ChartPanel.HasPoint(point))
            for (var i = 0; i < DifficultyLabels.Length; i++)
                if (ChipRect(i).HasPoint(point)) { chipIndex = i; return Hotspot.Chip; }
        if (AudioBrowse.HasPoint(point)) return Hotspot.Browse;
        // COVER 命中区随折叠动画一起上移：过渡中也要按当前动画值命中（点得着）。
        if (CoverZoneRect.HasPoint(point))
            return !_snapshot.AddDifficulty && _coverPath is not null && CoverClearRect.HasPoint(point)
                ? Hotspot.CoverClear
                : Hotspot.Cover;
        if (CancelRect.HasPoint(point)) return Hotspot.Cancel;
        if (CreateRect.HasPoint(point)) return Hotspot.Create;
        return Hotspot.None;
    }

    private static Rect2 ChipRect(int index) => new(ChipX + index * ChipPitch, ChipY, ChipWidth, ChipHeight);

    // ---------------------------------------------------------------- 绘制

    public override void _Draw()
    {
        DrawTopBar();
        DrawAudioPanel();
        DrawMetadataPanel();
        if (!_snapshot.AddDifficulty) DrawTimingPanel();
        DrawChartPanel();
        DrawCoverPanel();
        DrawDraftPanel();
        DrawBottomBar();
    }

    private void DrawTopBar()
    {
        DrawRect(new Rect2(0, 0, 1920f, BarHeight), new Color(UiFonts.Bg, .92f), true);
        DrawRect(new Rect2(0, BarHeight, 1920f, BarLineHeight), UiFonts.Line, true);
        EditorOverlayDraw.LeftText(this, Bold4, new Vector2(BarWordX, BarCenterY), "DYNAMAKER·UV", EditorFonts.Pt(24), UiFonts.Cyan);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(BarPageX, BarCenterY), "NEW PROJECT", EditorFonts.Pt(13), UiFonts.Dim);
    }

    /// <summary>AUDIO：真实音频校验态 + 55% 压暗的试听占位（本 shell 没有试听能力，所以不画波形与
    /// 进度条，也不伪造时长/采样率）+ 路径行 + BROWSE。</summary>
    private void DrawAudioPanel()
    {
        Panel(AudioPanel);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(HeaderX, AudioPanel.Position.Y + HeaderCenterYOffset),
            "AUDIO", EditorFonts.Pt(12), UiFonts.Dim);

        var status = AudioStatusText(out var statusColor);
        EditorOverlayDraw.RightText(this, Tech1, AudioStatusRight, AudioStatusCenterY,
            FitLabel(Tech1, status, EditorFonts.Pt(12), AudioStatusMaxWidth), EditorFonts.Pt(12), statusColor);

        // 试听占位：设计稿的播放钮在这里，但编辑器没有音频播放能力 —— 只有描边、整体压到 55%。
        var disabled = new Color(UiFonts.Cyan, DisabledAlpha);
        EditorOverlayDraw.CutStroke(this, AudioPlay, 10f, disabled, 2f);
        DrawColoredPolygon(new[]
        {
            AudioPlayGlyph, AudioPlayGlyph + new Vector2(16f, 10f), AudioPlayGlyph + new Vector2(0f, 20f),
        }, disabled);

        EditorOverlayDraw.CutPanel(this, AudioPathFrame, AudioFieldCut, new Color(UiFonts.Panel, .6f),
            FieldEdge(_audioPath), 2f);

        if (_snapshot.AddDifficulty)
        {
            // 音频属于已打开的包：行不可点，按占位惯例压到 55%。
            EditorOverlayDraw.CutStroke(this, AudioBrowse, AudioFieldCut, new Color(UiFonts.Line, DisabledAlpha), 2f);
            EditorOverlayDraw.CenteredText(this, Bold2, AudioBrowse.Position + AudioBrowse.Size * .5f,
                "BROWSE", EditorFonts.Pt(13), new Color(UiFonts.Dim, DisabledAlpha));
            return;
        }
        EditorOverlayDraw.CutStroke(this, AudioBrowse, AudioFieldCut,
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Browse)), 2f);
        EditorOverlayDraw.CenteredText(this, Bold2, AudioBrowse.Position + AudioBrowse.Size * .5f,
            "BROWSE", EditorFonts.Pt(13), UiFonts.Cyan);
    }

    /// <summary>音频状态是真实校验态：文件存在 + 扩展名受 draft factory 支持 + 落盘探测只吃 WAV。
    /// 设计稿的 `✓ 02:34 · 44.1KHZ` 需要时长/采样率探测，编辑器没有，所以一律不写。</summary>
    private string AudioStatusText(out Color color)
    {
        var path = _audioPath.Text.Trim();
        if (_snapshot.AddDifficulty)
        {
            color = path.Length == 0 ? UiFonts.Hard : UiFonts.Dim;
            return path.Length == 0 ? "NO AUDIO IN PACKAGE" : "FROM OPEN PACKAGE";
        }
        if (path.Length == 0) { color = UiFonts.Dim; return "NO AUDIO SELECTED"; }
        var name = System.IO.Path.GetFileName(path);
        if (!System.IO.File.Exists(path)) { color = UiFonts.Giga; return $"FILE NOT FOUND · {name}"; }
        var extension = System.IO.Path.GetExtension(path);
        if (!EditorProjectDraftFactory.IsSupportedAudioPath(path))
        {
            color = UiFonts.Giga;
            return $"{extension.TrimStart('.').ToUpperInvariant()} UNSUPPORTED";
        }
        if (!string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
        {
            color = UiFonts.Hard;
            return $"✓ {name} · SAVE NEEDS WAV";
        }
        color = UiFonts.Casual;
        return $"✓ {name}";
    }

    private void DrawMetadataPanel()
    {
        Panel(MetaPanel);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(HeaderX, MetaPanel.Position.Y + HeaderCenterYOffset),
            "METADATA", EditorFonts.Pt(12), UiFonts.Dim);
        DrawField(TitleFrame, "TITLE", _title);
        DrawField(ArtistFrame, "ARTIST", _artist);
        DrawField(PackIdFrame, "PACK ID", _packId);
        DrawField(PackCharterFrame, "CHARTER", _packCharter);
    }

    /// <summary>TIMING：BPM/offset 的大数字本身是 LineEdit，可直接输入；配设计稿的 32 格 tick 条
    /// （每 4 格高亮 cyan）。非法值由 <see cref="NumericField"/> 拒绝（短暂红框 + 回退），不流到 factory。</summary>
    private void DrawTimingPanel()
    {
        Panel(TimingPanel);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(HeaderX, TimingPanel.Position.Y + HeaderCenterYOffset),
            "TIMING", EditorFonts.Pt(12), UiFonts.Dim);
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(BpmLabelX, TimingLabelCenterY), "BPM", EditorFonts.Pt(11), UiFonts.Dim);
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(OffsetLabelX, TimingLabelCenterY), "OFFSET SEC", EditorFonts.Pt(11), UiFonts.Dim);
        for (var i = 0; i < TickCount; i++)
        {
            var major = i % 4 == 0;
            var height = major ? TicksHeight : TicksHeight * .5f;
            DrawRect(new Rect2(TicksX + Mathf.Floor(i * TicksWidth / TickCount), TicksY + (TicksHeight - height),
                3f, height), major ? new Color(UiFonts.Cyan, .9f) : new Color(UiFonts.Dim, .4f), true);
        }
    }

    private void DrawChartPanel()
    {
        Panel(ChartPanelRect);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(ChartHeaderX, ChartPanel.Position.Y + HeaderCenterYOffset),
            "FIRST CHART", EditorFonts.Pt(12), UiFonts.Dim);
        DrawField(ChartIdFrame, "CHART ID", _chartId);
        DrawField(ChartCharterFrame, "CHARTER", _chartCharter);
        for (var i = 0; i < DifficultyLabels.Length; i++) DrawChip(i);
        // DIFF KEY 框与提示随折叠过渡淡出/淡入（与 LineEdit 的 modulate 同步）；非 CUSTOM 收敛到
        // t=1 后整块不画，CUSTOM 展开态 t=0 时 alpha=1、坐标走 shift=0，与设计稿常量一致。
        var diffKeyAlpha = 1f - _collapseT;
        if (_difficulty != CustomIndex && diffKeyAlpha <= 0f) return;
        var diffKeyShift = Shift;
        DrawField(new Rect2(DiffKeyFrame.Position + new Vector2(0f, diffKeyShift), DiffKeyFrame.Size),
            "DIFF KEY", _diffKey, diffKeyAlpha);
        EditorOverlayDraw.LeftText(this, Tech1, new Vector2(DiffKeyHintX, DiffKeyHintCenterY + diffKeyShift),
            "≤ 24 CHARS · UNIQUE IN PACK", EditorFonts.Pt(11), new Color(UiFonts.Dim, diffKeyAlpha));
    }

    /// <summary>难度 chip：未选中是同色描边 + 同色文字；选中的那一枚转实心（同色填充 + ink 文字），
    /// CUSTOM 实心就是设计稿的 cyan。</summary>
    private void DrawChip(int index)
    {
        var rect = ChipRect(index);
        var color = ChipColor(index);
        var hovered = HoverT(Hotspot.Chip, index);
        if (index == _difficulty)
        {
            EditorOverlayDraw.CutPanel(this, rect, ChipCut, color.Lerp(color.Lightened(HoverLift), hovered),
                new Color(0, 0, 0, 0), 0f);
            EditorOverlayDraw.CenteredText(this, Bold1, rect.Position + rect.Size * .5f,
                DifficultyLabels[index], EditorFonts.Pt(13), UiFonts.InkText);
            return;
        }
        EditorOverlayDraw.CutStroke(this, rect, ChipCut, color.Lerp(color.Lightened(HoverLift), hovered), 2f);
        EditorOverlayDraw.CenteredText(this, Bold1, rect.Position + rect.Size * .5f,
            DifficultyLabels[index], EditorFonts.Pt(13), color);
    }

    /// <summary>COVER：可选封面。未选时是设计稿的虚线拖放区（点击 = 选文件），选中后显示真实校验态
    /// 与清空 ✕；AddDifficulty 下封面属于已打开的包，只读且压暗。</summary>
    private void DrawCoverPanel()
    {
        var origin = CoverPanelPos;
        var shift = Shift;
        Panel(new Rect2(origin, CoverPanel.Size));
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(ChartHeaderX, origin.Y + HeaderCenterYOffset),
            "COVER · OPTIONAL", EditorFonts.Pt(12), UiFonts.Dim);

        var readOnly = _snapshot.AddDifficulty;
        // 设计稿的拖放区是 line 色虚线；hover 提到 cyan 是 Pack 页虚线行同一套语言。
        var edge = readOnly
            ? new Color(UiFonts.Line, DisabledAlpha)
            : UiFonts.Line.Lerp(UiFonts.Cyan, HoverT(Hotspot.Cover));
        DashedCut(CoverZoneRect, CoverZoneCut, edge, 2f);
        DrawCoverIcon(new Vector2(CoverIconX, CoverIconY + shift),
            readOnly ? new Color(UiFonts.Dim, DisabledAlpha) : UiFonts.Dim);

        var (text, color) = CoverText();
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(CoverTextX, CoverTextCenterY + shift),
            FitLabel(Tech2, text, EditorFonts.Pt(13), CoverTextMaxWidth), EditorFonts.Pt(13),
            readOnly ? new Color(color, color.A * DisabledAlpha) : color);
        if (readOnly || _coverPath is null) return;

        var clear = HoverT(Hotspot.CoverClear);
        var tint = UiFonts.Dim.Lerp(UiFonts.Pink, clear);
        EditorOverlayDraw.CutStroke(this, CoverClearRect, 4f, tint, 2f);
        var center = CoverClearRect.Position + CoverClearRect.Size * .5f;
        DrawLine(center + new Vector2(-4f, -4f), center + new Vector2(4f, 4f), tint, 2f, true);
        DrawLine(center + new Vector2(4f, -4f), center + new Vector2(-4f, 4f), tint, 2f, true);
    }

    /// <summary>封面行是真实态：未选 = 设计稿文案；选中后按扩展名与存在性给可读状态。</summary>
    private (string Text, Color Color) CoverText()
    {
        if (_snapshot.AddDifficulty)
            return (_snapshot.CoverName.Length == 0 ? "NO COVER IN PACKAGE" : _snapshot.CoverName, UiFonts.Dim);
        if (_coverPath is null) return ("DROP IMAGE · PNG / JPG", UiFonts.Dim);
        var name = System.IO.Path.GetFileName(_coverPath);
        if (!System.IO.File.Exists(_coverPath)) return ($"✓ {name} · FILE NOT FOUND", UiFonts.Giga);
        var extension = System.IO.Path.GetExtension(_coverPath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg")) return ($"✓ {name} · NOT PNG / JPG", UiFonts.Hard);
        return ($"✓ {name}", UiFonts.Casual);
    }

    /// <summary>MEMORY DRAFT：真实状态行（新建 = 尚未落盘的内存草稿；加差分 = 并入已打开包的内存文档）。</summary>
    private void DrawDraftPanel()
    {
        var shift = Shift;
        Panel(new Rect2(DraftPanelPos, DraftPanel.Size));
        var color = _snapshot.DraftWarn ? UiFonts.Hard : UiFonts.Cyan;
        EditorOverlayDraw.CutPanel(this, new Rect2(DraftSquarePos, new Vector2(10f, 10f)), 3f, color,
            new Color(0, 0, 0, 0), 0f);
        EditorOverlayDraw.LeftText(this, Bold2, new Vector2(DraftLabelX, DraftCenterY + shift), _snapshot.DraftLabel, EditorFonts.Pt(13), color);
        EditorOverlayDraw.LeftText(this, Tech1, new Vector2(DraftDetailX, DraftCenterY + shift), _snapshot.DraftDetail, EditorFonts.Pt(12), UiFonts.Dim);
    }

    /// <summary>底部：CANCEL 描边键、中缝状态（校验不过时写原因并转红，同时 CREATE 压到 55% 禁用）、
    /// CREATE PACKAGE 实心键。</summary>
    private void DrawBottomBar()
    {
        EditorOverlayDraw.CutStroke(this, CancelRect, BottomCut,
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Cancel)), 2f);
        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(CancelCenterX, BottomCenterY), "CANCEL", EditorFonts.Pt(15), UiFonts.Cyan);

        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(StatusCenterX, BottomCenterY),
            FitLabel(Bold2, _statusText, EditorFonts.Pt(13), StatusMaxWidth), EditorFonts.Pt(13), _statusError ? UiFonts.Pink : UiFonts.Casual);

        if (!_createEnabled)
        {
            EditorOverlayDraw.CutStroke(this, CreateRect, BottomCut, new Color(UiFonts.Line, DisabledAlpha), 2f);
            EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(CreateCenterX, BottomCenterY),
                "CREATE PACKAGE", EditorFonts.Pt(15), new Color(UiFonts.Dim, DisabledAlpha));
            return;
        }
        EditorOverlayDraw.CutPanel(this, CreateRect, BottomCut,
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Create)), new Color(0, 0, 0, 0), 0f);
        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(CreateCenterX, BottomCenterY),
            "CREATE PACKAGE", EditorFonts.Pt(15), UiFonts.InkText);
    }

    // ---------------------------------------------------------------- 形状助手

    private void Panel(Rect2 rect) =>
        EditorOverlayDraw.CutPanel(this, rect, PanelCut, UiFonts.Panel, UiFonts.Line, 2f);

    /// <summary>字段框按对应 LineEdit 的焦点 glow 插值描边色：Line → Cyan（glow 0..1）。</summary>
    private Color FieldEdge(Control field) =>
        _fieldGlow.TryGetValue(field, out var glow) ? UiFonts.Line.Lerp(UiFonts.Cyan, glow) : UiFonts.Line;

    private void DrawField(Rect2 frame, string label, Control field, float alpha = 1f)
    {
        // alpha 供折叠过渡用（DIFF KEY 与它的 LineEdit 同步淡出/淡入）；1 时颜色原样 = 设计稿值。
        EditorOverlayDraw.CutPanel(this, frame, FieldCut, new Color(UiFonts.Panel, .6f * alpha),
            new Color(FieldEdge(field), FieldEdge(field).A * alpha), 2f);
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(frame.Position.X, frame.Position.Y + FieldLabelOffsetY),
            label, EditorFonts.Pt(FieldLabelSize), new Color(UiFonts.Dim, alpha));
    }

    /// <summary>切角矩形只切左上 + 右下；虚线版本按边逐段画（StyleBox 表达不了虚线切角框）。</summary>
    private void DashedCut(Rect2 rect, float cut, Color color, float width)
    {
        var points = UiGeometry.CutCorners(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, cut);
        for (var i = 0; i < points.Length; i++)
            DrawDashedLine(points[i], points[(i + 1) % points.Length], color, width, DashedDash, false);
    }

    /// <summary>设计稿 ICON_IMG：34 框里的相框 + 圆点 + 山线，1.6px 描边。</summary>
    private void DrawCoverIcon(Vector2 origin, Color color)
    {
        DrawRect(new Rect2(origin + new Vector2(3f, 5f), new Vector2(28f, 24f)), color, false, 1.6f);
        DrawArc(origin + new Vector2(12f, 13f), 3f, 0f, Mathf.Tau, 24, color, 1.6f, true);
        DrawPolyline(new[]
        {
            origin + new Vector2(5f, 26f), origin + new Vector2(13f, 18f), origin + new Vector2(19f, 24f),
            origin + new Vector2(24f, 19f), origin + new Vector2(31f, 26f),
        }, color, 1.6f, true);
    }

    /// <summary>状态行可能很长（中文错误原因），按真实字宽截断，避免压到两个动作键。</summary>
    private static string FitLabel(Font font, string text, int size, float maxWidth)
    {
        if (text.Length == 0 || font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth) return text;
        var trimmed = text;
        while (trimmed.Length > 1 &&
               font.GetStringSize(trimmed + "…", HorizontalAlignment.Left, -1, size).X > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed + "…";
    }

    // ---------------------------------------------------------------- BPM / OFFSET

    /// <summary>BPM 与 offset 的大数字：LineEdit 直接输入，提交时校验。非有限数（以及 BPM ≤ 0）不写进值，
    /// 短暂红框后回退到上一个合法值，非法参数不会流到 draft factory。</summary>
    private sealed partial class NumericField : Control
    {
        public enum Kind { Bpm, Offset }

        private const float FlashSeconds = .7f;
        private readonly Kind _kind;
        private readonly LineEdit _edit;
        private bool _suppress;
        private float _flash;
        // 焦点 glow（0..1）：本控件没有 stylebox 可给焦点反馈，cyan 描边框按这个值插值透明度。
        private float _glow;

        public double Value { get; private set; }
        public event Action? Committed;

        public NumericField(Kind kind, Rect2 rect)
        {
            _kind = kind;
            Name = kind == Kind.Bpm ? "Bpm" : "OffsetSec";
            Position = rect.Position;
            Size = rect.Size;
            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(false);
            _edit = new LineEdit
            {
                Position = new Vector2(2f, 3f), Size = rect.Size - new Vector2(4f, 6f),
                Alignment = HorizontalAlignment.Left,
                CaretBlink = true,
            };
            var empty = new StyleBoxEmpty();
            foreach (var state in new[] { "normal", "focus", "read_only" }) _edit.AddThemeStyleboxOverride(state, empty);
            // 与等级数字框同一防护：LineEdit 最小宽度 = minimum_character_width × 'W' 宽，
            // Control.SetSize 会钳到不小于它；34px 大数字在高 UI SCALE 档下会逼近框宽，
            // 清零避免控件被静默撑宽、溢出 TIMING 面板的数字位。
            _edit.AddThemeConstantOverride("minimum_character_width", 0);
            _edit.AddThemeFontOverride("font", UiFonts.TechBold);
            EditorFonts.ApplySize(_edit, 34);
            _edit.AddThemeColorOverride("font_color", UiFonts.Text);
            _edit.AddThemeColorOverride("caret_color", UiFonts.Cyan);
            _edit.TextSubmitted += _ => Commit();
            _edit.FocusExited += Commit;
            // 焦点反馈复用 _Process：glow 向 1 lerp（约 .12s），失焦向 0 回落；MOTION OFF 直接跳终值。
            _edit.FocusEntered += () =>
            {
                if (EditorSettings.Current.MotionMode == UiMotionMode.Off) _glow = 1f;
                SetProcess(true);
                QueueRedraw();
            };
            _edit.FocusExited += () => { SetProcess(true); QueueRedraw(); };
            AddChild(_edit);
            // 与等级数字框同理：树外设 Size 会被默认主题的最小宽度钳制，进树后再回正。
            _edit.Ready += () => _edit.Size = rect.Size - new Vector2(4f, 6f);
        }

        public void SetValue(double value)
        {
            Value = value;
            SetText(Format(value));
        }

        public override void _Process(double delta)
        {
            var step = (float)delta / GlowSeconds;
            var target = _edit.HasFocus() ? 1f : 0f;
            _glow = EditorSettings.Current.MotionMode == UiMotionMode.Off
                ? target
                : Mathf.MoveToward(_glow, target, step);
            // flash 仍在倒数，glow 未到终值也继续跑，两者都停了才关 _Process。
            if (_flash <= 0f && _glow == target) { SetProcess(false); return; }
            if (_flash > 0f) _flash -= (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_glow > 0f)
                EditorOverlayDraw.CutStroke(this, new Rect2(-3f, -3f, Size.X + 6f, Size.Y + 6f), 6f,
                    new Color(UiFonts.Cyan, _glow), 2f);
            if (_flash <= 0f) return;
            EditorOverlayDraw.CutStroke(this, new Rect2(-3f, -3f, Size.X + 6f, Size.Y + 6f), 6f, UiFonts.Giga, 2f);
        }

        private string Format(double value) => _kind == Kind.Bpm
            ? value.ToString("0.0###", CultureInfo.InvariantCulture)
            : value.ToString("+0.000;-0.000;+0.000", CultureInfo.InvariantCulture);

        private void Commit()
        {
            if (_suppress) return;
            if (TryParse(_edit.Text, out var parsed))
            {
                Value = parsed;
                SetText(Format(parsed));
                Committed?.Invoke();
                return;
            }
            _flash = FlashSeconds;
            SetProcess(true);
            SetText(Format(Value));
            QueueRedraw();
        }

        private bool TryParse(string text, out double value)
        {
            value = 0d;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                !double.IsFinite(parsed)) return false;
            if (_kind == Kind.Bpm && parsed <= 0d) return false;
            value = parsed;
            return true;
        }

        private void SetText(string text)
        {
            if (_edit.Text == text) return;
            _suppress = true;
            _edit.Text = text;
            _suppress = false;
        }
    }

    // ---------------------------------------------------------------- 等级输入

    /// <summary>等级输入（设计稿 Widget — Level States，规范 §4 第 5 条）：范围 1..99，‹ › 步进钳制、
    /// 99 时 › 禁用；数字是 LineEdit、永远是权威值；块编码 = 十位 26×34 切角大块 + 个位 12×18 半高小块
    /// （等距 18 不分组），0 位不出块；非法输入被拒（短暂红框 + 回到上一个合法值）；unrated 时数字禁用、
    /// 不出块、等级不写入。</summary>
    private sealed partial class LevelField : Control
    {
        public const int MinLevel = 1, MaxLevel = 99;

        private const float FlashSeconds = .7f;
        private const float StepY = 20f, StepSize = 30f, StepCut = 6f, StepGlyphOffsetX = 15f, StepGlyphCenterY = 35f;
        private const float BackX = 0f, ForwardX = 88f;
        // 数字框居中于两个步进器之间：间隙 30..88 的中点是 59（Widget — Level States 的居中规则——
        // 组件板数字 ctxt 280 正是 250..310 的中点；Create 板样例把数字放在 1062，比真实中点 1067
        // 偏左 5px，是样例的手误，以组件板为准）。框中心 (59,35)，左上角即 (39,15)。
        private const float NumberX = 39f, NumberY = 15f, NumberWidth = 40f, NumberHeight = 40f;
        private const float BlocksX = 152f, BlocksBaseline = 50f;
        private const float BigWidth = 26f, BigHeight = 34f, BigPitch = 34f, BigCut = 4f;
        private const float SmallWidth = 12f, SmallHeight = 18f, SmallPitch = 18f, SmallRadius = 2f;
        private const float GapAfterTens = 6f;
        private const float UnratedX = 772f, UnratedY = 20f, UnratedSize = 20f;
        // 设计稿 UNRATED 的行心是绝对 y=414，本层原点在 (1008,384)，所以局部中心是 30。
        private const float UnratedLabelX = 804f, UnratedLabelCenterY = 30f, UnratedHitWidth = 130f;
        // 红框/焦点框随数字框居中：框中心与数字框同一点 (59,35)。
        private static readonly Rect2 FlashFrame = new(25f, 13f, 68f, 44f);
        /// <summary>hover 目标的最新值（_GuiInput 写、_Process 读推进），与过渡值分开持有。</summary>
        private bool _backTarget, _forwardTarget, _unratedTarget;
        /// <summary>个位小块是设计稿 lvl_blocks 的 `rect r:2`（圆角 2），不是切角。</summary>
        private readonly StyleBoxFlat _smallBlock = new()
        {
            CornerRadiusTopLeft = (int)SmallRadius, CornerRadiusTopRight = (int)SmallRadius,
            CornerRadiusBottomLeft = (int)SmallRadius, CornerRadiusBottomRight = (int)SmallRadius,
        };

        private readonly LineEdit _number;
        private int _level = 10;
        private bool _unrated, _suppress;
        // hover 统一过渡（‹/›/UNRATED 框）：瞬时换色改为 0..1 lerp，_Process 推进到终值自关。
        private HoverFade _backHover, _forwardHover, _unratedHover;
        private float _flash;
        // 数字区焦点 glow（0..1）：cyan 描边框按这个值淡入淡出，_Process 与 flash 合并驱动。
        private float _glow;
        private Color _difficultyColor = UiFonts.Giga;

        public event Action? ValueChanged;

        public LevelField()
        {
            MouseFilter = MouseFilterEnum.Stop;
            SetProcess(false);
            _number = new LineEdit
            {
                Position = new Vector2(NumberX, NumberY), Size = new Vector2(NumberWidth, NumberHeight),
                Alignment = HorizontalAlignment.Center,
                CaretBlink = true,
            };
            var empty = new StyleBoxEmpty();
            foreach (var state in new[] { "normal", "focus", "read_only" }) _number.AddThemeStyleboxOverride(state, empty);
            // LineEdit 的最小宽度 = minimum_character_width（默认 4）× 当前字号 'W' 宽，而 Control.SetSize
            // 会把尺寸钳到不小于最小宽度：40px 的数字框在 22px+ 字号下被静默撑到 ~90px，居中文字因此
            // 偏出两个步进器之间（视觉上"数字不居中"的真因）。清零该常量，控件保持 40px。
            _number.AddThemeConstantOverride("minimum_character_width", 0);
            _number.AddThemeFontOverride("font", UiFonts.TechBold);
            EditorFonts.ApplySize(_number, 22);
            _number.AddThemeColorOverride("caret_color", UiFonts.Cyan);
            _number.AddThemeColorOverride("font_uneditable_color", UiFonts.Dim);
            _number.TextChanged += OnNumberChanged;
            _number.TextSubmitted += _ => CommitNumber();
            _number.FocusExited += CommitNumber;
            // 数字框 stylebox 全空，焦点反馈画在 cyan 描边框上；MOTION OFF 直接跳终值，不动画。
            _number.FocusEntered += () =>
            {
                if (EditorSettings.Current.MotionMode == UiMotionMode.Off) _glow = 1f;
                SetProcess(true);
                QueueRedraw();
            };
            _number.FocusExited += () => { SetProcess(true); QueueRedraw(); };
            AddChild(_number);
            // 初始化器里设 Size 时控件还吃默认主题（16px 字体 → 最小宽 4×'W'≈68.5），会被钳宽；
            // 而上面的 minimum_character_width 清零要进树、theme 缓存刷新后才生效（树外重设 Size
            // 仍按旧缓存钳），所以进树后的 Ready 里再回正一次尺寸。
            _number.Ready += () => _number.Size = new Vector2(NumberWidth, NumberHeight);
            ApplyColor();
            RefreshNumber();
        }

        /// <summary>unrated 时等级为 null（v2 要求 level 与 unrated 恰有其二）。</summary>
        public int? Level => _unrated ? null : _level;
        public bool Unrated => _unrated;

        public Color DifficultyColor
        {
            get => _difficultyColor;
            set { _difficultyColor = value; ApplyColor(); QueueRedraw(); }
        }

        public void SetValue(int level, bool unrated)
        {
            _level = Math.Clamp(level, MinLevel, MaxLevel);
            _unrated = unrated;
            RefreshNumber();
        }

        public override void _Process(double delta)
        {
            var step = (float)delta / GlowSeconds;
            var target = _number.HasFocus() ? 1f : 0f;
            _glow = EditorSettings.Current.MotionMode == UiMotionMode.Off
                ? target
                : Mathf.MoveToward(_glow, target, step);
            // hover 过渡：MOTION OFF 时 Update 直接跳终值，失能（划过禁用步进）也随之回落 0。
            var back = _backHover.Update(BackEnabled && _backTarget, delta);
            var forward = _forwardHover.Update(ForwardEnabled && _forwardTarget, delta);
            var unrated = _unratedHover.Update(_unratedTarget, delta);
            // flash 仍在倒数，glow/hover 未到终值也继续跑，全部停了才关 _Process。
            if (_flash <= 0f && _glow == target && !back && !forward && !unrated) { SetProcess(false); return; }
            if (_flash > 0f) _flash -= (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            EditorOverlayDraw.LeftText(this, Tech2, new Vector2(0f, 5.5f), "LEVEL", EditorFonts.Pt(11), UiFonts.Dim);
            DrawStepper(BackX, BackEnabled, _backHover.Value, "‹");
            DrawStepper(ForwardX, ForwardEnabled, _forwardHover.Value, "›");
            DrawBlocks();
            DrawUnrated();
            if (_glow > 0f)
                EditorOverlayDraw.CutStroke(this,
                    new Rect2(NumberX - 3f, NumberY - 3f, NumberWidth + 6f, NumberHeight + 6f), 6f,
                    new Color(UiFonts.Cyan, _glow), 2f);
            if (_flash <= 0f) return;
            EditorOverlayDraw.CutStroke(this, FlashFrame, 6f, UiFonts.Giga, 2f);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion motion)
            {
                // hover 目标变了才推进过渡值（_Process 已开时无需重复开，SetProcess 幂等）。
                var back = BackEnabled && BackRect.HasPoint(motion.Position);
                var forward = ForwardEnabled && ForwardRect.HasPoint(motion.Position);
                var unrated = UnratedRect.HasPoint(motion.Position);
                if (back == _backTarget && forward == _forwardTarget && unrated == _unratedTarget) return;
                _backTarget = back;
                _forwardTarget = forward;
                _unratedTarget = unrated;
                SetProcess(true);
                return;
            }
            if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left) return;
            if (BackEnabled && BackRect.HasPoint(button.Position)) { Step(-1); AcceptEvent(); return; }
            if (ForwardEnabled && ForwardRect.HasPoint(button.Position)) { Step(1); AcceptEvent(); return; }
            if (!UnratedRect.HasPoint(button.Position)) return;
            ToggleUnrated();
            AcceptEvent();
        }

        /// <summary>`‹` 只在 unrated 时禁用，到 1 仍可按但被钳制（规范只规定 99 时 `›` 禁用）。</summary>
        private bool BackEnabled => !_unrated;
        private bool ForwardEnabled => !_unrated && _level < MaxLevel;

        private static Rect2 BackRect => new(BackX, StepY, StepSize, StepSize);
        private static Rect2 ForwardRect => new(ForwardX, StepY, StepSize, StepSize);
        private static Rect2 UnratedRect => new(UnratedX, UnratedY - 6f, UnratedHitWidth, UnratedSize + 12f);

        private void ApplyColor()
        {
            _number.AddThemeColorOverride("font_color", _difficultyColor);
            _number.AddThemeColorOverride("selection_color", new Color(_difficultyColor, .35f));
        }

        /// <summary>数字框是唯一权威值；unrated 时禁用并显示 `--`，块不绘制。</summary>
        private void RefreshNumber()
        {
            _suppress = true;
            _number.Editable = !_unrated;
            _number.FocusMode = _unrated ? FocusModeEnum.None : FocusModeEnum.All;
            _number.Text = _unrated ? "--" : _level.ToString(CultureInfo.InvariantCulture);
            _suppress = false;
            QueueRedraw();
        }

        private void OnNumberChanged(string text)
        {
            if (_suppress || _unrated) return;
            if (!TryParseLevel(text, out var parsed) || parsed == _level) return;
            _level = parsed;
            QueueRedraw();
            ValueChanged?.Invoke();
        }

        private void CommitNumber()
        {
            if (_suppress || _unrated) return;
            if (TryParseLevel(_number.Text, out var parsed))
            {
                _level = parsed;
                RefreshNumber();
                ValueChanged?.Invoke();
                return;
            }
            // 非法值（-1、0、100…）拒绝：红框提示后回到上一个合法值。
            _flash = FlashSeconds;
            SetProcess(true);
            RefreshNumber();
            QueueRedraw();
        }

        private void Step(int delta)
        {
            var next = Math.Clamp(_level + delta, MinLevel, MaxLevel);
            if (next == _level) return;
            _level = next;
            RefreshNumber();
            ValueChanged?.Invoke();
        }

        private void ToggleUnrated()
        {
            _unrated = !_unrated;
            RefreshNumber();
            ValueChanged?.Invoke();
        }

        private static bool TryParseLevel(string text, out int level) =>
            int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out level) &&
            level is >= MinLevel and <= MaxLevel;

        private void DrawStepper(float x, bool enabled, float hovered, string glyph)
        {
            // hover 用 lerp 过渡：Line → Cyan 按值推进，失能态（35% alpha 灰描边）不参与 hover 提亮。
            var stroke = enabled
                ? UiFonts.Line.Lerp(UiFonts.Cyan, hovered)
                : new Color(UiFonts.Line, .35f);
            EditorOverlayDraw.CutStroke(this, new Rect2(x, StepY, StepSize, StepSize), StepCut, stroke, 2f);
            EditorOverlayDraw.CenteredText(this, EditorFonts.Medium, new Vector2(x + StepGlyphOffsetX, StepGlyphCenterY),
                glyph, EditorFonts.Pt(14), enabled ? UiFonts.Text : new Color(UiFonts.Text, .3f));
        }

        /// <summary>十进制块编码：十位大块（26×34 切角 4），个位小块（12×18 半高、圆角 2、等距 18 不分组），
        /// 0 位不出块。unrated 时整组不绘制。</summary>
        private void DrawBlocks()
        {
            if (_unrated) return;
            var tens = _level / 10;
            var ones = _level % 10;
            for (var i = 0; i < tens; i++)
                EditorOverlayDraw.CutPanel(this,
                    new Rect2(BlocksX + i * BigPitch, BlocksBaseline - BigHeight, BigWidth, BigHeight),
                    BigCut, _difficultyColor, new Color(0, 0, 0, 0), 0f);
            if (ones == 0) return;
            var start = BlocksX + tens * BigPitch + (tens > 0 ? GapAfterTens : 0f);
            _smallBlock.BgColor = new Color(_difficultyColor, .85f);
            for (var i = 0; i < ones; i++)
                DrawStyleBox(_smallBlock,
                    new Rect2(start + i * SmallPitch, BlocksBaseline - SmallHeight, SmallWidth, SmallHeight));
        }

        private void DrawUnrated()
        {
            var rect = new Rect2(UnratedX, UnratedY, UnratedSize, UnratedSize);
            if (_unrated)
            {
                EditorOverlayDraw.CutPanel(this, rect, 3f, new Color(UiFonts.Cyan, .15f), UiFonts.Cyan, 2f);
                DrawPolyline(new[]
                {
                    rect.Position + new Vector2(4f, 10.5f), rect.Position + new Vector2(8f, 14.5f),
                    rect.Position + new Vector2(16f, 5.5f),
                }, UiFonts.Cyan, 2.4f, true);
            }
            else
            {
                DrawRect(rect, UiFonts.Line.Lerp(UiFonts.Cyan, _unratedHover.Value), false, 2f);
            }
            EditorOverlayDraw.LeftText(this, EditorFonts.Medium, new Vector2(UnratedLabelX, UnratedLabelCenterY),
                "UNRATED", EditorFonts.Pt(13), _unrated ? UiFonts.Cyan : UiFonts.Dim);
        }
    }
}

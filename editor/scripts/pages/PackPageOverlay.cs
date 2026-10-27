using Godot;
using System;
using System.Collections.Generic;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>Pack Select 板（设计稿 Pack Select，施工值见 `.tmp/penpot_build4.py` 的 `pack_board()`）
/// 的自绘层。切角 + 虚线描边、密度条、超高列表的渐隐与 4px 细滚动条都不是控件树能表达的形状，
/// 所以整页自绘；点击命中走矩形表（与右键菜单同一套做法），绘制坐标与设计稿 1:1。
/// 本层不推断数据：包元数据、封面、差分行全部由 shell 以快照形式传入。</summary>
internal sealed partial class PackPageOverlay : Control
{
    // ---------------------------------------------------------------- 设计稿几何

    private const float BarHeight = 76f, BarLineHeight = 2f;
    private const float BarWordX = 32f, BarPageX = 330f, BarCenterY = 38f;

    private const float LeftX = 32f, PackRowWidth = 640f, LeftHeaderCenterY = 114f;
    private const float PackRowY = 142f, PackRowHeight = 86f, PackRowPitch = 100f, PackRowCut = 10f;
    private const float PackRowNameCenterY = 28f, PackRowMetaCenterY = 58f, PackRowTimeRight = 648f;
    private const float PackRowTextX = 62f;
    private static readonly Vector2 PackRowBarOffset = new(12f, 15f);
    private static readonly Vector2 PackRowBarSize = new(6f, 56f);
    private const float ImportRowHeight = 64f, ImportRowCut = 10f;
    private const float ImportPlusX = 56f, ImportPlusOffsetY = 21f, ImportTextX = 96f;

    private const float CoverX = 704f, CoverY = 142f, CoverSize = 220f, CoverCut = 22f;
    private const float PackTitleX = 948f, PackTitleCenterY = 190f;
    private const float ChipX = 948f, ChipY = 240f, ChipWidth = 92f, ChipHeight = 34f, ChipCut = 6f, ChipPitch = 102f;
    // 难度 chips 超过一行时换行（设计稿只给了单行 6 枚；包内可以有更多自定义难度）：
    // 行高 44、右缘不超过 1888（= 盾标/DIGEST 区左缘前的可用宽度）。
    private const float ChipRowHeight = 44f, ChipMaxRight = 1888f;
    private const int ChipsPerRow = 1 + (int)((ChipMaxRight - ChipX - ChipWidth) / ChipPitch);
    private const float ShieldX = 1808f, ShieldY = 180f, DigestCenterX = 1828f, DigestCenterY = 238f;
    private const float ShieldStroke = 1.8f, ShieldTickStroke = 2.2f;

    internal const float ListX = 704f, ListY = 420f, ListWidth = 1184f, ListHeight = 476f;
    private const float ListFadeTop = 390f, ListFadeHeight = 86f;
    private const float ListBarX = 1174f, ListBarWidth = 4f;
    private const float ChartRowHeight = 60f, ChartRowPitch = 68f, ChartRowCut = 8f;
    private const float RowChipX = 16f, RowLevelX = 130f, RowDensityX = 192f, RowNotesRight = 1152f;
    private const float RowChipOffsetY = 13f, RowLevelCenterY = 30f, RowDensityOffsetY = 12f;
    private const float DensityWidth = 620f, DensityHeight = 36f, DensityBars = 72f;
    private const float DensityFillRatio = .6f, DensityOpacity = .85f, DensityFloor = .15f;

    private const float NewChartX = 704f, NewChartY = 908f, NewChartWidth = 1184f, NewChartHeight = 44f, NewChartCut = 8f;
    private const float NewChartPlusX = 724f, NewChartPlusOffsetY = 11f, NewChartTextX = 760f, NewChartCenterY = 930f;
    private const float NewChartTextOpacity = .8f;

    private const float BackX = 32f, BackWidth = 180f, BackCenterX = 122f;
    private const float BottomY = 972f, BottomHeight = 52f, BottomCut = 10f, BottomCenterY = 998f;
    private const float StoryX = 1380f, StoryWidth = 220f, StoryCenterX = 1490f;
    private const float OpenX = 1608f, OpenWidth = 280f, OpenCenterX = 1748f;

    private const float DisabledAlpha = .55f, HoverLift = .12f, DashedDash = 6f;
    /// <summary>差分行 hover 的行底提亮（全局 hover 只做亮度/不透明度变化）。</summary>
    private const float RowHoverLift = .08f;

    // 设计稿字距（ls1..ls4）在 Godot 侧用 FontVariation 表达，与 Start 板/HUD 同一做法。
    private static readonly Font Tech1 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 1 };
    private static readonly Font Tech2 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };
    private static readonly Font Tech3 = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 3 };
    private static readonly Font Bold1 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 1 };
    private static readonly Font Bold2 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 2 };
    private static readonly Font Bold4 = new FontVariation { BaseFont = UiFonts.TechBold, SpacingGlyph = 4 };

    // ---------------------------------------------------------------- 数据

    /// <summary>差分行。数据全部来自 shell：难度名/等级/真实主判定数/真实 note 时间分布。
    /// <c>Density</c> 为 null 表示该差分没有 note（不画密度条），否则是 0..1 的 72 桶分布。</summary>
    public sealed record ChartRow(
        string Id, string Label, bool Custom, Color Color, string LevelText, string NotesText,
        float[]? Density, bool Selected);

    public sealed class Snapshot
    {
        public string PackName { get; init; } = string.Empty;
        public string PackMeta { get; init; } = string.Empty;
        public string PackTime { get; init; } = string.Empty;
        public Texture2D? Cover { get; init; }
        public bool DigestOk { get; init; }
        public IReadOnlyList<ChartRow> Charts { get; init; } = Array.Empty<ChartRow>();
    }

    public event Action? BackRequested;
    public event Action? ImportRequested;
    public event Action? NewChartRequested;
    public event Action? OpenInEditorRequested;
    public event Action<string>? ChartSelected;

    private enum Hotspot { None, Import, Chart, NewChart, Back, OpenEditor }

    private readonly Control _listClip;
    private readonly PackChartList _list;
    private readonly CoverPlaceholder _placeholder;
    private readonly List<ChartRow> _chips = new();
    private Snapshot _state = new();
    private Texture2D? _cover;
    private Hotspot _hover = Hotspot.None;
    private int _hoverChart = -1;
    /// <summary>最后一次已知的指针位置（motion 与滚轮都给位置）：惯性滚动期间行在光标下滑动，
    /// 要按同一位置重算 hover 目标，否则高亮会停在滚动前的行上。</summary>
    private Vector2 _pointer;
    private bool _pointerKnown;
    // hover 统一过渡（docs/editor-ui-design.md §3）：每个热点一枚 0..1 值，绘制处按值 lerp；
    // 全部到终值后 _Process 自关，页面空闲不逐帧重绘。
    private readonly HoverFadeMap _hoverFade = new();

    public PackPageOverlay()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // 两个子层的 Position/Size 都在入树之后再设（不依赖容器布局）。
        _placeholder = new CoverPlaceholder { Visible = false };
        AddChild(_placeholder);
        _placeholder.Position = new Vector2(CoverX, CoverY);
        _placeholder.Size = new Vector2(CoverSize, CoverSize);
        // 列表区：外层只按设计稿的 704,420,1184×476 裁切，行/渐隐/滚动条由子层按设计稿顺序绘制。
        // ClipContents 只在入树之后设置（与客户端 CoverPlaceholder 同一做法，避免裁剪矩形取到旧尺寸）。
        _listClip = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_listClip);
        _listClip.Position = new Vector2(ListX, ListY);
        _listClip.Size = new Vector2(ListWidth, ListHeight);
        _listClip.ClipContents = true;
        _list = new PackChartList();
        _listClip.AddChild(_list);
        _list.Position = Vector2.Zero;
        _list.Size = new Vector2(ListWidth, ListHeight);
    }

    public void Apply(Snapshot state)
    {
        _state = state;
        _cover = state.Cover;
        _placeholder.Visible = _cover is null;
        // chips 表达「这个包提供哪些难度」：同一难度重复出现（同名难度多张谱）只画一个。
        // 位置按「一行放得下就横排，超出右缘 1888 换行」计算，所以包内自定义难度多也不会溢出画布。
        _chips.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in state.Charts)
            if (seen.Add(row.Label)) _chips.Add(row);
        _list.SetRows(state.Charts);
        // 行集合变了，旧的 hover 下标可能指向别的行。
        _list.SetHover(-1);
        _hover = Hotspot.None;
        _hoverChart = -1;
        QueueRedraw();
    }

    // ---------------------------------------------------------------- 输入

    private static Rect2 ListRect => new(ListX, ListY, ListWidth, ListHeight);
    private bool OpenEnabled => _state.Charts.Count > 0;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            _pointer = motion.Position;
            _pointerKnown = true;
            Highlight(HotspotAt(motion.Position, out var index), index);
            return;
        }
        if (@event is not InputEventMouseButton button) return;
        // 滚轮事件没有配套的 motion，hover 重算只能靠事件自带的位置。
        if (button.Pressed || button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _pointer = button.Position;
            _pointerKnown = true;
        }
        if (!button.Pressed) return;
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (!ListRect.HasPoint(button.Position)) return;
            _list.ScrollBy((button.ButtonIndex == MouseButton.WheelUp ? -1f : 1f) * ChartRowPitch);
            // 滚动后光标下的行变了，hover 要按同一位置重算；显示值由子层按惯性推进，
            // 页面 _Process 在滚动期间保持开启（见 _Process 的 ScrollAnimating 分支）。
            Highlight(HotspotAt(button.Position, out var scrolled), scrolled);
            SetProcess(true);
            AcceptEvent();
            return;
        }
        if (button.ButtonIndex != MouseButton.Left) return;
        switch (HotspotAt(button.Position, out var chartIndex))
        {
            case Hotspot.Import: ImportRequested?.Invoke(); break;
            case Hotspot.Chart: ChartSelected?.Invoke(_state.Charts[chartIndex].Id); break;
            case Hotspot.NewChart: NewChartRequested?.Invoke(); break;
            case Hotspot.Back: BackRequested?.Invoke(); break;
            case Hotspot.OpenEditor: OpenInEditorRequested?.Invoke(); break;
            default: return;
        }
        AcceptEvent();
    }

    private void Highlight(Hotspot hotspot, int chartIndex)
    {
        var hovered = hotspot == Hotspot.Chart ? chartIndex : -1;
        if (hotspot == _hover && hovered == _hoverChart) return;
        _hover = hotspot;
        _hoverChart = hovered;
        // hover 目标变了才开 _Process：过渡值推进 + 重绘，全部到终值后自关（不空转）。
        SetProcess(true);
        // 行由子层绘制，hover 变化要同时把子层重绘排上。
        _list.SetHover(hovered);
        QueueRedraw();
    }

    private Hotspot HotspotAt(Vector2 point, out int chartIndex)
    {
        chartIndex = -1;
        if (ImportRect.HasPoint(point)) return Hotspot.Import;
        if (NewChartRect.HasPoint(point)) return Hotspot.NewChart;
        if (BackRect.HasPoint(point)) return Hotspot.Back;
        if (OpenEnabled && OpenRect.HasPoint(point)) return Hotspot.OpenEditor;
        if (!ListRect.HasPoint(point)) return Hotspot.None;
        var index = Mathf.FloorToInt((point.Y - ListY + _list.Scroll) / ChartRowPitch);
        if (index < 0 || index >= _state.Charts.Count) return Hotspot.None;
        var top = ListY + index * ChartRowPitch - _list.Scroll;
        if (point.Y < top || point.Y > top + ChartRowHeight) return Hotspot.None;
        chartIndex = index;
        return Hotspot.Chart;
    }

    private static Rect2 ImportRect => new(LeftX, ImportRowY, PackRowWidth, ImportRowHeight);
    private static Rect2 NewChartRect => new(NewChartX, NewChartY, NewChartWidth, NewChartHeight);
    private static Rect2 BackRect => new(BackX, BottomY, BackWidth, BottomHeight);
    private static Rect2 OpenRect => new(OpenX, BottomY, OpenWidth, BottomHeight);

    /// <summary>hover 热点 → 过渡值表的键：行命中下标占 0..999，其余热点挪到 1000 起；
    /// None/无 hover 编码为 -1（表里不存在，全部键向 0 回落）。</summary>
    private long HoverKey(Hotspot hotspot, int chartIndex) => hotspot switch
    {
        Hotspot.Chart => chartIndex,
        Hotspot.Import => 1000,
        Hotspot.NewChart => 1001,
        Hotspot.Back => 1002,
        Hotspot.OpenEditor => 1003,
        _ => -1,
    };

    /// <summary>绘制处取某热点的 hover 过渡值（0..1）：绘制色 = 常态色.Lerp(hover 色, 值)。</summary>
    private float HoverT(Hotspot hotspot, int chartIndex = -1) => _hoverFade[HoverKey(hotspot, chartIndex)];

    public override void _Process(double delta)
    {
        // hover 统一过渡：键推进、值绘出，全部到终值即关 _Process 不空转。
        // 滚动行数变化时 _hoverChart 可能越界，clamped 只影响目标 1 的键（原 0/0 回落不受影响）。
        var chartHover = _hover == Hotspot.Chart ? Math.Clamp(_hoverChart, 0, Math.Max(0, _state.Charts.Count - 1)) : -1;
        var unsettled = _hoverFade.Update(HoverKey(_hover, chartHover), delta);
        // 惯性滚动期间光标下的行在变：按最后一次已知指针位置重算 hover 目标，直到滚动停下。
        if (_list.ScrollAnimating && _pointerKnown)
        {
            Highlight(HotspotAt(_pointer, out var index), index);
            unsettled = true;
        }
        if (unsettled) return;
        SetProcess(false);
    }

    /// <summary>单包会话的行列表：只有已打开的当前包一行，IMPORT 行紧随其后。</summary>
    private static float ImportRowY => PackRowY + PackRowPitch;

    // ---------------------------------------------------------------- 绘制

    public override void _Draw()
    {
        DrawTopBar();
        DrawPackRow();
        DrawImportRow();
        DrawDetail();
        DrawNewChart();
        DrawBottomBar();
    }

    private void DrawTopBar()
    {
        DrawRect(new Rect2(0, 0, 1920f, BarHeight), new Color(UiFonts.Bg, .92f), true);
        DrawRect(new Rect2(0, BarHeight, 1920f, BarLineHeight), UiFonts.Line, true);
        EditorOverlayDraw.LeftText(this, Bold4, new Vector2(BarWordX, BarCenterY), "DYNAMAKER·UV", EditorFonts.Pt(24), UiFonts.Cyan);
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(BarPageX, BarCenterY), "PACKAGES", EditorFonts.Pt(13), UiFonts.Dim);
    }

    /// <summary>左列：`LOCAL PACKAGES` 表头 + 当前包一行（选中态 = cyan 10% 填充、cyan 描边、6×56 定位条）。</summary>
    private void DrawPackRow()
    {
        EditorOverlayDraw.LeftText(this, Tech3, new Vector2(LeftX, LeftHeaderCenterY), "LOCAL PACKAGES", EditorFonts.Pt(12), UiFonts.Dim);
        var rect = new Rect2(LeftX, PackRowY, PackRowWidth, PackRowHeight);
        EditorOverlayDraw.CutPanel(this, rect, PackRowCut, new Color(UiFonts.Cyan, .10f), UiFonts.Cyan, 2f);
        DrawRect(new Rect2(rect.Position + PackRowBarOffset, PackRowBarSize), UiFonts.Cyan, true);
        EditorOverlayDraw.LeftText(this, UiFonts.TechBold,
            new Vector2(PackRowTextX, PackRowY + PackRowNameCenterY), _state.PackName, EditorFonts.Pt(19), UiFonts.Text);
        EditorOverlayDraw.LeftText(this, Tech1,
            new Vector2(PackRowTextX, PackRowY + PackRowMetaCenterY), _state.PackMeta, EditorFonts.Pt(12), UiFonts.Dim);
        EditorOverlayDraw.RightText(this, EditorFonts.Medium, PackRowTimeRight,
            PackRowY + PackRowNameCenterY, _state.PackTime, EditorFonts.Pt(12), UiFonts.Dim);
    }

    private void DrawImportRow()
    {
        var rect = ImportRect;
        // hover 统一过渡：虚线描边 Line → Cyan、文字 Dim → Text，都按过渡值 lerp。
        var hovered = HoverT(Hotspot.Import);
        DashedCut(this, rect, ImportRowCut, UiFonts.Line.Lerp(UiFonts.Cyan, hovered), 2f);
        DrawPlus(this, new Vector2(ImportPlusX, rect.Position.Y + ImportPlusOffsetY), UiFonts.Cyan);
        EditorOverlayDraw.LeftText(this, Tech2, new Vector2(ImportTextX, rect.Position.Y + rect.Size.Y * .5f),
            "IMPORT PACKAGE", EditorFonts.Pt(15), UiFonts.Dim.Lerp(UiFonts.Text, hovered));
    }

    /// <summary>右侧详情：封面（真实封面图按切角多边形贴图，无封面时交回客户端 NO COVER 占位）、
    /// 包名、本包提供的难度 chips（按难度去重，custom 走虚线）、DIGEST 盾标。</summary>
    private void DrawDetail()
    {
        DrawCover();
        var title = _state.PackName.Length == 0 ? "—" : _state.PackName;
        EditorOverlayDraw.LeftText(this, UiFonts.TechBold, new Vector2(PackTitleX, PackTitleCenterY), title, EditorFonts.Pt(44), UiFonts.Text);
        for (var i = 0; i < _chips.Count; i++)
        {
            var chip = _chips[i];
            var origin = new Vector2(ChipX + i % ChipsPerRow * ChipPitch, ChipY + i / ChipsPerRow * ChipRowHeight);
            DrawChip(this, origin, FitLabel(Bold1, chip.Label, EditorFonts.Pt(13), ChipWidth - 12f), chip.Color, chip.Custom);
        }
        DrawDigest();
    }

    private void DrawCover()
    {
        var points = UiGeometry.CutCorners(CoverX, CoverY, CoverSize, CoverSize, CoverCut);
        var textureSize = _cover?.GetSize() ?? Vector2.Zero;
        if (_cover is null || textureSize.X <= 0f || textureSize.Y <= 0f)
        {
            // 无封面（或解码出空图）时不叠设计稿的 line 框：
            // CoverPlaceholder 自带 cyan 切角边框与 NO COVER 文案。
            return;
        }
        // 贴图沿切角多边形铺满封面区，uv 按客户端 TextureRect 的 KeepAspectCovered 语义
        // （等比放大到铺满 + 居中裁切，不拉伸）；uv = 顶点在裁切后图像里的归一化坐标。
        var visible = textureSize * Mathf.Max(CoverSize / textureSize.X, CoverSize / textureSize.Y);
        var origin = new Vector2(CoverX, CoverY);
        var offset = (visible - new Vector2(CoverSize, CoverSize)) * .5f;
        var colors = new Color[points.Length];
        var uvs = new Vector2[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            colors[i] = Colors.White;
            uvs[i] = (points[i] - origin + offset) / visible;
        }
        DrawPolygon(points, colors, uvs, _cover);
        DrawPolyline(UiGeometry.Close(points), UiFonts.Line, 2f, true);
    }

    /// <summary>DIGEST 盾标。OK = 当前编辑状态（内存谱面 + 包内音频字节）能算出 gameplay-v1 digest，
    /// 与保存时 writer 写入的身份同一算法；算不出来时盾标与文案一起压暗并转 `DIGEST —`。</summary>
    private void DrawDigest()
    {
        var ok = _state.DigestOk;
        var color = ok ? UiFonts.Casual : UiFonts.Dim;
        DrawShield(this, new Vector2(ShieldX, ShieldY), color);
        EditorOverlayDraw.CenteredText(this, Tech2, new Vector2(DigestCenterX, DigestCenterY),
            ok ? "DIGEST OK" : "DIGEST —", EditorFonts.Pt(12), color);
    }

    private void DrawNewChart()
    {
        var rect = NewChartRect;
        // hover 统一过渡：虚线 alpha .6 → .95、文字/加号 .8 → 1，都按过渡值 lerp。
        var hovered = HoverT(Hotspot.NewChart);
        DashedCut(this, rect, NewChartCut, new Color(UiFonts.Cyan, Mathf.Lerp(.6f, .95f, hovered)), 2f);
        DrawPlus(this, new Vector2(NewChartPlusX, NewChartY + NewChartPlusOffsetY),
            new Color(UiFonts.Cyan, Mathf.Lerp(NewChartTextOpacity, 1f, hovered)));
        EditorOverlayDraw.LeftText(this, Bold2, new Vector2(NewChartTextX, NewChartCenterY), "NEW CHART", EditorFonts.Pt(14),
            new Color(UiFonts.Cyan, Mathf.Lerp(NewChartTextOpacity, 1f, hovered)));
    }

    /// <summary>底部操作条：‹ BACK、中间状态行（shell 的 Label 画在中缝）、STORYBOARD 禁用占位、
    /// OPEN IN EDITOR 实心键。状态行不在本层绘制，避免与 shell 的保存/错误回报抢同一行。</summary>
    private void DrawBottomBar()
    {
        EditorOverlayDraw.CutPanel(this, BackRect, BottomCut, new Color(0, 0, 0, 0),
            UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.Back)), 2f);
        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(BackCenterX, BottomCenterY), "‹ BACK", EditorFonts.Pt(15), UiFonts.Cyan);

        // 已拍板的 STORYBOARD 预留入口：只描边不填色、整体 55% 压暗、不进入命中表。
        var story = new Rect2(StoryX, BottomY, StoryWidth, BottomHeight);
        EditorOverlayDraw.CutPanel(this, story, BottomCut, new Color(0, 0, 0, 0), new Color(UiFonts.Line, DisabledAlpha), 2f);
        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(StoryCenterX, BottomCenterY), "STORYBOARD", EditorFonts.Pt(15),
            new Color(UiFonts.Dim, DisabledAlpha));

        if (!OpenEnabled)
        {
            EditorOverlayDraw.CutPanel(this, OpenRect, BottomCut, new Color(0, 0, 0, 0), new Color(UiFonts.Line, DisabledAlpha), 2f);
            EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(OpenCenterX, BottomCenterY), "OPEN IN EDITOR", EditorFonts.Pt(15),
                new Color(UiFonts.Dim, DisabledAlpha));
            return;
        }
        var fill = UiFonts.Cyan.Lerp(UiFonts.Cyan.Lightened(HoverLift), HoverT(Hotspot.OpenEditor));
        EditorOverlayDraw.CutPanel(this, OpenRect, BottomCut, fill, new Color(0, 0, 0, 0), 0f);
        EditorOverlayDraw.CenteredText(this, Bold2, new Vector2(OpenCenterX, BottomCenterY), "OPEN IN EDITOR", EditorFonts.Pt(15), UiFonts.InkText);
    }

    // ---------------------------------------------------------------- 形状助手

    /// <summary>切角矩形只切左上 + 右下，虚线版本按边逐段画（StyleBox 表达不了虚线切角框）。</summary>
    private static void DashedCut(CanvasItem item, Rect2 rect, float cut, Color color, float width)
    {
        var points = UiGeometry.CutCorners(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, cut);
        for (var i = 0; i < points.Length; i++)
            item.DrawDashedLine(points[i], points[(i + 1) % points.Length], color, width, DashedDash, false);
    }

    /// <summary>难度 chip：标准难度实线描边 + 同色文字，custom 走虚线 chip。</summary>
    private static void DrawChip(CanvasItem item, Vector2 origin, string label, Color color, bool dashed)
    {
        var rect = new Rect2(origin, new Vector2(ChipWidth, ChipHeight));
        if (dashed) DashedCut(item, rect, ChipCut, color, 2f);
        else EditorOverlayDraw.CutPanel(item, rect, ChipCut, new Color(0, 0, 0, 0), color, 2f);
        EditorOverlayDraw.CenteredText(item, Bold1, origin + new Vector2(ChipWidth, ChipHeight) * .5f, label, EditorFonts.Pt(13), color);
    }

    /// <summary>设计稿 ICON_PLUS：22 框里两条 2.4px 笔，描边色由调用方给。</summary>
    private static void DrawPlus(CanvasItem item, Vector2 origin, Color color)
    {
        item.DrawLine(origin + new Vector2(11f, 4f), origin + new Vector2(11f, 18f), color, 2.4f, true);
        item.DrawLine(origin + new Vector2(4f, 11f), origin + new Vector2(18f, 11f), color, 2.4f, true);
    }

    /// <summary>设计稿 ICON_SHIELD：40 框盾形（两段三次曲线近似）+ 对勾。</summary>
    private static void DrawShield(CanvasItem item, Vector2 origin, Color color)
    {
        var outline = new List<Vector2>
        {
            origin + new Vector2(20f, 3f), origin + new Vector2(33f, 9.5f), origin + new Vector2(33f, 19f),
        };
        outline.AddRange(Cubic(origin + new Vector2(33f, 19f), origin + new Vector2(33f, 27f),
            origin + new Vector2(27.5f, 34.5f), origin + new Vector2(20f, 35f)));
        outline.AddRange(Cubic(origin + new Vector2(20f, 35f), origin + new Vector2(12.5f, 34.5f),
            origin + new Vector2(7f, 27f), origin + new Vector2(7f, 19f)));
        outline.Add(origin + new Vector2(7f, 9.5f));
        item.DrawPolyline(UiGeometry.Close(outline.ToArray()), color, ShieldStroke, true);
        item.DrawPolyline(new[]
        {
            origin + new Vector2(14f, 19.5f), origin + new Vector2(18f, 23.5f), origin + new Vector2(26f, 15.5f),
        }, color, ShieldTickStroke, true);
    }

    private static IEnumerable<Vector2> Cubic(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end,
        int steps = 12)
    {
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var u = 1f - t;
            yield return u * u * u * start + 3f * u * u * t * control1 + 3f * u * t * t * control2 + t * t * t * end;
        }
    }

    /// <summary>chip 只有 92px，自定义难度 key 可能远超它——按真实字宽截断成带省略号的短标签。</summary>
    private static string FitLabel(Font font, string text, int size, float maxWidth)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= maxWidth) return text;
        var trimmed = text;
        while (trimmed.Length > 1 &&
               font.GetStringSize(trimmed + "…", HorizontalAlignment.Left, -1, size).X > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed + "…";
    }

    /// <summary>列表区的行层：只画行、底部渐隐与 4px 细滚动条（渐隐先画、滚动条压在上面）。
    /// 裁切由外层 <see cref="_listClip"/> 的 ClipContents 负责，滚动只改这一层内部的 y 偏移。</summary>
    private sealed partial class PackChartList : Control
    {
        private IReadOnlyList<ChartRow> _rows = Array.Empty<ChartRow>();
        private float _scroll;
        /// <summary>滚轮推出的滚动目标值；<see cref="_scroll"/> 是指针看到的显示值，两者按指数趋近
        /// （见 <see cref="EditorScrollInertia"/>）。内容整体换一份（切包/换行集合）这类程序性变更
        /// 直接把两者一起落，不把上一份内容的滚动目标带到新内容上。</summary>
        private float _scrollTarget;
        private int _hover = -1;
        // 行 hover 统一过渡：行底提亮按 0..1 值 lerp；行数无上限（滚动列表），键 = 行下标，
        // 各行独立持有过渡值，未出现的键自动向 0 回落（HoverFadeMap 值落到 0 即删条目，
        // 不会随行数无限增长）。
        private readonly HoverFadeMap _hoverFade = new();

        public PackChartList()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(false);
        }

        public float Scroll => _scroll;

        /// <summary>显示的滚动值还在追目标（惯性途中）。页面据此在滚动期间保持 _Process 开着，
        /// 每帧按光标位置重算 hover。</summary>
        public bool ScrollAnimating => _scroll != _scrollTarget;

        /// <summary>差分行 hover（全局 hover 语言：只做亮度/不透明度变化）。目标变了才开 _Process
        /// 推进过渡值，到终值自关；键 = 行下标，离开的行按自己的值回落，快速划过多行时各行
        /// 余晖互不干扰，观感「跟手但不闪」。</summary>
        public void SetHover(int index)
        {
            if (_hover == index) return;
            _hover = index;
            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            // 过渡中每帧重绘行层；hover 与滚动惯性都到终值才关 _Process 不空转。
            QueueRedraw();
            // 目标键 = 当前 hover 的行下标；无 hover 传 -1，全部行随各自的过渡值向 0 回落。
            var unsettled = _hoverFade.Update(_hover, delta);
            unsettled |= StepScroll(delta);
            if (unsettled) return;
            SetProcess(false);
        }

        private float ContentHeight => _rows.Count == 0 ? 0f : _rows.Count * ChartRowPitch - (ChartRowPitch - ChartRowHeight);
        private float MaxScroll => Mathf.Max(0f, ContentHeight - Size.Y);

        /// <summary>内容整体换了一份（切包/换行集合）是程序性刷新，显示值与目标值一起落，
        /// 不把上一份内容的滚动目标带到新内容上。</summary>
        public void SetRows(IReadOnlyList<ChartRow> rows)
        {
            _rows = rows;
            _scroll = Mathf.Clamp(_scroll, 0f, MaxScroll);
            _scrollTarget = _scroll;
            QueueRedraw();
        }

        /// <summary>滚轮推力：只加目标值，显示值交给 <see cref="_Process"/> 的指数趋近。
        /// 惯性为 0（滑条最左，或 MOTION = OFF）时立刻落目标并重绘一次，
        /// 与改动前的瞬时滚动逐值一致（那时也是即时重绘）。</summary>
        public void ScrollBy(float delta)
        {
            var next = Mathf.Clamp(_scrollTarget + delta, 0f, MaxScroll);
            if (Mathf.IsEqualApprox(next, _scrollTarget)) return;
            _scrollTarget = next;
            if (EditorSettings.Current.ScrollInertiaTauSeconds <= 0d)
            {
                _scroll = _scrollTarget;
                QueueRedraw();
                return;
            }
            SetProcess(true);
        }

        /// <summary>推进显示值；返回 true 表示还在惯性途中（调用方据此保持 _Process）。
        /// 惯性为 0 时 <see cref="EditorScrollInertia.Step"/> 直接返回目标值，与改动前的瞬时滚动一致。</summary>
        private bool StepScroll(double delta)
        {
            if (_scroll == _scrollTarget) return false;
            _scroll = EditorScrollInertia.Step(_scroll, _scrollTarget, EditorSettings.Current.ScrollInertiaTauSeconds, delta);
            return _scroll != _scrollTarget;
        }

        public override void _Draw()
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                var top = i * ChartRowPitch - _scroll;
                if (top + ChartRowHeight < 0f || top > Size.Y) continue;
                DrawRow(i, _rows[i], new Rect2(0f, top, ListWidth, ChartRowHeight));
            }
            DrawFade();
            DrawScrollBar();
        }

        private void DrawRow(int index, ChartRow row, Rect2 rect)
        {
            // hover 只提亮行底（+8% 亮度，随过渡值推进），与设计板的选中态（类型色描边）区分开；
            // 过渡值按行取，只 hover 到当前这一行。
            EditorOverlayDraw.CutPanel(this, rect, ChartRowCut,
                new Color(UiFonts.Panel.Lightened(RowHoverLift * _hoverFade[index]), .5f),
                row.Selected ? row.Color : UiFonts.Line, 2f);
            DrawChip(this, rect.Position + new Vector2(RowChipX, RowChipOffsetY),
                FitLabel(Bold1, row.Label, EditorFonts.Pt(13), ChipWidth - 12f), row.Color, row.Custom);
            EditorOverlayDraw.LeftText(this, UiFonts.TechBold,
                new Vector2(rect.Position.X + RowLevelX, rect.Position.Y + RowLevelCenterY),
                row.LevelText, EditorFonts.Pt(20), row.Color);
            if (row.Density is { } density)
                DrawDensity(new Vector2(rect.Position.X + RowDensityX, rect.Position.Y + RowDensityOffsetY),
                    density, row.Color);
            EditorOverlayDraw.RightText(this, EditorFonts.Medium, rect.Position.X + RowNotesRight,
                rect.Position.Y + RowLevelCenterY, row.NotesText, EditorFonts.Pt(15), UiFonts.Dim);
        }

        /// <summary>密度条：72 个竖条按真实 note 时间分布取值，条高 = 36×(0.15 + 0.85×归一化桶值)，
        /// 空桶保留设计稿的最小柱（密度语言里的静区），0.15 只是渲染下限，不改变数据。</summary>
        private void DrawDensity(Vector2 origin, float[] density, Color color)
        {
            var pitch = DensityWidth / DensityBars;
            var barWidth = Mathf.Max(1f, pitch * DensityFillRatio);
            var tint = new Color(color, DensityOpacity);
            for (var i = 0; i < density.Length; i++)
            {
                var height = DensityHeight * (DensityFloor + (1f - DensityFloor) * density[i]);
                DrawRect(new Rect2(origin.X + i * pitch, origin.Y + DensityHeight - height, barWidth, height), tint, true);
            }
        }

        /// <summary>底部渐隐 = 86 条 1px 线性 alpha 的 bg 矩形（设计稿的 svg_fade_down 渐变）。</summary>
        private void DrawFade()
        {
            for (var i = 0; i < ListFadeHeight; i++)
            {
                var alpha = i / (ListFadeHeight - 1f);
                DrawRect(new Rect2(0f, ListFadeTop + i, ListWidth, 1f), new Color(UiFonts.Bg, alpha), true);
            }
        }

        private void DrawScrollBar()
        {
            if (MaxScroll <= 0f || ContentHeight <= 0f) return;
            var track = new Rect2(ListBarX, 0f, ListBarWidth, Size.Y);
            DrawRect(track, new Color(UiFonts.Text, .10f), true);
            var gripHeight = Mathf.Clamp(Size.Y * (Size.Y / ContentHeight), 24f, Size.Y);
            var gripY = _scroll / MaxScroll * (Size.Y - gripHeight);
            EditorOverlayDraw.CutPanel(this, new Rect2(ListBarX, gripY, ListBarWidth, gripHeight),
                ListBarWidth * .5f, new Color(UiFonts.Cyan, .55f), new Color(0, 0, 0, 0), 0f);
        }
    }
}

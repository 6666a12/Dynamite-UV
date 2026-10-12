using Godot;
using System;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

/// <summary>放置手势视觉（设计板 <c>Editor — Placing</c>，设计稿 §4 第 9、10 条）：
/// 过幽灵时间的粉色导引时间线（两端渐隐）、过幽灵位置的白色位置线、线上三读数（位置 /
/// 宽度 / 分数式时间）、虚线幽灵描边、宽度拖拽指示（双向箭头 + 光标十字）、Hold 拖尾虚线
/// 预览与尾端菱形幽灵，以及场上已放对象锚点的对齐提示方块。
///
/// 分层：导引线画在画布自己的 <c>_Draw</c>，也就是 shared 舞台 Note **之下**（设计板要求
/// 导引压在 Note 下，幽灵与已放对象保持可读）；读数、幽灵描边、箭头、拖尾预览与对齐方块
/// 必须压在 Note **之上**，所以放在 <see cref="PlacementChrome"/> 这个排在音符层之上的兄弟节点里。
///
/// 视觉只在放置 / Hold 拖尾 / 搬运手势进行中显示，手势结束即消失；全部数值 1:1 跟随输入，
/// 不做插值，也不参与吸附、提交、undo 或 dirty 语义。
/// </summary>
public partial class AuthoringCanvas
{
    // 幽灵虚线描边（设计稿 svg_note_dashed：dasharray 9 6、2px、75% 不透明）。
    private const float GhostDash = 9f, GhostGap = 6f, GhostStroke = 2f, GhostAlpha = .75f;
    // 导引线：过幽灵时间的粉线 2px（中段 90%、两端各 200px 线性渐隐）、过幽灵位置的白线 3px 60%。
    private const float TimeGuideWidth = 2f, TimeGuideFade = 200f, TimeGuideAlpha = .9f, TimeGuideFadeStep = 10f;
    private const float PositionGuideWidth = 3f, PositionGuideAlpha = .6f;
    // 线上读数：20px bold，位置线两侧各偏 22px，竖直方向贴在幽灵上缘上方 18px，
    // 带 1px 偏移的深色底衬（设计稿用底衬代替原版的黑描边）。
    private static int GuideTextSize => EditorFonts.Pt(20);
    private const float GuideTextOffset = 22f, GuideTextLift = 18f, GuideUnderlayOffset = 1f;
    /// <summary>中轨时间读数贴在中心轨左缘右移 20px 处（设计稿 x=300 = 轨道左缘 280 + 20）。</summary>
    private const float GuideTimeInset = 20f;
    // 侧轨没有板面几何：三个读数沿判定线外侧的空档竖排。
    private const float GuideSideInset = 16f;
    // 宽度拖拽指示：从幽灵中心到光标的双向箭头（1.5px 85% + 两端箭头 + 中段刻度）
    // 与光标十字（28px 线 + 16×16 切角 4：cyan 30% 填充 + cyan 1.5px 描边）。
    private const float ArrowWidth = 1.5f, ArrowAlpha = .85f, ArrowHead = 11f, ArrowHeadSpread = 5f, ArrowTick = 5f;
    private const float CursorArm = 28f, CursorArmWidth = 2f, CursorAlpha = .55f;
    private const float CursorBox = 16f, CursorBoxCut = 4f, CursorBoxAlpha = .3f;
    // Hold 拖尾预览：虚线路径（设计稿 svg_dashed_v：1.5px、dash 7 6、60%）+ 尾端 20px 虚线菱形
    // （设计稿 svg_diamond_dashed：panel 底 + 类型色 2px 描边 dash 5 4，整体 85%）。
    private const float TailPathWidth = 1.5f, TailPathAlpha = .6f, TailPathDash = 7f, TailPathGap = 6f;
    private const float TailGhostSize = 20f, TailGhostDash = 5f, TailGhostGap = 4f, TailGhostAlpha = .85f;
    // 阶段标签：13px dim、字距 1.5（Godot 的 FontVariation 字形间距是整数像素，按仓库既有约定
    // ls1.5 编码为 2，见 EditorFlow 的 HUD 字体注释），贴对应幽灵下方（设计稿 cx+14 / cy+33）。
    private static int PhaseTextSize => EditorFonts.Pt(13);
    private const float PhaseIndent = 14f, PhaseGap = 21f;
    // 对齐提示方块（设计稿 §4 第 10 条）：26×26、90% 不透明、无描边。
    private const float AlignMarkerSize = 26f, AlignMarkerAlpha = .9f;
    private const int ChromeZIndex = 3;

    private static readonly Color AlignPositionColor = new("4060ff");
    private static readonly Color AlignWidthColor = new("ff4040");
    private static readonly Color AlignBothColor = new("ff40ff");
    private static readonly Color TimeGuideColor = UiFonts.Pink;
    private static readonly Font PhaseFont = new FontVariation { BaseFont = EditorFonts.Medium, SpacingGlyph = 2 };

    /// <summary>排在 shared 音符层之上的手势前景层；由 <see cref="_Ready"/> 建立。</summary>
    private PlacementChrome? _placementChrome;
    /// <summary>上一帧是否画了手势视觉：手势结束后的第一帧还要重绘一次前景层把它擦掉。</summary>
    private bool _interactionCueDrawn;

    /// <summary>时间吸附是否生效（按住 Z 或 SNAP OFF 时为自由态）。</summary>
    private bool TimeSnapActive => SnapEnabled && TimeSnapOverride;

    // ---------------------------------------------------------------- 分层与刷新

    private void BuildPlacementChrome()
    {
        _placementChrome = new PlacementChrome { Name = "PlacementChrome", ZIndex = ChromeZIndex };
        _placementChrome.Bind(this);
        AddChild(_placementChrome);
    }

    /// <summary>手势视觉跨两层（画布导引线 + 前景叠加层），重绘必须成对调用。</summary>
    private void QueueInteractionRedraw()
    {
        QueueRedraw();
        _placementChrome?.QueueRedraw();
    }

    /// <summary>UI SCALE 换档后的重绘入口：本画布与手势前景层的字号都在 <c>_Draw</c> 里现算
    /// <see cref="EditorFonts.Pt"/>，所以重绘即生效——不要走 <see cref="RefreshStage"/>，
    /// 那会顺带重建舞台，与字号无关。</summary>
    public void RedrawForUiScale() => QueueInteractionRedraw();

    /// <summary>手势视觉的逐帧推进：手势进行中逐帧重绘（1:1 跟随、无插值），手势结束后的
    /// 第一帧再补一次重绘，清掉前景层上残留的读数与方块。</summary>
    private void StepInteractionVisual()
    {
        var interacting = _draft is not null || _gesture is not null;
        if (!interacting && !_interactionCueDrawn) return;
        _interactionCueDrawn = interacting;
        QueueInteractionRedraw();
    }

    /// <summary>前景叠加层：画布自己的 <c>_Draw</c> 排在 shared 舞台 Note 之下，而读数、
    /// 幽灵描边、宽度指示与对齐方块必须画在 Note 之上，所以单独放一个兄弟节点。
    /// Godot 只允许把 <c>Draw*</c> 调进「正在 _draw 的那个 item」，所以这里把本节点作为绘制目标
    /// 传给画布的绘制函数（绘制函数一律用传进来的 <paramref name="target"/> 而不是画布自己）。</summary>
    private sealed partial class PlacementChrome : Node2D
    {
        private AuthoringCanvas? _canvas;
        public void Bind(AuthoringCanvas canvas) => _canvas = canvas;
        public override void _Draw() => _canvas?.DrawPlacementForeground(this);
    }

    // ---------------------------------------------------------------- 导引线（Note 之下）

    /// <summary>放置导引十字线（设计板把导引画在 Note 之下）：过幽灵时间的全宽粉线（两端渐隐）
    /// 与过幽灵位置的位置线。中轨时间沿 y、位置沿 x；侧轨按 <c>PATH_FIELD</c> 的做法交换两轴，
    /// 位置线取判定线到该轨可放置端（设计稿只给了中轨几何）。</summary>
    private void DrawPlacementGuides()
    {
        if (_draft is null) return;
        var side = _draft.Side;
        var timeAxis = AxisAt(side, _draft.Time);
        if (side == 0)
            DrawFadingGuideLine(new Vector2(0f, timeAxis), new Vector2(DesignWidth, timeAxis));
        else
            DrawFadingGuideLine(new Vector2(timeAxis, 0f), new Vector2(timeAxis, DesignHeight));

        var head = DraftHeadRect();
        var positionAxis = side == 0 ? head.Position.X + head.Size.X * .5f : head.Position.Y + head.Size.Y * .5f;
        var from = side == 0 ? new Vector2(positionAxis, 0f) : side == 1 ? new Vector2(LeftJudgeX, positionAxis) : new Vector2(RightJudgeX, positionAxis);
        var to = side == 0
            ? new Vector2(positionAxis, CenterJudgeY)
            : new Vector2(side == 1 ? LeftJudgeX + GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale
                : RightJudgeX - GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale, positionAxis);
        DrawLine(from, to, new Color(Colors.White, PositionGuideAlpha), PositionGuideWidth, true);
    }

    /// <summary>两端渐隐的全宽导引线：中段实色，两端各 <paramref name="TimeGuideFade"/> 像素线性
    /// 淡出（Godot 的 DrawLine 没有渐变，按 10px 一段逼近设计稿的线性渐变）。</summary>
    private void DrawFadingGuideLine(Vector2 from, Vector2 to)
    {
        var length = from.DistanceTo(to);
        if (length <= TimeGuideFade * 2f + 1f) return;
        var direction = (to - from) / length;
        var steps = Math.Max(1, (int)Math.Ceiling(TimeGuideFade / TimeGuideFadeStep));
        var step = TimeGuideFade / steps;
        DrawLine(from + direction * TimeGuideFade, to - direction * TimeGuideFade,
            new Color(TimeGuideColor, TimeGuideAlpha), TimeGuideWidth, true);
        for (var index = 0; index < steps; index++)
        {
            var alpha = TimeGuideAlpha * (index + 1) / steps;
            var color = new Color(TimeGuideColor, alpha);
            DrawLine(from + direction * (index * step), from + direction * ((index + 1) * step), color, TimeGuideWidth, true);
            DrawLine(to - direction * ((index + 1) * step), to - direction * (index * step), color, TimeGuideWidth, true);
        }
    }

    // ---------------------------------------------------------------- 前景元素（Note 之上）

    /// <summary>前景叠加层的绘制入口（在 shared 舞台 Note 之上）；<paramref name="target"/> 是
    /// 正在 <c>_draw</c> 的 PlacementChrome，所有绘制都必须落在它身上。</summary>
    internal void DrawPlacementForeground(CanvasItem target)
    {
        if (_draft is null && _gesture is null) return;
        DrawAlignMarkers(target);
        // 双击插入幽灵的读数也在这一层：幽灵本体留在画布层，读数要被 Note 读得到就必须压在它上面。
        DrawPathInsertReadouts(target);
        if (_draft is not null) DrawDraftForeground(target);
    }

    private void DrawDraftForeground(CanvasItem target)
    {
        if (_draft is not { } draft) return;
        var head = DraftHeadRect();
        var accent = NoteVisualSpec.BaseColor(HeadRuntimeType(draft.Kind));
        if (draft.TailStage)
        {
            DrawHoldTailPreview(target, draft, head, accent);
            // 板面的拖尾标签贴幽灵左缘，单阶段的两个标签贴幽灵中心右移 14px。
            DrawPhaseLabel(target, head, "HOLD · TAIL DRAG", 0f);
            return;
        }
        DrawGhostOutline(target, head, accent);
        DrawDraftReadouts(target, draft, head, accent);
        if (draft.Moved) DrawWidthDragCue(target, draft, head);
        DrawPhaseLabel(target, head, draft.Moved ? "PHASE 1 · WIDTH" : "PHASE 0 · PLACE",
            head.Size.X * .5f + PhaseIndent);
    }

    /// <summary>草稿头（或拖尾阶段的已放置头）在舞台上的矩形，取 shared 渲染同一套几何。</summary>
    private Rect2 DraftHeadRect() => _draft is { } draft
        ? NoteRect(draft.Side, draft.Time, draft.Center, draft.Width, HeadRuntimeType(draft.Kind))
        : new Rect2();

    /// <summary>草稿的真实落点中心：吸附锚点决定宽度对称展开，但贴轨边时左缘被钳住，
    /// 提交用的中心会与锚点不同，所以读数与对齐判定都取这个值（与 <c>CommitDraft</c> 同一个算式）。</summary>
    private static double DraftAnchorCenter(Draft draft) => draft.Center + draft.Width * .5d;

    /// <summary>幽灵 Note 的虚线描边（实色渐变由 shared 渲染器的 ghost 层给出，这里只补描边）：
    /// 切角多边形与 NoteView 着色器的切角同源，描边 dasharray 9 6、75% 不透明。</summary>
    private void DrawGhostOutline(CanvasItem target, Rect2 rect, Color accent)
    {
        var cut = Mathf.Min(7f, Mathf.Min(rect.Size.X, rect.Size.Y) * .34f);
        var points = UiGeometry.CutCorners(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, cut);
        DrawDashedOutline(target, points, new Color(accent.Lerp(Colors.White, .55f), GhostAlpha),
            GhostStroke, GhostDash, GhostGap);
    }

    /// <summary>线上三读数，取吸附后的真实落点：位置（类型色，位置线一侧）与宽度（白，另一侧）
    /// 贴在幽灵上方；时间贴在时间线旁——吸附态是分数式 BarTime，时间自由态显示原始小数小节。
    /// 中轨按设计板几何（位置线两侧 ±22px、时间读数在中心轨左缘上方）；侧轨没有板面几何，
    /// 三个读数沿判定线外侧的空档竖排。</summary>
    private void DrawDraftReadouts(CanvasItem target, Draft draft, Rect2 head, Color accent)
    {
        var positionText = EditorPlacementGuide.ValueText(DraftAnchorCenter(draft));
        var widthText = EditorPlacementGuide.ValueText(draft.Width);
        var timeText = EditorPlacementGuide.TimeText(draft.Time, CurrentBpms, GridDivisor, TimeSnapActive);
        var center = head.Position + head.Size * .5f;
        if (draft.Side == 0)
        {
            var top = head.Position.Y - GuideTextLift;
            DrawGuideText(target, new Vector2(center.X - GuideTextOffset, top), positionText, accent, true);
            DrawGuideText(target, new Vector2(center.X + GuideTextOffset, top), widthText, Colors.White, false);
            DrawGuideText(target, new Vector2(CenterX + GuideTimeInset, AxisAt(0, draft.Time) - GuideTextOffset),
                timeText, UiFonts.Text, false);
            return;
        }
        var edgeX = draft.Side == 1 ? LeftJudgeX - GuideSideInset : RightJudgeX + GuideSideInset;
        var rightAligned = draft.Side == 1;
        DrawGuideText(target, new Vector2(edgeX, center.Y - GuideTextOffset), timeText, UiFonts.Text, rightAligned);
        DrawGuideText(target, new Vector2(edgeX, center.Y), positionText, accent, rightAligned);
        DrawGuideText(target, new Vector2(edgeX, center.Y + GuideTextOffset), widthText, Colors.White, rightAligned);
    }

    /// <summary>线上读数：20px bold 正文 + 1px 偏移的深色底衬（设计稿的 <c>storage.under</c>）。</summary>
    private void DrawGuideText(CanvasItem target, Vector2 anchor, string text, Color color, bool rightAligned)
    {
        var underlay = anchor + new Vector2(GuideUnderlayOffset, GuideUnderlayOffset);
        if (rightAligned)
        {
            EditorOverlayDraw.RightText(target, ChipFont, underlay.X, underlay.Y, text, GuideTextSize, UiFonts.Bg);
            EditorOverlayDraw.RightText(target, ChipFont, anchor.X, anchor.Y, text, GuideTextSize, color);
            return;
        }
        EditorOverlayDraw.LeftText(target, ChipFont, underlay, text, GuideTextSize, UiFonts.Bg);
        EditorOverlayDraw.LeftText(target, ChipFont, anchor, text, GuideTextSize, color);
    }

    /// <summary>宽度拖拽指示（阶段 1）：从幽灵中心到光标的双向箭头线（沿宽度轴，两端箭头向外、
    /// 中段一根刻度）+ 光标十字。箭头只量宽度轴的位移，时间轴分量不参与，所以箭头保持轴向；
    /// 光标十字标在真实指针上（设计板那一帧两者重合）。</summary>
    private void DrawWidthDragCue(CanvasItem target, Draft draft, Rect2 head)
    {
        var origin = head.Position + head.Size * .5f;
        var cursor = _pointerPoint;
        var timeAxis = TimeAxisDirection(draft.Side);
        var widthAxis = WidthAxisDirection(draft.Side);
        var cyan = new Color(UiFonts.Cyan, ArrowAlpha);
        var offset = (cursor - origin).Dot(widthAxis);
        if (Math.Abs(offset) >= 1f)
        {
            var tip = origin + widthAxis * offset;
            target.DrawLine(origin, tip, cyan, ArrowWidth, true);
            target.DrawColoredPolygon([origin, origin - widthAxis * ArrowHead + timeAxis * ArrowHeadSpread,
                origin - widthAxis * ArrowHead - timeAxis * ArrowHeadSpread], cyan);
            target.DrawColoredPolygon([tip, tip + widthAxis * ArrowHead + timeAxis * ArrowHeadSpread,
                tip + widthAxis * ArrowHead - timeAxis * ArrowHeadSpread], cyan);
            var middle = origin + widthAxis * (offset * .5f);
            target.DrawLine(middle - timeAxis * ArrowTick, middle + timeAxis * ArrowTick, cyan, ArrowWidth, true);
        }
        DrawCursorCross(target, cursor);
    }

    /// <summary>光标十字：28px 交叉线（cyan 55%）+ 16×16 切角 4 方块（cyan 30% 填充、cyan 1.5px 描边）。</summary>
    private void DrawCursorCross(CanvasItem target, Vector2 cursor)
    {
        var arm = new Color(UiFonts.Cyan, CursorAlpha);
        target.DrawLine(cursor - new Vector2(CursorArm * .5f, 0f), cursor + new Vector2(CursorArm * .5f, 0f),
            arm, CursorArmWidth, true);
        target.DrawLine(cursor - new Vector2(0f, CursorArm * .5f), cursor + new Vector2(0f, CursorArm * .5f),
            arm, CursorArmWidth, true);
        var half = new Vector2(CursorBox * .5f, CursorBox * .5f);
        EditorOverlayDraw.CutPanel(target, new Rect2(cursor - half, half * 2f), CursorBoxCut,
            new Color(UiFonts.Cyan, CursorBoxAlpha), UiFonts.Cyan, 1.5f);
    }

    /// <summary>Hold 拖尾预览（设计板 <c>HOLD · TAIL DRAG</c>）：从头沿时间轴到尾部时间的虚线
    /// 路径预览 + 尾端 20px 虚线菱形幽灵。头部在这一阶段是已放置实体外观，body 由前景层的虚线
    /// 预览代表，共享渲染器不画链接（见 <c>RefreshGameplayStage</c>）。</summary>
    private void DrawHoldTailPreview(CanvasItem target, Draft draft, Rect2 head, Color accent)
    {
        var side = draft.Side;
        var tail = NoteRect(side, draft.EndTime, draft.Center, draft.Width, NodeRuntimeType(draft.Kind));
        var headCenter = head.Position + head.Size * .5f;
        var tailCenter = tail.Position + tail.Size * .5f;
        var start = side switch
        {
            0 => new Vector2(headCenter.X, head.Position.Y),
            1 => new Vector2(head.End.X, headCenter.Y),
            _ => new Vector2(head.Position.X, headCenter.Y),
        };
        var path = accent.Lerp(Colors.White, .5f);
        DrawDashedSegment(target, start, tailCenter, new Color(path, TailPathAlpha), TailPathWidth, TailPathDash, TailPathGap);
        DrawDashedDiamond(target, tailCenter, TailGhostSize,
            new Color(UiFonts.Panel.R, UiFonts.Panel.G, UiFonts.Panel.B, TailGhostAlpha),
            new Color(accent, TailGhostAlpha));
    }

    /// <summary>虚线菱形幽灵（设计稿 svg_diamond_dashed 的 dash 5 4 + 2px 描边）。</summary>
    private void DrawDashedDiamond(CanvasItem target, Vector2 center, float size, Color fill, Color stroke)
    {
        var half = size * .5f - 1f;
        var points = new[]
        {
            center + new Vector2(0f, -half), center + new Vector2(half, 0f),
            center + new Vector2(0f, half), center + new Vector2(-half, 0f),
        };
        target.DrawColoredPolygon(points, fill);
        DrawDashedOutline(target, points, stroke, 2f, TailGhostDash, TailGhostGap);
    }

    /// <summary>阶段标签：13px dim、字距 1.5，贴对应幽灵下方；<paramref name="insetX"/> 是相对
    /// 幽灵左缘的横向起点（板面单阶段标签在幽灵中心右移 14px，拖尾标签贴幽灵左缘）。</summary>
    private void DrawPhaseLabel(CanvasItem target, Rect2 ghost, string text, float insetX) =>
        EditorOverlayDraw.LeftText(target, PhaseFont,
            new Vector2(ghost.Position.X + insetX, ghost.End.Y + PhaseGap),
            text, PhaseTextSize, UiFonts.Dim);

    /// <summary>对齐提示方块（设计稿 §4 第 10 条）：放置中按「同位置 / 同宽度 / 两者皆同」给同轨
    /// 已放对象的锚点上色（蓝 #4060FF / 红 #FF4040 / 品红 #FF40FF），都不同就不画；搬运已有对象时
    /// 全部锚点转白。判定用吸附后的最终值，容差取吸附步长的一半。</summary>
    private void DrawAlignMarkers(CanvasItem target)
    {
        int side;
        double position, width;
        bool carrying;
        if (_draft is { } draft)
        {
            side = draft.Side;
            position = DraftAnchorCenter(draft);
            width = draft.Width;
            carrying = false;
        }
        else if (_gesture is { } gesture && gesture.NoteIndex >= 0 && gesture.NoteIndex < Notes.Count)
        {
            side = Notes[gesture.NoteIndex].Side;
            position = Notes[gesture.NoteIndex].Center + Notes[gesture.NoteIndex].Width * .5d;
            width = Notes[gesture.NoteIndex].Width;
            carrying = true;
        }
        else return;

        var tolerance = (SpaceSnapOverride ? EditorSettings.Current.SnapStep : EditorGeometry.FineCenterSnap) * .5d;
        for (var index = 0; index < Notes.Count; index++)
        {
            if (index == _gesture?.NoteIndex) continue;
            var note = Notes[index];
            if (note.Side != side) continue;
            var marker = carrying
                ? new Color(Colors.White, AlignMarkerAlpha)
                : MarkerColor(EditorPlacementGuide.Classify(position, width,
                    note.Center + note.Width * .5d, note.Width, tolerance));
            if (marker is not { } color) continue;
            var rect = NoteRect(note, note.Time);
            var center = rect.Position + rect.Size * .5f;
            var half = new Vector2(AlignMarkerSize * .5f, AlignMarkerSize * .5f);
            target.DrawRect(new Rect2(center - half, half * 2f), color);
        }
    }

    private static Color? MarkerColor(EditorAlignRelation relation) => relation switch
    {
        EditorAlignRelation.Both => new Color(AlignBothColor, AlignMarkerAlpha),
        EditorAlignRelation.SamePosition => new Color(AlignPositionColor, AlignMarkerAlpha),
        EditorAlignRelation.SameWidth => new Color(AlignWidthColor, AlignMarkerAlpha),
        _ => null,
    };

    // ---------------------------------------------------------------- 共用几何

    /// <summary>按弧长步进的虚线折线（闭合多边形）：逐边铺 dash/gap，交点处重新起相位。</summary>
    private void DrawDashedOutline(CanvasItem target, IReadOnlyList<Vector2> points, Color color,
        float width, float dash, float gap)
    {
        for (var index = 0; index < points.Count; index++)
            DrawDashedSegment(target, points[index], points[(index + 1) % points.Count], color, width, dash, gap);
    }

    /// <summary>时间增大的方向：中轨向下（判定线在下方），左轨向右、右轨向左
    /// （两侧的剩余像素都从判定线往外量）。</summary>
    private static Vector2 TimeAxisDirection(int side) => side switch
    {
        1 => Vector2.Right,
        2 => Vector2.Left,
        _ => Vector2.Down,
    };

    /// <summary>轨内位置增大的方向：中轨向右，侧轨向上。</summary>
    private static Vector2 WidthAxisDirection(int side) => side == 0 ? Vector2.Right : Vector2.Up;
}

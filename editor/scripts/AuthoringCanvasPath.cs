using Godot;
using System;
using System.Collections.Generic;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using ChartNote = DynamiteUniverse.Shared.Chart.Note;
using Gesture = DynamiteUniverse.ChartEditor.Core.CanvasEditGesture;
using GestureKind = DynamiteUniverse.ChartEditor.Core.CanvasGestureKind;

namespace DynamiteUniverse.Editor;

/// <summary>
/// 路径编辑层（设计稿 Editor — Path Edit / <c>PATH_FIELD</c>）：Hold/Mixer 的真实曲线 body 投影、
/// 控制点菱形手柄与两级选中、过节点中心的虚线 spine，以及双击路径身体插入控制节点的手势。
/// 与 <see cref="AuthoringCanvas"/> 是同一个 partial 类，共用画布的吸附、时间换算与鼠标模型；
/// 曲线求值全部走 shared <see cref="V2PathEvaluator"/>（经
/// <see cref="EditorPathProjection"/>），这里不复制曲线数学。
/// </summary>
public partial class AuthoringCanvas
{
	// PATH_FIELD 几何：头手柄 24px 实心菱形、中间/尾手柄 18px（panel 底 + 类型色描边）、
	// 选中节点 32×32 切角 8 的 cyan 70% 描边环；hover 由 60% 提到 100%（ui-motion §9.1）。
	private const float HeadHandleSize = 24f, NodeHandleSize = 18f;
	private const float SelectedRingSize = 32f, SelectedRingCut = 8f;
	private const float HandleBaseAlpha = .6f, HandleHoverAlpha = 1f;
	// Mixer 的 1/8 小节判定 tick（设计稿 PATH_FIELD：18×2 的 #FF9CC4 75% 短横）。
	private const float MixerTickLength = 18f, MixerTickThickness = 2f, MixerTickAlpha = .75f;
	private static readonly Color MixerTickColor = new("ff9cc4");
	// spine：过节点中心的 1.5px #FFE0A0 55% 虚线，dasharray 7 6（设计稿 svg_spine；Godot 的
	// DrawDashedLine 只有等长间隔，所以这里按设计稿的 7/6 自己步进）。
	private const float SpineWidth = 1.5f, SpineAlpha = .55f, SpineDash = 7f, SpineGap = 6f;
	private static readonly Color SpineColor = new("ffe0a0");
	// 双击插入幽灵：16px 深底 cyan 菱形 + 170px 双向宽度箭头 + 两行读数（PATH_FIELD 相对几何）。
	private const float GhostNodeSize = 16f, GhostArrowWidth = 170f, GhostArrowOffset = 22f;
	private const float GhostTextGap = 34f, GhostTextInset = 103f;
	// 插入幽灵的位置导引线（设计稿 PATH_FIELD 的 (975,140,2×720)）：2px 白 35%。
	private const float InsertGuideWidth = 2f, InsertGuideAlpha = .35f;
	// 路径身体的带状命中宽容（谱面单位换算前是 6px）。
	private const float PathBodySlackPx = 6f;
	// 细分像素系数与实机 V2RuntimeAdapter.RequiredSubdivisions 取同一组值：位置误差按轨道单位
	// 像素，宽度误差按带宽像素（侧轨 102 与 GameplayVisualMapper 侧轨带宽同源）。
	private const double SideNoteWidthUnitPx = GameplayStageGeometry.SideNoteLengthUnitPx;

	/// <summary>指针当前所在的手柄（noteIndex, pointIndex；0 = 头），用于 hover 提亮。</summary>
	private (int NoteIndex, int PointIndex)? _hoverHandle;

	// ---------------------------------------------------------------- 路径求值输入

	/// <summary>authoring 投影（左缘坐标 + 秒）→ 路径求值输入（中心坐标 + 精确 BarTime）。
	/// 没有 node 的 Hold/Mixer 草稿补一个末端点，保持放置幽灵的头部—尾部连线行为。</summary>
	private EditorPathShape PathShape(AuthoringNote note)
	{
		var nodes = new EditorPathPoint[note.Nodes.Count];
		for (var index = 0; index < nodes.Length; index++)
		{
			var node = note.Nodes[index];
			nodes[index] = new EditorPathPoint(node.TimeExact, node.Center + node.Width * .5d,
				node.Width, node.CurveToNext);
		}
		if (nodes.Length == 0 && note.Kind is AuthoringNoteKind.Hold or AuthoringNoteKind.Mixer)
			nodes = [new EditorPathPoint(note.EndExact, note.Center + note.Width * .5d, note.Width, null)];
		return new EditorPathShape(new EditorPathPoint(note.TimeExact, note.Center + note.Width * .5d,
			note.Width, note.CurveToNext), nodes);
	}

	/// <summary>文档真源 → 路径求值输入。插入节点取样走这一份（文档坐标不钳制、精确 BarTime
	/// 就是节点时间），保证写回的值逐位落在路径上。</summary>
	private static EditorPathShape PathShape(EditableNote note) => EditorPathProjection.ShapeOf(note);

	private static (double Position, double Width) ProjectionScale(int side) => side == 0
		? (GameplayStageGeometry.CenterUnitPx, GameplayStageGeometry.CenterUnitPx)
		: (GameplayStageGeometry.SideUnitPx, SideNoteWidthUnitPx);

	private static double ProjectionUnit(int side) =>
		side == 0 ? GameplayStageGeometry.CenterUnitPx : GameplayStageGeometry.SideUnitPx;

	// ---------------------------------------------------------------- 真实路径 body

	/// <summary>
	/// 一条 Hold/Mixer 的运行时 Note 链：头 + 逐段曲线细分采样 + 每个控制节点，SubNoteId 指向
	/// 下一点、末点为 -1。形状与实机 <c>V2RuntimeAdapter</c> 的展开同形，因此共享
	/// <see cref="GameplayStageRenderer"/> 的逐段带状渲染就是实机观感；非路径 Note 只有头一个点。
	/// <paramref name="headOnly"/> 为 true 时只输出头一个点：Hold 拖尾阶段的头按已放置实体外观画、
	/// body 由前景层的虚线预览代表，不能让共享渲染器同时把实心链接画出来。
	/// </summary>
	private List<ChartNote> BuildRuntimeChain(AuthoringNote note, int headId, bool headOnly = false)
	{
		var scale = ProjectionScale(note.Side);
		IReadOnlyList<EditorPathSample> samples =
			!headOnly && note.Kind is AuthoringNoteKind.Hold or AuthoringNoteKind.Mixer
				? EditorPathProjection.Flatten(PathShape(note), scale.Position, scale.Width)
				: [new EditorPathSample(note.TimeExact, note.Center + note.Width * .5d, note.Width, true)];

		var chain = new List<ChartNote>(samples.Count);
		for (var index = 0; index < samples.Count; index++)
		{
			var sample = samples[index];
			var second = EditorTime.BarToSeconds(sample.Time, CurrentBpms);
			chain.Add(new ChartNote
			{
				Id = headId + index,
				SubNoteId = index + 1 < samples.Count ? headId + index + 1 : -1,
				Type = index == 0 ? HeadRuntimeType(note.Kind) : NodeRuntimeType(note.Kind),
				Track = RuntimeTrack(note.Side),
				// BarTime 是谱面小节坐标（runtime 会再经 BarTimeToSeconds 换算），
				// 这里存采样点的小节数，不能填 seconds。
				BarTime = sample.Time.ToDouble(),
				Position = sample.Center - sample.Width * .5d,
				Width = sample.Width,
				Second = second,
				BakedSecond = second,
				SyncNote = 0,
			});
		}
		return chain;
	}

	/// <summary>路径身体命中（设计稿 §4 第 3 条「点身体选整个 Note」）：把光标换算到谱面
	/// (time, center)，再与路径求值结果做带状比较；时间方向只要求落在路径区间内，宽度方向按
	/// 该处采样宽度 + 6px 宽容，因此宽长条的身体可点、判定不会沿时间轴无限外扩。</summary>
	private bool HitPathBody(int noteIndex, Vector2 p)
	{
		if (noteIndex < 0 || noteIndex >= Notes.Count) return false;
		var note = Notes[noteIndex];
		if (note.Kind is not (AuthoringNoteKind.Hold or AuthoringNoteKind.Mixer) || note.Nodes.Count == 0)
			return false;
		var place = Place(p, note.Side);
		var seconds = Clamp(place.Time, 0, DurationSeconds);
		if (seconds <= note.Time || seconds >= note.EndTime) return false;
		var sample = EditorPathProjection.Sample(PathShape(note),
			EditorTime.SecondsToBar(seconds, CurrentBpms));
		return Math.Abs(place.Center - sample.Center) <=
			sample.Width * .5d + PathBodySlackPx / ProjectionUnit(note.Side);
	}

	// ---------------------------------------------------------------- 语境与两级选中

	/// <summary>路径编辑语境下的单选目标（Select 工具 + 单选一条 Hold/Mixer + 没有放置/框选）。
	/// <paramref name="includeInsertDrag"/> 为 true 时插入节点的宽度拖拽期间也算，这样拖宽度时
	/// 整条路径仍然可见。</summary>
	private (int Index, AuthoringNote Note)? PathTarget(bool includeInsertDrag)
	{
		if (_tool != 4 || _draft is not null || _marqueeActive) return null;
		if (_gesture is not null && !(includeInsertDrag && _gesture.RelativeWidth)) return null;
		if (_selectedNotes.Count != 1) return null;
		var index = FirstSelectedNote();
		if (index < 0 || index >= Notes.Count) return null;
		var note = Notes[index];
		return note.Kind is AuthoringNoteKind.Hold or AuthoringNoteKind.Mixer ? (index, note) : null;
	}

	/// <summary>指针命中的菱形手柄（<c>|dx|+|dy| ≤ half</c>，即菱形而不是外接矩形）。</summary>
	private (int NoteIndex, int PointIndex)? PathHandleAt(Vector2 p)
	{
		if (PathTarget(false) is not { } target) return null;
		var note = target.Note;
		if (InDiamond(HeadHandleCenter(note), HeadHandleSize, p)) return (target.Index, 0);
		for (var index = 0; index < note.Nodes.Count; index++)
			if (InDiamond(NodeHandleCenter(note, index), NodeHandleSize, p))
				return (target.Index, index + 1);
		return null;
	}

	private Vector2 HeadHandleCenter(AuthoringNote note)
	{
		var rect = NoteRect(note.Side, note.Time, note.Center, note.Width, HeadRuntimeType(note.Kind));
		return rect.Position + rect.Size * .5f;
	}

	private Vector2 NodeHandleCenter(AuthoringNote note, int nodeIndex)
	{
		var node = note.Nodes[nodeIndex];
		var rect = NoteRect(note.Side, node.Time, node.Center, node.Width, NodeRuntimeType(note.Kind));
		return rect.Position + rect.Size * .5f;
	}

	private static bool InDiamond(Vector2 center, float size, Vector2 p) =>
		Math.Abs(p.X - center.X) + Math.Abs(p.Y - center.Y) <= size * .5f + 2f;

	private int FindNoteIndex(string id)
	{
		for (var index = 0; index < Notes.Count; index++)
			if (Notes[index].Id == id) return index;
		return -1;
	}

	private EditableNote? FindDocumentNote(string id) =>
		_document?.SelectedChart.Notes.FirstOrDefault(note => note.Id == id);

	/// <summary>返回是否需要重绘：手柄 hover 由 60% 提到 100%（ui-motion §9.1）。</summary>
	private bool UpdatePathHandleHover(Vector2 p)
	{
		var next = PathHandleAt(p);
		if (next == _hoverHandle) return false;
		_hoverHandle = next;
		return true;
	}

	// ---------------------------------------------------------------- 双击插入节点

	/// <summary>双击路径身体插入控制节点（设计稿 §4 第 2 条）：双击点定 time，center/width 取
	/// 路径求值结果（形状零跳变），插入后新节点即选中并直接进入宽度拖拽。返回 false 表示这不是
	/// 一次插入手势，调用方仍按普通点击处理（双击空白处、头部、手柄都不插入）。</summary>
	private bool TryInsertPathNodeAt(Vector2 p)
	{
		if (_tool != 4 || _side == 3 || _draft is not null || _gesture is not null || _marqueeActive)
			return false;
		var hit = HitTest(p);
		if (hit.NoteIndex < 0 || hit.NodeIndex >= 0) return false;
		var note = Notes[hit.NoteIndex];
		if (!HitPathBody(hit.NoteIndex, p)) return false;
		var time = ExactTimeAt(SnapTime(Place(p, note.Side).Time));
		if (!CanInsertPathNode(note, time)) return false;
		if (InsertPathNode(note, time) is not { } inserted) return false;
		BeginNodeWidthDrag(p, inserted.NoteIndex, inserted.NodeId);
		return true;
	}

	/// <summary>插入点是否合法：游标严格落在头与尾之间，且不与既有节点同刻（同刻单击即可选中，
	/// 不需要再插一个零长度区间）。</summary>
	private static bool CanInsertPathNode(AuthoringNote note, ExactBarTime time) =>
		note.Kind is AuthoringNoteKind.Hold or AuthoringNoteKind.Mixer &&
		note.Nodes.Count > 0 &&
		time > note.TimeExact && time < note.EndExact &&
		!note.Nodes.Any(node => node.TimeExact == time);

	/// <summary>区间左端的出向曲线种类：插入点继承它，后半段保持同一种曲线语言；
	/// linear 不写值（v2 省略即线性）。</summary>
	private static V2PathCurve? IntervalCurve(EditableNote note, ExactBarTime time)
	{
		var curve = note.CurveToNext ?? V2PathCurve.Linear;
		foreach (var node in note.Nodes)
		{
			if (node.Time >= time) break;
			curve = node.CurveToNext ?? V2PathCurve.Linear;
		}
		return curve == V2PathCurve.Linear ? null : curve;
	}

	/// <summary>在 <paramref name="time"/> 处插入控制节点：center/width 采样文档路径的求值结果
	/// （因此插入后路径在该时刻的值与插入前逐位相同），命令层负责严格递增与 v2 校验。</summary>
	private (int NoteIndex, string NodeId)? InsertPathNode(AuthoringNote note, ExactBarTime time)
	{
		if (_controller is null) return null;
		var source = FindDocumentNote(note.Id);
		if (source is null) return null;
		var sample = EditorPathProjection.Sample(PathShape(source), time);
		var nodeId = _controller.AddPathNode(note.Id, time, sample.Center, sample.Width,
			IntervalCurve(source, time));
		SyncFromDocument();
		var index = FindNoteIndex(note.Id);
		if (index < 0) return null;
		// 两级选中：第二级直接落到新节点上，右键菜单据此进节点态。
		_selectedNotes.Clear();
		_selectedNotes.Add(index);
		_selectedNote = index;
		_selectedEvent = -1;
		_selectedNodeId = nodeId;
		SelectionChanged?.Invoke(index);
		EmitStatus($"NODE INSERTED · {ExactBarTimeText.Format(time)}");
		QueueRedraw();
		return (index, nodeId);
	}

	/// <summary>第二击不松手直接进宽度拖拽：新节点宽度以采样值为基准做相对拖拽（左缘不动，
	/// 1:1 跟手、0.05 步进），起手值等于采样宽度，所以拖拽第一帧不会跳变。</summary>
	private void BeginNodeWidthDrag(Vector2 pointer, int noteIndex, string nodeId)
	{
		if (noteIndex < 0 || noteIndex >= Notes.Count) return;
		var note = Notes[noteIndex];
		var nodeIndex = note.Nodes.FindIndex(node => node.Id == nodeId);
		if (nodeIndex < 0) return;
		var node = note.Nodes[nodeIndex];
		var place = Place(pointer, note.Side);
		_gesture = new Gesture
		{
			Kind = GestureKind.ResizeNode,
			NoteIndex = noteIndex,
			NodeIndex = nodeIndex,
			StartPoint = new System.Numerics.Vector2(pointer.X, pointer.Y),
			Anchor = new CanvasDragAnchor(node.TimeExact, node.Time,
				node.Center + node.Width * .5d, place.Time, place.Center),
			OriginalCenter = note.Center,
			OriginalWidth = note.Width,
			OriginalNodeCenter = node.Center,
			OriginalNodeWidth = node.Width,
			ResizeNodeWidth = true,
			RelativeWidth = true,
			WidthPointer = SnapSpatial(place.Center),
		};
		_rememberedWidth = node.Width;
		_stageDirty = true;
		EmitStatus("NODE INSERTED · DRAG WIDTH, RELEASE TO COMMIT");
		QueueRedraw();
	}

	/// <summary>右键菜单 ADD NODE 的可用性：单选路径 + 游标落在路径内部。</summary>
	public bool CanInsertPathNodeAtCursor =>
		TrySelectedPathNote(out var note) && CanInsertPathNode(note, ExactTimeAt(_cursorTime));

	/// <summary>右键菜单 ADD NODE：在当前游标时刻插入控制节点。双击路径身体是同一入口的
	/// 手势形式，这一行是键盘/菜单形式。</summary>
	public void InsertPathNodeAtCursor()
	{
		if (!TrySelectedPathNote(out var note))
		{
			EmitStatus("ADD NODE NEEDS A SELECTED HOLD/MIXER");
			return;
		}
		var time = ExactTimeAt(_cursorTime);
		if (!CanInsertPathNode(note, time))
		{
			EmitStatus("ADD NODE REJECTED · CURSOR IS OUTSIDE THE PATH");
			return;
		}
		if (InsertPathNode(note, time) is null)
			EmitStatus("ADD NODE REJECTED");
		QueueRedraw();
	}

	private bool TrySelectedPathNote(out AuthoringNote note)
	{
		note = null!;
		if (_selectedNotes.Count != 1) return false;
		var index = FirstSelectedNote();
		if (index < 0 || index >= Notes.Count) return false;
		var candidate = Notes[index];
		if (candidate.Kind is not (AuthoringNoteKind.Hold or AuthoringNoteKind.Mixer)) return false;
		note = candidate;
		return true;
	}

	// ---------------------------------------------------------------- PATH_FIELD 绘制

	/// <summary>选中路径的菱形手柄 + 虚线 spine。头手柄是实心类型色（24px，
	/// 描边取设计稿 svg_diamond 的「类型浅色顶边」，PATH_FIELD 里 Hold = #FFF2D1），其余是 panel 底
	/// 加类型浅色边描边（18px，Hold = #FFCC59），选中节点换 cyan 实心菱形并在外面套
	/// 32×32 切角 8 的 cyan 70% 环。</summary>
	private void DrawPathHandles(int noteIndex, AuthoringNote note)
	{
		var centers = new List<Vector2>(note.Nodes.Count + 1) { HeadHandleCenter(note) };
		for (var index = 0; index < note.Nodes.Count; index++)
			centers.Add(NodeHandleCenter(note, index));

		for (var index = 1; index < centers.Count; index++)
			DrawDashedSegment(this, centers[index - 1], centers[index], new Color(SpineColor, SpineAlpha), SpineWidth);

		var surface = EditorOverlayDraw.Surface(note.Kind);
		var panelFill = new Color(UiFonts.Panel.R, UiFonts.Panel.G, UiFonts.Panel.B, 1f);
		for (var index = 0; index < centers.Count; index++)
		{
			var selected = index > 0 && note.Nodes[index - 1].Id == _selectedNodeId;
			var hovered = _hoverHandle == (noteIndex, index);
			var alpha = selected ? 1f : hovered ? HandleHoverAlpha : HandleBaseAlpha;
			if (index == 0)
			{
				DrawHandleDiamond(centers[index], HeadHandleSize, surface.Base, surface.Top, alpha);
				continue;
			}
			var fill = selected ? UiFonts.Cyan : panelFill;
			var stroke = selected ? Colors.White : surface.Border;
			DrawHandleDiamond(centers[index], NodeHandleSize, fill, stroke, alpha);
			if (!selected) continue;
			var rect = new Rect2(centers[index] - new Vector2(SelectedRingSize * .5f, SelectedRingSize * .5f),
				new Vector2(SelectedRingSize, SelectedRingSize));
			EditorOverlayDraw.CutStroke(this, rect, SelectedRingCut, new Color(UiFonts.Cyan, .7f), 2f);
		}
		DrawMixerTicks(note);
	}

	/// <summary>Mixer 的 1/8 小节判定网格（设计稿 PATH_FIELD 的 tick 环）：沿 body 的 shared
	/// <see cref="V2MixerTicks"/> 网格取每个 tick 时刻，位置用路径求值器在该时刻的 center，
	/// 画 18×2 的 #FF9CC4 75% 短横（中轨时间沿 y、宽度沿 x；侧轨交换两轴）。</summary>
	private void DrawMixerTicks(AuthoringNote note)
	{
		if (note.Kind != AuthoringNoteKind.Mixer || note.Nodes.Count == 0) return;
		// tick 时刻只由头/尾决定（V2MixerTicks.TickCount 读的也是这两个值），所以这里不需要
		// 完整的节点表；路径求值仍走编辑器的 PathShape。
		var shape = PathShape(note);
		var source = new V2PathNote
		{
			Id = note.Id, Type = V2NoteType.Mixer, Time = note.TimeExact,
			Center = note.Center + note.Width * .5d, Width = note.Width,
			Nodes = [new V2PathNode { Id = note.Id, Time = note.EndExact, Center = note.Center + note.Width * .5d, Width = note.Width }],
		};
		var color = new Color(MixerTickColor, MixerTickAlpha);
		foreach (var tick in V2MixerTicks.Enumerate(source))
		{
			var sample = EditorPathProjection.Sample(shape, tick);
			var seconds = EditorTime.BarToSeconds(tick, CurrentBpms);
			var center = NoteRect(note.Side, seconds, sample.Center - sample.Width * .5d, sample.Width,
				NodeRuntimeType(note.Kind));
			var origin = center.Position + center.Size * .5f;
			DrawRect(note.Side == 0
				? new Rect2(origin.X - MixerTickLength * .5f, origin.Y - MixerTickThickness * .5f, MixerTickLength, MixerTickThickness)
				: new Rect2(origin.X - MixerTickThickness * .5f, origin.Y - MixerTickLength * .5f, MixerTickThickness, MixerTickLength), color);
		}
	}

	private void DrawHandleDiamond(Vector2 center, float size, Color fill, Color stroke, float alpha)
	{
		var half = size * .5f - 1f;
		var points = new[]
		{
			center + new Vector2(0f, -half),
			center + new Vector2(half, 0f),
			center + new Vector2(0f, half),
			center + new Vector2(-half, 0f),
		};
		DrawColoredPolygon(points, new Color(fill.R, fill.G, fill.B, alpha));
		DrawPolyline([points[0], points[1], points[2], points[3], points[0]],
			new Color(stroke.R, stroke.G, stroke.B, stroke.A * alpha), 2f, true);
	}

	/// <summary>设计稿 dasharray 7 6 的虚线（Godot 的 DrawDashedLine 只能等长间隔）。</summary>
	private void DrawDashedSegment(CanvasItem target, Vector2 from, Vector2 to, Color color, float width) =>
		DrawDashedSegment(target, from, to, color, width, SpineDash, SpineGap);

	/// <summary>自定义 dash/gap 的虚线：放置手势的幽灵描边（9 6）、拖尾预览与尾端菱形
	/// （7 6 / 5 4）走这一档，spine 用上面的 7 6 默认值。<paramref name="target"/> 是正在
	/// <c>_draw</c> 的节点——画布自己的层与压在 Note 之上的前景层共用这一处绘制。</summary>
	private void DrawDashedSegment(CanvasItem target, Vector2 from, Vector2 to, Color color, float width,
		float dash, float gap)
	{
		var length = from.DistanceTo(to);
		if (length <= .01f) return;
		var direction = (to - from) / length;
		var position = 0f;
		while (position < length)
		{
			var end = Math.Min(position + dash, length);
			target.DrawLine(from + direction * position, from + direction * end, color, width, true);
			position = end + gap;
		}
	}

	/// <summary>双击插入的宽度拖拽幽灵（PATH_FIELD 的 ghost-node / ghost-width-drag）：
	/// 过幽灵中心的位置导引线 + 16px 深底 cyan 菱形 + 双向宽度箭头；两行读数在前景层
	/// （<see cref="DrawPathInsertReadouts"/>），否则会被 shared 舞台的 Note 盖住。</summary>
	private void DrawPathInsertGhost()
	{
		if (!TryInsertGhost(out var note, out _, out var center)) return;
		// 中轨时间轴向下、宽度轴向右；侧轨相反（时间沿 x、宽度沿 y）。
		var timeAxis = note.Side == 0 ? Vector2.Down : Vector2.Right;
		var widthAxis = note.Side == 0 ? Vector2.Right : Vector2.Up;

		DrawPathInsertGuideLine(note.Side, center);
		DrawHandleDiamond(center, GhostNodeSize, new Color("0a0e1a"), UiFonts.Cyan, .9f);
		var arrowCenter = center + timeAxis * GhostArrowOffset;
		var half = GhostArrowWidth * .5f;
		var tail = arrowCenter + widthAxis * (half - 8f);
		var head = arrowCenter - widthAxis * (half - 8f);
		DrawLine(head, tail, UiFonts.Cyan, 2f, true);
		DrawColoredPolygon([head, head + widthAxis * 10f + timeAxis * 5f, head + widthAxis * 10f - timeAxis * 5f],
			UiFonts.Cyan);
		DrawColoredPolygon([tail, tail - widthAxis * 10f + timeAxis * 5f, tail - widthAxis * 10f - timeAxis * 5f],
			UiFonts.Cyan);
		DrawLine(arrowCenter - widthAxis * 5f, arrowCenter + widthAxis * 5f, UiFonts.Cyan, 2f, true);
	}

	/// <summary>插入幽灵位置轴上的时间导引线（设计稿 PATH_FIELD 的 (975,140,2×720)）：2px 白 35%，
	/// 贯穿该轨可放区间的全高段。中轨在幽灵中心的 x 上竖直画、侧轨按轴交换转横——与放置手势那条
	/// 白色位置线同一语义，因此都画在 Note 之下。</summary>
	private void DrawPathInsertGuideLine(int side, Vector2 center)
	{
		var color = new Color(Colors.White, InsertGuideAlpha);
		if (side == 0)
		{
			DrawRect(new Rect2(center.X - InsertGuideWidth * .5f, CenterTop,
				InsertGuideWidth, CenterJudgeY - CenterTop), color);
			return;
		}
		var extent = GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale;
		var from = side == 1 ? LeftJudgeX : RightJudgeX - extent;
		DrawRect(new Rect2(from, center.Y - InsertGuideWidth * .5f, extent, InsertGuideWidth), color);
	}

	/// <summary>插入幽灵的两行读数（画面层压在 Note 之上的 PlacementChrome）：板面几何——中轨贴在
	/// 幽灵上下（左缘 = 幽灵 −103px），侧轨放到幽灵右侧。</summary>
	internal void DrawPathInsertReadouts(CanvasItem target)
	{
		if (!TryInsertGhost(out var note, out var node, out var center)) return;
		var text = $"{ExactBarTimeText.Format(node.TimeExact)} · x{node.Center + node.Width * .5d:0.00}";
		if (note.Side == 0)
		{
			var left = center.X - GhostTextInset;
			EditorOverlayDraw.LeftText(target, ChipFont, new Vector2(left, center.Y - GhostTextGap), text, EditorFonts.Pt(14), UiFonts.Cyan);
			EditorOverlayDraw.LeftText(target, ChipFont, new Vector2(left, center.Y + GhostArrowOffset + 22f),
				$"w {node.Width:0.00}", EditorFonts.Pt(12), UiFonts.Dim);
			return;
		}
		EditorOverlayDraw.LeftText(target, ChipFont, new Vector2(center.X + 16f, center.Y - 10f), text, EditorFonts.Pt(14), UiFonts.Cyan);
		EditorOverlayDraw.LeftText(target, ChipFont, new Vector2(center.X + 16f, center.Y + 16f),
			$"w {node.Width:0.00}", EditorFonts.Pt(12), UiFonts.Dim);
	}

	/// <summary>当前插入幽灵的目标（被插入的路径 + 新节点 + 屏幕中心）；不在插入拖拽时返回 false。</summary>
	private bool TryInsertGhost(out AuthoringNote note, out AuthoringPathNode node, out Vector2 center)
	{
		note = null!;
		node = null!;
		center = Vector2.Zero;
		if (_gesture is not { RelativeWidth: true } gesture) return false;
		if (gesture.NoteIndex < 0 || gesture.NoteIndex >= Notes.Count) return false;
		var candidate = Notes[gesture.NoteIndex];
		if (gesture.NodeIndex < 0 || gesture.NodeIndex >= candidate.Nodes.Count) return false;
		note = candidate;
		node = candidate.Nodes[gesture.NodeIndex];
		var rect = NoteRect(note.Side, node.Time, node.Center, node.Width, NodeRuntimeType(note.Kind));
		center = rect.Position + rect.Size * .5f;
		return true;
	}

	/// <summary>路径节点编辑态（设计稿 PATH_FIELD 的顶中 `PATH EDIT` 标签）：Select 工具 +
	/// 单选一条 Hold/Mixer；双击插入的宽度拖拽期间也算，这样进入编辑态后标签不会在拖拽中途闪掉。</summary>
	public bool PathEditActive => PathTarget(true) is not null;
}

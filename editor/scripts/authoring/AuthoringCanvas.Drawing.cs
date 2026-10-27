using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using ChartNote = DynamiteUniverse.Shared.Chart.Note;

namespace DynamiteUniverse.Editor;

public partial class AuthoringCanvas
{

	public override void _Draw()
	{
		DrawGridLines();
		DrawBarNumbers();
		DrawBarRuler();
		DrawProgressLine();
		DrawPlacementGuides();
		DrawBpmEvents();
		DrawSelectionOverlay();
		DrawSelectionChip();
	}

	private void DrawSelectionOverlay()
	{
		var pathTarget = PathTarget(true);
		for (var index = 0; index < Notes.Count; index++)
		{
			var note = Notes[index];
			if (!_selection.NoteIds.Contains(note.Id)) continue;
			var pathEdit = pathTarget is { } target && target.Index == index && note.Nodes.Count > 0;
			DrawSelectionHandle(NoteRect(note.Side, note.Time, note.Center, note.Width, HeadRuntimeType(note.Kind)));
			if (pathEdit)
				DrawPathHandles(index, note);
			else foreach (var node in note.Nodes)
				DrawSelectionHandle(NoteRect(note.Side, node.Time, node.Center, node.Width, NodeRuntimeType(note.Kind)));
		}
		if (_selection.MarqueeActive && _selection.MarqueeMoved)
		{
			var rect = new Rect2(_marqueeStart, Vector2.Zero).Expand(_marqueeCurrent);
			DrawRect(rect, new Color(0.2f, 0.85f, 1f, .12f), true);
			DrawRect(rect, new Color(0.2f, 0.85f, 1f, .9f), false, 2f);
		}
		DrawPathInsertGhost();
	}

	// The handle is the runtime note rect grown by the design's 6px bleed, so the authoring outline
	// cannot drift from the size the shared renderer draws. 设计稿 MAIN_SELECTION 的 sel-outline 是
	// cyan 2px 且只在 Note 外圈，没有中心圆点；路径编辑语境的节点手柄改用 PATH_FIELD 的菱形
	// （DrawPathHandles）。
	private void DrawSelectionHandle(Rect2 rect) =>
		DrawRect(rect.Grow(SelectionOutlineGrow), new Color(UiFonts.Cyan, .95f), false, 2f);
	private void DrawGridLines()
	{
		var bpms = CurrentBpms;
		// Only enumerate the time span that can project onto the fixed stage.
		var firstSecond = Math.Max(0d, CursorTime - 49d / PixelsPerSecond);
		var lastSecond = Math.Min(DurationSeconds,
			CursorTime + GameplayStageGeometry.SidePreviewTravelPx / PixelsPerSecond);
		var firstBar = EditorTime.SecondsToBar(firstSecond, bpms);
		var lastBar = EditorTime.SecondsToBar(lastSecond, bpms);
		var first = (long)Math.Floor(firstBar.ToDouble() * GridDivisor) - 1;
		var last = (long)Math.Ceiling(lastBar.ToDouble() * GridDivisor) + 1;
		var centerAlpha = VisibilityAlpha(0);
		var leftAlpha = VisibilityAlpha(1);
		var rightAlpha = VisibilityAlpha(2);
		// 三档共用同一条点阵：小节线 / 拍线 / 细分线。屏幕密度不足时逐档退让，
		// 最差只留小节线，不会把细分线画成一片实色。
		var beatsPerBar = GridDivisor % 4 == 0 ? GridDivisor / 4 : 0;
		var subStepPx = BarSecondsAt(CursorTime) / GridDivisor * PixelsPerSecond;
		var drawSubs = subStepPx >= MinimumGridStepPx;
		var drawBeats = beatsPerBar > 0 && subStepPx * beatsPerBar >= MinimumGridStepPx;
		var sideExtent = GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale;
		for (var i = first; i <= last && i - first <= 10000; i++)
		{
			var gridTime = EditorTime.BarToSeconds(ExactBarTime.FromFraction(i, GridDivisor), bpms);
			if (gridTime < firstSecond - .001d || gridTime > lastSecond + .001d) continue;
			GridTier? tier = null;
			if (i % GridDivisor == 0) tier = BarTier;
			else if (drawBeats && i % beatsPerBar == 0) tier = BeatTier;
			else if (drawSubs) tier = SubTier;
			if (tier is not { } line) continue;
			if (centerAlpha > 0f)
			{
				var axis = AxisAt(0, gridTime);
				if (axis >= CenterTop && axis <= CenterJudgeY)
					DrawTieredLine(true, CenterX, CenterX + CenterWidth, axis, line, centerAlpha);
			}
			if (leftAlpha > 0f)
			{
				var left = AxisAt(1, gridTime);
				if (left >= LeftJudgeX && left <= LeftJudgeX + sideExtent)
					DrawTieredLine(false, SideTop, SideTop + SideHeight, left, line, leftAlpha);
			}
			if (rightAlpha > 0f)
			{
				var right = AxisAt(2, gridTime);
				if (right <= RightJudgeX && right >= RightJudgeX - sideExtent)
					DrawTieredLine(false, SideTop, SideTop + SideHeight, right, line, rightAlpha);
			}
		}
	}

	/// <summary>一档网格线 = 线芯 + 贴身软光。设计稿的软光是线段自身附近 8px（小节）/4px（拍）
	/// 的白→透明渐变，这里用几条半透明带近似，越靠近线芯越亮，不做整屏距离渐隐。
	/// 三档一起乘设置里的 GRID OPACITY 倍率（100 = 设计稿浓度）。</summary>
	private void DrawTieredLine(bool horizontal, float start, float end, float axis, GridTier tier, float alphaScale)
	{
		var opacity = EditorSettings.Current.GridOpacityScale;
		if (tier.GlowHeight > 0f)
		{
			var strip = tier.GlowHeight / GlowSteps;
			var first = axis + tier.CoreWidth * .5f - tier.GlowHeight * .5f;
			for (var k = 0; k < GlowSteps; k++)
			{
				var offset = first + k * strip;
				var alpha = tier.GlowAlpha * (k + .5f) / GlowSteps * alphaScale * opacity;
				DrawRect(horizontal ? new Rect2(start, offset, end - start, strip)
					: new Rect2(offset, start, strip, end - start), new Color(UiFonts.Text, alpha));
			}
		}
		var core = tier.CoreAlpha * alphaScale * opacity;
		if (core <= 0f) return;
		DrawRect(horizontal ? new Rect2(start, axis, end - start, tier.CoreWidth)
			: new Rect2(axis, start, tier.CoreWidth, end - start), new Color(UiFonts.Text, core));
	}

	/// <summary>小节号紧贴小节线右端（设计稿列 x=1450，11px dim）；密度不足时按 2 的倍数稀疏。</summary>
	private void DrawBarNumbers()
	{
		var bpms = CurrentBpms;
		var firstSecond = Math.Max(0d, CursorTime - 49d / PixelsPerSecond);
		var lastSecond = Math.Min(DurationSeconds,
			CursorTime + GameplayStageGeometry.SidePreviewTravelPx / PixelsPerSecond);
		var firstBar = Math.Max(0L, (long)Math.Floor(EditorTime.SecondsToBar(firstSecond, bpms).ToDouble()));
		var lastBar = (long)Math.Ceiling(EditorTime.SecondsToBar(lastSecond, bpms).ToDouble());
		var step = 1L;
		var barStepPx = BarSecondsAt(CursorTime) * PixelsPerSecond;
		while (step < 4096L && barStepPx * step < BarNumberSpacingPx) step *= 2;
		for (var bar = firstBar / step * step; bar <= lastBar; bar += step)
		{
			var seconds = EditorTime.BarToSeconds(ExactBarTime.FromFraction(bar, 1), bpms);
			var axis = AxisAt(0, seconds);
			if (axis < CenterTop || axis > CenterJudgeY) continue;
			DrawString(DecorFont, new Vector2(BarNumberX, axis + BarNumberBaseline), bar.ToString(CultureInfo.InvariantCulture),
				HorizontalAlignment.Left, -1, BarNumberSize, UiFonts.Dim);
		}
	}

	/// <summary>右缘小节标尺：整曲缩略时间轴（小节刻度 + 当前位置三角）。
	/// 书签菱形本轮不画，v2 文档里还没有书签数据。</summary>
	private void DrawBarRuler()
	{
		var totalBars = TotalBars();
		var height = BarRulerBottom - BarRulerTop;
		DrawRect(new Rect2(BarRulerX, BarRulerTop, BarRulerWidth, height), new Color(UiFonts.Text, .12f));
		var pxPerBar = height / totalBars;
		var drawMinor = pxPerBar >= 3.5d;
		var drawMid = pxPerBar * 4d >= MinimumGridStepPx;
		for (var bar = 0; bar <= totalBars; bar++)
		{
			var major = bar % 16 == 0;
			var mid = bar % 4 == 0;
			if (!major && !(mid ? drawMid : drawMinor)) continue;
			var length = major ? 14f : mid ? 9f : 5f;
			var alpha = major ? .5f : mid ? .3f : .15f;
			var axis = BarRulerBottom - (float)(bar / (double)totalBars) * height;
			DrawRect(new Rect2(BarRulerX - length, axis, length, 1.5f), new Color(UiFonts.Text, alpha));
		}
		var nowAxis = BarRulerBottom - (float)Math.Clamp(CursorBar / totalBars, 0d, 1d) * height;
		// 设计稿 stage_furniture 的 tri（M10 0 L0 7 L10 14）：底边压在标尺上、尖朝左（朝舞台）。
		DrawColoredPolygon(new[]
		{
			new Vector2(BarRulerX - 2f, nowAxis - 7f),
			new Vector2(BarRulerX - 2f, nowAxis + 7f),
			new Vector2(BarRulerX - 12f, nowAxis),
		}, UiFonts.Cyan);
	}

	/// <summary>底部 2px 进度细线：白 10% 底 + cyan 已播段（设计稿 y=1076，全宽）。</summary>
	private void DrawProgressLine()
	{
		DrawRect(new Rect2(0f, ProgressLineY, DesignWidth, ProgressLineHeight), new Color(UiFonts.Text, .10f));
		var progress = DurationSeconds <= 0d ? 0d : Math.Clamp(CursorTime / DurationSeconds, 0d, 1d);
		if (progress <= 0d) return;
		DrawRect(new Rect2(0f, ProgressLineY, DesignWidth * (float)progress, ProgressLineHeight), UiFonts.Cyan);
	}
	/// <summary>BPM 事件（设计稿 MAIN_FIELD 的 bpm-event）：x=280 起 1216 宽的 2px #FF6BB4 横线加
	/// 线右端 6px 处的 15px bold 纯数值读数（板面 `180.0`）。长度按板面取 1216 —— 编辑器自己的网格
	/// 是有记录地画到 1366（`CenterWidth`）的，这里不跟着改；要跟网格同宽只需改这一处常量。
	/// 选中只用不透明度区分（全局「状态=亮度变化」的语言），几何不随选中改变。</summary>
	private void DrawBpmEvents()
	{
		for (var index = 0; index < BpmEvents.Count; index++)
		{
			var e = BpmEvents[index];
			var y = AxisAt(0, e.Time);
			if (y < CenterTop - 20 || y > CenterJudgeY + 60) continue;
			var alpha = _selection.EventTime == e.TimeExact ? 1f : BpmEventLineAlpha;
			var color = new Color(BpmEventColor, alpha);
			DrawRect(new Rect2(CenterX, y, BpmEventSpanWidth, BpmEventLineWidth), color);
			EditorOverlayDraw.LeftText(this, ChipFont,
				new Vector2(CenterX + BpmEventSpanWidth + BpmEventValueGap, y + BpmEventLineWidth * .5f),
				e.Bpm.ToString("0.0", CultureInfo.InvariantCulture), BpmEventValueSize, color);
		}
	}

	private void RefreshGameplayStage()
	{
		if (_gameplayStage is null) return;
		_gameplayNotes.Clear();
		var ghostIds = new HashSet<int>();
		foreach (var note in Notes)
			AppendGameplayNote(note);
		if (_draft is not null)
		{
			// 拖尾阶段头按已放置实体外观画（设计板 HOLD · TAIL DRAG）：不标成幽灵、不展开成
			// body 链接，尾端由前景层的虚线路径预览 + 虚线菱形幽灵表示。
			var tailStage = _draft.TailStage;
			var draft = new AuthoringNote
			{
				Id = "__draft__", Kind = _draft.Kind, Side = _draft.Side,
				Time = _draft.Time, EndTime = _draft.EndTime,
				Center = _draft.Center, Width = _draft.Width,
				// 草稿只有显示秒，路径求值需要精确 BarTime；这里补上与显示同一套 BPM 换算的值。
				TimeExact = BarAt(_draft.Time), EndExact = BarAt(_draft.EndTime),
			};
			var ghostStart = _gameplayNotes.Count;
			AppendGameplayNote(draft, tailStage);
			if (!tailStage)
				for (var i = ghostStart; i < _gameplayNotes.Count; i++)
					ghostIds.Add(_gameplayNotes[i].Id);
		}
		else if (BuildHoverGhost() is { } hover)
		{
			var ghostStart = _gameplayNotes.Count;
			AppendGameplayNote(hover);
			for (var i = ghostStart; i < _gameplayNotes.Count; i++)
				ghostIds.Add(_gameplayNotes[i].Id);
		}
		_gameplayStage.SetGhostNoteIds(ghostIds);
		_gameplayStage.SetNotes(_gameplayNotes);
		_gameplayStage.SetPreviewTime(_cursorTime);
		_stageDirty = false;
	}

	private bool CanBuildHoverGhost() =>
		_pointerInside && _draft is null && _gesture is null && !_selection.MarqueeActive &&
		_side != 3 && _tool is >= 1 and <= 3 && PointInsideTrack(_pointerPoint, _side);

	private AuthoringNote? BuildHoverGhost()
	{
		if (!CanBuildHoverGhost()) return null;
		var place = Place(_pointerPoint, _side);
		var anchor = SnapSpatial(place.Center);
		var width = Clamp(EditorStagePlacement.SnapWidth(_rememberedWidth), .1, MaxChartCoordinate);
		var left = Clamp(anchor - width * .5, 0, MaxChartCoordinate - width);
		var time = SnapTime(place.Time);
		if (_tool == 3) time = Math.Min(time, Math.Max(0, DurationSeconds - MinimumHoldDuration(time)));
		return new AuthoringNote
		{
			Id = "__hover__",
			Kind = _tool == 2 ? AuthoringNoteKind.Chain : _tool == 3 ? AuthoringNoteKind.Hold : AuthoringNoteKind.Normal,
			Side = _side, Time = time, EndTime = time, Center = left, Width = width,
			TimeExact = BarAt(time), EndExact = BarAt(time),
		};
	}
}

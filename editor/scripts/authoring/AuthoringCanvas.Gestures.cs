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
using Gesture = DynamiteUniverse.ChartEditor.Core.CanvasEditGesture;
using GestureKind = DynamiteUniverse.ChartEditor.Core.CanvasGestureKind;

namespace DynamiteUniverse.Editor;

public partial class AuthoringCanvas
{

	public override void _Input(InputEvent e)
	{
		if (InputSuspended) return;
		if (e is InputEventKey key && key.Pressed && !key.Echo) { HandleKey(key); return; }
		if (e is InputEventMouseButton button) { HandleButton(button); return; }
		if (e is InputEventMouseMotion motion) { HandleMotion(motion); }
	}
	private void HandleKey(InputEventKey key)
	{
		// Global PC shortcuts are centralized in StandaloneEditorMain so that
		// the shell and canvas cannot react twice to the same key. Escape is
		// canvas-local because it cancels an in-progress mouse gesture.
		if (key.Keycode == Key.Escape) CancelInteraction();
	}
	private void HandleButton(InputEventMouseButton b)
	{
		if (!GetGlobalRect().HasPoint(b.Position)) return;
		var p = CanvasPoint(b.Position);
		// The stage is full-window to preserve fixed 1920x1080 geometry, but
		// chrome controls own the surrounding hit regions.
		if (!IsAuthoringArea(p)) return;
		var pointerChanged = !_pointerInside || _pointerPoint.DistanceSquaredTo(p) > 0.01f;
		_pointerPoint = p;
		_pointerInside = true;
		if (pointerChanged && _tool <= 3) _stageDirty = true;
		if (b.ButtonIndex is MouseButton.Right or MouseButton.Middle && b.Pressed)
		{
			// In the original mouse state machine a right click while writing or
			// moving is a cancel/reset gesture; it never opens the menu.
			if (_draft is not null || _gesture is not null || _selection.MarqueeActive)
			{
				CancelInteraction();
				GetViewport().SetInputAsHandled();
				return;
			}
			// A right click opens the shell's context menu; the shell reads the canvas
			// selection to pick the note panel or the node panel.
			// The interaction hit test uses local stage coordinates, while the shell
			// draws the menu in viewport coordinates.
			ContextMenuRequested?.Invoke(b.Position);
			GetViewport().SetInputAsHandled();
			return;
		}
		if (b.ButtonIndex != MouseButton.Left) return;
		// The floating chip owns its rectangle: a click there is never a stage click.
		if (HandleChipPress(p, b.Pressed)) { GetViewport().SetInputAsHandled(); return; }
		// 双击路径身体插入控制节点（第二击不松手直接进宽度拖拽）；命中失败就退回普通点击。
		if (b.Pressed && b.DoubleClick && TryInsertPathNodeAt(p)) { GetViewport().SetInputAsHandled(); return; }
		if (b.Pressed) BeginPrimary(p); else EndPrimary(p); GetViewport().SetInputAsHandled();
	}
	private void HandleMotion(InputEventMouseMotion m)
	{
		if (!GetGlobalRect().HasPoint(m.Position))
		{
			if (_pointerInside)
			{
				_pointerInside = false;
				_stageDirty = true;
				QueueRedraw();
			}
			return;
		}
		var p = CanvasPoint(m.Position);
		if (!IsAuthoringArea(p))
		{
			if (_pointerInside)
			{
				_pointerInside = false;
				_stageDirty = true;
				QueueRedraw();
			}
			return;
		}
		var pointerChanged = !_pointerInside || _pointerPoint.DistanceSquaredTo(p) > 0.01f;
		_pointerPoint = p;
		_pointerInside = true;
		if (pointerChanged && (_tool <= 3 || _draft is not null)) _stageDirty = true;
		if (UpdateChipHover(p)) QueueRedraw();
		if (UpdatePathHandleHover(p)) QueueRedraw();
		if (_selection.MarqueeActive) { _marqueeCurrent = p; _selection.UpdateMarquee(p.DistanceTo(_marqueeStart)); QueueRedraw(); return; }
		if (_draft?.TailStage == true) UpdateHoldTail(p); else if (_draft is not null) UpdateWidthDraft(p); else if (_gesture is not null) UpdateGesture(p);
		if (_draft is not null || _gesture is not null || CanBuildHoverGhost()) QueueRedraw();
	}

	private void BeginPrimary(Vector2 p)
	{
		if (_draft?.TailStage == true) { if (PointInsideTrack(p, _draft.Side)) { UpdateHoldTail(p); CommitDraft(); } return; }
		if (_side == 3)
		{
			if (_tool == 4 && TryEvent(p, out var eventIndex)) { SelectEvent(eventIndex); QueueRedraw(); return; }
			AddBpmEvent(TimeFromPoint(p));
			return;
		}
		if (_tool == 4)
		{
			var hit = HitTest(p);
			if (hit.NoteIndex >= 0)
			{
				if (Input.IsKeyPressed(Key.Shift))
				{
					_selection.ToggleNote(Notes[hit.NoteIndex].Id);
					SelectionChanged?.Invoke(hit.NoteIndex); QueueRedraw(); return;
				}
				SelectNote(hit.NoteIndex);
				// 二级选中：点中路径节点方块时同时选中该节点（设计稿 §4.3），
				// 右键菜单据此出 Node Menu。
				_selection.SelectNode(Notes[hit.NoteIndex].Id,
					hit.NodeIndex >= 0 && hit.NodeIndex < Notes[hit.NoteIndex].Nodes.Count
						? Notes[hit.NoteIndex].Nodes[hit.NodeIndex].Id : null);
				BeginGesture(p, hit);
			}
			else if (_side == 3 && TryEvent(p, out var ei)) SelectEvent(ei);
			else
			{
				var additive = Input.IsKeyPressed(Key.Shift);
				var hadSelection = HasSelection;
				_selection.BeginMarquee(additive);
				_marqueeStart = _marqueeCurrent = p;
				if (!additive && hadSelection) SelectionChanged?.Invoke(-1);
			}
			QueueRedraw(); return;
		}
		if (!PointInsideTrack(p, _side)) return;
		var place = Place(p, _side);
		var anchor = SnapSpatial(place.Center);
		var rememberedWidth = Clamp(EditorStagePlacement.SnapWidth(_rememberedWidth), .1, MaxChartCoordinate);
		var left = Clamp(anchor - rememberedWidth * .5, 0, MaxChartCoordinate - rememberedWidth);
		var t = SnapTime(place.Time);
		if (_tool == 3) t = Math.Min(t, Math.Max(0, DurationSeconds - MinimumHoldDuration(t)));
		_draft = new CanvasPlacementDraft { Side = _side, Kind = _tool == 2 ? AuthoringNoteKind.Chain : _tool == 3 ? AuthoringNoteKind.Hold : AuthoringNoteKind.Normal, Time = t, AnchorCenter = anchor, BaseWidth = rememberedWidth, Center = left, Width = rememberedWidth, EndTime = t, StartPoint = new System.Numerics.Vector2(p.X, p.Y) };
		_stageDirty = true;
		EmitStatus(_tool == 3 ? "HOLD STAGE 1/2 · DRAG WIDTH, RELEASE" : "DRAG WIDTH, RELEASE TO COMMIT"); QueueRedraw();
	}
	private void EndPrimary(Vector2 p)
	{
		if (_selection.MarqueeActive) { _marqueeCurrent = p; FinishMarquee(); QueueRedraw(); return; }
		if (_draft is not null)
		{
			if (_draft.TailStage) return;
			UpdateWidthDraft(p);
			if (_draft.Kind == AuthoringNoteKind.Hold)
			{
				_draft.BeginTail();
				// 头要从幽灵换成已放置实体外观，链接也要停掉，所以这里必须重刷舞台。
				_stageDirty = true;
				EmitStatus("HOLD STAGE 2/2 · MOVE TO TAIL TIME, CLICK TO COMMIT");
			}
			else CommitDraft();
			QueueRedraw();
			return;
		}
		if (_gesture is not null) { UpdateGesture(p); FinishGesture(); }
	}
	private void UpdateWidthDraft(Vector2 p)
	{
		if (_draft is null || _draft.TailStage) return;
		var place = Place(p, _draft.Side);
		if (!_draft.UpdateWidth(place.Center, p.DistanceTo(new Vector2(_draft.StartPoint.X, _draft.StartPoint.Y)))) return;
		_stageDirty = true;
	}
	private void UpdateHoldTail(Vector2 p)
	{
		if (_draft is null || !_draft.TailStage || !PointInsideTrack(p, _draft.Side)) return;
		_draft.UpdateTail(SnapTime(Place(p, _draft.Side).Time), DurationSeconds); _stageDirty = true;
	}
	private void CommitDraft()
	{
		if (_draft is null) return;
		var d = _draft;
		if (_controller is null) { _draft = null; return; }
		var time = ExactTimeAt(d.Time);
		ExactBarTime? tail = null;
		if (d.Kind == AuthoringNoteKind.Hold)
			tail = ExactTimeAt(Math.Max(d.EndTime, d.Time + MinimumHoldDuration(d.Time)));
		_rememberedWidth = d.Commit(_controller, time, tail);
		_draft = null;
		SyncFromDocument();
		EmitStatus($"{d.Kind.ToString().ToUpperInvariant()} COMMITTED / READY");
		// Keep the active writing tool armed for the next note.
		_tool = d.Kind == AuthoringNoteKind.Chain ? 2 : d.Kind == AuthoringNoteKind.Hold ? 3 : 1;
	}
	private void AddBpmEvent(double time)
	{
		if (_controller is null || _document is null) return;
		var t = SnapTime(time);
		var existing = BpmEvents.FindIndex(x => Math.Abs(x.Time - t) < .0001);
		if (existing >= 0) { SelectEvent(existing); QueueRedraw(); return; }
		var exact = ExactTimeAt(t);
		if (_document.SelectedChart.Bpms.Any(item => item.Time == exact)) return;
		_controller.AddBpm(exact, 150);
		SyncFromDocument();
		SelectEvent(BpmEvents.FindIndex(item => item.TimeExact == exact));
		EmitStatus("BPM EVENT ADDED · DEFAULT 150");
	}

	private void BeginGesture(Vector2 p, Hit hit)
	{
		var n = Notes[hit.NoteIndex];
		_rememberedWidth = n.Width;
		var m = Place(p, n.Side);
		var node = hit.NodeIndex >= 0 && hit.NodeIndex < n.Nodes.Count ? n.Nodes[hit.NodeIndex] : null;
		var g = new Gesture
		{
			Kind = node is not null ? GestureKind.ResizeNode : hit.Resize ? GestureKind.ResizeWidth : GestureKind.Move,
			ResizeLeading = hit.ResizeLeading,
			ResizeNodeWidth = hit.ResizeNodeWidth,
			NoteIndex = hit.NoteIndex,
			NodeIndex = node is not null ? hit.NodeIndex : -1,
			StartPoint = new System.Numerics.Vector2(p.X, p.Y),
			Anchor = new CanvasDragAnchor(node?.TimeExact ?? n.TimeExact, node?.Time ?? n.Time,
				(node?.Center ?? n.Center) + (node?.Width ?? n.Width) * .5, m.Time, m.Center),
			OriginalCenter = n.Center,
			OriginalWidth = n.Width,
			OriginalNodeCenter = node?.Center ?? 0,
			OriginalNodeWidth = node?.Width ?? 0,
		};
		_gesture = g; EmitStatus(g.Kind == GestureKind.ResizeWidth ? "RESIZE · DRAG NOTE EDGE" : "SELECTED · DRAG TO MOVE");
	}
	private void UpdateGesture(Vector2 p)
	{
		if (_gesture is null || _gesture.NoteIndex < 0 || _gesture.NoteIndex >= Notes.Count) return;
		if (!_gesture.Advance(p.DistanceTo(new Vector2(_gesture.StartPoint.X, _gesture.StartPoint.Y)))) return;
		var n = Notes[_gesture.NoteIndex]; var m = Place(p, n.Side);
		var bpms = CurrentBpms;
		var tolerance = 1.5d / (PixelsPerSecond * (n.Side == 0 ? 1d : GameplayStageGeometry.SideDistanceScale));
		if (_gesture.Kind == GestureKind.ResizeNode && _gesture.NodeIndex >= 0 && _gesture.NodeIndex < n.Nodes.Count)
		{
			var node = n.Nodes[_gesture.NodeIndex];
			var previous = _gesture.NodeIndex == 0 ? n.TimeExact : n.Nodes[_gesture.NodeIndex - 1].TimeExact;
			var next = _gesture.NodeIndex + 1 < n.Nodes.Count ? n.Nodes[_gesture.NodeIndex + 1] : null;
			var preview = CanvasGesturePreview.MoveNodeTime(_gesture, m.Time, bpms, GridDivisor,
				SnapEnabled && TimeSnapOverride, tolerance, previous, next?.TimeExact, DurationSeconds);
			if (preview is { } accepted)
			{
				node.TimeExact = accepted.Time;
				node.Time = accepted.Seconds;
			}
			if (_gesture.ResizeNodeWidth)
			{
				// 双击插入的宽度拖拽按位移相对起手采样宽度走（左缘不动），其余仍沿用
				// 「光标即右缘」的绝对拖拽。
				node.Width = CanvasGesturePreview.ResizeNodeWidth(SnapSpatial(m.Center), node.Center,
					_gesture.RelativeWidth, _gesture.OriginalNodeWidth, _gesture.WidthPointer);
			}
			else
				node.Center = DraggedLeft(_gesture, m, n.Side, node.Width, _gesture.OriginalNodeCenter);
			n.EndExact = n.Nodes[^1].TimeExact;
			n.EndTime = n.Nodes[^1].Time;
		}
		else if (_gesture.Kind == GestureKind.ResizeWidth)
		{
			(n.Center, n.Width) = CanvasGesturePreview.ResizeNoteWidth(_gesture.ResizeLeading,
				SnapSpatial(m.Center), n.Center, _gesture.OriginalCenter, _gesture.OriginalWidth);
		}
		else
		{
			var preview = CanvasGesturePreview.MoveNote(_gesture.Anchor, m.Time, bpms, GridDivisor,
				SnapEnabled && TimeSnapOverride, tolerance, n.TimeExact, n.EndExact, DurationSeconds);
			n.TimeExact = preview.Time;
			n.Time = preview.Seconds;
			n.Center = DraggedLeft(_gesture, m, n.Side, n.Width, _gesture.OriginalCenter);
			foreach (var node in n.Nodes)
				(node.TimeExact, node.Time) = CanvasGesturePreview.ShiftPathTime(node.TimeExact, preview.Delta, bpms);
			n.EndExact = n.Nodes.Count > 0 ? n.Nodes[^1].TimeExact : preview.Time;
			n.EndTime = EditorTime.BarToSeconds(n.EndExact, bpms);
		}
		_rememberedWidth = n.Width;
		_stageDirty = true;
	}
	private void FinishGesture()
	{
		var gesture = _gesture;
		_gesture = null;
		if (gesture is null) return;
		if (_controller is null || gesture.NoteIndex < 0 || gesture.NoteIndex >= Notes.Count)
		{
			_stageDirty = true; QueueRedraw(); return;
		}
		if (!gesture.Moved)
		{
			_stageDirty = true;
			QueueRedraw();
			return;
		}
		var n = Notes[gesture.NoteIndex];
		bool changed;
		if (gesture.Kind == GestureKind.ResizeNode && gesture.NodeIndex >= 0 &&
			gesture.NodeIndex < n.Nodes.Count)
		{
			var node = n.Nodes[gesture.NodeIndex];
			changed = gesture.Commit(_controller, n.Id, node.Id, node.TimeExact, node.Center, node.Width);
		}
		else
		{
			changed = gesture.Commit(_controller, n.Id, null, n.TimeExact, n.Center, n.Width);
		}
		SyncFromDocument();
		_stageDirty = true;
		if (changed) EmitStatus("EDIT COMMITTED · OBJECT UPDATED");
		QueueRedraw();
	}

	private void FinishMarquee()
	{
		if (!_selection.MarqueeActive) return;
		var rect = new Rect2(_marqueeStart, Vector2.Zero).Expand(_marqueeCurrent);
		var hits = new List<string>();
		if (_selection.MarqueeMoved)
		{
			foreach (var note in Notes)
				if (note.Side == _side && rect.Intersects(NoteRect(note, note.Time))) hits.Add(note.Id);
		}
		if (_selection.FinishMarquee(hits))
		{
			EmitStatus(_selection.NoteIds.Count == 0 ? "SELECTION CLEARED" : $"{_selection.NoteIds.Count} NOTES SELECTED");
			SelectionChanged?.Invoke(SelectedNoteIndex);
		}
	}
}

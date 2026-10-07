using Godot;
using System;
using System.Collections.Generic;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using ChartNote = DynamiteUniverse.Shared.Chart.Note;

namespace DynamiteUniverse.Editor;

/// <summary>Fixed 1920x1080 DynaMaker authoring stage and its PC mouse model.</summary>
public partial class AuthoringCanvas : Control
{
	public const float DesignWidth = 1920f, DesignHeight = 1080f;
	public const float CenterX = GameplayStageGeometry.CenterX0, CenterWidth = GameplayStageGeometry.CenterUnitPx * 5f, CenterTop = 95f, CenterJudgeY = GameplayStageGeometry.CenterLineY;
	public const float LeftJudgeX = GameplayStageGeometry.LeftLineX, RightJudgeX = GameplayStageGeometry.RightLineX, SideTop = GameplayStageGeometry.SideY0 - 5f * GameplayStageGeometry.SideUnitPx, SideHeight = 5f * GameplayStageGeometry.SideUnitPx;
	private const double MaxChartCoordinate = 5d;
	public const float PixelsPerSecond = 1026f;
	public const double DefaultDurationSeconds = 32d;

	/// 0 center, 1 left, 2 right, 3 events.
	public int CurrentSide { get => _side; set => SetSide(value); }
	/// 1 normal, 2 chain, 3 hold, 4 select.
	public int Tool { get => _tool; set => SetTool(value); }
	public bool SnapEnabled { get; set; } = true;
	/// Temporary modifier flags used by the shell's Z/X free-edit shortcuts.
	/// The shell sets these false while a key is held and restores them on release.
	public bool TimeSnapOverride { get; set; } = true;
	public bool SpaceSnapOverride { get; set; } = true;
	// DynaMaker's default magnetic time grid is 1/32 of a chart bar.
	private int _gridDivisor = 32;
	public int GridDivisor
	{
		get => _gridDivisor;
		private set => _gridDivisor = Math.Max(1, value);
	}
	public string SideName => _side switch { 0 => "CENTER", 1 => "LEFT", 2 => "RIGHT", _ => "EVENTS" };
	public double PreviewTime { get => CursorTime; set => CursorTime = value; }
	public double DurationSeconds { get; set; } = DefaultDurationSeconds;
	public double CursorTime { get => _cursorTime; set { _cursorTime = Clamp(value, 0, DurationSeconds); _gameplayStage?.SetPreviewTime(_cursorTime); if (CanBuildHoverGhost()) _stageDirty = true; QueueRedraw(); } }

	public List<AuthoringNote> Notes { get; } = new();
	public List<AuthoringBpmEvent> BpmEvents { get; } = new();
	public int NoteCount => Notes.Count;
	public void RefreshStage()
	{
		_stageDirty = true;
		QueueRedraw();
	}

	/// <summary>
	/// Binds the canvas to the open document. The document is the single authoring authority;
	/// every later canvas mutation is executed as a document command and re-projected here.
	/// </summary>
	public void AttachDocument(EditorDocument document)
	{
		_document = document ?? throw new ArgumentNullException(nameof(document));
		_controller = new CanvasAuthoringController(document);
		_cursorTime = 0;
		SyncFromDocument();
	}

	/// <summary>Rebuilds the canvas projection from the document's selected chart.</summary>
	public void SyncFromDocument()
	{
		var cursor = _cursorTime;
		var selectedIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var index in _selectedNotes)
			if (index >= 0 && index < Notes.Count) selectedIds.Add(Notes[index].Id);
		var selectedEventTime = _selectedEvent >= 0 && _selectedEvent < BpmEvents.Count
			? BpmEvents[_selectedEvent].TimeExact : (ExactBarTime?)null;

		Notes.Clear(); BpmEvents.Clear();
		_selectedNotes.Clear(); _selectedNote = _selectedEvent = -1;
		_stageDirty = true;
		if (_document is null) { QueueRedraw(); return; }

		var chart = _document.SelectedChart;
		var bpms = chart.Bpms;
		var orderedBpms = bpms.OrderBy(item => item.Time).ToArray();
		for (var i = 0; i < orderedBpms.Length; i++)
		{
			var eventItem = orderedBpms[i];
			BpmEvents.Add(new AuthoringBpmEvent
			{
				Id = $"bpm-{i + 1}",
				TimeExact = eventItem.Time,
				Time = EditorTime.BarToSeconds(eventItem.Time, bpms),
				Bpm = eventItem.Bpm,
				Locked = i == 0,
			});
		}

		var maxTime = 16d;
		foreach (var note in chart.Notes)
		{
			var tail = note.Nodes.Count > 0 ? note.Nodes[^1].Time : note.Time;
			var item = new AuthoringNote
			{
				Id = note.Id,
				Kind = CanvasKind(note.Type),
				Side = note.Track switch { EditorTrack.Left => 1, EditorTrack.Right => 2, _ => 0 },
				TimeExact = note.Time,
				Time = EditorTime.BarToSeconds(note.Time, bpms),
				EndExact = tail,
				EndTime = EditorTime.BarToSeconds(tail, bpms),
				Center = Math.Clamp(note.Center - note.Width / 2d, 0, 5 - note.Width),
				Width = Math.Clamp(note.Width, .1, 5),
			};
			maxTime = Math.Max(maxTime, item.Time + 4);
			foreach (var node in note.Nodes)
			{
				var nodeTime = EditorTime.BarToSeconds(node.Time, bpms);
				item.EndTime = Math.Max(item.EndTime, nodeTime);
				maxTime = Math.Max(maxTime, nodeTime + 2);
				item.Nodes.Add(new AuthoringPathNode
				{
					Id = node.Id,
					TimeExact = node.Time,
					Time = nodeTime,
					Center = Math.Clamp(node.Center - node.Width / 2d, 0, 5 - node.Width),
					Width = Math.Clamp(node.Width, .1, 5),
				});
			}
			Notes.Add(item);
		}

		DurationSeconds = Math.Clamp(maxTime, 16, 600);
		_cursorTime = Clamp(cursor, 0, DurationSeconds);
		_gameplayStage?.SetPreviewTime(_cursorTime);
		for (var i = 0; i < Notes.Count; i++)
			if (selectedIds.Contains(Notes[i].Id)) _selectedNotes.Add(i);
		_selectedNote = _selectedNotes.Count == 1 ? FirstSelectedNote() : -1;
		if (selectedEventTime is { } eventTime)
			_selectedEvent = BpmEvents.FindIndex(item => item.TimeExact == eventTime);
		QueueRedraw();
	}

	public void Undo()
	{
		if (_controller is null) return;
		_controller.Undo();
		SyncFromDocument();
	}

	public void Redo()
	{
		if (_controller is null) return;
		_controller.Redo();
		SyncFromDocument();
	}
	public int SelectedCount => _selectedNotes.Count + (_selectedEvent >= 0 ? 1 : 0);
	public bool HasSelection => _selectedNotes.Count > 0 || _selectedNote >= 0 || _selectedEvent >= 0;
	/// True when the most recent context-menu request was made over the selected object.
	/// The root uses this to expose the object's one-command menu (Delete) separately
	/// from the full blank-canvas menu.
	public bool ContextMenuOnSelection { get; private set; }
	public bool InputSuspended { get; set; }
	public int SelectedNoteIndex => _selectedNote;
	public int SelectedEventIndex => _selectedEvent;
	public string SelectedSummary
	{
		get
		{
			if (_selectedNotes.Count > 1) return $"{_selectedNotes.Count} NOTES SELECTED";
			if (_selectedNotes.Count == 1)
			{
				var index = -1;
				foreach (var candidate in _selectedNotes) { index = candidate; break; }
				if (index >= 0 && index < Notes.Count) return Notes[index].ToString();
			}
			if (_selectedNote >= 0 && _selectedNote < Notes.Count) return Notes[_selectedNote].ToString();
			return _selectedEvent >= 0 && _selectedEvent < BpmEvents.Count
				? $"BPM {BpmEvents[_selectedEvent].Bpm:0.##}"
				: string.Empty;
		}
	}

	public event Action<string>? StatusChanged;
	public event Action<int>? SelectionChanged;
	public event Action<Vector2>? ContextMenuRequested;

	private int _side, _tool = 1, _selectedNote = -1, _selectedEvent = -1;
	// Original showD/showL/showR cycle hidden -> dim -> full (and repeat).
	private readonly int[] _trackVisibility = { 2, 2, 2 };
	private readonly HashSet<int> _selectedNotes = new();
	private Vector2 _marqueeStart, _marqueeCurrent;
	private bool _marqueeActive, _marqueeMoved, _selectionWasMarquee, _marqueeAdditive;
	private double _cursorTime;
	// Original DynaMaker keeps one preCoverWidth for every surface and tool.
	private double _rememberedWidth = 1d;
	private Draft? _draft;
	private Gesture? _gesture;
	// The reference editor keeps noteTemp alive while a writing tool is armed,
	// even before the first mouse press. Keep the last stage-space pointer so
	// the shared renderer can draw that low-alpha placement preview.
	private Vector2 _pointerPoint;
	private bool _pointerInside;
	private DynamiteUniverse.Game.GameplayStageRenderer? _gameplayStage;
	private readonly List<ChartNote> _gameplayNotes = new();
	private bool _stageDirty = true;
	private EditorDocument? _document;
	private CanvasAuthoringController? _controller;

	private enum GestureKind { Move, ResizeWidth, ResizeNode }
	private sealed class Draft
	{
		public required int Side;
		public required AuthoringNoteKind Kind;
		public required double Time, AnchorCenter;
		public required double BaseWidth;
		public double Center, Width, EndTime;
		public Vector2 StartPoint;
		public bool Moved;
		private bool _tailStage;
		public bool TailStage
		{
			get => _tailStage;
			set { _tailStage = value; if (value) EndTime = Time; }
		}
	}
	private sealed class Gesture
	{
		public required GestureKind Kind;
		public required int NoteIndex, NodeIndex;
		public required Vector2 StartPoint;
		public required CanvasDragAnchor Anchor;
		public double OriginalCenter, OriginalWidth;
		public bool Moved;
		public bool ResizeLeading;
		public bool ResizeNodeWidth;
		public double OriginalNodeCenter, OriginalNodeWidth;
	}
	private readonly record struct Placement(double Time, double Center);
	private readonly record struct Hit(int NoteIndex, int NodeIndex, bool Resize, bool ResizeLeading, bool ResizeNodeWidth);

	public override void _Ready()
	{
		// The stage is a scene child, matching the gameplay scene hierarchy.
		// The canvas itself stays at the base layer so the shared chrome is
		// visible; notes remain above the authoring overlay in the renderer.
		ZIndex = 0;
		SetProcessInput(true);
		_gameplayStage = GetNodeOrNull<DynamiteUniverse.Game.GameplayStageRenderer>("SharedGameplayStage");
		if (_gameplayStage is null)
		{
			_gameplayStage = new DynamiteUniverse.Game.GameplayStageRenderer
			{ Name = "SharedGameplayStage", ZIndex = 0 };
			AddChild(_gameplayStage);
		}
		_gameplayStage.BuildCommunityStageChrome();
		_gameplayStage.ShowControlNodes = false;
		_gameplayStage.SetPreviewDistanceProvider((note, time) =>
			(float)((note.Second - time) * DynamiteUniverse.Game.GameplayVisualMapper.BaseFallSpeedPx));
		_stageDirty = true;
		QueueRedraw();
	}
	public override void _Process(double delta)
	{
		if (_stageDirty) RefreshGameplayStage();
		if (_draft is not null || _gesture is not null) QueueRedraw();
	}

	public void SetTool(int tool)
	{
		var next = Math.Clamp(tool, 1, 4);
		if (_tool == next) return;
		CancelInteraction(false); _tool = next;
		EmitStatus(next switch
		{
			1 => "NORMAL · DRAG WIDTH, RELEASE TO COMMIT",
			2 => "CHAIN · DRAG WIDTH, RELEASE TO COMMIT",
			3 => "HOLD · STAGE 1 WIDTH, STAGE 2 TAIL",
			_ => "SELECT · CLICK OR DRAG OBJECT",
		});
		QueueRedraw();
	}
	public void SetSide(int side)
	{
		var next = Math.Clamp(side, 0, 3);
		if (_side == next) return;
		CancelInteraction(false); _side = next;
		EmitStatus(next switch
		{
			0 => "CENTER · TIME MOVES TOWARD Y=861",
			1 => "LEFT · TIME MOVES OUTWARD FROM X=184",
			2 => "RIGHT · TIME MOVES OUTWARD FROM X=1736",
			_ => "EVENTS · CLICK TO ADD BPM EVENT",
		});
		QueueRedraw();
	}
	public void CycleGrid() { GridDivisor = GridDivisor switch { 8 => 16, 16 => 32, _ => 8 }; _stageDirty = true; EmitStatus($"GRID 1/{GridDivisor}"); QueueRedraw(); }

	/// <summary>Adjusts the magnetic grid using DynaMaker's discrete C/V ladder.</summary>
	public void AdjustGridDivisor(bool increase, bool fine)
	{
		var current = GridDivisor;
		if (fine)
		{
			GridDivisor = Math.Max(1, current + (increase ? 1 : -1));
		}
		else if (increase)
		{
			GridDivisor = current <= 4 ? current * 2 : current switch
			{
				8 => 12,
				12 => 16,
				16 => 24,
				24 => 32,
				32 => 48,
				48 => 64,
				_ when current % 32 == 0 => current + 32,
				_ => Math.Max(1, (int)Math.Ceiling(current / 32d) * 32),
			};
		}
		else if (current > 1)
		{
			GridDivisor = current <= 8 ? Math.Max(1, current / 2) : current switch
			{
				12 => 8,
				16 => 12,
				24 => 16,
				32 => 24,
				48 => 32,
				64 => 48,
				_ when current % 32 == 0 => Math.Max(1, current - 32),
				_ => Math.Max(1, (int)Math.Floor(current / 32d) * 32),
			};
		}
		_stageDirty = true;
		EmitStatus($"GRID 1/{GridDivisor}");
		QueueRedraw();
	}

	public void ResetGridDivisor()
	{
		GridDivisor = 32;
		_stageDirty = true;
		QueueRedraw();
	}

	public void CycleTrackVisibility(int side)
	{
		if (side is < 0 or > 2) return;
		_trackVisibility[side] = (_trackVisibility[side] + 1) % 6;
		_stageDirty = true;
		var state = (_trackVisibility[side] % 3) switch { 0 => "OFF", 1 => "DIM", _ => "FULL" };
		EmitStatus($"{side switch { 0 => "CENTER", 1 => "LEFT", _ => "RIGHT" }} GRID {state}");
		QueueRedraw();
	}
	public void ToggleSnap() { SnapEnabled = !SnapEnabled; EmitStatus($"SNAP {(SnapEnabled ? "ON" : "OFF")}"); QueueRedraw(); }

	public void DeleteSelection()
	{
		if (_draft is not null || _gesture is not null) { EmitStatus("DELETE REJECTED"); return; }
		if (_controller is null || _document is null) return;
		if (_selectedNotes.Count > 0)
		{
			var ids = new List<string>();
			foreach (var index in _selectedNotes)
				if (index >= 0 && index < Notes.Count) ids.Add(Notes[index].Id);
			if (ids.Count == 0) return;
			_controller.DeleteNotes(ids);
			SyncFromDocument();
			EmitStatus(ids.Count == 1 ? "NOTE REMOVED" : $"{ids.Count} NOTES REMOVED");
			return;
		}
		if (_selectedNote >= 0 && _selectedNote < Notes.Count)
		{
			var note = Notes[_selectedNote];
			_controller.DeleteNotes([note.Id]);
			SyncFromDocument();
			EmitStatus($"{note.Kind.ToString().ToUpperInvariant()} REMOVED");
		}
		else if (_selectedEvent >= 0 && _selectedEvent < BpmEvents.Count)
		{
			var exact = BpmEvents[_selectedEvent].TimeExact;
			try { _controller.DeleteBpm(exact); }
			catch (InvalidOperationException) { EmitStatus("DELETE REJECTED · BASE BPM IS LOCKED"); return; }
			SyncFromDocument();
			EmitStatus("BPM EVENT REMOVED");
		}
	}
	public void ClearSelection()
	{
		if (!HasSelection) return;
		_selectedNotes.Clear();
		_selectedNote = _selectedEvent = -1;
		_selectionWasMarquee = false;
		ContextMenuOnSelection = false;
		SelectionChanged?.Invoke(-1); QueueRedraw();
	}
	public void CancelInteraction(bool announce = true)
	{
		var restoreProjection = _gesture is not null;
		if (_gesture is not null)
			_rememberedWidth = _gesture.OriginalWidth;
		var active = _draft is not null || _gesture is not null || _marqueeActive; _draft = null; _gesture = null; _marqueeActive = false; _marqueeMoved = false; _stageDirty = true;
		// Restore every path node from the document, including nodes shifted by a head drag.
		if (restoreProjection) SyncFromDocument();
		if (announce) EmitStatus(active ? "CANCELLED · READY" : "NOTHING TO CANCEL");
		QueueRedraw();
	}

	public void ClearPointerHover()
	{
		if (!_pointerInside) return;
		_pointerInside = false;
		_stageDirty = true;
		QueueRedraw();
	}

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
			if (_draft is not null || _gesture is not null || _marqueeActive)
			{
				CancelInteraction();
				GetViewport().SetInputAsHandled();
				return;
			}
			// The reference only exposes its delete menu for a non-zero marquee
			// selection. A right click on a single note opens the basic menu.
			ContextMenuOnSelection = _selectionWasMarquee && _selectedNotes.Count > 0;
			// The interaction hit test uses local stage coordinates, while the shell
			// draws the menu in viewport coordinates.
			ContextMenuRequested?.Invoke(b.Position);
			GetViewport().SetInputAsHandled();
			return;
		}
		if (b.ButtonIndex != MouseButton.Left) return;
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
		if (_marqueeActive) { _marqueeCurrent = p; _marqueeMoved = _marqueeMoved || p.DistanceTo(_marqueeStart) >= 3f; QueueRedraw(); return; }
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
					if (!_selectedNotes.Add(hit.NoteIndex)) _selectedNotes.Remove(hit.NoteIndex);
					_selectedNote = _selectedNotes.Count == 1 ? hit.NoteIndex : -1;
					_selectedEvent = -1; _selectionWasMarquee = false; SelectionChanged?.Invoke(hit.NoteIndex); QueueRedraw(); return;
				}
				SelectNote(hit.NoteIndex); BeginGesture(p, hit);
			}
			else if (_side == 3 && TryEvent(p, out var ei)) SelectEvent(ei);
			else
			{
				_marqueeActive = true; _marqueeMoved = false; _marqueeStart = _marqueeCurrent = p; _marqueeAdditive = Input.IsKeyPressed(Key.Shift);
				if (!_marqueeAdditive) ClearSelection();
			}
			QueueRedraw(); return;
		}
		if (!PointInsideTrack(p, _side)) return;
		var place = Place(p, _side);
		var anchor = SnapSpatial(place.Center);
		var rememberedWidth = Clamp(SnapWidth(_rememberedWidth), .1, MaxChartCoordinate);
		var left = Clamp(anchor - rememberedWidth * .5, 0, MaxChartCoordinate - rememberedWidth);
		var t = SnapTime(place.Time);
		if (_tool == 3) t = Math.Min(t, Math.Max(0, DurationSeconds - MinimumHoldDuration(t)));
		_draft = new Draft { Side = _side, Kind = _tool == 2 ? AuthoringNoteKind.Chain : _tool == 3 ? AuthoringNoteKind.Hold : AuthoringNoteKind.Normal, Time = t, AnchorCenter = anchor, BaseWidth = rememberedWidth, Center = left, Width = rememberedWidth, EndTime = t, StartPoint = p };
		_stageDirty = true;
		EmitStatus(_tool == 3 ? "HOLD STAGE 1/2 · DRAG WIDTH, RELEASE" : "DRAG WIDTH, RELEASE TO COMMIT"); QueueRedraw();
	}
	private void EndPrimary(Vector2 p)
	{
		if (_marqueeActive) { _marqueeCurrent = p; FinishMarquee(); QueueRedraw(); return; }
		if (_draft is not null) { if (_draft.TailStage) return; UpdateWidthDraft(p); if (_draft.Kind == AuthoringNoteKind.Hold) { _draft.TailStage = true; _draft.EndTime = _draft.Time; EmitStatus("HOLD STAGE 2/2 · MOVE TO TAIL TIME, CLICK TO COMMIT"); } else CommitDraft(); QueueRedraw(); return; }
		if (_gesture is not null) { UpdateGesture(p); FinishGesture(); }
	}
	private void UpdateWidthDraft(Vector2 p)
	{
		if (_draft is null || _draft.TailStage) return;
		var place = Place(p, _draft.Side); if (!_draft.Moved && p.DistanceTo(_draft.StartPoint) < 3) return; _draft.Moved = true;
		var delta = place.Center - _draft.AnchorCenter;
		_draft.Width = Clamp(SnapWidth(_draft.BaseWidth + Math.Abs(delta) * delta), .1, MaxChartCoordinate);
		_draft.Center = Clamp(_draft.AnchorCenter - _draft.Width * .5, 0, MaxChartCoordinate - _draft.Width);
		_stageDirty = true;
	}
	private void UpdateHoldTail(Vector2 p)
	{
		if (_draft is null || !_draft.TailStage || !PointInsideTrack(p, _draft.Side)) return;
		var min = _draft.Time; _draft.EndTime = Clamp(Math.Max(min, SnapTime(Place(p, _draft.Side).Time)), min, DurationSeconds); _stageDirty = true;
	}
	private void CommitDraft()
	{
		if (_draft is null) return;
		var d = _draft;
		if (_controller is null) { _draft = null; return; }
		var width = Clamp(d.Width, .1, MaxChartCoordinate - Clamp(d.Center, 0, MaxChartCoordinate - d.Width));
		var left = Clamp(d.Center, 0, MaxChartCoordinate - width);
		var time = ExactTimeAt(d.Time);
		ExactBarTime? tail = null;
		if (d.Kind == AuthoringNoteKind.Hold)
			tail = ExactTimeAt(Math.Max(d.EndTime, d.Time + MinimumHoldDuration(d.Time)));
		_controller.AddNote(CanvasType(d.Kind), CanvasTrack(d.Side), time,
			left + width / 2d, width, tail);
		_rememberedWidth = width;
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
			StartPoint = p,
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
		if (!_gesture.Moved && p.DistanceTo(_gesture.StartPoint) < 3f) return;
		_gesture.Moved = true;
		var n = Notes[_gesture.NoteIndex]; var m = Place(p, n.Side);
		var bpms = CurrentBpms;
		var tolerance = 1.5d / (PixelsPerSecond * (n.Side == 0 ? 1d : GameplayStageGeometry.SideDistanceScale));
		if (_gesture.Kind == GestureKind.ResizeNode && _gesture.NodeIndex >= 0 && _gesture.NodeIndex < n.Nodes.Count)
		{
			var node = n.Nodes[_gesture.NodeIndex];
			var time = _gesture.ResizeNodeWidth ? _gesture.Anchor.Time : _gesture.Anchor.TimeAt(
				m.Time, bpms, GridDivisor, SnapEnabled && TimeSnapOverride, tolerance);
			var previous = _gesture.NodeIndex == 0 ? n.TimeExact : n.Nodes[_gesture.NodeIndex - 1].TimeExact;
			var next = _gesture.NodeIndex + 1 < n.Nodes.Count ? n.Nodes[_gesture.NodeIndex + 1] : null;
			var seconds = EditorTime.BarToSeconds(time, bpms);
			if (time == _gesture.Anchor.Time ||
				(time > previous && (next is null || time < next.TimeExact) && seconds <= DurationSeconds))
			{
				node.TimeExact = time;
				node.Time = seconds;
			}
			if (_gesture.ResizeNodeWidth)
			{
				var edge = Clamp(SnapSpatial(m.Center), node.Center + .1, MaxChartCoordinate);
				node.Width = Clamp(SnapWidth(edge - node.Center), .1, MaxChartCoordinate - node.Center);
			}
			else
				node.Center = DraggedLeft(_gesture, m, n.Side, node.Width, _gesture.OriginalNodeCenter);
			n.EndExact = n.Nodes[^1].TimeExact;
			n.EndTime = n.Nodes[^1].Time;
		}
		else if (_gesture.Kind == GestureKind.ResizeWidth)
		{
			if (_gesture.ResizeLeading)
			{
				var right = _gesture.OriginalCenter + _gesture.OriginalWidth;
				var left = Clamp(SnapSpatial(m.Center), 0, right - .1);
				n.Width = Clamp(SnapWidth(right - left), .1, MaxChartCoordinate - left);
				n.Center = Clamp(right - n.Width, 0, MaxChartCoordinate - n.Width);
			}
			else
			{
				var right = Clamp(SnapSpatial(m.Center), n.Center + .1, MaxChartCoordinate);
				n.Width = right - n.Center;
			}
			n.Width = Clamp(SnapWidth(n.Width), .1, MaxChartCoordinate - n.Center);
		}
		else
		{
			var time = _gesture.Anchor.TimeAt(m.Time, bpms, GridDivisor,
				SnapEnabled && TimeSnapOverride, tolerance);
			if (time != _gesture.Anchor.Time)
			{
				var latest = EditorTime.SecondsToBar(DurationSeconds, bpms) - (n.EndExact - n.TimeExact);
				if (time > latest) time = latest;
				if (time < ExactBarTime.Zero) time = ExactBarTime.Zero;
			}
			var delta = time - n.TimeExact;
			n.TimeExact = time;
			n.Time = EditorTime.BarToSeconds(time, bpms);
			n.Center = DraggedLeft(_gesture, m, n.Side, n.Width, _gesture.OriginalCenter);
			foreach (var node in n.Nodes)
			{
				node.TimeExact += delta;
				node.Time = EditorTime.BarToSeconds(node.TimeExact, bpms);
			}
			n.EndExact = n.Nodes.Count > 0 ? n.Nodes[^1].TimeExact : time;
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
			changed = node.TimeExact != gesture.Anchor.Time ||
				!NearlyEqual(node.Center, gesture.OriginalNodeCenter) ||
				!NearlyEqual(node.Width, gesture.OriginalNodeWidth);
			if (changed)
				_controller.EditPathNode(n.Id, node.Id, node.TimeExact,
					node.Center + node.Width / 2d, node.Width);
		}
		else
		{
			changed = n.TimeExact != gesture.Anchor.Time ||
				!NearlyEqual(n.Center, gesture.OriginalCenter) ||
				!NearlyEqual(n.Width, gesture.OriginalWidth);
			if (changed)
				_controller.MoveNote(n.Id, n.TimeExact, n.Center + n.Width / 2d, n.Width);
		}
		SyncFromDocument();
		_stageDirty = true;
		if (changed) EmitStatus("EDIT COMMITTED · OBJECT UPDATED");
		QueueRedraw();
	}

	private void FinishMarquee()
	{
		if (!_marqueeActive) return;
		_marqueeActive = false;
		var rect = new Rect2(_marqueeStart, Vector2.Zero).Expand(_marqueeCurrent);
		if (_marqueeMoved)
		{
			for (var i = 0; i < Notes.Count; i++)
			{
				var n = Notes[i];
				if (n.Side == _side && rect.Intersects(NoteRect(n, n.Time))) _selectedNotes.Add(i);
			}
			_selectionWasMarquee = _selectedNotes.Count > 0;
			_selectedNote = _selectedNotes.Count == 1 ? FirstSelectedNote() : -1;
			_selectedEvent = -1;
			EmitStatus(_selectedNotes.Count == 0 ? "SELECTION CLEARED" : $"{_selectedNotes.Count} NOTES SELECTED");
			SelectionChanged?.Invoke(_selectedNote);
		}
		_marqueeMoved = false;
	}
	private int FirstSelectedNote() { foreach (var index in _selectedNotes) return index; return -1; }
	private void SelectNote(int index) { _selectedNotes.Clear(); _selectedNotes.Add(index); _selectedNote = index; _selectedEvent = -1; _selectionWasMarquee = false; SelectionChanged?.Invoke(index); EmitStatus("NOTE SELECTED"); }
	private void SelectEvent(int index) { _selectedNotes.Clear(); _selectedNote = -1; _selectedEvent = index; _selectionWasMarquee = false; SelectionChanged?.Invoke(-1); QueueRedraw(); }
	private bool TryEvent(Vector2 p, out int index) { for (var i = BpmEvents.Count - 1; i >= 0; i--) { if (Math.Abs(p.Y - AxisAt(0, BpmEvents[i].Time)) <= 15 && p.X >= CenterX && p.X <= CenterX + 260) { index = i; return true; } } index = -1; return false; }
	private Hit HitTest(Vector2 p)
	{
		if (_side == 3) return new Hit(-1, -1, false, false, false);
		for (var i = Notes.Count - 1; i >= 0; i--) { var n = Notes[i]; if (n.Side != _side) continue; for (var j = 0; j < n.Nodes.Count; j++) { var nr = NoteRect(n.Side, n.Nodes[j].Time, n.Nodes[j].Center, n.Nodes[j].Width); if (nr.Grow(9).HasPoint(p)) { var nodeEdge = n.Side == 0 ? Math.Abs(p.X - nr.End.X) < 12 : Math.Abs(p.Y - nr.End.Y) < 12; return new Hit(i, j, false, false, nodeEdge); } } var r = NoteRect(n, n.Time); if (r.Grow(9).HasPoint(p)) { var leading = n.Side == 0 ? Math.Abs(p.X - r.Position.X) < 12 : Math.Abs(p.Y - r.Position.Y) < 12; var resizing = n.Side == 0 ? Math.Abs(p.X - r.Position.X) < 12 || Math.Abs(p.X - r.End.X) < 12 : Math.Abs(p.Y - r.Position.Y) < 12 || Math.Abs(p.Y - r.End.Y) < 12; return new Hit(i, -1, resizing, leading, false); } if (n.Kind == AuthoringNoteKind.Hold && NoteRect(n, n.EndTime).Grow(9).HasPoint(p)) return new Hit(i, -1, false, false, false); }
		return new Hit(-1, -1, false, false, false);
	}

	public override void _Draw()
	{
		DrawGridLines();
		DrawBpmEvents();
		DrawSelectionOverlay();
	}

	private void DrawSelectionOverlay()
	{
		foreach (var index in _selectedNotes)
		{
			if (index < 0 || index >= Notes.Count) continue;
			var note = Notes[index];
			DrawSelectionHandle(note.Side, note.Time, note.Center, note.Width);
			foreach (var node in note.Nodes) DrawSelectionHandle(note.Side, node.Time, node.Center, node.Width);
		}
		if (_marqueeActive && _marqueeMoved)
		{
			var rect = new Rect2(_marqueeStart, Vector2.Zero).Expand(_marqueeCurrent);
			DrawRect(rect, new Color(0.2f, 0.85f, 1f, .12f), true);
			DrawRect(rect, new Color(0.2f, 0.85f, 1f, .9f), false, 2f);
		}
	}

	private void DrawSelectionHandle(int side, double time, double center, double width)
	{
		var track = RuntimeTrack(side);
		var remaining = (float)((time - _cursorTime) * DynamiteUniverse.Game.GameplayVisualMapper.BaseFallSpeedPx);
		var runtimePosition = GameplayStageGeometry.PositionAt(track, center + width * .5, remaining);
		var position = new Vector2(runtimePosition.X, runtimePosition.Y);
		var size = track == Track.Center
			? new Vector2(Mathf.Max(12f, (float)width * GameplayStageGeometry.CenterUnitPx * GameplayStageGeometry.NoteVisualScale), 32f)
			: new Vector2(24f, Mathf.Max(12f, (float)width * 102f * GameplayStageGeometry.NoteVisualScale));
		DrawRect(new Rect2(position - size / 2f, size), new Color(1f, 1f, 1f, .9f), false, 2f);
		DrawCircle(position, 4f, new Color("35e0ff"));
	}
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
		for (var i = first; i <= last && i - first <= 10000; i++)
		{
			var gridTime = EditorTime.BarToSeconds(ExactBarTime.FromFraction(i, GridDivisor), bpms);
			if (gridTime < firstSecond - .001d || gridTime > lastSecond + .001d) continue;
			var barLine = i % GridDivisor == 0;
			if (centerAlpha > 0f)
			{
				var axis = AxisAt(0, gridTime);
				if (axis >= CenterTop && axis <= CenterJudgeY + 49)
					DrawLine(new Vector2(CenterX, axis), new Vector2(CenterX + CenterWidth, axis), new Color("80d9ec", (barLine ? .28f : .1f) * centerAlpha), barLine ? 2 : 1);
			}
			if (leftAlpha > 0f)
			{
				var left = AxisAt(1, gridTime);
				if (left >= LeftJudgeX && left <= LeftJudgeX + GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale)
					DrawLine(new Vector2(left, SideTop), new Vector2(left, SideTop + SideHeight), new Color("80d9ec", .1f * leftAlpha));
			}
			if (rightAlpha > 0f)
			{
				var right = AxisAt(2, gridTime);
				if (right <= RightJudgeX && right >= RightJudgeX - GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale)
					DrawLine(new Vector2(right, SideTop), new Vector2(right, SideTop + SideHeight), new Color("80d9ec", .1f * rightAlpha));
			}
		}
	}
	private float VisibilityAlpha(int side) => (_trackVisibility[side] % 3) switch { 0 => 0f, 1 => .5f, _ => 1f };
	private void DrawBpmEvents()
	{ foreach (var e in BpmEvents) { var y = AxisAt(0, e.Time); if (y < CenterTop - 20 || y > CenterJudgeY + 60) continue; var selected = _selectedEvent == BpmEvents.IndexOf(e); DrawLine(new Vector2(CenterX, y), new Vector2(CenterX + CenterWidth, y), new Color("ff6bb4", selected ? .8f : .4f), selected ? 3 : 1); DrawString(ThemeDB.FallbackFont, new Vector2(CenterX + 18, y - 8), $"BPM {e.Bpm:0.##}", HorizontalAlignment.Left, -1, 16, selected ? Colors.White : new Color("ff6bb4")); } }

	private void RefreshGameplayStage()
	{
		if (_gameplayStage is null) return;
		_gameplayNotes.Clear();
		var ghostIds = new HashSet<int>();
		foreach (var note in Notes)
			AppendGameplayNote(note);
		if (_draft is not null)
		{
			var draft = new AuthoringNote
			{
				Id = "__draft__", Kind = _draft.Kind, Side = _draft.Side,
				Time = _draft.Time, EndTime = _draft.EndTime,
				Center = _draft.Center, Width = _draft.Width,
			};
			if (_draft.TailStage)
				draft.Nodes.Add(new AuthoringPathNode { Id = "__draft_tail__", Time = _draft.EndTime, Center = _draft.Center, Width = _draft.Width });
			var ghostStart = _gameplayNotes.Count;
			AppendGameplayNote(draft);
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
		_pointerInside && _draft is null && _gesture is null && !_marqueeActive &&
		_side != 3 && _tool is >= 1 and <= 3 && PointInsideTrack(_pointerPoint, _side);

	private AuthoringNote? BuildHoverGhost()
	{
		if (!CanBuildHoverGhost()) return null;
		var place = Place(_pointerPoint, _side);
		var anchor = SnapSpatial(place.Center);
		var width = Clamp(SnapWidth(_rememberedWidth), .1, MaxChartCoordinate);
		var left = Clamp(anchor - width * .5, 0, MaxChartCoordinate - width);
		var time = SnapTime(place.Time);
		if (_tool == 3) time = Math.Min(time, Math.Max(0, DurationSeconds - MinimumHoldDuration(time)));
		return new AuthoringNote
		{
			Id = "__hover__",
			Kind = _tool == 2 ? AuthoringNoteKind.Chain : _tool == 3 ? AuthoringNoteKind.Hold : AuthoringNoteKind.Normal,
			Side = _side, Time = time, EndTime = time, Center = left, Width = width,
		};
	}

	private void AppendGameplayNote(AuthoringNote note)
	{
		var headId = _gameplayNotes.Count + 1;
		var tailId = note.Kind == AuthoringNoteKind.Hold ? headId + 1 : -1;
		_gameplayNotes.Add(new ChartNote
		{
				Id = headId,
				SubNoteId = tailId,
				Type = note.Kind switch
				{
					AuthoringNoteKind.Chain => NoteType.Drag,
					AuthoringNoteKind.Hold => NoteType.HoldHead,
					AuthoringNoteKind.Mixer => NoteType.MixerHead,
					AuthoringNoteKind.Mine => NoteType.Mine,
					AuthoringNoteKind.ExTap => NoteType.ExTap,
					AuthoringNoteKind.BarLine => NoteType.BarLine,
					_ => NoteType.Tap,
				},
				Track = note.Side switch
				{ 1 => Track.Left, 2 => Track.Right, _ => Track.Center },
				BarTime = note.Time,
				Position = note.Center,
				Width = note.Width,
				Second = note.Time,
				BakedSecond = note.Time,
				SyncNote = 0,
		});
		if (tailId <= 0) return;
		var tail = note.Nodes.Count > 0 ? note.Nodes[^1] : new AuthoringPathNode
		{
			Time = note.EndTime, Center = note.Center, Width = note.Width,
		};
		_gameplayNotes.Add(new ChartNote
		{
			Id = tailId,
			SubNoteId = -1,
			Type = NoteType.HoldNode,
			Track = note.Side switch
			{ 1 => Track.Left, 2 => Track.Right, _ => Track.Center },
			BarTime = tail.Time,
			Position = tail.Center,
			Width = tail.Width,
			Second = tail.Time,
			BakedSecond = tail.Time,
			SyncNote = 0,
		});
	}
	private Placement Place(Vector2 p, int side)
	{
		var track = RuntimeTrack(side);
		var remaining = track == Track.Center
			? CenterJudgeY - p.Y
			: track == Track.Left ? (p.X - LeftJudgeX) / GameplayStageGeometry.SideDistanceScale : (RightJudgeX - p.X) / GameplayStageGeometry.SideDistanceScale;
		var time = _cursorTime + remaining / PixelsPerSecond;
		var center = GameplayStageGeometry.CenterAtStagePoint(track, p.X, p.Y);
		return new Placement(Clamp(time, 0, DurationSeconds), Clamp(center, 0, MaxChartCoordinate));
	}
	private double TimeFromPoint(Vector2 p) => Place(p, _side == 3 ? 0 : _side).Time;
	private Vector2 CanvasPoint(Vector2 viewportPoint) => GetGlobalTransformWithCanvas().AffineInverse() * viewportPoint;
	private bool PointInsideTrack(Vector2 p, int side) => side == 0 || side == 3
		? p.X >= CenterX && p.X <= CenterX + CenterWidth && p.Y >= CenterTop && p.Y <= CenterJudgeY + 32f
		: side == 1
			? p.X >= LeftJudgeX && p.X <= LeftJudgeX + GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale && p.Y >= SideTop && p.Y <= GameplayStageGeometry.SideY0 + 8f
			: p.X <= RightJudgeX && p.X >= RightJudgeX - GameplayStageGeometry.SidePreviewTravelPx * GameplayStageGeometry.SideDistanceScale && p.Y >= SideTop && p.Y <= GameplayStageGeometry.SideY0 + 8f;
	// The reference editor has no permanent chrome: the complete fixed stage is
	// interactive and the right-click menu is the only expanded control surface.
	private static bool IsAuthoringArea(Vector2 p) => p.X >= 0 && p.X <= 1920 && p.Y >= 0 && p.Y <= 1080;
	private float AxisAt(int side, double t) => side == 0 || side == 3
		? (float)(CenterJudgeY - (t - _cursorTime) * PixelsPerSecond)
		: side == 1
			? (float)(LeftJudgeX + (t - _cursorTime) * PixelsPerSecond * GameplayStageGeometry.SideDistanceScale)
			: (float)(RightJudgeX - (t - _cursorTime) * PixelsPerSecond * GameplayStageGeometry.SideDistanceScale);
	private Rect2 NoteRect(AuthoringNote n, double t) => NoteRect(n.Side, t, n.Center, n.Width);
	private Rect2 NoteRect(int side, double t, double c, double w)
	{
		var track = RuntimeTrack(side);
		var remaining = (float)((t - _cursorTime) * PixelsPerSecond);
		var runtimePosition = GameplayStageGeometry.PositionAt(track, c + w * .5, remaining);
		var position = new Vector2(runtimePosition.X, runtimePosition.Y);
		var size = track == Track.Center
			? new Vector2(Mathf.Max(12f, (float)w * GameplayStageGeometry.CenterUnitPx * GameplayStageGeometry.NoteVisualScale), 32f)
			: new Vector2(24f, Mathf.Max(12f, (float)w * 102f * GameplayStageGeometry.NoteVisualScale));
		return new Rect2(position - size / 2f, size);
	}
	private IReadOnlyList<EditableBpm> CurrentBpms => _document is null
		? Array.Empty<EditableBpm>()
		: _document.SelectedChart.Bpms;
	private double SnapTime(double t)
	{
		var value = Clamp(t, 0, DurationSeconds);
		if (!SnapEnabled || !TimeSnapOverride) return value;
		var snapped = EditorTime.SnapSeconds(value, CurrentBpms, GridDivisor, true);
		return Clamp(EditorTime.BarToSeconds(snapped, CurrentBpms), 0, DurationSeconds);
	}
	private double DraggedLeft(Gesture gesture, Placement pointer, int side, double width, double originalLeft)
	{
		var pixelsPerUnit = side == 0 ? GameplayStageGeometry.CenterUnitPx : GameplayStageGeometry.SideUnitPx;
		if (Math.Abs(pointer.Center - gesture.Anchor.PointerCenter) * pixelsPerUnit <= 1.5d)
			return originalLeft;
		return Clamp(SnapSpatial(gesture.Anchor.CenterAt(pointer.Center)) - width * .5d,
			0, MaxChartCoordinate - width);
	}
	private double SnapSpatial(double v)
	{
		var value = Clamp(v, 0, MaxChartCoordinate);
		if (!SnapEnabled) return value;
		var increment = SpaceSnapOverride ? .1 : .01;
		return Clamp(Math.Floor(value / increment + .5d) * increment, 0, MaxChartCoordinate);
	}
	// JavaScript Math.round (used by DynaMaker) rounds positive half-steps up.
	private static double SnapWidth(double width) => Math.Floor(width * 20d + .5d) / 20d;
	private static Track RuntimeTrack(int side) => side switch { 1 => Track.Left, 2 => Track.Right, _ => Track.Center };

	private ExactBarTime ExactTimeAt(double seconds)
	{
		var bpms = _document is null
			? (IReadOnlyList<EditableBpm>)Array.Empty<EditableBpm>()
			: _document.SelectedChart.Bpms;
		return EditorTime.SnapSeconds(seconds, bpms, GridDivisor, SnapEnabled && TimeSnapOverride);
	}

	private static AuthoringNoteKind CanvasKind(V2NoteType type) => type switch
	{
		V2NoteType.Drag => AuthoringNoteKind.Chain,
		V2NoteType.Hold => AuthoringNoteKind.Hold,
		V2NoteType.Mixer => AuthoringNoteKind.Mixer,
		V2NoteType.ExTap => AuthoringNoteKind.ExTap,
		V2NoteType.Mine => AuthoringNoteKind.Mine,
		V2NoteType.BarLine => AuthoringNoteKind.BarLine,
		_ => AuthoringNoteKind.Normal,
	};

	private static V2NoteType CanvasType(AuthoringNoteKind kind) => kind switch
	{
		AuthoringNoteKind.Chain => V2NoteType.Drag,
		AuthoringNoteKind.Hold => V2NoteType.Hold,
		AuthoringNoteKind.Mixer => V2NoteType.Mixer,
		AuthoringNoteKind.ExTap => V2NoteType.ExTap,
		AuthoringNoteKind.Mine => V2NoteType.Mine,
		AuthoringNoteKind.BarLine => V2NoteType.BarLine,
		_ => V2NoteType.Tap,
	};

	private static EditorTrack CanvasTrack(int side) => side switch
	{
		1 => EditorTrack.Left,
		2 => EditorTrack.Right,
		_ => EditorTrack.Center,
	};
	private double MinimumHoldDuration(double t) => SnapEnabled ? GridStepSecondsAt(t) : .04;
	private double GridStepSecondsAt(double seconds)
	{
		var bpms = CurrentBpms;
		var bar = EditorTime.SecondsToBar(Clamp(seconds, 0, DurationSeconds), bpms);
		var next = bar + ExactBarTime.FromFraction(1, GridDivisor);
		return Math.Max(.000001d, EditorTime.BarToSeconds(next, bpms) - EditorTime.BarToSeconds(bar, bpms));
	}
	private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 1e-9;
	private static double Clamp(double v, double min, double max) => Math.Min(max, Math.Max(min, v));
	private void EmitStatus(string text) => StatusChanged?.Invoke(text);

	public enum AuthoringNoteKind { Normal, Chain, Hold, Mixer, ExTap, Mine, BarLine }
	public sealed class AuthoringPathNode { public string Id { get; init; } = string.Empty; public ExactBarTime TimeExact { get; set; } public double Time { get; set; } public double Center { get; set; } public double Width { get; set; } }
	public sealed class AuthoringNote { public string Id { get; init; } = string.Empty; public AuthoringNoteKind Kind { get; set; } public int Side { get; set; } public ExactBarTime TimeExact { get; set; } public double Time { get; set; } public ExactBarTime EndExact { get; set; } public double EndTime { get; set; } public double Center { get; set; } public double Width { get; set; } public List<AuthoringPathNode> Nodes { get; } = new(); public override string ToString() => $"{Kind} · {Id} · {Time:0.###}s"; }
	public sealed class AuthoringBpmEvent { public string Id { get; init; } = string.Empty; public ExactBarTime TimeExact { get; set; } public double Time { get; set; } public double Bpm { get; set; } public bool Locked { get; init; } }
}

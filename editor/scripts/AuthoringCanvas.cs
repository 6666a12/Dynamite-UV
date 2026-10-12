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

/// <summary>Fixed 1920x1080 DynaMaker authoring stage and its PC mouse model. Every stage
/// constant comes from shared <see cref="GameplayStageGeometry"/>; the canvas only owns the
/// mouse model on top of it.</summary>
public partial class AuthoringCanvas : Control
{
	public const float DesignWidth = GameplayStageGeometry.DesignWidth, DesignHeight = GameplayStageGeometry.DesignHeight;
	// 中轨的可交互/可绘制上限 = 8 小节预览行程顶端（861 − 790 = 71），与 shared 行程语义对齐。
	public const float CenterX = GameplayStageGeometry.CenterX0, CenterWidth = GameplayStageGeometry.CenterUnitPx * (float)EditorStagePlacement.MaximumCenter,
		CenterTop = GameplayStageGeometry.CenterLineY - GameplayStageGeometry.CenterPreviewTravelPx, CenterJudgeY = GameplayStageGeometry.CenterLineY;
	public const float LeftJudgeX = GameplayStageGeometry.LeftLineX, RightJudgeX = GameplayStageGeometry.RightLineX,
		SideTop = GameplayStageGeometry.SideY0 - (float)EditorStagePlacement.MaximumCenter * GameplayStageGeometry.SideUnitPx,
		SideHeight = (float)EditorStagePlacement.MaximumCenter * GameplayStageGeometry.SideUnitPx;
	private const double MaxChartCoordinate = EditorStagePlacement.MaximumCenter;
	// 画布按实机落速滚动，落速真源在 GameplayVisualMapper。
	public const float PixelsPerSecond = GameplayVisualMapper.BaseFallSpeedPx;
	public const double DefaultDurationSeconds = 32d;

	// 编辑层叠加（设计稿 Editor — Main，docs/editor-ui-design.md §4.1）：三档网格、小节号、
	// 右缘小节标尺、底部进度细线。判定线与 Note 由 GameplayStageRenderer 负责，这里不重复画。
	private const float BarNumberX = 1450f;
	private const float BarRulerX = 1762f, BarRulerWidth = 2f;
	private const float BarRulerTop = 90f, BarRulerBottom = 859f;
	private const float ProgressLineY = 1076f, ProgressLineHeight = 2f;
	private const float BarNumberBaseline = 4f;
	/// <summary>字号是**现值属性**不是 const/static readonly：UI SCALE 换档后下一次 <c>_Draw</c>
	/// 就要取到新值，冻结在静态字段里会永远停在启动时的档位（见 <see cref="EditorFonts"/>）。</summary>
	private static int BarNumberSize => EditorFonts.Pt(11);
	// BPM 事件横线的几何与读数（设计稿 bpm-event：x=280..1496 的 2px #FF6BB4 55%，右端 6px 处 15px bold 数值）。
	private const float BpmEventSpanWidth = 1216f, BpmEventLineWidth = 2f, BpmEventLineAlpha = .55f, BpmEventValueGap = 6f;
	private static int BpmEventValueSize => EditorFonts.Pt(15);
	private static readonly Color BpmEventColor = new("ff6bb4");
	// 设计稿 MAIN_SELECTION 的 sel-outline 比 Note 矩形每边外扩 6px（700,770 130×24 → 694,764 142×36）。
	private const float SelectionOutlineGrow = 6f;
	// 小节号之间至少留这么多像素，不够就按 2 的倍数稀疏标注。
	private const double BarNumberSpacingPx = 44d;
	private const int GlowSteps = 4;
	// 网格密度下限：细分线/拍线在屏幕上挤到一起时按档退让，只留更粗的档。
	private const double MinimumGridStepPx = 5d;
	// 无 BPM 事件时 EditorTime 走 1.6s/小节的回退换算，等价 150 BPM。
	private const double FallbackBpm = 150d;
	// 正文字重统一升 Medium：窗口 1600×900 时视口打 0.833 折，Regular 偏细看不清（见 EditorFonts）。
	private static readonly Font DecorFont = EditorFonts.Medium;

	/// <summary>一档网格线的画法：线宽与不透明度，加一层贴身软光（高度 0 = 无光）。</summary>
	private readonly record struct GridTier(float CoreAlpha, float CoreWidth, float GlowHeight, float GlowAlpha);
	private static readonly GridTier BarTier = new(.22f, 2f, 8f, .10f);
	private static readonly GridTier BeatTier = new(.10f, 1.5f, 4f, .06f);
	private static readonly GridTier SubTier = new(.045f, 1f, 0f, 0f);

	/// <summary>游标时刻的现行 BPM；HUD 的 BAR/BPM 与网格密度共用这一处换算。</summary>
	public double CursorBpm => BpmAt(_cursorTime);
	/// <summary>游标所在的整曲小节位置（0 起，保留小数用于标尺与 BAR 显示）。</summary>
	public double CursorBar => EditorTime.SecondsToBar(_cursorTime, CurrentBpms).ToDouble();

	/// 0 center, 1 left, 2 right, 3 events.
	public int CurrentSide { get => _side; set => SetSide(value); }
	/// 1 normal, 2 chain, 3 hold, 4 select.
	public int Tool { get => _tool; set => SetTool(value); }
	public bool SnapEnabled { get; set; } = true;
	/// Temporary modifier flags used by the shell's Z/X free-edit shortcuts.
	/// The shell sets these false while a key is held and restores them on release.
	public bool TimeSnapOverride { get; set; } = true;
	public bool SpaceSnapOverride { get; set; } = true;
	/// Magnetic time grid, owned by the open document.
	public int GridDivisor
	{
		get => _document?.GridDivisor ?? EditorSettings.Current.DefaultGridDivisor;
		private set { if (_document is not null) _document.GridDivisor = Math.Max(1, value); }
	}
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
		// 投影重建后旧的 (noteIndex, pointIndex) hover 可能指向别的对象，丢掉等下一次移动重算。
		_hoverHandle = null;
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
				CurveToNext = note.CurveToNext,
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
					CurveToNext = node.CurveToNext,
					Judge = node.Judge == true,
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
	// 二级选中：单选 Note 时再选中其中一个路径节点（设计稿 §4.3）。用 id 而不是下标保存，
	// 这样命令执行后的 SyncFromDocument 投影重建不会丢选中。
	private string? _selectedNodeId;
	// Original showD/showL/showR cycle hidden -> dim -> full (and repeat).
	private readonly int[] _trackVisibility = { 2, 2, 2 };
	private readonly HashSet<int> _selectedNotes = new();
	private Vector2 _marqueeStart, _marqueeCurrent;
	private bool _marqueeActive, _marqueeMoved, _marqueeAdditive;
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
	private GameplayStageRenderer? _gameplayStage;
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
		/// <summary>双击插入节点后的宽度拖拽：宽度按位移相对起手值走（左缘不动），
		/// 不是把光标当右缘的绝对拖拽，因此起手帧与采样形状逐位一致。</summary>
		public bool RelativeWidth;
		public double WidthPointer;
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
		_gameplayStage = GetNodeOrNull<GameplayStageRenderer>("SharedGameplayStage");
		if (_gameplayStage is null)
		{
			_gameplayStage = new GameplayStageRenderer
			{ Name = "SharedGameplayStage", ZIndex = 0 };
			AddChild(_gameplayStage);
		}
		_gameplayStage.BuildCommunityStageChrome();
		_gameplayStage.ShowControlNodes = false;
		_gameplayStage.SetPreviewDistanceProvider((note, time) =>
			(float)EditorStagePlacement.RemainingPxAtTime(note.Second, time, PixelsPerSecond));
		// 手势前景（读数/幽灵描边/宽度指示/拖尾预览/对齐方块）要压在 shared Note 之上，
		// 单独一个排在音符层之上的兄弟节点负责（本画布自己的 _Draw 在 Note 之下，画导引线）。
		BuildPlacementChrome();
		ApplyBackgroundDim();
		_stageDirty = true;
		QueueRedraw();
	}
	public override void _Process(double delta)
	{
		if (_stageDirty) RefreshGameplayStage();
		StepInteractionVisual();
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
		GridDivisor = EditorSettings.Current.DefaultGridDivisor;
		_stageDirty = true;
		QueueRedraw();
	}

	/// <summary>把设置的默认网格（Settings / EDITOR / DEFAULT GRID）写进当前文档。
	/// v2 不持久化编辑期网格分母，所以打开文档与改动设置时都由这里对齐；
	/// <see cref="EditorDocument.DefaultGridDivisor"/> 仍是 core 的常量兜底，编辑器不再直接用它。</summary>
	public void ApplyDefaultGridDivisor()
	{
		if (_document is null) return;
		GridDivisor = EditorSettings.Current.DefaultGridDivisor;
		_stageDirty = true;
		EmitStatus($"GRID 1/{GridDivisor}");
		QueueRedraw();
	}

	/// <summary>EDITOR / BACKGROUND DIM 的接点：shared 的 <c>GameplayBackdrop</c> 没有压暗参数，
	/// 编辑器按住它的 Modulate 做整体压暗（RGB 乘系数、alpha 不变，所以背景仍是不透明的），
	/// 不叠遮挡层、也不改 shared/client 源码。</summary>
	public void ApplyBackgroundDim()
	{
		if (_gameplayStage is null) return;
		var scale = EditorSettings.Current.BackgroundDimScale;
		var tint = new Color(scale, scale, scale, 1f);
		foreach (var child in _gameplayStage.FindChildren("*", "", true, false))
			if (child is GameplayBackdrop backdrop) backdrop.Modulate = tint;
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
		_selectedNodeId = null;
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
		_hoverHandle = null;
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
					_selectedEvent = -1; _selectedNodeId = null; SelectionChanged?.Invoke(hit.NoteIndex); QueueRedraw(); return;
				}
				SelectNote(hit.NoteIndex);
				// 二级选中：点中路径节点方块时同时选中该节点（设计稿 §4.3），
				// 右键菜单据此出 Node Menu。
				_selectedNodeId = hit.NodeIndex >= 0 && hit.NodeIndex < Notes[hit.NoteIndex].Nodes.Count
					? Notes[hit.NoteIndex].Nodes[hit.NodeIndex].Id
					: null;
				BeginGesture(p, hit);
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
		var rememberedWidth = Clamp(EditorStagePlacement.SnapWidth(_rememberedWidth), .1, MaxChartCoordinate);
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
		if (_draft is not null)
		{
			if (_draft.TailStage) return;
			UpdateWidthDraft(p);
			if (_draft.Kind == AuthoringNoteKind.Hold)
			{
				_draft.TailStage = true; _draft.EndTime = _draft.Time;
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
		var place = Place(p, _draft.Side); if (!_draft.Moved && p.DistanceTo(_draft.StartPoint) < 3) return; _draft.Moved = true;
		var delta = place.Center - _draft.AnchorCenter;
		_draft.Width = Clamp(EditorStagePlacement.SnapWidth(_draft.BaseWidth + Math.Abs(delta) * delta), .1, MaxChartCoordinate);
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
				// 双击插入的宽度拖拽按位移相对起手采样宽度走（左缘不动），其余仍沿用
				// 「光标即右缘」的绝对拖拽。
				var width = _gesture.RelativeWidth
					? _gesture.OriginalNodeWidth + (SnapSpatial(m.Center) - _gesture.WidthPointer)
					: SnapSpatial(m.Center) - node.Center;
				node.Width = Clamp(EditorStagePlacement.SnapWidth(width), .1, MaxChartCoordinate - node.Center);
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
				n.Width = Clamp(EditorStagePlacement.SnapWidth(right - left), .1, MaxChartCoordinate - left);
				n.Center = Clamp(right - n.Width, 0, MaxChartCoordinate - n.Width);
			}
			else
			{
				var right = Clamp(SnapSpatial(m.Center), n.Center + .1, MaxChartCoordinate);
				n.Width = right - n.Center;
			}
			n.Width = Clamp(EditorStagePlacement.SnapWidth(n.Width), .1, MaxChartCoordinate - n.Center);
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
			_selectedNote = _selectedNotes.Count == 1 ? FirstSelectedNote() : -1;
			_selectedEvent = -1;
			_selectedNodeId = null;
			EmitStatus(_selectedNotes.Count == 0 ? "SELECTION CLEARED" : $"{_selectedNotes.Count} NOTES SELECTED");
			SelectionChanged?.Invoke(_selectedNote);
		}
		_marqueeMoved = false;
	}
	private int FirstSelectedNote() { foreach (var index in _selectedNotes) return index; return -1; }
	private void SelectNote(int index) { _selectedNotes.Clear(); _selectedNotes.Add(index); _selectedNote = index; _selectedEvent = -1; _selectedNodeId = null; SelectionChanged?.Invoke(index); EmitStatus("NOTE SELECTED"); }
	private void SelectEvent(int index) { _selectedNotes.Clear(); _selectedNote = -1; _selectedEvent = index; _selectedNodeId = null; SelectionChanged?.Invoke(-1); QueueRedraw(); }
	private bool TryEvent(Vector2 p, out int index) { for (var i = BpmEvents.Count - 1; i >= 0; i--) { if (Math.Abs(p.Y - AxisAt(0, BpmEvents[i].Time)) <= 15 && p.X >= CenterX && p.X <= CenterX + 260) { index = i; return true; } } index = -1; return false; }
	/// <summary>命中顺序：路径节点 → 头部（含左右缘的宽度/移动带）→ 尾部 → 路径身体带状。
	/// 身体判定让长 Hold/Mixer 的任意一点都能选中整条 Note（设计稿 §4 第 3 条），
	/// 因此宽曲线路径不再只有头尾两个可点区域。</summary>
	private Hit HitTest(Vector2 p)
	{
		if (_side == 3) return new Hit(-1, -1, false, false, false);
		for (var i = Notes.Count - 1; i >= 0; i--) { var n = Notes[i]; if (n.Side != _side) continue; for (var j = 0; j < n.Nodes.Count; j++) { var nr = NoteRect(n.Side, n.Nodes[j].Time, n.Nodes[j].Center, n.Nodes[j].Width, NodeRuntimeType(n.Kind)); if (nr.Grow(9).HasPoint(p)) { var nodeEdge = n.Side == 0 ? Math.Abs(p.X - nr.End.X) < 12 : Math.Abs(p.Y - nr.End.Y) < 12; return new Hit(i, j, false, false, nodeEdge); } } var r = NoteRect(n, n.Time); if (r.Grow(9).HasPoint(p)) { var leading = n.Side == 0 ? Math.Abs(p.X - r.Position.X) < 12 : Math.Abs(p.Y - r.Position.Y) < 12; var resizing = n.Side == 0 ? Math.Abs(p.X - r.Position.X) < 12 || Math.Abs(p.X - r.End.X) < 12 : Math.Abs(p.Y - r.Position.Y) < 12 || Math.Abs(p.Y - r.End.Y) < 12; return new Hit(i, -1, resizing, leading, false); } if (n.Kind == AuthoringNoteKind.Hold && NoteRect(n.Side, n.EndTime, n.Center, n.Width, NodeRuntimeType(n.Kind)).Grow(9).HasPoint(p)) return new Hit(i, -1, false, false, false); if (HitPathBody(i, p)) return new Hit(i, -1, false, false, false); }
		return new Hit(-1, -1, false, false, false);
	}

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
		foreach (var index in _selectedNotes)
		{
			if (index < 0 || index >= Notes.Count) continue;
			var note = Notes[index];
			var pathEdit = pathTarget is { } target && target.Index == index && note.Nodes.Count > 0;
			DrawSelectionHandle(NoteRect(note.Side, note.Time, note.Center, note.Width, HeadRuntimeType(note.Kind)));
			if (pathEdit)
				DrawPathHandles(index, note);
			else foreach (var node in note.Nodes)
				DrawSelectionHandle(NoteRect(note.Side, node.Time, node.Center, node.Width, NodeRuntimeType(note.Kind)));
		}
		if (_marqueeActive && _marqueeMoved)
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

	/// <summary>标尺与标尺刻度映射的整曲小节数（沿用画布的时长估算）。</summary>
	private int TotalBars() => Math.Max(1,
		(int)Math.Ceiling(EditorTime.SecondsToBar(DurationSeconds, CurrentBpms).ToDouble()));

	private double BarSecondsAt(double seconds) => 240d / BpmAt(seconds);

	/// <summary>该时刻生效的 BPM：最后一个不晚于它的 BPM 事件（BPM 与 BAR 都用这一处换算）。</summary>
	private double BpmAt(double seconds)
	{
		var bpms = CurrentBpms;
		if (bpms.Count == 0) return FallbackBpm;
		var bar = EditorTime.SecondsToBar(seconds, bpms);
		var bpm = bpms[0].Bpm;
		var resolved = bpms[0].Time;
		foreach (var entry in bpms)
			if (entry.Time <= bar && entry.Time >= resolved) { bpm = entry.Bpm; resolved = entry.Time; }
		return bpm;
	}
	private float VisibilityAlpha(int side) => (_trackVisibility[side] % 3) switch { 0 => 0f, 1 => .5f, _ => 1f };
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
			var alpha = index == _selectedEvent ? 1f : BpmEventLineAlpha;
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
		_pointerInside && _draft is null && _gesture is null && !_marqueeActive &&
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

	/// <summary>显示秒 → 精确 BarTime（与 <see cref="Place"/>、<see cref="SnapTime"/> 同一套
	/// BPM 换算），只给未提交的草稿补求值输入用。</summary>
	private ExactBarTime BarAt(double seconds) =>
		EditorTime.SecondsToBar(Math.Max(0d, seconds), CurrentBpms);

	/// <summary>把一条 authoring 投影追加成运行时 Note 链：Hold/Mixer 走逐段曲线细分，
	/// 其余类型只有一个头点。链接关系（SubNoteId）由 <see cref="BuildRuntimeChain"/> 一次给出。</summary>
	private void AppendGameplayNote(AuthoringNote note, bool headOnly = false) =>
		_gameplayNotes.AddRange(BuildRuntimeChain(note, _gameplayNotes.Count + 1, headOnly));
	private Placement Place(Vector2 p, int side)
	{
		var track = RuntimeTrack(side);
		var time = EditorStagePlacement.TimeAtRemainingPx(RemainingPx(p, track), _cursorTime,
			PixelsPerSecond);
		var center = EditorStagePlacement.CenterAt(CanvasTrack(side), p.X, p.Y);
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
	private static bool IsAuthoringArea(Vector2 p) => p.X >= 0 && p.X <= DesignWidth && p.Y >= 0 && p.Y <= DesignHeight;
	// Shared stage distances are unscaled rail pixels; a side rail only draws them at
	// SideDistanceScale, so a screen point on one has to be divided back into that unit.
	private static float RemainingPx(Vector2 p, Track track) =>
		GameplayStageGeometry.StageDistancePx(track, p.X, p.Y) /
		(track == Track.Center ? 1f : GameplayStageGeometry.SideDistanceScale);
	// A rail axis is the shared stage mapping sampled at chart centre 0: the point keeps only its
	// judge-line distance, so the drawn x/y come straight from GameplayStageGeometry.PositionAt.
	private float AxisAt(int side, double t)
	{
		var track = RuntimeTrack(side);
		var axis = GameplayStageGeometry.PositionAt(track, 0d,
			(float)EditorStagePlacement.RemainingPxAtTime(t, _cursorTime, PixelsPerSecond));
		return track == Track.Center ? axis.Y : axis.X;
	}
	private Rect2 NoteRect(AuthoringNote n, double t) => NoteRect(n.Side, t, n.Center, n.Width, HeadRuntimeType(n.Kind));
	private Rect2 NoteRect(int side, double t, double c, double w, NoteType type)
	{
		var track = RuntimeTrack(side);
		var runtimePosition = GameplayStageGeometry.PositionAt(track, c + w * .5,
			(float)EditorStagePlacement.RemainingPxAtTime(t, _cursorTime, PixelsPerSecond));
		var size = GameplayVisualMapper.SizeFor(type, track, w);
		return new Rect2(new Vector2(runtimePosition.X, runtimePosition.Y) - size / 2f, size);
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
	/// <summary>空间吸附（放置锚点、拖动、宽度边）的唯一步长来源：常规走 Settings / EDITOR / SNAP，
	/// 按住 X 仍走编辑器的 0.01 细步。半档向上取整，与原版 Math.round 一致。</summary>
	private double SnapSpatial(double v)
	{
		var value = Clamp(v, 0, MaxChartCoordinate);
		if (!SnapEnabled) return value;
		if (!SpaceSnapOverride) return EditorStagePlacement.SnapCenter(value, true, MaxChartCoordinate);
		var increment = Math.Clamp(EditorSettings.Current.SnapStep, .01d, MaxChartCoordinate);
		return Math.Clamp(Math.Round(Math.Floor(value / increment + .5d) * increment, 2, MidpointRounding.AwayFromZero),
			0d, MaxChartCoordinate);
	}
	// Note kinds reach the shared renderer through these two mappings, so the authoring
	// overlays (selection outline, hit test) size themselves from the runtime note type.
	private static NoteType HeadRuntimeType(AuthoringNoteKind kind) => kind switch
	{
		AuthoringNoteKind.Chain => NoteType.Drag,
		AuthoringNoteKind.Hold => NoteType.HoldHead,
		AuthoringNoteKind.Mixer => NoteType.MixerHead,
		AuthoringNoteKind.Mine => NoteType.Mine,
		AuthoringNoteKind.ExTap => NoteType.ExTap,
		AuthoringNoteKind.BarLine => NoteType.BarLine,
		_ => NoteType.Tap,
	};
	private static NoteType NodeRuntimeType(AuthoringNoteKind kind) => kind switch
	{
		AuthoringNoteKind.Hold => NoteType.HoldNode,
		AuthoringNoteKind.Mixer => NoteType.MixerNode,
		_ => HeadRuntimeType(kind),
	};
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
	// 投影除几何外还带回曲线与 judge：右键菜单的 Node Menu 直接读这两个值，
	// 不再单独去查文档。
	public sealed class AuthoringPathNode { public string Id { get; init; } = string.Empty; public ExactBarTime TimeExact { get; set; } public double Time { get; set; } public double Center { get; set; } public double Width { get; set; } public V2PathCurve? CurveToNext { get; set; } public bool Judge { get; set; } }
	public sealed class AuthoringNote { public string Id { get; init; } = string.Empty; public AuthoringNoteKind Kind { get; set; } public int Side { get; set; } public ExactBarTime TimeExact { get; set; } public double Time { get; set; } public ExactBarTime EndExact { get; set; } public double EndTime { get; set; } public double Center { get; set; } public double Width { get; set; } public V2PathCurve? CurveToNext { get; set; } public List<AuthoringPathNode> Nodes { get; } = new(); public override string ToString() => $"{Kind} · {Id} · {Time:0.###}s"; }
	public sealed class AuthoringBpmEvent { public string Id { get; init; } = string.Empty; public ExactBarTime TimeExact { get; set; } public double Time { get; set; } public double Bpm { get; set; } public bool Locked { get; init; } }
}

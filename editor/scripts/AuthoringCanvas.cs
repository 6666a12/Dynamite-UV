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
	private readonly CanvasProjection _projection = new();
	public List<AuthoringNote> Notes => _projection.Notes;
	public List<AuthoringBpmEvent> BpmEvents => _projection.BpmEvents;
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

		_projection.Clear();
		_selectedNotes.Clear(); _selectedNote = _selectedEvent = -1;
		// 投影重建后旧的 (noteIndex, pointIndex) hover 可能指向别的对象，丢掉等下一次移动重算。
		_hoverHandle = null;
		_stageDirty = true;
		if (_document is null) { QueueRedraw(); return; }

		_projection.Rebuild(_document.SelectedChart);
		DurationSeconds = _projection.DurationSeconds;
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
	private CanvasPlacementDraft? _draft;
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
		_gesture?.Cancel();
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
	private static double Clamp(double v, double min, double max) => Math.Min(max, Math.Max(min, v));
	private void EmitStatus(string text) => StatusChanged?.Invoke(text);
}

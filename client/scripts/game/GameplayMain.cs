using Godot;
using DynamiteUniverse.Audio;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Game;

/// <summary>
/// MVP 玩法主场景：加载谱面+音频，按 SongClock 生成/移动音符，三判定区
/// （Left 左侧 / Center 底部 / Right 右侧），鼠标点击/触摸判定，F1 切 Auto，
/// Esc/顶部按钮暂停。HUD 与结算按 docs/ui-mock 样式稿实现。
/// 谱面/音频来自 GameSession 选中的谱面包；编辑器和 Internal Testdata 构建可回退开发谱。
/// 音符使用 clean-room 程序化材质，不依赖任何原版素材。
/// </summary>
public partial class GameplayMain : Node2D
{
	private const string FallbackChartPath = "res://testdata/chart_tablear.json";
	private const string FallbackSongPath = "res://testdata/song_tablear.wav";

	// ---- 布局（1920x1080，16:9，用户 2026-08-10 拍板：4:3 上方空间太大；
	//      x 向常量 = 1440 值 ×4/3，y 向不变。几何依据见 docs/gameplay-spec.md §8.6、
	//      docs/video-geometry-analysis.md §8、_rev/notes/NOTE_POSITION_ANALYSIS.md）----
	// Center 判定线：隐形水平线 y=861（谱面确认视频直出实测：亮线体 y 860–862；
	// 21:9 时代的拍板下移值 985 已废弃，勿回退）。
	// 粉色横条（y=632, x 675..1244）是 Mixer 装饰 UI，不是判定线（同视频实测）。
	private const float CenterLineY = 861f;
	private const float MixerBarY = 632f;
	private const float MixerBarLeft = 675f;
	private const float MixerBarLen = 569f;
	private const float CenterTrackX = 960f; // Center 轨横向中心（P=2.0 处）
	// Center x 映射（用户拍板：note 在边上的位置 = 谱面数值的比例映射，分辨率无关）：
	// P 为条左缘，跨域 [P, P+W]，P∈[0,4] 铺满整条下边 [CenterX0, CenterX0+4·PosUnitPx]。
	// 数值来源：手元视频星爆拟合 x_hit = 312.5+204.9·P @1440（n=249，r²=0.9968），
	// 16:9 下 ×4/3。
	private const float CenterX0 = 280f;
	private const float PosUnitPx = 273.2f;
	// 侧轨：竖条从屏幕中央附近生成、向左右边缘外移（实机验证的运动方式）。
	// 命中点 x：iPad 实测 138/1302 @1440 ×4/3 = 184/1736 @1920。
	private const float LeftLineX = 184f;
	private const float RightLineX = 1736f;
	// 侧轨流速 = 正面的 75%（用户拍板）；行程 = 中央两侧 ∓85px → 侧线（691px @1920），
	// 生成提前量 = 691/0.75 ≈ 921px（≈0.9s，正面为 790px/0.77s）
	private const float SideDistScale = 0.75f;
	private const float SideLeadPx = 691f / SideDistScale; // ≈921
	// 侧轨命中 y 使用当前固定的左右对称二维映射：y = 840 - 115 * (P + W/2)。
	private const float SideY0 = 840f;
	private const float SideUnit = 115f;
	// 侧小节线（T9）视觉长度仍按满跨域渲染的换算单位（装饰，非实测映射）
	private const float SidePosUnitPx = 190f;
	// 侧轨条的**视觉长度**单位（≠ 位置单位）：谱面确认视频实测 W=2.0 → 长 ≈204px
	// @1440 ≈ 102px/W（video-geometry-analysis §4/§8，单侧样本但与用户观感一致）。
	private const float SideNoteLenUnitPx = 102f;
	// 下落使用固定二维映射，不做透视投影。Lv10、DropSpeed=1 时恒为
	// 150 BPM 基准的 1026px/s，不再随谱面 BPM 改变。
	private const float BaseFallSpeedPx = 1026f;
	private const float TravelPx = 790f;
	private const double PostHitViewLifetimeSec = 0.77;
	// 音符视觉宽 = Width × 1 Position 单位 × NoteVisualScale（用户拍板条宽压到 95%；
	// 原值 1.0 依据：手元视频实测 W=1 条宽 211±16px @1440 ≈ 位置单位 204.9px @1440）
	private const float NoteVisualScale = 0.95f;

	// 点击区域划分（MVP）：底部横带 → Center；上部的左右边缘带 → 侧轨。
	// Center 轨横向铺满 [171, 1564]，不能再按 x 粗分（P=0 的 note 会落进 Left 区）。
	private const float CenterRegionMinY = 771f; // 判定线上 90px
	private const float LeftRegionMaxX = 400f;
	private const float RightRegionMinX = 1520f;
	// 原版空间扩边公式已确认，但 NSTouchWidth 的设备运行值仍未知。这里集中使用一个
	// 试玩后提高的社区值（谱面 Position 单位）；每侧扩 0.20，Mine 仍不扩边。
	private const double CommunityTouchWidth = 0.40;
	private const int MousePointerId = -1;
	private static readonly Rect2 PauseButtonRect = new(900f, 60f, 120f, 64f);
	private static readonly Color JudgeLineColor = new(0.9f, 0.9f, 0.9f);

	private DynamiteUniverse.Shared.Chart.Chart _chart = null!;
	private LoadedChart _loaded = null!;
	private JudgePlan.Plan _plan = null!;
	private JudgeEngine _engine = null!;
	private SongPlayback _playback = null!;
	private V2IntegrationVerification? _verification;
	private V2IntegrationTrace? _trace;

	// Current pack/chart presentation and score identity stay immutable after startup.
	private GameplayRunContext _run = GameplayRunContext.InternalFallback;

	private List<Note> _notesByTime = new();
	private int _nextSpawn;
	private readonly List<NoteView> _active = new();
	private readonly Dictionary<int, NoteView> _viewByNoteId = new();
	private readonly HashSet<int> _holdTailIds = new();
	private readonly Dictionary<int, (JudgeUnit Unit, JudgeGrade Grade)> _deferredNoteViews = new();

	// 同一帧所有 note 都使用当前 BarTime 采样的流速，但移动距离以音频秒计算：
	// visualDistancePx = (noteSecond - currentSecond) * speed(currentBar)
	//     * playerScale * BaseFallSpeedPx。
	// 该距离可随流速变化而增加，因此已生成 note 必须允许回退出屏并继续保留。
	private DropSpeedMap _dropSpeedMap = DropSpeedMap.Empty;
	private float _fallSpeedMultiplier = 1f;

	// 链/长条连接体（SubNoteId 链接的可视化）：梯形面板段。Hold/Chain 为刚性
	// 形状整体下落，过线部分被判定线裁剪（连续吞噬），见 UpdateViews/ClipToLine
	private sealed class NoteLink
	{
		public int FromId;
		public int ToId;
		public Track Track;
		public float Wa; // 起点半宽（Center=面板半宽，侧轨=缎带半厚）
		public float Wb; // 终点半宽
		public bool IsHold;
		public Color BaseColor;
		public Polygon2D Poly = null!;
		public Line2D? Frame; // Hold 面板的亮描边轮廓（实机面板边缘亮条）
	}
	private readonly List<NoteLink> _links = new();
	private Dictionary<int, Note> _noteById = new();
	private Control _mixerBar = null!;

	private enum PointerAction { Press, Move, Release }
	private readonly record struct PendingPointerInput(
		int Id, Track Track, double Position, double Time, PointerAction Action);
	private sealed class ActivePointer
	{
		public required int Id;
		public required Track Track;
		public required double Position;
		public ContactPhase Phase;
	}
	private sealed class SustainRuntime
	{
		public required SustainPath Path;
		public bool StartResolved;
		public bool Holding;
		public bool Broken;
		public bool EndResolved;
		public double LastContactUpdateTime;
		public double LostContactSecond;
		public double? ContactLostAt;
		public double? GraceDeadline;
		public bool SliderInitialized;
		public double SliderPosition;
		public NoteView? MixerHeadView;
		public GameplaySustainEffect? ContactEffect;
	}
	private readonly List<PendingPointerInput> _pendingPointerInputs = new();
	private readonly Dictionary<int, ActivePointer> _activePointers = new();
	private readonly HashSet<int> _ignoredTouchIds = new();
	private readonly List<TouchSample> _frameTouchSamples = new();
	private readonly List<TouchSample> _frameStartTouchSamples = new();
	private readonly List<PendingPointerInput> _framePointerInputs = new();
	private readonly InputTimeGroupGate _inputTimeGroupGate = new();
	private readonly Dictionary<int, SustainRuntime> _sustainStates = new();
		private bool _auto;
		private bool _finished;
		private bool _paused;
		private bool _playbackStarted;
		private double _startSecond;
		private double _nextLogSec = 10.0;

	// ---- HUD / 菜单节点 ----
	private Node2D _stageRoot = null!;
	private Node2D _noteRoot = null!;
	private Node2D _hudRoot = null!;
	private ColorRect _progressFill = null!;
	private Label _scoreLabel = null!;
	private Label _pillP = null!;
	private Label _pillGr = null!;
	private Label _pillGo = null!;
	private Label _pillM = null!;
	private Label _pillAcc = null!;
	private Label _pillMc = null!;
	private Label _comboLabel = null!;
	private Label _comboSub = null!;
	private Label _judgeFlash = null!;
	private CutButton _pauseBtn = null!;
	// 暂停菜单展示节点（命名保持 presentation 前缀，避免与场景导航状态混用）
	private Control _pauseLayer = null!;
	private ColorRect _presentationPauseBlocker = null!;
	private Control _presentationPauseContent = null!;
	private CutButton _presentationPauseResume = null!;
	private CutButton _presentationPauseRetry = null!;
	private CutButton _presentationPauseQuit = null!;
	private Tween? _presentationPauseTween;
	private bool _presentationPauseMotionInFlight;
	// 结算展示节点
	private Control _resultLayer = null!;
	private Control _presentationResultContent = null!;
	private Control _presentationResultGradeGroup = null!;
	private Control _presentationResultScoreGroup = null!;
	private Control _presentationResultStatsGroup = null!;
	private Control _presentationResultButtonsGroup = null!;
	private Label _presentationResultGradeEcho = null!;
	private Label _presentationResultGradeEchoPink = null!;
	private ColorRect _presentationResultPrelock = null!;
	private ColorRect _presentationResultSignalScan = null!;
	private CutButton _presentationResultBack = null!;
	private CutButton _presentationResultNext = null!;
	private CutButton _presentationResultRetry = null!;
	private Tween? _presentationResultTween;
	private bool _presentationResultMotionInFlight;
	private TextureRect _resultCover = null!;
	private Label _gradeLabel = null!;
	private Control _newRecChip = null!;
	private Label _resultScore = null!;
	private Label _resultAcc = null!;
	private ColorRect _distP = null!;
	private ColorRect _distGr = null!;
	private ColorRect _distGo = null!;
	private ColorRect _distM = null!;
	private Label _resultCountP = null!;
	private Label _resultCountGr = null!;
	private Label _resultCountGo = null!;
	private Label _resultCountM = null!;

	public override void _Ready()
	{
		GameSession.EnsureInit();

		// Chart source: editor/internal verification override, selected package, or internal fallback.
		string songPath;
		_verification = V2IntegrationVerification.FromEnvironment();
		if (_verification is { } verification)
		{
			var selection = verification.Load();
			ApplyLoadedSelection(selection.Pack, selection.Diff, selection.Loaded);
			_loaded = selection.Loaded;
			songPath = _loaded.ResolvedAudioPath;
			_auto = verification.Auto;
		}
		else if (GameSession.CurrentSelection is { } selection)
		{
			var pack = selection.Pack;
			var diff = selection.Diff;
			_loaded = selection.LoadedChart ?? pack.LoadChart(diff);
			GameSession.CommitSelection(pack, diff, _loaded);
			ApplyLoadedSelection(pack, diff, _loaded);
			songPath = _loaded.ResolvedAudioPath;
			_auto = false;
		}
		else if (ChartPack.IsEditorOrInternal)
		{
			var chart = DynamixChartLoader.Load(
				Godot.FileAccess.GetFileAsString(FallbackChartPath));
			var fallbackPreset = V2Integration.PresetFor(_run.LegacyScoreKey);
			var fallbackPlan = JudgePlan.Build(chart, JudgeSettings.ForPreset(fallbackPreset));
			_loaded = new LoadedChart
			{
				RuntimeChart = chart,
				PackId = _run.PackId,
				ChartId = _run.ChartId,
				ResolvedAudioPath = FallbackSongPath,
				NoteCount = fallbackPlan.HeadlineUnitCount,
				DurationSec = fallbackPlan.EndTime + 2.0,
				Preset = fallbackPreset,
				SyncAccentRuntimeIds = V2Integration.DeriveLegacySyncAccents(chart),
			};
			songPath = _loaded.ResolvedAudioPath;
			_auto = true; // 编辑器/Internal 演示默认 AUTO（F1 可切回手动）
		}
		else
		{
			GD.PushError("Public build entered gameplay without a selected chart pack.");
			TransitionDirector.ReportSceneReady(() =>
				TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Back));
			return;
		}

		BuildStage();

		_chart = _loaded.RuntimeChart;
		var preset = _loaded.Preset;
		_plan = JudgePlan.Build(_chart, JudgeSettings.ForPreset(preset));
		_engine = new JudgeEngine(preset, _plan.HeadlineUnitCount);
		foreach (var path in _plan.Sustains.Values)
		{
			_sustainStates[path.HeadId] = new SustainRuntime
			{
				Path = path,
				LastContactUpdateTime = path.StartTime,
			};
			if (path.Kind == SustainKind.Hold)
				_holdTailIds.Add(path.End.Id);
		}
		_fallSpeedMultiplier = (float)GameSession.Settings.FallSpeedMultiplier;
		_notesByTime = _chart.AllNotes.OrderBy(n => n.Second).ToList();
		_noteById = _notesByTime.ToDictionary(n => n.Id);
		_dropSpeedMap = new DropSpeedMap(_chart.DropSpeeds);
		GD.Print($"chart '{_chart.Title}' notes={_notesByTime.Count} " +
				 $"units={_plan.Units.Count} headline={_plan.HeadlineUnitCount} " +
				 $"theoreticalMax={_plan.TheoreticalMax} speed={GameSession.Settings.FallSpeedLevel}");

		_playback = new SongPlayback(this, songPath, GameSession.Settings.TimingOffsetMs,
			_verification);
		// 调试：DYNAMITE_UNIVERSE_START_SEC=起始秒（用于与录屏做同刻对比截图）
		_startSecond = 0.0;
		if (double.TryParse(System.Environment.GetEnvironmentVariable("DYNAMITE_UNIVERSE_START_SEC"),
				out var envStart) && envStart > 0)
			_startSecond = envStart;
		_trace = _verification?.CreateTrace(_loaded);
		if (_verification is not null)
		{
			StartPlayback();
			_verification.SignalReady();
		}
			else
			{
				PrepareGameplayEntryPresentation();
				TransitionDirector.ReportSceneReady(
					ApplyGameplayEntryPresentation,
					StartPlayback);
			}

			GD.Print($"identity pack={_run.PackId} chart={_run.ChartId} " +
			$"ruleset={_run.RulesetId ?? "legacy-direct"} digest={_run.GameplayDigest ?? "none"}");
		RefreshTitleTag();
	}

	private void ApplyLoadedSelection(ChartPack pack, ChartDiff diff, LoadedChart loaded) =>
		_run = GameplayRunContext.FromSelection(pack, diff, loaded);

	private void StartPlayback()
	{
		if (_playbackStarted || _finished || !IsInsideTree())
			return;
		_playbackStarted = true;
		_playback.Play(_startSecond);
	}

	public override void _ExitTree()
	{
		_trace?.Dispose();
		_trace = null;
	}

	public override void _Process(double delta)
	{
		if (!_playbackStarted || _finished || _paused)
		{
			_pendingPointerInputs.Clear();
			return;
		}
		if (_verification is { FixedClockEnabled: true } verification &&
			!verification.CanAdvance)
		{
			_pendingPointerInputs.Clear();
			return;
		}
		_playback.AdvanceFixedFrame();
		var t = _playback.GetSongTime();
		var currentBar = CurrentBarAt(t);

		// Frame order is a behavior contract. Input snapshots, sustain updates, sweeping, mixer
		// finalization and the trace write must remain in this exact order during refactoring.
		SpawnNotes(t, currentBar);
		UpdateViews(t, currentBar, delta);
		FlushPendingInputs(t);
		UpdateSustainStates(t);
		SweepJudges(t);
		FinalizeEndedMixers(t);
		UpdateHud(t);
		_trace?.Write(t, currentBar, _active, _plan, _engine);
		if (_verification?.CompleteFrame() == true)
		{
			GetTree().Quit();
			return;
		}

		if (t >= _nextLogSec) // 周期性日志，便于 headless 验证判定在跑
		{
			_nextLogSec = Math.Floor(t / 10) * 10 + 10;
			GD.Print($"t={t:F1}s score={_engine.NormalizedScore(_plan.TheoreticalMax)} " +
					 $"rawScore={_engine.Score} combo={_engine.Combo} " +
					 $"hp={_engine.Health} activeViews={_active.Count}");
		}

		// 结束检测：时间超过谱面末尾 +2s，或音频流自然播完。
		// 注意：流播完后 GetPlaybackPosition 归零，t 塌缩，不能用 t 做前提——
		// 用时钟自身的 IsPlaying（Play 后恒 true 直到 Pause/Seek）判断"确实在播"。
		var audioEnded = _playback.HasNaturallyEnded;
		if (t > _plan.EndTime + 2.0 || audioEnded)
			ShowResults();
	}

	public override void _Input(InputEvent e)
	{
		if (!_playbackStarted || _finished || _paused || TransitionDirector.IsBusy)
			return;
		if (e is InputEventScreenTouch touch)
		{
			if (touch.Pressed && PauseButtonRect.HasPoint(touch.Position))
			{
				_ignoredTouchIds.Add(touch.Index);
				return;
			}
			if (_ignoredTouchIds.Contains(touch.Index))
			{
				if (!touch.Pressed)
					_ignoredTouchIds.Remove(touch.Index);
				return;
			}
			QueuePointerInput(e);
		}
		else if (e is InputEventScreenDrag drag &&
			!_ignoredTouchIds.Contains(drag.Index))
		{
			QueuePointerInput(e);
		}
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!_playbackStarted || _finished || TransitionDirector.IsBusy)
			return;

		if (e is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Escape)
			{
				TogglePause();
				return;
			}
			if (_paused) return;
			if (key.Keycode == Key.F1)
			{
				_auto = !_auto;
				RefreshTitleTag();
				return;
			}
			if (key.Keycode == Key.F12) // 调试：跳到谱尾前 3s，快速验证结算
			{
				_playback.Seek(Math.Max(0.0, _plan.EndTime - 3.0));
				return;
			}
		}
		if (_paused) return;
		if (e is InputEventMouseButton or InputEventMouseMotion)
			QueuePointerInput(e);
	}

	private void QueuePointerInput(InputEvent e)
	{

		Vector2 pos;
		int pointerId;
		PointerAction action;
		switch (e)
		{
			case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
				pos = mb.Position;
				pointerId = MousePointerId;
				action = mb.Pressed ? PointerAction.Press : PointerAction.Release;
				break;
			case InputEventScreenTouch screenTouch:
				pos = screenTouch.Position;
				pointerId = screenTouch.Index;
				action = screenTouch.Pressed ? PointerAction.Press : PointerAction.Release;
				break;
			case InputEventScreenDrag drag:
				pos = drag.Position;
				pointerId = drag.Index;
				action = PointerAction.Move;
				break;
			case InputEventMouseMotion motion
				when (motion.ButtonMask & MouseButtonMask.Left) != 0:
				pos = motion.Position;
				pointerId = MousePointerId;
				action = PointerAction.Move;
				break;
			default:
				return;
		}

		var track = RegionOf(pos);
		var t = _playback.GetSongTime();
		_pendingPointerInputs.Add(new PendingPointerInput(
			pointerId, track, TrackPositionOf(track, pos), t, action));
	}

	// ---- 输入 ----

	private static Track RegionOf(Vector2 pos) =>
		pos.Y > CenterRegionMinY ? Track.Center :
		pos.X < LeftRegionMaxX ? Track.Left :
		pos.X > RightRegionMinX ? Track.Right :
		Track.Center;

	private static double TrackPositionOf(Track track, Vector2 pos) => track switch
	{
		Track.Center => (pos.X - CenterX0) / PosUnitPx,
		_ => (SideY0 - pos.Y) / SideUnit,
	};

	private void FlushPendingInputs(double frameTime)
	{
		_frameTouchSamples.Clear();
		_frameStartTouchSamples.Clear();
		_framePointerInputs.Clear();
		var framePresses = new List<(TouchSample Touch, double Time)>();
		var frameReleases = new List<(ActivePointer Pointer, double Time)>();
		foreach (var pointer in _activePointers.Values)
		{
			pointer.Phase = ContactPhase.Stationary;
			var sample = new TouchSample(
				pointer.Id, pointer.Track, pointer.Position, pointer.Phase);
			_frameTouchSamples.Add(sample);
			_frameStartTouchSamples.Add(sample);
		}
		_framePointerInputs.AddRange(_pendingPointerInputs);

		// 先按到达顺序更新触点；press 判定推迟到完整帧快照形成以后，确保
		// Tap/Drag/Mine 共用同一个目标时间仲裁，而不是依赖 Godot 事件顺序。
		foreach (var input in _pendingPointerInputs)
		{
			switch (input.Action)
			{
				case PointerAction.Press:
				{
					var pointer = new ActivePointer
					{
						Id = input.Id,
						Track = input.Track,
						Position = input.Position,
						Phase = ContactPhase.Began,
					};
					_activePointers[input.Id] = pointer;
					ReplaceFrameTouch(new TouchSample(
						input.Id, input.Track, input.Position, ContactPhase.Began));
					framePresses.Add((new TouchSample(
						input.Id, input.Track, input.Position, ContactPhase.Began),
						input.Time));
					break;
				}
				case PointerAction.Move:
				{
					var phase = ContactPhase.Moved;
					if (_activePointers.TryGetValue(input.Id, out var existing) &&
						existing.Phase == ContactPhase.Began)
						phase = ContactPhase.Began;
					var pointer = existing ?? new ActivePointer
					{
						Id = input.Id,
						Track = input.Track,
						Position = input.Position,
					};
					pointer.Track = input.Track;
					pointer.Position = input.Position;
					pointer.Phase = phase;
					_activePointers[input.Id] = pointer;
					ReplaceFrameTouch(new TouchSample(
						input.Id, input.Track, input.Position, phase));
					break;
				}
				case PointerAction.Release:
					if (_activePointers.TryGetValue(input.Id, out var released))
						frameReleases.Add((released, input.Time));
					_activePointers.Remove(input.Id);
					for (var i = _frameTouchSamples.Count - 1; i >= 0; i--)
					{
						if (_frameTouchSamples[i].Id == input.Id)
							_frameTouchSamples.RemoveAt(i);
					}
					break;
			}
		}

		// 原版一次 Manual 更新共享 JudgeState.time：从本帧所有 press 和持续
		// Contact/Mine 候选中锁定时间最早的一组，同刻多押继续放行。
		_inputTimeGroupGate.Reset();
		var targetTime = FindEarliestInputTarget(frameTime, framePresses);
		if (targetTime.HasValue)
			_inputTimeGroupGate.TryLock(targetTime.Value);
		foreach (var press in framePresses)
			OnPress(press.Touch, press.Time, _inputTimeGroupGate);
		foreach (var release in frameReleases)
			RecordHoldRelease(release.Pointer, release.Time);

		_pendingPointerInputs.Clear();
	}

	private void RecordHoldRelease(ActivePointer pointer, double releaseTime)
	{
		foreach (var state in _sustainStates.Values)
		{
			if (state.Path.Kind != SustainKind.Hold || !state.StartResolved ||
				state.EndResolved || state.Path.Track != pointer.Track)
				continue;
			var judgedReleaseTime = Math.Min(releaseTime, state.Path.EndTime);
			var bounds = state.Path.BoundsAt(judgedReleaseTime);
			if (InputJudgeRules.Overlaps(bounds, pointer.Position,
				CommunityTouchWidth))
				state.ContactLostAt ??= judgedReleaseTime;
		}
	}

	private void ReplaceFrameTouch(TouchSample sample)
	{
		for (var i = _frameTouchSamples.Count - 1; i >= 0; i--)
		{
			if (_frameTouchSamples[i].Id == sample.Id)
				_frameTouchSamples.RemoveAt(i);
		}
		_frameTouchSamples.Add(sample);
	}

	private double? FindEarliestInputTarget(double frameTime,
		IReadOnlyList<(TouchSample Touch, double Time)> framePresses)
	{
		// JudgePlan 按目标时间排序，因此首个可被本帧任一输入覆盖的单元
		// 就是本批唯一允许命中的时间组。
		foreach (var u in _plan.Units)
		{
			if (u.Judged ||
				u.Kind is not (UnitKind.Input or UnitKind.Contact or UnitKind.Mine))
				continue;

			foreach (var press in framePresses)
			{
				// 本家的共享时间锁只出现在尚未到点的 early 分支；
				// late 输入逐触点扫描，不参与本批目标时刻竞争。
				if (press.Touch.Track != u.Track || u.Time < press.Time)
					continue;
				if (PressCanJudge(u, press.Touch.Track,
					press.Touch.Position, press.Time))
					return u.Time;
			}
			if (u.Time >= frameTime &&
				u.Kind is UnitKind.Contact or UnitKind.Mine &&
				FrameTouchCanJudge(u, frameTime))
				return u.Time;
		}

		return null;
	}

	private bool FrameTouchCanJudge(JudgeUnit unit, double time)
	{
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return false;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		var prefect = _engine.Settings.PrefectSec * unit.WindowScale;
		if (unit.Kind == UnitKind.Contact)
		{
			if (time > unit.Time + _engine.Settings.MissSec * unit.WindowScale)
				return false;
			return _frameTouchSamples.Any(touch => touch.Track == unit.Track &&
				InputJudgeRules.AcceptsContactPhase(
					unit.Time, time, prefect, touch.Phase) &&
				InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth));
		}
		if (unit.Kind == UnitKind.Mine)
			return _frameTouchSamples.Any(touch => touch.Track == unit.Track &&
				InputJudgeRules.AcceptsMinePhase(
					unit.Time, time, prefect, touch.Phase) &&
				InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth,
					expandByTouchWidth: false));
		return false;
	}

	private bool PressCanJudge(JudgeUnit unit, Track track, double position, double time)
	{
		if (unit.Track != track || !_noteById.TryGetValue(unit.NoteId, out var note))
			return false;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		if (!PressTimeCanJudge(unit, time))
			return false;
		return InputJudgeRules.Overlaps(bounds, position, CommunityTouchWidth,
			expandByTouchWidth: unit.Kind != UnitKind.Mine);
	}

	private bool PressTimeCanJudge(JudgeUnit unit, double time)
	{
		if (unit.Kind == UnitKind.Mine)
			return InputJudgeRules.AcceptsMinePhase(unit.Time, time,
				_engine.Settings.PrefectSec * unit.WindowScale, ContactPhase.Began);
		return unit.Kind == UnitKind.Input &&
			_engine.InWindow(unit.Time, time, unit.WindowScale);
	}

	private void OnPress(TouchSample touch, double t, InputTimeGroupGate timeGroupGate)
	{
		// Each Note scans the complete input snapshot independently. A hit does not
		// consume the touch, so same-time overlapping Notes can share one Press.
		var matches = InputJudgeRules.MatchingCandidates(
			_plan.Units,
			touch,
			u => u.Track,
			u =>
			{
				var note = _noteById[u.NoteId];
				return InputJudgeRules.Bounds(note.Position, note.Width);
			},
			u => !u.Judged &&
				u.Kind is UnitKind.Input or UnitKind.Mine &&
				_noteById.ContainsKey(u.NoteId) &&
				timeGroupGate.Allows(u.Time, t) &&
				PressTimeCanJudge(u, t),
			CommunityTouchWidth,
			expandByTouchWidthFor: u => u.Kind != UnitKind.Mine);
		foreach (var u in matches)
		{
			if (u.Kind == UnitKind.Mine)
			{
				_engine.ApplyMine(touched: true);
				u.Judged = true;
				ResolveNoteView(u, JudgeGrade.Miss);
				FlashJudge("MINE!");
				continue;
			}

			var r = _engine.Judge(u.Time, t, u.WindowScale);
			ApplyUnit(u, r.Grade, r.Resolution);
			u.Judged = true;
			ResolveSustainStart(u, r.Grade, touch, t);
			ResolveNoteView(u, r.Grade);
			FlashJudge(GradeText(r));
		}
	}

	private void ResolveSustainStart(JudgeUnit unit, JudgeGrade grade,
		TouchSample? touch, double resolvedTime)
	{
		if (unit.SustainHeadId is not { } headId ||
			!_sustainStates.TryGetValue(headId, out var state))
			return;
		if (unit.Category is not (ScoreCategory.HoldStart or ScoreCategory.MixerStart))
			return;

		state.StartResolved = true;
		state.LastContactUpdateTime = Math.Max(state.Path.StartTime, resolvedTime);
		state.LostContactSecond = 0.0;
		state.ContactLostAt = null;
		state.GraceDeadline = null;
		if (state.Path.Kind == SustainKind.Hold)
		{
			state.Holding = grade != JudgeGrade.Miss;
			if (grade == JudgeGrade.Miss)
				FailRemainingHold(state);
			return;
		}

		// MixerStart 的头判只影响头本身的得分。漏头时若当前帧已通过
		// Body 接回，不能再把连接状态清掉。
		if (grade != JudgeGrade.Miss)
		{
			state.Holding = true;
			state.SliderPosition = touch?.Position ?? state.Path.BoundsAt(resolvedTime).Center;
			state.SliderInitialized = true;
		}
	}

	private void UpdateSustainStates(double t)
	{
		foreach (var state in _sustainStates.Values)
		{
			if (state.EndResolved)
				continue;

			if (state.Path.Kind == SustainKind.Mixer)
			{
				if (t >= state.Path.StartTime)
					UpdateMixerState(state, t);
				continue;
			}

			if (!state.StartResolved || !state.Holding || state.Broken)
				continue;
			UpdateHoldState(state, t);
		}
	}

	private void UpdateHoldState(SustainRuntime state, double t)
	{
		var activeUntil = Math.Min(t, state.Path.EndTime);
		var from = Math.Max(state.LastContactUpdateTime, state.Path.StartTime);
		var bounds = state.Path.BoundsAt(activeUntil);
		var touch = FindNearestTouch(state.Path.Track, bounds.Center,
			bounds, expandByTouchWidth: true);
		var touching = _auto || touch.HasValue;
		UpdateHoldHeadContactView(state, touching);
		UpdateSustainEffect(state, touching, bounds, bounds.Center);
		if (activeUntil <= from)
			return;

		if (touching)
		{
			state.LostContactSecond = 0.0;
			state.ContactLostAt = null;
			state.GraceDeadline = null;
		}
		else
		{
			if (state.ContactLostAt is null)
			{
				var lostAt = from;
				var loss = SustainJudgementRules.BeginHoldContactLoss(
					lostAt, _chart.BpmAtSeconds(lostAt), _engine.Settings);
				state.ContactLostAt = loss.LostAt;
				state.GraceDeadline = loss.Deadline;
			}
			state.LostContactSecond = Math.Max(0.0,
				activeUntil - state.ContactLostAt.Value);
		}

		state.LastContactUpdateTime = activeUntil;
		if (state.ContactLostAt is { } lost && state.GraceDeadline is { } deadline)
		{
			var loss = new SustainJudgementRules.HoldContactLoss(
				lost, deadline - lost, deadline);
			var settlementTime = SustainJudgementRules.HoldSettlementTime(
				state.Path.EndTime, loss);
			if (SustainJudgementRules.ShouldBreakHold(
				activeUntil, state.Path.EndTime, loss))
			{
				BreakHold(state, lost);
			}
			else if (settlementTime == state.Path.EndTime &&
				activeUntil >= settlementTime)
			{
				SettleReleasedHold(state, lost);
			}
		}
	}

	private void UpdateHoldHeadContactView(SustainRuntime state, bool connected)
	{
		if (!_viewByNoteId.TryGetValue(state.Path.HeadId, out var view))
			return;
		if (connected)
		{
			view.RestoreHoldContact();
			view.Position = PositionAt(state.Path.Head, 0f);
		}
		else
			view.BeginRecoverableHoldFallthrough();
	}

	private void UpdateMixerState(SustainRuntime state, double t)
	{
		var activeUntil = Math.Min(t, state.Path.EndTime);
		var bounds = state.Path.BoundsAt(activeUntil);
		var anchor = state.SliderInitialized ? state.SliderPosition : bounds.Center;
		var touch = FindNearestTouch(state.Path.Track, anchor, bounds,
			expandByTouchWidth: true);

		if (_auto)
		{
			state.Holding = true;
			state.SliderInitialized = true;
			state.SliderPosition = bounds.Center;
		}
		else
		{
			state.Holding = touch.HasValue;
			if (touch.HasValue)
			{
				state.SliderInitialized = true;
				state.SliderPosition = Math.Clamp(
					touch.Value.Position, bounds.Left, bounds.Right);
			}
		}

		state.LastContactUpdateTime = activeUntil;
		UpdateMixerHeadView(state);
		UpdateSustainEffect(state, state.Holding, bounds, state.SliderPosition);
	}

	private void FinalizeEndedMixers(double t)
	{
		foreach (var state in _sustainStates.Values)
		{
			if (state.Path.Kind != SustainKind.Mixer || state.EndResolved ||
				t < state.Path.EndTime)
				continue;
			state.Holding = false;
			state.EndResolved = true;
			ReleaseMixerHeadView(state);
		}
	}

	private void UpdateMixerHeadView(SustainRuntime state)
	{
		if (!state.Holding)
		{
			if (state.MixerHeadView != null)
				state.MixerHeadView.Visible = false;
			return;
		}

		if (state.MixerHeadView == null)
		{
			state.MixerHeadView = NoteView.Create(state.Path.Head,
				SizeFor(state.Path.Head), NoteVisualSpec.Mixer);
			state.MixerHeadView.Name = $"MixerSlider_{state.Path.HeadId}";
			state.MixerHeadView.ZIndex = 2;
			_noteRoot.AddChild(state.MixerHeadView);
		}

		state.MixerHeadView.Visible = true;
		state.MixerHeadView.Position = TrackPositionAtLine(
			state.Path.Track, state.SliderPosition);
		state.MixerHeadView.SetDepthAlpha(1f);
	}

	private static Vector2 TrackPositionAtLine(Track track, double position) =>
		track switch
		{
			Track.Center => new Vector2(
				CenterX0 + (float)position * PosUnitPx, CenterLineY),
			Track.Left => new Vector2(
				LeftLineX, SideY0 - (float)position * SideUnit),
			_ => new Vector2(
				RightLineX, SideY0 - (float)position * SideUnit),
		};

	private static void ReleaseMixerHeadView(SustainRuntime state)
	{
		state.MixerHeadView?.QueueFree();
		state.MixerHeadView = null;
		ReleaseSustainEffect(state);
	}

	private void UpdateSustainEffect(SustainRuntime state, bool connected,
		NoteBounds bounds, double position)
	{
		// Hold and Mixer contact effects are available on all three tracks.
		if (!connected)
		{
			ReleaseSustainEffect(state);
			return;
		}

		if (state.ContactEffect == null)
		{
			state.ContactEffect = new GameplaySustainEffect
			{
				Name = $"{state.Path.Kind}Contact_{state.Path.HeadId}",
				Kind = state.Path.Kind == SustainKind.Hold
					? SustainEffectKind.Hold : SustainEffectKind.Mixer,
				Accent = state.Path.Kind == SustainKind.Hold ? NoteVisualSpec.Hold : NoteVisualSpec.Mixer,
				Rotation = state.Path.Track == Track.Center ? 0f : Mathf.Pi * 0.5f,
				ZIndex = 4,
			};
			_noteRoot.AddChild(state.ContactEffect);
		}

		state.ContactEffect.Position = TrackPositionAtLine(state.Path.Track, position);
		state.ContactEffect.SpanPx = SustainSpanPx(state.Path.Track, bounds);
	}

	private static float SustainSpanPx(Track track, NoteBounds bounds)
	{
		var width = (float)Math.Max(0.0, bounds.Right - bounds.Left);
		return Mathf.Max(24f, width * (track == Track.Center
			? PosUnitPx : SideNoteLenUnitPx) * NoteVisualScale);
	}

	private static void ReleaseSustainEffect(SustainRuntime state)
	{
		state.ContactEffect?.QueueFree();
		state.ContactEffect = null;
	}

	private TouchSample? FindNearestTouch(Track track, double anchor,
		NoteBounds? requiredBounds = null, bool expandByTouchWidth = true) =>
		FindNearestTouchIn(_frameTouchSamples, track, anchor,
			requiredBounds, expandByTouchWidth);

	private static TouchSample? FindNearestTouchIn(IEnumerable<TouchSample> samples,
		Track track, double anchor, NoteBounds? requiredBounds = null,
		bool expandByTouchWidth = true)
	{
		TouchSample? best = null;
		var bestDistance = double.MaxValue;
		foreach (var touch in samples)
		{
			if (touch.Track != track)
				continue;
			if (requiredBounds.HasValue && !InputJudgeRules.Overlaps(
				requiredBounds.Value, touch.Position, CommunityTouchWidth,
				expandByTouchWidth))
				continue;
			var distance = Math.Abs(touch.Position - anchor);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = touch;
			}
		}
		return best;
	}

	private bool MixerConnectedAt(SustainRuntime state, double tickTime, NoteBounds bounds)
	{
		var samples = _frameStartTouchSamples.ToDictionary(sample => sample.Id);
		foreach (var input in _framePointerInputs)
		{
			if (input.Time > tickTime)
				continue;
			switch (input.Action)
			{
				case PointerAction.Press:
					samples[input.Id] = new TouchSample(input.Id, input.Track,
						input.Position, ContactPhase.Began);
					break;
				case PointerAction.Move:
				{
					var phase = samples.TryGetValue(input.Id, out var current) &&
						current.Phase == ContactPhase.Began
						? ContactPhase.Began : ContactPhase.Moved;
					samples[input.Id] = new TouchSample(input.Id, input.Track,
						input.Position, phase);
					break;
				}
				case PointerAction.Release:
					samples.Remove(input.Id);
					break;
			}
		}
		return FindNearestTouchIn(samples.Values, state.Path.Track,
			bounds.Center, bounds, expandByTouchWidth: true).HasValue;
	}

	private void BreakHold(SustainRuntime state, double releaseTime)
	{
		if (state.Broken || state.EndResolved)
			return;
		state.Broken = true;
		state.Holding = false;
		if (_viewByNoteId.TryGetValue(state.Path.HeadId, out var headView))
			headView.CommitHoldMissFallthrough();
		ReleaseSustainEffect(state);
		SettleReleasedHold(state, releaseTime);
	}

	private void SettleReleasedHold(SustainRuntime state, double releaseTime)
	{
		if (state.EndResolved)
			return;
		state.EndResolved = true;
		foreach (var settlement in SustainJudgementRules.SettleReleasedHold(
			_plan.Units, state.Path.HeadId, releaseTime, _engine))
		{
			_deferredNoteViews[settlement.Unit.NoteId] =
				(settlement.Unit, settlement.Grade);
			if (settlement.Unit.Category == ScoreCategory.HoldEnd)
				FlashJudge(GradeText(settlement.Grade, settlement.Timing));
		}
	}

	private void FailRemainingHold(SustainRuntime state)
	{
		if (state.EndResolved)
			return;
		state.Broken = true;
		state.Holding = false;
		state.EndResolved = true;
		ReleaseSustainEffect(state);
		foreach (var unit in SustainJudgementRules.FailRemainingHold(
			_plan.Units, state.Path.HeadId, _engine))
		{
			// 判定立即结算；未来节点的视觉仍等实际到线后再进入 Miss 生命周期。
			_deferredNoteViews[unit.NoteId] = (unit, JudgeGrade.Miss);
		}
	}

	// ---- 判定扫尾 ----

	private void SweepJudges(double t)
	{
		// 单元按时间排序；每帧全量扫描（数千单元，开销可忽略）。
		foreach (var u in _plan.Units)
		{
			if (u.Judged) continue;
			if (u.Time - t > _engine.Settings.MissSec * u.WindowScale)
				break; // 之后的单元都还远在窗口外
			JudgeOne(u, t);
		}
		_inputTimeGroupGate.Reset();
	}

	private void JudgeOne(JudgeUnit u, double t)
	{
		var miss = _engine.Settings.MissSec;
		switch (u.Kind)
		{
			case UnitKind.Auto: // 保留给无需输入的普通自动单元
				if (t >= u.Time)
				{
					ApplyUnit(u, JudgeGrade.Prefect, JudgeResolution.Prefect);
					u.Judged = true;
					ResolveNoteView(u, JudgeGrade.Prefect);
				}
				break;

			case UnitKind.HoldPoint:
				JudgeHoldingPoint(u, t);
				break;

			case UnitKind.Mine:
				JudgeMine(u, t);
				break;

			case UnitKind.Contact:
				JudgeContact(u, t);
				break;

			case UnitKind.Input:
				if (_auto && t >= u.Time)
				{
					var r = _engine.Judge(u.Time, u.Time, u.WindowScale); // Auto：精确时刻判定
					ApplyUnit(u, r.Grade, r.Resolution);
					u.Judged = true;
					ResolveSustainStart(u, r.Grade, null, u.Time);
					ResolveNoteView(u, JudgeGrade.Prefect);
				}
				else if (t > u.Time + miss * u.WindowScale)
				{
					ApplyUnit(u, JudgeGrade.Miss, JudgeResolution.AutoMiss);
					u.Judged = true;
					ResolveSustainStart(u, JudgeGrade.Miss, null, t);
					ResolveNoteView(u, JudgeGrade.Miss);
					FlashJudge("MISS");
				}
				break;

			case UnitKind.HoldEnd:
				ResolveHoldEnd(u, t);
				break;

		}
	}

	private void JudgeContact(JudgeUnit unit, double t)
	{
		var prefect = _engine.Settings.PrefectSec * unit.WindowScale;
		var miss = _engine.Settings.MissSec * unit.WindowScale;
		if (t < unit.Time - prefect)
			return;
		if (t > unit.Time + miss)
		{
			ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Miss);
			FlashJudge("MISS");
			return;
		}
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		if (_auto && t >= unit.Time)
		{
			ApplyUnit(unit, JudgeGrade.Prefect, JudgeResolution.Prefect);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Prefect);
			return;
		}
		foreach (var touch in _frameTouchSamples)
		{
			if (touch.Track != unit.Track ||
				!InputJudgeRules.AcceptsContactPhase(
					unit.Time, t, prefect, touch.Phase) ||
				!InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth))
				continue;
			if (!_inputTimeGroupGate.Allows(unit.Time, t))
				return;
			var result = _engine.Judge(unit.Time, t, unit.WindowScale);
			ApplyUnit(unit, result.Grade, result.Resolution);
			unit.Judged = true;
			ResolveNoteView(unit, result.Grade);
			FlashJudge(GradeText(result));
			return;
		}
	}

	private void JudgeMine(JudgeUnit unit, double t)
	{
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return;
		var prefect = _engine.Settings.PrefectSec * unit.WindowScale;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		foreach (var touch in _frameTouchSamples)
		{
			if (touch.Track != unit.Track ||
				!InputJudgeRules.AcceptsMinePhase(unit.Time, t, prefect, touch.Phase) ||
				!InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth,
					expandByTouchWidth: false))
				continue;
			if (!_inputTimeGroupGate.Matches(unit.Time))
				return;
			_engine.ApplyMine(touched: true);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Miss);
			FlashJudge("MINE!");
			return;
		}
		if (t >= unit.Time)
		{
			_engine.ApplyMine(touched: false);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Prefect, emitEffect: false);
		}
	}

	private void JudgeHoldingPoint(JudgeUnit unit, double t)
	{
		if (t < unit.Time)
			return;
		if (unit.SustainHeadId is not { } headId ||
			!_sustainStates.TryGetValue(headId, out var state))
		{
			ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			unit.Judged = true;
			return;
		}

		if (unit.Category == ScoreCategory.HoldHolding)
		{
			// 头仍在 Late 窗内时保留节点；头一旦接起或 Miss，节点会在同帧结算。
			if (!state.StartResolved)
				return;
			unit.Judged = true;
			if (_auto || (state.StartResolved && !state.Broken &&
				(state.Holding || state.ContactLostAt is not null)))
				ApplyUnit(unit, JudgeGrade.Prefect, JudgeResolution.Prefect);
			else
				ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			return;
		}

		unit.Judged = true;
		var tickBounds = state.Path.BoundsAt(unit.Time);
		var connectedAtTick = _auto || MixerConnectedAt(state, unit.Time, tickBounds);
		ApplyUnit(unit, connectedAtTick ? JudgeGrade.Prefect : JudgeGrade.Miss,
			connectedAtTick ? JudgeResolution.Prefect : JudgeResolution.AutoMiss);
	}

	private void ResolveHoldEnd(JudgeUnit unit, double t)
	{
		if (t < unit.Time || unit.Judged)
			return;
		SustainRuntime? state = null;
		if (unit.SustainHeadId is { } headId)
			_sustainStates.TryGetValue(headId, out state);
		var grade = JudgeGrade.Miss;
		var resolution = JudgeResolution.AutoMiss;
		var timing = HitTiming.Exact;
		if (_auto || state is { StartResolved: true, ContactLostAt: null,
			Holding: true, Broken: false })
		{
			grade = JudgeGrade.Prefect;
			resolution = JudgeResolution.Prefect;
		}
		else if (state is { StartResolved: true, ContactLostAt: { } releaseTime,
			Broken: false })
		{
			var judgedReleaseTime = Math.Min(releaseTime, unit.Time);
			var result = _engine.Judge(unit.Time, judgedReleaseTime, unit.WindowScale);
			grade = result.Grade;
			resolution = result.Resolution;
			timing = result.Timing;
		}
		ApplyUnit(unit, grade, resolution);
		unit.Judged = true;
		if (state != null)
		{
			if (_viewByNoteId.TryGetValue(state.Path.HeadId, out var headView))
			{
				if (grade != JudgeGrade.Miss)
				{
					headView.RestoreHoldContact();
					headView.MarkJudged(NoteVisualSpec.Hold);
				}
				else
					headView.CommitHoldMissFallthrough();
			}
			state.Holding = false;
			state.EndResolved = true;
			ReleaseSustainEffect(state);
		}
		ResolveNoteView(unit, grade);
		FlashJudge(GradeText(grade, timing));
	}

	private void ApplyUnit(JudgeUnit unit, JudgeGrade grade,
		JudgeResolution? resolution = null) =>
		_engine.Apply(unit.Category, grade, unit.AffectsCombo,
			unit.AffectsJudgeCounts, resolution);

	// ---- 音符生成与移动 ----

	private void SpawnNotes(double t, double currentBar)
	{
		while (_nextSpawn < _notesByTime.Count)
		{
			var peek = _notesByTime[_nextSpawn];
			// 侧轨流速 75% 但行程不变 → 生成提前量更短（SideLeadPx），晚些生成
			var threshold = peek.Track == Track.Center ? TravelPx : SideLeadPx;
			if (VisualDistanceFor(peek, t, currentBar) > threshold)
				break;
			var n = _notesByTime[_nextSpawn++];
			if (ShouldRenderStaticNote(n))
			{
					var view = NoteView.Create(n, SizeFor(n), ColorFor(n),
						_loaded.SyncAccentRuntimeIds.Contains(n.Id));

				view.Position = PositionFor(n, t, currentBar);
				view.SetDepthAlpha(n.Track == Track.Center ? TopFadeAlpha(view.Position.Y) : 1f);
				_noteRoot.AddChild(view);
				_viewByNoteId[n.Id] = view;
				_active.Add(view);
			}
			if (n.SubNoteId != -1 && _noteById.TryGetValue(n.SubNoteId, out var toNote))
			{
				var isHold = n.Type is NoteType.HoldHead or NoteType.HoldNode;
				Line2D? frame = null;
				if (isHold)
				{
					// 亮描边轮廓（原版面板边缘亮条）
					frame = new Line2D
					{
						Width = 3f,
						DefaultColor = new Color(1.0f, 0.80f, 0.35f, 0.9f),
						Visible = false,
					};
					_noteRoot.AddChild(frame);
				}
				var poly = new Polygon2D
				{
					Color = LinkColorFor(n),
					Visible = false, // ZIndex 0：判定线之上、音符之下
				};
				_noteRoot.AddChild(poly);
				_links.Add(new NoteLink
				{
					FromId = n.Id,
					ToId = n.SubNoteId,
					Track = n.Track,
					Wa = LinkHalfFor(n),
					Wb = LinkHalfFor(toNote),
					IsHold = isHold,
					BaseColor = LinkColorFor(n),
					Poly = poly,
					Frame = frame,
				});
			}
		}
	}

	private bool ShouldRenderStaticNote(Note note) => note.Type switch
	{
		// Hold 中间节点只负责折线控制，尾节点仍保留实体材质。
		NoteType.HoldNode => _holdTailIds.Contains(note.Id),
		// Mixer Body 由连接段渲染；运行时头只在当前接上时出现在判定线。
		NoteType.MixerNode => false,
		_ => true,
	};

	private void UpdateViews(double t, double currentBar, double delta)
	{
		FlushDeferredNoteViews(t);
		for (var i = _active.Count - 1; i >= 0; i--)
		{
			var v = _active[i];
			var recycle = v.TickTtl(delta) ||
				(v.Model.Type == NoteType.BarLine && t >= v.Model.Second);
			var startedFallthrough = false;
			if (!v.IsResolved && !v.IsMissFalling &&
				UsesOrdinaryMissFallthrough(v.Model.Type))
			{
				var remaining = VisualDistanceFor(v.Model, t, currentBar);
				if (remaining < 0f)
				{
					v.BeginMissFallthrough(
						PastLineDistanceFromRemaining(v.Model.Track, remaining));
					startedFallthrough = true;
					recycle |= v.MissDistancePx >= 64f;
				}
			}
			if (v.IsMissFalling || v.IsRecoverableHoldFalling)
			{
				if (!startedFallthrough)
				{
					var step = MissFallSpeedPx(v.Model, t, currentBar) * (float)delta;
					recycle |= v.AdvanceMissFallthrough(step);
				}
			}
			else if (!(v.IsLineAnchored && v.Model.Type == NoteType.HoldHead))
				recycle |= t - v.Model.Second > PostHitViewLifetimeSec;

			if (recycle)
			{
				_viewByNoteId.Remove(v.Model.Id);
				v.QueueFree();
				_active.RemoveAt(i);
				continue;
			}
			// 不按屏幕边界回收：变速期间 note 可能先退出画面，随后再次进入。
			v.Position = v.IsMissFalling || v.IsRecoverableHoldFalling
				? PositionPastLine(v.Model, v.MissDistancePx)
				: v.IsLineAnchored
					? PositionAt(v.Model, 0f)
					: PositionForActiveView(v.Model, t, currentBar);
			v.SetDepthAlpha(v.IsMissFalling || v.IsRecoverableHoldFalling ||
				v.Model.Track != Track.Center
				? 1f : TopFadeAlpha(v.Position.Y));
		}

		// 更新链/长条连接体：Hold/Chain 是**刚性形状**整体对着判定线下落
		// （节点只是形状顶点，端点位置不钳制）；过线部分被判定线裁剪，
		// 裁剪点沿线连续滑动＝连续吞噬；整段过线后销毁。全程可见，
		// 不要求节点视图在场。
		for (var i = _links.Count - 1; i >= 0; i--)
		{
			var l = _links[i];
			var from = _noteById[l.FromId];
			var to = _noteById[l.ToId];
			var rA = VisualDistanceFor(from, t, currentBar);
			var rB = VisualDistanceFor(to, t, currentBar);
			var a = PositionAt(from, rA);
			var b = PositionAt(to, rB);
			var wa = l.Wa;
			var wb = l.Wb;
			var anchoredEarlyHold = IsEarlyHoldAnchored(l, rA);
			if (anchoredEarlyHold)
				a = PositionAt(from, 0f);
			if (!ClipToLine(l.Track, ref a, ref b, ref wa, ref wb))
			{
				l.Poly.QueueFree();
				l.Frame?.QueueFree();
				_links.RemoveAt(i);
				continue;
			}
			l.Poly.Polygon = l.Track == Track.Center
				? new[]
				{
					new Vector2(a.X - wa, a.Y), new Vector2(a.X + wa, a.Y),
					new Vector2(b.X + wb, b.Y), new Vector2(b.X - wb, b.Y),
				}
				: new[]
				{
					new Vector2(a.X, a.Y - wa), new Vector2(b.X, b.Y - wb),
					new Vector2(b.X, b.Y + wb), new Vector2(a.X, a.Y + wa),
				};
			var proximity = 1f;
			if (l.Track != Track.Center)
			{
				var nearestRem = anchoredEarlyHold
					? 0f : Mathf.Max(0f, Mathf.Min(rA, rB));
				proximity = 1f - Mathf.Clamp(nearestRem / SideLeadPx, 0f, 1f);
			}
			var fillAlpha = l.BaseColor.A;
			if (l.Track == Track.Center)
			{
				// 长 Hold 的远端可能仍在屏幕上方很远，不能用整段中点控制渐入：
				// 否则头部已经入场，身体仍会因中点位于透明区而长时间完全不可见。
				// 连接方向按时间从近端 a 指向远端 b；取更靠近判定线的一端，
				// 让面板身体与最先进入画面的 Hold 头同步渐入。
				fillAlpha *= TopFadeAlpha(Mathf.Max(a.Y, b.Y));
			}
			else if (l.IsHold)
				fillAlpha = Mathf.Lerp(0.08f, 0.28f, proximity);
			l.Poly.Color = new Color(l.BaseColor, fillAlpha);
			l.Poly.Visible = true;
			if (l.Frame != null)
			{
				// 描边 = 面板轮廓线（首尾相接闭合）
				l.Frame.Points = l.Track == Track.Center
					? new[]
					{
						new Vector2(a.X - wa, a.Y), new Vector2(a.X + wa, a.Y),
						new Vector2(b.X + wb, b.Y), new Vector2(b.X - wb, b.Y),
						new Vector2(a.X - wa, a.Y),
					}
					: new[]
					{
						new Vector2(a.X, a.Y - wa), new Vector2(b.X, b.Y - wb),
						new Vector2(b.X, b.Y + wb), new Vector2(a.X, a.Y + wa),
						new Vector2(a.X, a.Y - wa),
					};
				var frameAlpha = l.Track == Track.Center
					? 0.9f * TopFadeAlpha(Mathf.Max(a.Y, b.Y))
					: Mathf.Lerp(0.25f, 0.9f, proximity);
				l.Frame.DefaultColor = new Color(1.0f, 0.80f, 0.35f, frameAlpha);
				l.Frame.Visible = true;
			}
		}
	}

	private bool IsEarlyHoldAnchored(NoteLink link, float nearRemainingPx) =>
		link.IsHold && nearRemainingPx > 0f &&
		_sustainStates.TryGetValue(link.FromId, out var state) &&
		state.Path.Kind == SustainKind.Hold && state.StartResolved &&
		state.Holding && !state.Broken && !state.EndResolved;

	private void FlushDeferredNoteViews(double t)
	{
		if (_deferredNoteViews.Count == 0)
			return;

		var resolvedIds = new List<int>();
		foreach (var (noteId, pending) in _deferredNoteViews)
		{
			if (!_noteById.TryGetValue(noteId, out var note) || t < note.Second)
				continue;
			ResolveNoteView(pending.Unit, pending.Grade);
			resolvedIds.Add(noteId);
		}
		foreach (var noteId in resolvedIds)
			_deferredNoteViews.Remove(noteId);
	}

	// 将段 a→b（中心线端点，半宽 wa/wb）按判定线裁剪：a 端已过线时用
	// 中心线与判定线的交点取而代之（面板边缘沿线滑动）；整段过线返回 false。
	// 链按时间有序，from 端恒领先于 b 端，故 b 不会单独过线。
	private static bool ClipToLine(Track track, ref Vector2 a, ref Vector2 b,
		ref float wa, ref float wb)
	{
		bool Beyond(Vector2 p) => track switch
		{
			Track.Center => p.Y > CenterLineY,
			Track.Left => p.X < LeftLineX,
			_ => p.X > RightLineX,
		};
		if (Beyond(b))
			return false; // b 过线则 a 早已过线：整段消耗完毕
		if (!Beyond(a))
			return true;
		var k = track switch
		{
			Track.Center => (CenterLineY - a.Y) / (b.Y - a.Y),
			Track.Left => (LeftLineX - a.X) / (b.X - a.X),
			_ => (RightLineX - a.X) / (b.X - a.X),
		};
		a += (b - a) * k;
		wa += (wb - wa) * k;
		return true;
	}

	private double CurrentBarAt(double second) =>
		_loaded.V2Timeline != null
			? _loaded.V2Timeline.ToBarTime(ChartAudioSecond(second)).ToDouble()
			: _chart.Sections.Count > 0 ? _chart.SecondsToBarTime(second) : second;

	private double ChartAudioSecond(double songClockSecond) =>
		songClockSecond - GameSession.Settings.TimingOffsetMs / 1000.0;

	private float VisualDistanceFor(Note n, double t, double currentBar)
	{
		if (_loaded.V2Scroll != null && _loaded.V2Timeline != null)
		{
			var currentSecond = ChartAudioSecond(t);
			var currentExact = _loaded.V2Timeline.ToBarTime(currentSecond);
			var noteSecond = _loaded.ExactTimesByRuntimeId.TryGetValue(n.Id, out var exact)
				? _loaded.V2Timeline.ToSeconds(exact)
				: n.Second;
			return (float)VisualScrollMath.DistancePixels(
				noteSecond, currentSecond, BaseFallSpeedPx,
				_loaded.V2Scroll.SpeedAt(currentExact), _fallSpeedMultiplier);
		}
		return (float)VisualScrollMath.DistancePixels(
			n.Second, t, BaseFallSpeedPx, _dropSpeedMap.SpeedAt(currentBar),
			_fallSpeedMultiplier);
	}

	private Vector2 PositionFor(Note n, double t, double currentBar)
	{
		// 生成时尚未到线；保留钳制只防止跳转启动时把新视图放到线外。
		var rem = Mathf.Max(0f, VisualDistanceFor(n, t, currentBar));
		return PositionAt(n, rem);
	}

	private Vector2 PositionForActiveView(Note n, double t, double currentBar)
	{
		var rem = VisualDistanceFor(n, t, currentBar);
		if (!MovesPastJudgeLineContinuously(n.Type))
			rem = Mathf.Max(0f, rem);
		return PositionAt(n, rem);
	}

	private static bool MovesPastJudgeLineContinuously(NoteType type) =>
		UsesOrdinaryMissFallthrough(type);

	private double VisualScrollAt(double t, double currentBar)
	{
		if (_loaded.V2Scroll != null && _loaded.V2Timeline != null)
		{
			var currentExact = _loaded.V2Timeline.ToBarTime(ChartAudioSecond(t));
			return _loaded.V2Scroll.SpeedAt(currentExact);
		}
		return _dropSpeedMap.SpeedAt(currentBar);
	}

	private float MissFallSpeedPx(Note note, double t, double currentBar)
	{
		var speed = (float)VisualScrollMath.FallthroughSpeedPixelsPerSecond(
			BaseFallSpeedPx, VisualScrollAt(t, currentBar), _fallSpeedMultiplier);
		if (note.Track != Track.Center)
			speed *= SideDistScale;
		return Mathf.Max(60f, speed);
	}

	private static Vector2 PositionPastLine(Note note, float distancePx)
	{
		var linePosition = PositionAt(note, 0f);
		return note.Track switch
		{
			Track.Center => linePosition + new Vector2(0f, distancePx),
			Track.Left => linePosition - new Vector2(distancePx, 0f),
			_ => linePosition + new Vector2(distancePx, 0f),
		};
	}

	private static float DistancePastJudgeLine(Track track, Vector2 position) =>
		Mathf.Max(0f, track switch
		{
			Track.Center => position.Y - CenterLineY,
			Track.Left => LeftLineX - position.X,
			_ => position.X - RightLineX,
		});

	private static float PastLineDistanceFromRemaining(Track track, float remaining) =>
		Mathf.Max(0f, -remaining * (track == Track.Center ? 1f : SideDistScale));

	private static Vector2 PositionAt(Note n, float rem) => n.Track switch
	{
		// Center：匀速落向底部隐形判定线。Position = 条的**左缘**（跨域 [P, P+W]，
		// 视频实证：Tablear 结尾 W=5.5 大 Hold 左缘落屏 23.1%≈左缘模型 23.5%，
		// 中心模型 -8.9% 不成立），视觉中心 = P + W/2。
		Track.Center => new Vector2(
			CenterX0 + ((float)n.Position + (float)n.Width * 0.5f) * PosUnitPx,
			CenterLineY - rem),
		// Left：竖条从屏幕中央附近生成、向左缘判定线外移；
		// y = SideY0 - (P+W/2)·SideUnit（左右对称，见顶部注释）
		Track.Left => new Vector2(
			LeftLineX + rem * SideDistScale,
			SideY0 - ((float)n.Position + (float)n.Width * 0.5f) * SideUnit),
		// Right：完全对称
		_ => new Vector2(
			RightLineX - rem * SideDistScale,
			SideY0 - ((float)n.Position + (float)n.Width * 0.5f) * SideUnit),
	};

	private static float TopFadeAlpha(float y) =>
		y <= 128f ? 0f : y >= 200f ? 1f : (y - 128f) / 72f;

	private static Vector2 SizeFor(Note n)
	{
		// 长轴 = Width × 1 Position 单位（NoteVisualScale≈1，见顶部注释）
		var axis = Mathf.Max(12f, (float)n.Width * PosUnitPx * NoteVisualScale);
		if (n.Track != Track.Center)
		{
			// 侧轨竖条长度单位 ≠ 位置单位：谱面确认视频逐帧实测 W=2.0 条长
			// ≈204px @1440（≈102px/W，约为位置单位 205 的一半，
			// video-geometry-analysis §4/§8）；位置中心仍按 P+W/2 摆放。
			// 小节线是全宽装饰，仍按跨域全长渲染。
			if (n.Type == NoteType.BarLine)
			{
				var barLen = Mathf.Max(12f, (float)n.Width * SidePosUnitPx * NoteVisualScale);
				return new Vector2(10f, barLen);
			}
			// 侧轨 Hold 节点：小帽（本体由连接体宽面板呈现，实机为面板边缘亮条）
			if (n.Type is NoteType.HoldHead or NoteType.HoldNode)
				return new Vector2(17f, 30f);
			var len = Mathf.Max(12f, (float)n.Width * SideNoteLenUnitPx * NoteVisualScale);
			return new Vector2(17f, len); // 厚 17–24px @2340 实测 ≈ 14–20px @1920（×0.82）
		}
		return n.Type switch
		{
			NoteType.HoldHead or NoteType.HoldNode => new Vector2(axis, 26f),
			NoteType.MixerHead or NoteType.MixerNode => new Vector2(axis, 40f),
			NoteType.BarLine => new Vector2(axis, 6f), // 小节线：细线
			_ => new Vector2(axis, 24f), // Tap/EX-Tap/Drag/Mine：实测高 22–24px
		};
	}

	private static Color ColorFor(Note n) => NoteVisualSpec.BaseColor(n.Type);

	private void ResolveNoteView(JudgeUnit unit, JudgeGrade grade,
		bool emitEffect = true)
	{
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return;

		var isMine = note.Type == NoteType.Mine;
		var isMixer = note.Type is NoteType.MixerHead or NoteType.MixerNode;
		var isMiss = grade == JudgeGrade.Miss;
		if (isMiss && isMixer)
		{
			RecycleNoteViewImmediately(note.Id);
			return;
		}

		var missFallthrough = isMiss && UsesOrdinaryMissFallthrough(note.Type);
		var keepsLineHead = note.Type is NoteType.HoldHead or NoteType.HoldNode or
			NoteType.MixerHead or NoteType.MixerNode;
		var accent = HitAccentFor(note.Type);
		if (!_viewByNoteId.TryGetValue(unit.NoteId, out var view) &&
			!isMiss && unit.Category == ScoreCategory.HoldStart)
		{
			// 极晚接头时本体可能已经完成普通下穿；重新建立在线 Hold 头。
				view = NoteView.Create(note, SizeFor(note), ColorFor(note),
					_loaded.SyncAccentRuntimeIds.Contains(note.Id));

			view.Position = PositionAt(note, 0f);
			view.SetDepthAlpha(1f);
			_noteRoot.AddChild(view);
			_viewByNoteId[note.Id] = view;
			_active.Add(view);
		}
		if (view != null)
		{
			if (missFallthrough)
				view.BeginMissFallthrough(
					DistancePastJudgeLine(note.Track, view.Position));
			else if (keepsLineHead)
			{
				view.AnchorToJudgeLine();
				view.MarkJudged(accent,
					unit.Category == ScoreCategory.HoldStart ? -1f : 0.25f);
			}
			else
				RecycleNoteViewImmediately(note.Id);
		}

		// 普通 Miss 只下穿淡出；Mixer Miss 静默回收；Mine 触发危险爆发。
		if (missFallthrough || !emitEffect)
			return;

		var size = SizeFor(note);
		_noteRoot.AddChild(new GameplayHitBloom
		{
			Position = PositionAt(note, 0f),
			Rotation = note.Track == Track.Center ? 0f : Mathf.Pi * 0.5f,
			Accent = accent,
			Kind = HitEffectKindFor(note.Type),
			SpanPx = note.Track == Track.Center ? size.X : size.Y,
			Strength = isMine ? 1f : grade switch
			{
				JudgeGrade.Prefect => 1f,
				JudgeGrade.Great => 0.82f,
				JudgeGrade.Good => 0.68f,
				_ => 0.75f,
			},
			ZIndex = 3,
		});
	}

	private void RecycleNoteViewImmediately(int noteId)
	{
		if (!_viewByNoteId.Remove(noteId, out var view))
			return;
		_active.Remove(view);
		view.QueueFree();
	}

	private static bool UsesOrdinaryMissFallthrough(NoteType type) => type is
		NoteType.Tap or NoteType.ExTap or NoteType.Drag or
		NoteType.HoldHead or NoteType.HoldNode;

	private static Color HitAccentFor(NoteType type) => NoteVisualSpec.HitAccent(type);

	private static HitEffectKind HitEffectKindFor(NoteType type) => NoteVisualSpec.HitEffect(type);

	// 连接体半宽：Hold 是带亮边的宽面板；Mixer 只有节点间细连线，
	// 不生成完整宽面板；其他链沿用窄缎带。
	private static float LinkHalfFor(Note n)
	{
		if (n.Type is NoteType.HoldHead or NoteType.HoldNode)
		{
			return n.Track == Track.Center
				? Mathf.Max(4f, (float)n.Width * PosUnitPx * 0.4f)
				: Mathf.Max(6f,
					(float)n.Width * SideNoteLenUnitPx * 0.5f * NoteVisualScale);
		}
		if (n.Type is NoteType.MixerHead or NoteType.MixerNode)
			return 5f;
		return n.Track == Track.Center
			? Mathf.Max(4f, (float)n.Width * PosUnitPx * 0.4f)
			: 4f;
	}

	private static Color LinkColorFor(Note n) => NoteVisualSpec.LinkColor(n.Type);

	private static string GradeText(JudgeResult r) => GradeText(r.Grade, r.Timing);

	private static string GradeText(JudgeGrade grade, HitTiming timing) => grade switch
	{
		JudgeGrade.Prefect => "PREFECT",
		JudgeGrade.Great => $"GREAT {(timing == HitTiming.Early ? "E" : "L")}",
		JudgeGrade.Good => $"GOOD {(timing == HitTiming.Early ? "E" : "L")}",
		_ => "MISS",
	};
}

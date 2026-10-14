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
/// （Left 左侧 / Center 底部 / Right 右侧），鼠标点击/触摸判定，Auto 由设置
/// （GameSettings.AutoEnabled）或验证环境变量驱动，Esc/顶部按钮暂停。HUD 与结算按 docs/ui-mock 样式稿实现。
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
	// 侧轨流速 = 正面 × 行程比（691/790 ≈ 0.8747，实验性改动，用户拍板试手）：
	// 生成提前量因此与正面一致（691/(691/790)=790px ≈0.77s），正侧同刻 note 同时入场。
	private const float SideDistScale = 691f / TravelPx;
	private const float SideLeadPx = 691f / SideDistScale; // = 790，与 TravelPx 相同
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

	// Touch projection intentionally allows overlapping track candidates.
	// A lower corner can therefore judge both the Center and adjacent side track.
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
	private readonly Dictionary<NoteType, Stack<NoteView>> _noteViewPools = new();
	private readonly List<GameplayHitBloom> _activeBlooms = new();
	private readonly Stack<GameplayHitBloom> _bloomPool = new();
	private readonly List<GameplayHitParticles> _activeHitParticles = new();
	private readonly Stack<GameplayHitParticles> _hitParticlePool = new();
	private readonly List<SustainRuntime> _effectOwners = new();
	private readonly List<SustainRuntime> _particleOwners = new();
	private readonly List<GameplaySustainParticles> _drainingParticles = new();
	private readonly Dictionary<bool, Stack<NoteLink>> _linkPools = new();
	private readonly HashSet<int> _holdTailIds = new();
	private readonly HashSet<int> _failedHoldNoteIds = new();
	private const float HoldMissFadeDurationSec = 0.18f;
	private readonly List<FadingHoldView> _fadingHoldViews = new();

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
		public Vector2[] PolyScratch = new Vector2[4]; // 复用缓冲，避免每帧 new
		public Vector2[] FrameScratch = new Vector2[5];
	}
	private readonly List<NoteLink> _links = new();
	private Dictionary<int, Note> _noteById = new();

	private sealed class FadingHoldView
	{
		public required Node2D Root;
		public float RemainingSec = HoldMissFadeDurationSec;
		public readonly List<NoteView> Notes = new();
		public readonly List<NoteLink> Links = new();
	}

	private enum PointerAction { Press, Move, Release }
	private readonly record struct PendingPointerInput(
		int Id, Vector2 ScreenPosition, double Time, PointerAction Action);
	private sealed class ActivePointer
	{
		public required int Id;
		public required Vector2 ScreenPosition;
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
		public HoldContactState Contact;
		public MixerRuntime? Mixer;
		public readonly List<JudgeUnit> MixerTicks = new();
		public int MixerTickCursor;
		public readonly Dictionary<JudgeUnit, JudgeGrade> MixerGrades = new();
		public NoteView? MixerHeadView;
		public GameplaySustainEffect? ContactEffect;
		public GameplaySustainParticles? ContactParticles;
	}
	private readonly List<PendingPointerInput> _pendingPointerInputs = new();
	private readonly Dictionary<int, ActivePointer> _activePointers = new();
	private readonly HashSet<int> _ignoredTouchIds = new();
	private readonly List<TouchSample> _frameTouchSamples = new();
	private readonly List<TouchSample> _frameStartTouchSamples = new();
	private readonly List<PendingPointerInput> _framePointerInputs = new();
	private readonly List<(TouchSample Touch, double Time)> _framePresses = new();
	private readonly HoldContactFrame _holdContactFrame = new();
	private readonly V2InputProtection _v2InputProtection = new();
	private readonly List<TouchSample>[] _touchesByTrack =
		[[], [], []];
	private readonly List<JudgeUnit>[] _pressUnitsByTrack =
		[[], [], []];
	private readonly int[] _trackPressWindowStart = new int[3];
	private readonly Dictionary<int, double> _runtimeNoteSeconds = new();
	private readonly Dictionary<int, SustainRuntime> _sustainStates = new();
	private readonly Stack<GameplaySustainEffect> _sustainEffectPool = new();
	private readonly Stack<GameplaySustainParticles> _sustainParticlePool = new();
	private int _sustainPoolCapacity = VisualPoolPlanning.PoolMinimumCapacity;
	private int _sweepCursor;
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
	private ResultScreen _resultScreen = null!;

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
			_auto = GameSession.Settings.AutoEnabled;
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
			_auto = true; // 编辑器/Internal 演示默认 AUTO
		}
		else
		{
			GD.PushError("Public build entered gameplay without a selected chart pack.");
			TransitionDirector.ReportSceneReady(() =>
				TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Back));
			return;
		}

		BuildStage();
		GameplayHitParticles.Prewarm(_noteRoot);

		_chart = _loaded.RuntimeChart;
		var preset = _loaded.Preset;
		_plan = JudgePlan.Build(_chart, JudgeSettings.ForPreset(preset));
		BuildRuntimeJudgeIndex();
		_engine = new JudgeEngine(preset, _plan.HeadlineUnitCount);
		foreach (var path in _plan.Sustains.Values)
		{
			_sustainStates[path.HeadId] = new SustainRuntime
			{
				Path = path,
				LastContactUpdateTime = path.StartTime,
				Mixer = path.Kind == SustainKind.Mixer
					? new MixerRuntime(path, MixerTimeline.BarAt(_chart, path)) : null,
			};
			if (path.Kind == SustainKind.Hold)
				_holdTailIds.Add(path.End.Id);
		}
		foreach (var unit in _plan.Units)
			if (unit.Category == ScoreCategory.MixerHolding && unit.SustainHeadId is { } headId)
				_sustainStates[headId].MixerTicks.Add(unit);
		_fallSpeedMultiplier = (float)GameSession.Settings.FallSpeedMultiplier;
		_notesByTime = _chart.AllNotes.OrderBy(n => n.Second).ToList();
		_noteById = _notesByTime.ToDictionary(n => n.Id);
		PrepareVisualPools();
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

	private void BuildRuntimeJudgeIndex()
	{
		for (var i = 0; i < 3; i++)
		{
			_pressUnitsByTrack[i].Clear();
			foreach (var unit in _plan.Units)
				if ((int)unit.Track == i &&
					unit.Kind is UnitKind.Input or UnitKind.Mine)
					_pressUnitsByTrack[i].Add(unit);
		}
		_runtimeNoteSeconds.Clear();
		if (_loaded.V2Timeline is not null)
			foreach (var pair in _loaded.ExactTimesByRuntimeId)
				_runtimeNoteSeconds[pair.Key] = _loaded.V2Timeline.ToSeconds(pair.Value);
	}

	private void PrepareVisualPools()
	{
		var peaks = VisualPoolPlanning.Analyze(
			_plan.Units,
			_notesByTime,
			_plan.Sustains.Values);
		var activities = new List<VisualPoolPlanning.TypedActivity>();
		foreach (var note in _notesByTime.Where(ShouldRenderStaticNote))
		{
			var lead = (note.Track == Track.Center ? TravelPx : SideLeadPx) /
				(BaseFallSpeedPx * _fallSpeedMultiplier);
			var end = note.Second + PostHitViewLifetimeSec;
			if (_plan.Sustains.TryGetValue(note.Id, out var sustain))
				end = sustain.EndTime + PostHitViewLifetimeSec;
			activities.Add(new(note.Type, new(note.Second - lead, end)));
		}
		var typedPeaks = VisualPoolPlanning.PeaksByType(activities);
		foreach (var pair in typedPeaks)
		{
			var pool = new Stack<NoteView>();
			_noteViewPools[pair.Key] = pool;
			var model = _notesByTime.First(note => VisualPoolPlanning.PoolType(note.Type) == pair.Key);
			for (var i = 0; i < VisualPoolPlanning.ClampCapacity(pair.Value); i++)
			{
				var view = NoteView.Create(model, SizeFor(model), ColorFor(model));
				_noteRoot.AddChild(view);
				view.ResetForPool();
				pool.Push(view);
			}
		}
		for (var i = 0; i < peaks.HitCapacity; i++)
		{
			var bloom = new GameplayHitBloom();
			_noteRoot.AddChild(bloom);
			bloom.ResetForPool();
			_bloomPool.Push(bloom);
			_hitParticlePool.Push(GameplayHitParticles.CreatePooled(_noteRoot));
		}
		foreach (var isHold in new[] { false, true })
		{
			var pool = new Stack<NoteLink>();
			_linkPools[isHold] = pool;
			for (var i = 0; i < peaks.NoteCapacity; i++)
				pool.Push(CreateLink(isHold));
		}
		_sustainPoolCapacity = peaks.SustainCapacity;
		for (var i = 0; i < _sustainPoolCapacity; i++)
		{
			var effect = new GameplaySustainEffect { Visible = false };
			_noteRoot.AddChild(effect);
			_sustainEffectPool.Push(effect);
			var particles = GameplaySustainParticles.CreatePooled(_noteRoot);
			_sustainParticlePool.Push(particles);
		}
		GD.Print($"visual pools hits={peaks.PeakHits}/{peaks.HitCapacity} " +
			$"notes={peaks.PeakNotes}/{peaks.NoteCapacity} " +
			$"sustains={peaks.PeakSustains}/{peaks.SustainCapacity}");
	}

	private void StartPlayback()
	{
		if (_playbackStarted || _finished || !IsInsideTree())
			return;
		_playbackStarted = true;
		_playback.Play(_startSecond);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMWindowFocusOut)
			ClearPointerState();
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
		// Auto 演示不接受任何会导致判定的输入：结果严格全 Perfect，触点一律不进入判定管线。
		if (_auto)
			return;

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

		var t = _playback.GetSongTime();
		_pendingPointerInputs.Add(new PendingPointerInput(
			pointerId, pos, t, action));
	}

	// ---- 输入 ----

	private static TouchTrackMask ProjectedTrackMask(Vector2 pos) =>
		InputJudgeRules.ProjectedTrackMask(pos.X, pos.Y, CenterRegionMinY,
			LeftRegionMaxX, RightRegionMinX);

	private static TouchTrackMask MaskFor(Track track) => track switch
	{
		Track.Center => TouchTrackMask.Center,
		Track.Left => TouchTrackMask.Left,
		Track.Right => TouchTrackMask.Right,
		_ => TouchTrackMask.None,
	};

	/// <summary>
	/// 屏幕位置 → **显示空间**坐标（未镜像）：中轨是面板横向坐标，侧轨是沿判定线的纵向坐标
	/// （纵向不受镜像影响）。镜像**不在这里**施加——判定侧的唯一入口是
	/// <see cref="ChartTouchOf"/> / <see cref="ChartPointOf"/>。
	/// </summary>
	private static double DisplayPositionOf(Track track, Vector2 pos) => track switch
	{
		Track.Center => (pos.X - CenterX0) / PosUnitPx,
		_ => (SideY0 - pos.Y) / SideUnit,
	};

	/// <summary>
	/// 屏幕触点 → 判定用 <see cref="TouchSample"/> 的**唯一**入口：
	/// 屏幕区域 → 显示轨道 → 镜像逆映射 → 谱面 (轨道, 坐标)。
	/// <see cref="GameplayMirror.Display"/> 自反，正反两个方向共用同一函数，所以视觉镜像
	/// 与输入逆映射永远互为逆运算；判定侧只认谱面坐标，**别处不要再镜像一次**——
	/// 施加两次会互相抵消（历史上正是这里与 <see cref="DisplayPositionOf"/> 各镜像一次，
	/// 结果输入回到了显示坐标，镜像后 note 判不上）。
	/// </summary>
	private static TouchSample ChartTouchOf(int id, Track screenTrack, Vector2 pos,
		ContactPhase phase) =>
		GameplayMirror.ProjectTouch(id, screenTrack, DisplayPositionOf(screenTrack, pos), phase);

	/// <summary>同上，只要谱面 (轨道, 坐标) 不要 TouchSample（Hold 断触的覆盖检查用）。</summary>
	private static (Track Track, double Position) ChartPointOf(Track screenTrack, Vector2 pos) =>
		GameplayMirror.Display(screenTrack, DisplayPositionOf(screenTrack, pos));

	private static void AddProjectedTouches(List<TouchSample> target, int id,
		Vector2 pos, ContactPhase phase)
	{
		var mask = ProjectedTrackMask(pos);
		// 屏幕区域 -> 显示轨道 -> 谱面轨道/坐标（镜像只在这里施加一次）。
		if ((mask & TouchTrackMask.Center) != 0)
			target.Add(ChartTouchOf(id, Track.Center, pos, phase));
		if ((mask & TouchTrackMask.Left) != 0)
			target.Add(ChartTouchOf(id, Track.Left, pos, phase));
		if ((mask & TouchTrackMask.Right) != 0)
			target.Add(ChartTouchOf(id, Track.Right, pos, phase));
	}

	private static void AddProjectedPresses(
		List<(TouchSample Touch, double Time)> target, int id, Vector2 pos, double time)
	{
		var mask = ProjectedTrackMask(pos);
		if ((mask & TouchTrackMask.Center) != 0)
			target.Add((ChartTouchOf(id, Track.Center, pos, ContactPhase.Began), time));
		if ((mask & TouchTrackMask.Left) != 0)
			target.Add((ChartTouchOf(id, Track.Left, pos, ContactPhase.Began), time));
		if ((mask & TouchTrackMask.Right) != 0)
			target.Add((ChartTouchOf(id, Track.Right, pos, ContactPhase.Began), time));
	}

	private void FlushPendingInputs(double frameTime)
	{
		_frameTouchSamples.Clear();
		_frameStartTouchSamples.Clear();
		_framePointerInputs.Clear();
		_framePresses.Clear();
		_holdContactFrame.Clear();
		foreach (var pointer in _activePointers.Values)
		{
			_holdContactFrame.Seed(pointer.Id, ProjectHoldTouch(pointer.Id, pointer.ScreenPosition));
			pointer.Phase = ContactPhase.Stationary;
			AddProjectedTouches(_frameTouchSamples, pointer.Id,
				pointer.ScreenPosition, pointer.Phase);
			AddProjectedTouches(_frameStartTouchSamples, pointer.Id,
				pointer.ScreenPosition, pointer.Phase);
		}

		// 先按到达顺序更新触点；press 判定推迟到完整帧快照形成以后，确保
		// Tap/Drag/Mine 共用同一个目标时间仲裁，而不是依赖 Godot 事件顺序。
		foreach (var input in _pendingPointerInputs)
		{
			switch (input.Action)
			{
				case PointerAction.Press:
				{
					_framePointerInputs.Add(input);
					var pointer = new ActivePointer
					{
						Id = input.Id,
						ScreenPosition = input.ScreenPosition,
						Phase = ContactPhase.Began,
					};
					_activePointers[input.Id] = pointer;
					ReplaceFrameTouches(input.Id, input.ScreenPosition, ContactPhase.Began);
					AddProjectedPresses(_framePresses, input.Id, input.ScreenPosition, input.Time);
					_holdContactFrame.Record(input.Time, input.Id, ProjectHoldTouch(input.Id, input.ScreenPosition));
					break;
				}
				case PointerAction.Move:
				{
					if (!_activePointers.TryGetValue(input.Id, out var pointer))
						break;
					_framePointerInputs.Add(input);
					var phase = pointer.Phase == ContactPhase.Began
						? ContactPhase.Began
						: ContactPhase.Moved;
					pointer.ScreenPosition = input.ScreenPosition;
					pointer.Phase = phase;
					ReplaceFrameTouches(input.Id, input.ScreenPosition, phase);
					_holdContactFrame.Record(input.Time, input.Id, ProjectHoldTouch(input.Id, input.ScreenPosition));
					break;
				}
				case PointerAction.Release:
					_framePointerInputs.Add(input);
					_holdContactFrame.Record(input.Time, input.Id, null);
					_activePointers.Remove(input.Id);
					RemoveFrameTouches(input.Id);
					break;
			}
		}

		RebuildTrackBuckets();
		AdvancePressWindows(frameTime);
		_mixerEarlyPlacements.Clear();
		_v2InputProtection.Reset();
		foreach (var press in _framePresses)
			ProcessPress(press.Touch, press.Time);

		_pendingPointerInputs.Clear();
	}

	// 已判定或远超迟到窗口的单元对后续帧永久不可判，按轨道游标前移跳过；
	// 时间侧留 1s 余量，覆盖同帧 press 时刻早于 frameTime（卡顿帧）的情形。
	private void AdvancePressWindows(double frameTime)
	{
		for (var i = 0; i < 3; i++)
		{
			var bucket = _pressUnitsByTrack[i];
			var start = _trackPressWindowStart[i];
			while (start < bucket.Count)
			{
				var u = bucket[start];
				if (!u.Judged &&
					u.Time + _engine.Settings.MissSec * u.WindowScale >= frameTime - 1.0)
					break;
				start++;
			}
			_trackPressWindowStart[i] = start;
		}
	}

	private static HoldTouchProjection ProjectHoldTouch(int id, Vector2 position)
	{
		var mask = ProjectedTrackMask(position);
		TouchSample? center = null, left = null, right = null;
		Add(Track.Center);
		Add(Track.Left);
		Add(Track.Right);
		return new HoldTouchProjection(center, left, right);

		void Add(Track displayTrack)
		{
			if ((mask & MaskFor(displayTrack)) == 0) return;
			var sample = ChartTouchOf(id, displayTrack, position, ContactPhase.Moved);
			switch (sample.Track)
			{
				case Track.Center: center = sample; break;
				case Track.Left: left = sample; break;
				case Track.Right: right = sample; break;
			}
		}
	}

	private void RebuildTrackBuckets()
	{
		for (var i = 0; i < 3; i++)
			_touchesByTrack[i].Clear();
		foreach (var touch in _frameTouchSamples)
			_touchesByTrack[(int)touch.Track].Add(touch);
	}

	private void ReplaceFrameTouches(int id, Vector2 pos, ContactPhase phase)
	{
		RemoveFrameTouches(id);
		AddProjectedTouches(_frameTouchSamples, id, pos, phase);
	}

	private void RemoveFrameTouches(int id)
	{
		for (var i = _frameTouchSamples.Count - 1; i >= 0; i--)
		{
			if (_frameTouchSamples[i].Id == id)
				_frameTouchSamples.RemoveAt(i);
		}
	}

	private bool PressTimeCanJudge(JudgeUnit unit, double time)
	{
		if (unit.Kind == UnitKind.Mine)
			return InputJudgeRules.AcceptsMinePhase(unit.Time, time,
				_engine.Settings.PerfectSec * unit.WindowScale, ContactPhase.Began);
		return unit.Kind == UnitKind.Input &&
			Math.Abs(unit.Time - time) <= _engine.Settings.MissSec * unit.WindowScale;
	}

	private void ProcessPress(TouchSample touch, double t)
	{
		var bucket = _pressUnitsByTrack[(int)touch.Track];
		// 扫描上限沿用原全计划窗口的口径：Miss 窗乘最宽窗口系数（EX-Tap 1.5×）。
		var scanAhead = _engine.Settings.MissSec * JudgePlan.ExTapWindowScale;
		for (var i = _trackPressWindowStart[(int)touch.Track]; i < bucket.Count; i++)
		{
			var u = bucket[i];
			// 桶按时间升序：对本 press 已超出未来窗的单元，之后只会更远。
			if (u.Time - t > scanAhead)
				break;
			if (u.Judged || !PressTimeCanJudge(u, t) ||
				!_noteById.TryGetValue(u.NoteId, out var note) ||
				!InputJudgeRules.Overlaps(InputJudgeRules.Bounds(note.Position, note.Width),
					touch.Position, CommunityTouchWidth, expandByTouchWidth: u.Kind != UnitKind.Mine))
				continue;
			if (u.Kind != UnitKind.Mine && !_v2InputProtection.CanUse(u.Time))
				continue;
			if (u.Kind == UnitKind.Mine)
			{
				_engine.ApplyMine(touched: true);
				u.Judged = true;
				ResolveNoteView(u, JudgeGrade.Miss);
				FlashJudge("MINE!");
				continue;
			}
			var r = _engine.JudgePress(u.Time, t, u.WindowScale, u.IsExTap);
			if (r.Resolution == JudgeResolution.Pending)
				continue;
			_v2InputProtection.CommitResolved(u.Time);
			ApplyUnit(u, r.Grade, r.Resolution);
			u.Judged = true;
			ResolveSustainStart(u, r.Grade, touch, t);
			ResolveNoteView(u, r.Grade, timing: r.Timing);
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
		state.LastContactUpdateTime = state.Path.Kind == SustainKind.Hold
			? resolvedTime : Math.Max(state.Path.StartTime, resolvedTime);
		state.LostContactSecond = 0.0;
		state.Contact = HoldContactState.Contact;
		if (state.Path.Kind == SustainKind.Hold)
		{
			state.Holding = grade != JudgeGrade.Miss;
			if (grade == JudgeGrade.Miss)
				FailRemainingHold(state);
			return;
		}

		// Head scoring authorizes early placement only. The actual operation is replayed
		// at its timestamp with all other inputs; a late head cannot rewind the slider.
		if (grade != JudgeGrade.Miss && touch is { } mixerTouch)
			_mixerEarlyPlacements.Add((state.Path.HeadId, mixerTouch.Id, resolvedTime));
	}

	private void UpdateSustainStates(double t)
	{
		foreach (var state in _sustainStates.Values)
		{
			if (state.EndResolved)
				continue;

			if (state.Path.Kind == SustainKind.Mixer)
			{
				if (t >= state.Path.StartTime || state.StartResolved)
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
		var from = state.LastContactUpdateTime;
		var bounds = state.Path.BoundsAtRuntime(activeUntil);
		var touch = FindNearestTouch(state.Path.Track, bounds.Center,
			bounds, expandByTouchWidth: true);
		var touching = _auto || touch.HasValue;
		UpdateHoldHeadContactView(state, touching, bounds);
		UpdateSustainEffect(state, touching, bounds, bounds.Center);
		if (activeUntil < from)
			return;

		state.Contact = _auto ? HoldContactState.Contact : _holdContactFrame.Observe(
			state.Path, from, activeUntil, state.Contact, _engine.Settings,
			_chart.BpmAtSeconds, CommunityTouchWidth);
		state.LostContactSecond = state.Contact.ContactLostAt is { } lostAt
			? Math.Max(0.0, activeUntil - lostAt) : 0.0;

		state.LastContactUpdateTime = activeUntil;
		if (state.Contact is { ContactLostAt: { } lost, GraceDeadline: { } deadline })
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

	private void UpdateHoldHeadContactView(SustainRuntime state, bool connected,
		NoteBounds bounds)
	{
		if (!_viewByNoteId.TryGetValue(state.Path.HeadId, out var view))
			return;
		if (connected)
			view.RestoreHoldContact();
		else
			view.BeginRecoverableHoldFallthrough();
		UpdateSustainHeadGeometry(view, bounds, bounds.Center,
			view.IsRecoverableHoldFalling ? view.MissDistancePx : 0f);
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

	private void UpdateMixerHeadView(SustainRuntime state, NoteBounds bounds)
	{
		// A placed receiver survives release and temporary loss until this path ends.
		RecycleNoteViewImmediately(state.Path.HeadId);
		if (state.Mixer is not { Placed: true } mixer)
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
		UpdateSustainHeadGeometry(state.MixerHeadView, bounds, mixer.Position);
	}

	private static void UpdateSustainHeadGeometry(NoteView view, NoteBounds bounds,
		double position, float pastLineDistance = 0f)
	{
		view.SetSize(GameplayVisualMapper.SizeFor(
			view.Model.Type, view.Model.Track, bounds.Right - bounds.Left));
		view.Position = PositionPastLine(view.Model.Track,
			TrackPositionAtLine(view.Model.Track, position), pastLineDistance);
		view.SetDepthAlpha(1f);
	}

	private static Vector2 TrackPositionAtLine(Track track, double position)
	{
		// 谱面坐标 -> 屏幕：轨道与中轨水平坐标都过一层镜像映射。
		var displayTrack = GameplayMirror.DisplayTrack(track);
		var displayPosition = track == Track.Center
			? GameplayMirror.DisplayCenter(position) : position;
		return displayTrack switch
		{
			Track.Center => new Vector2(
				CenterX0 + (float)displayPosition * PosUnitPx, CenterLineY),
			Track.Left => new Vector2(
				LeftLineX, SideY0 - (float)displayPosition * SideUnit),
			_ => new Vector2(
				RightLineX, SideY0 - (float)displayPosition * SideUnit),
		};
	}

	private void ReleaseMixerHeadView(SustainRuntime state)
	{
		if (state.MixerHeadView is { } view)
			ReturnNoteView(view);
		state.MixerHeadView = null;
		ReleaseSustainEffect(state);
	}

	private void UpdateSustainEffect(SustainRuntime state, bool connected,
		NoteBounds bounds, double position)
	{
		if (!GameSession.Settings.GameplayEffectsEnabled)
		{
			ReleaseSustainEffect(state);
			return;
		}
		if (!connected)
		{
			state.ContactEffect?.SetContact(false);
			if (state.ContactParticles is { } retained)
			{
				retained.SetEmitting(false);
				retained.Visible = false;
			}
			return;
		}
		if (state.ContactEffect == null)
		{
			if (_sustainEffectPool.Count == 0)
			{
				var oldest = _effectOwners[0];
				ReleaseContactEffect(oldest);
			}
			state.ContactEffect = _sustainEffectPool.Pop();
			_effectOwners.Add(state);
			state.ContactEffect.ResetFor(state.Path.Kind == SustainKind.Hold
				? SustainEffectKind.Hold : SustainEffectKind.Mixer,
				state.Path.Track == Track.Center ? 0f : Mathf.Pi * 0.5f);
		}
		state.ContactEffect.SetContact(true);
		state.ContactEffect.Position = TrackPositionAtLine(state.Path.Track, position);
		state.ContactEffect.SpanPx = SustainSpanPx(state.Path.Track, bounds);

		var particleMode = GameSession.Settings.MotionMode;
		if (particleMode == UiMotionMode.Off)
			return;
		if (state.ContactParticles == null)
		{
			if (_sustainParticlePool.Count == 0)
			{
				if (_drainingParticles.Count > 0)
				{
					var oldest = _drainingParticles[0];
					_drainingParticles.RemoveAt(0);
					oldest.ResetForPool();
					_sustainParticlePool.Push(oldest);
				}
				else
				{
					var oldest = _particleOwners[0];
					var stolen = oldest.ContactParticles!;
					oldest.ContactParticles = null;
					_particleOwners.RemoveAt(0);
					stolen.ResetForPool();
					_sustainParticlePool.Push(stolen);
				}
			}
			state.ContactParticles = _sustainParticlePool.Pop();
			_particleOwners.Add(state);
			state.ContactParticles.ResetFor(GameplayMirror.DisplayTrack(state.Path.Track) switch
			{
				Track.Center => 0f,
				Track.Left => Mathf.Pi * 0.5f,
				_ => -Mathf.Pi * 0.5f,
			}, state.Path.Kind == SustainKind.Hold ? HitEffectKind.Hold : HitEffectKind.Mixer,
				particleMode, SustainSpanPx(state.Path.Track, bounds));
		}
		state.ContactParticles.Visible = true;
		state.ContactParticles.SetEmitting(true);
		state.ContactParticles.Position = TrackPositionAtLine(state.Path.Track, position);
	}

	private static float SustainSpanPx(Track track, NoteBounds bounds)
	{
		var width = (float)Math.Max(0.0, bounds.Right - bounds.Left);
		return Mathf.Max(24f, width * (track == Track.Center
			? PosUnitPx : SideNoteLenUnitPx) * NoteVisualScale);
	}

	private void ReleaseContactEffect(SustainRuntime state)
	{
		if (state.ContactEffect == null) return;
		_effectOwners.Remove(state);
		state.ContactEffect.ResetForPool();
		_sustainEffectPool.Push(state.ContactEffect);
		state.ContactEffect = null;
	}

	private void ReleaseSustainEffect(SustainRuntime state)
	{
		ReleaseContactEffect(state);
		if (state.ContactParticles == null) return;
		_particleOwners.Remove(state);
		state.ContactParticles.BeginRelease();
		_drainingParticles.Add(state.ContactParticles);
		state.ContactParticles = null;
	}

	private TouchSample? FindNearestTouch(Track track, double anchor,
		NoteBounds? requiredBounds = null, bool expandByTouchWidth = true) =>
		FindNearestTouchIn(_touchesByTrack[(int)track], track, anchor,
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

	private void BreakHold(SustainRuntime state, double releaseTime)
	{
		if (state.Broken || state.EndResolved)
			return;
		state.Broken = true;
		SettleReleasedHold(state, releaseTime);
	}

	private void SettleReleasedHold(SustainRuntime state, double releaseTime)
	{
		if (state.EndResolved)
			return;
		SustainJudgementRules.Settlement? tail = null;
		foreach (var settlement in SustainJudgementRules.SettleReleasedHold(
			_plan.Units, state.Path.HeadId, releaseTime, _engine))
		{
			state.Broken |= settlement.Grade == JudgeGrade.Miss;
			if (settlement.Unit.Category == ScoreCategory.HoldEnd)
				tail = settlement;
		}
		FinishHoldViews(state, tail?.Grade ?? JudgeGrade.Miss);
		if (tail is { } settledTail)
		{
			ResolveNoteView(settledTail.Unit, settledTail.Grade);
			FlashJudge(GradeText(settledTail.Grade, settledTail.Timing));
		}
	}

	private void FailRemainingHold(SustainRuntime state)
	{
		if (state.EndResolved)
			return;
		state.Broken = true;
		SustainJudgementRules.FailRemainingHold(_plan.Units, state.Path.HeadId, _engine);
		FinishHoldViews(state, JudgeGrade.Miss);
	}

	private void FinishHoldViews(SustainRuntime state, JudgeGrade grade)
	{
		state.Holding = false;
		state.EndResolved = true;
		ReleaseSustainEffect(state);
		if (state.Broken || grade == JudgeGrade.Miss)
		{
			state.Broken = true;
			// 判定结束后仍保留下落所需的视图和连接段，透明度统一由退场层控制。
			var fadeRoot = new Node2D { Name = $"HoldMissFade_{state.Path.HeadId}" };
			_noteRoot.AddChild(fadeRoot);
			var fading = new FadingHoldView { Root = fadeRoot };
			foreach (var node in state.Path.Nodes)
			{
				// 未来尚未入场的节点和连接段也不能再生成。
				_failedHoldNoteIds.Add(node.Id);
				if (_viewByNoteId.Remove(node.Id, out var view))
				{
					_active.Remove(view);
					view.Reparent(fadeRoot);
					fading.Notes.Add(view);
				}
			}
			for (var i = _links.Count - 1; i >= 0; i--)
			{
				var link = _links[i];
				if (!link.IsHold || !_failedHoldNoteIds.Contains(link.FromId))
					continue;
				link.Poly.Reparent(fadeRoot);
				link.Frame?.Reparent(fadeRoot);
				fading.Links.Add(link);
				_links.RemoveAt(i);
			}
			if (fadeRoot.GetChildCount() > 0)
				_fadingHoldViews.Add(fading);
			else
				fadeRoot.QueueFree();
			return;
		}

		if (_viewByNoteId.TryGetValue(state.Path.HeadId, out var headView))
		{
			var bounds = state.Path.BoundsAt(state.Path.EndTime);
			UpdateSustainHeadGeometry(headView, bounds, bounds.Center);
			headView.RestoreHoldContact();
			headView.MarkJudged(NoteVisualSpec.Hold);
		}
	}

	// ---- 判定扫尾 ----

	private void SweepJudges(double t)
	{
		var scan = _sweepCursor;
		while (scan < _plan.Units.Count)
		{
			var u = _plan.Units[scan];
			if (u.Judged)
			{
				if (scan == _sweepCursor)
					_sweepCursor++;
				scan++;
				continue;
			}
			if (u.Time - t > _engine.Settings.MissSec * u.WindowScale)
				break;
			JudgeOne(u, t);
			if (u.Judged && scan == _sweepCursor)
				_sweepCursor++;
			if (!u.Judged && u.Time > t)
				break;
			scan++;
		}
	}

	private void JudgeOne(JudgeUnit u, double t)
	{
		var miss = _engine.Settings.MissSec * u.WindowScale;
		switch (u.Kind)
		{
			case UnitKind.Auto: // 保留给无需输入的普通自动单元
				if (t >= u.Time)
				{
					ApplyUnit(u, JudgeGrade.Perfect, JudgeResolution.Perfect);
					u.Judged = true;
					ResolveNoteView(u, JudgeGrade.Perfect);
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
					var r = _engine.Judge(u.Time, u.Time, u.WindowScale, u.IsExTap); // Auto：精确时刻判定
					ApplyUnit(u, r.Grade, r.Resolution);
					u.Judged = true;
					ResolveSustainStart(u, r.Grade, null, u.Time);
					ResolveNoteView(u, JudgeGrade.Perfect);
				}
				else if (t > u.Time + miss)
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
		var perfect = _engine.Settings.PerfectSec * unit.WindowScale;
		var miss = _engine.Settings.MissSec * unit.WindowScale;
		var good = _engine.Settings.GoodSec * unit.WindowScale;
		if (t < unit.Time - perfect)
			return;
		if (t > unit.Time + miss)
		{

			ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Miss);
			FlashJudge("MISS");
			return;
		}
		if (t > unit.Time + good)
			return;
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		if (_auto && t >= unit.Time)
		{
			ApplyUnit(unit, JudgeGrade.Perfect, JudgeResolution.Perfect);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Perfect);
			return;
		}
		foreach (var touch in _touchesByTrack[(int)unit.Track])
		{
			if (touch.Track != unit.Track ||
				!InputJudgeRules.AcceptsContactPhase(
					unit.Time, t, perfect, touch.Phase) ||
				!InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth))
				continue;
			var result = _engine.Judge(unit.Time, t, unit.WindowScale);
			ApplyUnit(unit, result.Grade, result.Resolution);
			unit.Judged = true;
			ResolveNoteView(unit, result.Grade, timing: result.Timing);
			FlashJudge(GradeText(result));
			return;
		}
	}

	private void JudgeMine(JudgeUnit unit, double t)
	{
		if (!_noteById.TryGetValue(unit.NoteId, out var note))
			return;
		var perfect = _engine.Settings.PerfectSec * unit.WindowScale;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		foreach (var touch in _touchesByTrack[(int)unit.Track])
		{
			if (touch.Track != unit.Track ||
				!InputJudgeRules.AcceptsMinePhase(unit.Time, t, perfect, touch.Phase) ||
				!InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth,
					expandByTouchWidth: false))
				continue;
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
			ResolveNoteView(unit, JudgeGrade.Perfect, emitEffect: false);
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
			if (_auto || (state.StartResolved && !state.Broken &&
				(state.Holding || state.Contact.HasLoss)))
				ApplyUnit(unit, JudgeGrade.Perfect, JudgeResolution.Perfect);
			else
				ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			unit.Judged = true;
			return;
		}

		// Replay computes grades; this globally ordered sweep still owns score/Combo application.
		if (state.MixerGrades.Remove(unit, out var mixerGrade))
			ApplyUnit(unit, mixerGrade, mixerGrade == JudgeGrade.Perfect
				? JudgeResolution.Perfect : JudgeResolution.AutoMiss);
	}

	private void ResolveHoldEnd(JudgeUnit unit, double t)
	{
		if (t < unit.Time || unit.Judged)
			return;
		SustainRuntime? state = null;
		if (unit.SustainHeadId is { } headId)
			_sustainStates.TryGetValue(headId, out state);
		var result = SustainJudgementRules.JudgeHoldTail(unit.Time, t,
			state == null || state.StartResolved, _auto || state?.Holding == true,
			state?.Broken ?? true, state?.Contact ?? default, _engine, unit.WindowScale);
		if (result is not { } settled)
			return;
		ApplyUnit(unit, settled.Grade, settled.Resolution);
		unit.Judged = true;
		if (state != null)
			FinishHoldViews(state, settled.Grade);
		ResolveNoteView(unit, settled.Grade);
		FlashJudge(GradeText(settled.Grade, settled.Timing));
	}

	private void ApplyUnit(JudgeUnit unit, JudgeGrade grade,
		JudgeResolution? resolution = null)
	{
		if (unit.Judged)
			return;
		_engine.Apply(unit.Category, grade, unit.AffectsCombo,
			unit.AffectsJudgeCounts, resolution);
		unit.Judged = true;
	}

	// ---- 音符生成与移动 ----

	private void SpawnNotes(double t, double currentBar)
	{
		while (_nextSpawn < _notesByTime.Count)
		{
			var peek = _notesByTime[_nextSpawn];
			// 侧轨流速 = 行程比，生成提前量与正面相同（SideLeadPx == TravelPx），同刻入场
			var threshold = peek.Track == Track.Center ? TravelPx : SideLeadPx;
			if (VisualDistanceFor(peek, t, currentBar) > threshold)
				break;
			var n = _notesByTime[_nextSpawn++];
			if (_failedHoldNoteIds.Contains(n.Id))
				continue;
			if (ShouldRenderStaticNote(n))
			{
				// TakeNoteView 已负责把视图挂到 _noteRoot（新建/归位），这里不再 AddChild。
				var view = TakeNoteView(n);

				view.Position = PositionFor(n, t, currentBar);
				view.SetDepthAlpha(n.Track == Track.Center ? TopFadeAlpha(view.Position.Y) : 1f);
				_viewByNoteId[n.Id] = view;
				_active.Add(view);
			}
			if (n.SubNoteId != -1 && _noteById.TryGetValue(n.SubNoteId, out var toNote))
			{
				var isHold = n.Type is NoteType.HoldHead or NoteType.HoldNode;
				var pool = _linkPools[isHold];
				// 防御：历史版本曾把挂在 fadeRoot 上的连接体入池后被 QueueFree；
				// 弹到死节点时丢弃换新，不让坏视图进场。
				NoteLink? link = null;
				while (pool.Count > 0)
				{
					var candidate = pool.Pop();
					if (GodotObject.IsInstanceValid(candidate.Poly))
					{
						link = candidate;
						break;
					}
				}
				link ??= CreateLink(isHold);
				link.FromId = n.Id;
				link.ToId = n.SubNoteId;
				link.Track = n.Track;
				link.Wa = LinkHalfFor(n);
				link.Wb = LinkHalfFor(toNote);
				link.BaseColor = LinkColorFor(n);
				link.Poly.Color = link.BaseColor;
				if (link.Frame != null) _noteRoot.MoveChild(link.Frame, -1);
				_noteRoot.MoveChild(link.Poly, -1);
				_links.Add(link);
			}
		}
	}

	private NoteLink CreateLink(bool isHold)
	{
		var poly = new Polygon2D { Visible = false, ZIndex = 0 };
		var frame = isHold ? new Line2D
		{
			Width = 3f,
			DefaultColor = new Color(1.0f, 0.80f, 0.35f, 0.9f),
			Visible = false,
			ZIndex = 0,
		} : null;
		_noteRoot.AddChild(poly);
		if (frame != null) _noteRoot.AddChild(frame);
		return new NoteLink { IsHold = isHold, Poly = poly, Frame = frame };
	}

	private void ReleaseLink(NoteLink link)
	{
		// 入池前必须归位：断触退场时 Poly/Frame 被挂在 fadeRoot 下，
		// fadeRoot 随后 QueueFree，不归位会把已释放节点留在池里。
		if (link.Poly.GetParent() != _noteRoot)
			link.Poly.Reparent(_noteRoot);
		if (link.Frame != null && link.Frame.GetParent() != _noteRoot)
			link.Frame.Reparent(_noteRoot);
		link.FromId = 0;
		link.ToId = 0;
		link.Track = Track.Center;
		link.Wa = 0f;
		link.Wb = 0f;
		link.BaseColor = Colors.Transparent;
		link.Poly.Visible = false;
		link.Poly.Color = Colors.Transparent;
		link.Poly.Polygon = Array.Empty<Vector2>();
		if (link.Frame != null)
		{
			link.Frame.Visible = false;
			link.Frame.DefaultColor = Colors.Transparent;
			link.Frame.Points = Array.Empty<Vector2>();
		}
		_linkPools[link.IsHold].Push(link);
	}

	private bool ShouldRenderStaticNote(Note note) => note.Type switch
	{
		NoteType.HoldNode => _holdTailIds.Contains(note.Id),
		NoteType.MixerNode => false,
		_ => true,
	};


	private void UpdateViews(double t, double currentBar, double delta)
	{
		RecycleExpiredBlooms();
		UpdateHoldMissFades(t, currentBar, delta);
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
				ReturnNoteView(v);
				_active.RemoveAt(i);
				continue;
			}
			// 不按屏幕边界回收：变速期间 note 可能先退出画面，随后再次进入。
			// 已接起的 Hold 由当前路径更新位置和宽度，结束后保留尾端位置淡出。
			if (v.Model.Type != NoteType.HoldHead || !v.IsResolved ||
				!_sustainStates.ContainsKey(v.Model.Id))
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
			if (UpdateLinkView(l, t, currentBar))
				continue;
			ReleaseLink(l);
			_links.RemoveAt(i);
		}
	}

	private bool UpdateLinkView(NoteLink link, double t, double currentBar,
		bool updateAppearance = true)
	{
		var from = _noteById[link.FromId];
		var to = _noteById[link.ToId];
		var rA = VisualDistanceFor(from, t, currentBar);
		var rB = VisualDistanceFor(to, t, currentBar);
		var a = PositionAt(from, rA);
		var b = PositionAt(to, rB);
		var wa = link.Wa;
		var wb = link.Wb;
		var anchoredEarlyHold = IsEarlyHoldAnchored(link, rA);
		if (anchoredEarlyHold)
			a = PositionAt(from, 0f);
		if (!ClipToLine(link.Track, ref a, ref b, ref wa, ref wb))
			return false;
		if (link.PolyScratch.Length < 4)
			link.PolyScratch = new Vector2[4];
		GameplayVisualMapper.WriteLinkPolygon(link.Track, a, b, wa, wb, link.PolyScratch);
		link.Poly.Polygon = link.PolyScratch;
		if (link.Frame != null)
		{
			if (link.FrameScratch.Length < 5)
				link.FrameScratch = new Vector2[5];
			GameplayVisualMapper.WriteLinkFrame(link.Track, a, b, wa, wb, link.FrameScratch);
			link.Frame.Points = link.FrameScratch;
		}
		if (!updateAppearance)
			return true;

		var proximity = 1f;
		if (link.Track != Track.Center)
		{
			var nearestRem = anchoredEarlyHold
				? 0f : Mathf.Max(0f, Mathf.Min(rA, rB));
			proximity = 1f - Mathf.Clamp(nearestRem / SideLeadPx, 0f, 1f);
		}
		var fillAlpha = link.BaseColor.A;
		if (link.Track == Track.Center)
		{
			// 以靠近判定线的一端控制渐入，避免长 Hold 的远端让整条 Body 不可见。
			fillAlpha *= TopFadeAlpha(Mathf.Max(a.Y, b.Y));
		}
		else if (link.IsHold)
			fillAlpha = Mathf.Lerp(0.08f, 0.28f, proximity);
		link.Poly.Color = new Color(link.BaseColor, fillAlpha);
		link.Poly.Visible = true;
		if (link.Frame != null)
		{
			var frameAlpha = link.Track == Track.Center
				? 0.9f * TopFadeAlpha(Mathf.Max(a.Y, b.Y))
				: Mathf.Lerp(0.25f, 0.9f, proximity);
			link.Frame.DefaultColor = new Color(1.0f, 0.80f, 0.35f, frameAlpha);
			link.Frame.Visible = true;
		}
		return true;
	}

	private void UpdateHoldMissFades(double t, double currentBar, double delta)
	{
		// 跟随 gameplay 帧推进，因此暂停时也冻结退场动画。
		for (var i = _fadingHoldViews.Count - 1; i >= 0; i--)
		{
			var fading = _fadingHoldViews[i];
			fading.RemainingSec -= (float)delta;
			if (fading.RemainingSec <= 0f)
			{
				foreach (var view in fading.Notes)
				{
					view.Reparent(_noteRoot);
					ReturnNoteView(view);
				}
				foreach (var link in fading.Links)
					ReleaseLink(link);
				fading.Root.QueueFree();
				_fadingHoldViews.RemoveAt(i);
				continue;
			}
			foreach (var view in fading.Notes)
			{
				if (view.IsLineAnchored || view.IsMissFalling || view.IsRecoverableHoldFalling)
				{
					// 已接起或已下穿的头从当前位置继续落下，不能跳回谱面头的原始位置。
					var step = MissFallSpeedPx(view.Model, t, currentBar) * (float)delta;
					view.Position = PositionPastLine(view.Model.Track, view.Position, step);
				}
				else
					view.Position = PositionForActiveView(view.Model, t, currentBar);
			}
			for (var j = fading.Links.Count - 1; j >= 0; j--)
			{
				var link = fading.Links[j];
				if (UpdateLinkView(link, t, currentBar, updateAppearance: false))
					continue;
				ReleaseLink(link);
				fading.Links.RemoveAt(j);
			}
			// 父节点乘透明度，保留各端帽/条身原本的深度和断触淡出状态。
			fading.Root.Modulate = new Color(1f, 1f, 1f,
				fading.RemainingSec / HoldMissFadeDurationSec);
		}
	}

	private bool IsEarlyHoldAnchored(NoteLink link, float nearRemainingPx) =>
		link.IsHold && nearRemainingPx > 0f &&
		_sustainStates.TryGetValue(link.FromId, out var state) &&
		state.Path.Kind == SustainKind.Hold && state.StartResolved &&
		state.Holding && !state.Broken && !state.EndResolved;

	// 将段 a→b（中心线端点，半宽 wa/wb）按判定线裁剪：a 端已过线时用
	// 中心线与判定线的交点取而代之（面板边缘沿线滑动）；整段过线返回 false。
	// 链按时间有序，from 端恒领先于 b 端，故 b 不会单独过线。
	private static bool ClipToLine(Track track, ref Vector2 a, ref Vector2 b,
		ref float wa, ref float wb)
	{
		return GameplayVisualMapper.ClipToJudgeLine(track, ref a, ref b, ref wa, ref wb,
			GameplayMirror.Enabled);
	/*
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
	*/ }

	private double CurrentBarAt(double second) =>
		_loaded.V2Timeline != null
			? _loaded.V2Timeline.RuntimeBarAt(ChartAudioSecond(second))
			: _chart.Sections.Count > 0 ? _chart.SecondsToBarTime(second) : second;

	private double ChartAudioSecond(double songClockSecond) =>
		songClockSecond - GameSession.Settings.TimingOffsetMs / 1000.0;

	private float VisualDistanceFor(Note n, double t, double currentBar)
	{
		if (_loaded.V2Scroll != null && _loaded.V2Timeline != null)
		{
			var currentSecond = ChartAudioSecond(t);
			var currentBarRuntime = _loaded.V2Timeline.RuntimeBarAt(currentSecond);
			var noteSecond = _runtimeNoteSeconds.TryGetValue(n.Id, out var cached)
				? cached : n.Second;
			return (float)VisualScrollMath.DistancePixels(
				noteSecond, currentSecond, BaseFallSpeedPx,
				_loaded.V2Scroll.RuntimeSpeedAt(currentBarRuntime), _fallSpeedMultiplier);
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
			var currentBarRuntime = _loaded.V2Timeline.RuntimeBarAt(ChartAudioSecond(t));
			return _loaded.V2Scroll.RuntimeSpeedAt(currentBarRuntime);
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

	private static Vector2 PositionPastLine(Note note, float distancePx) =>
		PositionPastLine(note.Track, PositionAt(note, 0f), distancePx);

	private static Vector2 PositionPastLine(Track track, Vector2 linePosition, float distancePx)
	{
		return GameplayMirror.DisplayTrack(track) switch
		{
			Track.Center => linePosition + new Vector2(0f, distancePx),
			Track.Left => linePosition - new Vector2(distancePx, 0f),
			_ => linePosition + new Vector2(distancePx, 0f),
		};
	}

	private static float DistancePastJudgeLine(Track track, Vector2 position) =>
		Mathf.Max(0f, GameplayMirror.DisplayTrack(track) switch
		{
			Track.Center => position.Y - CenterLineY,
			Track.Left => LeftLineX - position.X,
			_ => position.X - RightLineX,
		});

	private static float PastLineDistanceFromRemaining(Track track, float remaining) =>
		Mathf.Max(0f, -remaining * (track == Track.Center ? 1f : SideDistScale));

	private static Vector2 PositionAt(Note n, float rem)
		=> GameplayVisualMapper.PositionAt(n, rem, GameplayMirror.Enabled);
	/* => n.Track switch
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
	}; */

	private static float TopFadeAlpha(float y) =>
		y <= 128f ? 0f : y >= 200f ? 1f : (y - 128f) / 72f;

	private static Vector2 SizeFor(Note n)
		=> GameplayVisualMapper.SizeFor(n);
	/*{
		// 长轴 = Width × 1 Position 单位（NoteVisualScale≈1，见顶部注释）
		var axis = GameplayStageGeometry.CenterWidthPx(n.Width);
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
			// 侧轨 Hold 首尾与 Body 使用同一条长度标尺，避免端帽与面板错位。
			if (n.Type is NoteType.HoldHead or NoteType.HoldNode)
			{
				var holdLen = Mathf.Max(12f,
					(float)n.Width * SideNoteLenUnitPx * NoteVisualScale);
				return new Vector2(17f, holdLen);
			}
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

	*/

	private static Color ColorFor(Note n) => GameplayVisualMapper.ColorFor(n);

	private void ResolveNoteView(JudgeUnit unit, JudgeGrade grade,
		bool emitEffect = true, HitTiming timing = HitTiming.Exact)
	{
		if (_failedHoldNoteIds.Contains(unit.NoteId) ||
			!_noteById.TryGetValue(unit.NoteId, out var note))
			return;

		var isMine = note.Type == NoteType.Mine;
		var isMixer = note.Type is NoteType.MixerHead or NoteType.MixerNode;
		var isMiss = grade == JudgeGrade.Miss;
		var earlyMissEffect = isMiss && timing == HitTiming.Early &&
			note.Type is NoteType.Tap or NoteType.ExTap;
		if (isMiss && (isMixer || note.Type is NoteType.HoldHead or NoteType.HoldNode))
		{
			RecycleNoteViewImmediately(note.Id);
			return;
		}

		var missFallthrough = isMiss && UsesOrdinaryMissFallthrough(note.Type);
		var keepsLineHead = note.Type is NoteType.HoldHead or NoteType.HoldNode;
		var accent = HitAccentFor(note.Type);
		var effectAccent = NoteVisualSpec.EffectAccent(note.Type, grade);
		var fixedPerfectEffect = note.Type is NoteType.Drag or NoteType.HoldHead or NoteType.HoldNode or
			NoteType.MixerHead or NoteType.MixerNode;
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
				view.Position = PositionAt(note, 0f);
				if (unit.Category == ScoreCategory.HoldStart &&
					_sustainStates.TryGetValue(note.Id, out var state))
				{
					var bounds = state.Path.BoundsAt(state.LastContactUpdateTime);
					UpdateSustainHeadGeometry(view, bounds, bounds.Center);
				}
			}
			else
				RecycleNoteViewImmediately(note.Id);
		}

		// 普通 Miss 只下穿淡出；Mixer Miss 静默回收；Mine 触发危险爆发。
		if (!GameSession.Settings.GameplayEffectsEnabled || (missFallthrough && !earlyMissEffect) || !emitEffect)
			return;

		var size = SizeFor(note);
		var effectKind = HitEffectKindFor(note.Type);
		var effectPosition = PositionAt(note, 0f);
		var effectRotation = note.Track == Track.Center ? 0f : Mathf.Pi * 0.5f;
		var effectStrength = isMine || fixedPerfectEffect ? 1f : grade switch
		{
			JudgeGrade.Perfect => 1f,
			JudgeGrade.Great => 0.82f,
			JudgeGrade.Good => 0.68f,
			_ => 0.75f,
		};

		EmitBloom(effectPosition, effectRotation, effectAccent, effectKind,
			note.Track == Track.Center ? size.X : size.Y, effectStrength);
		var mode = GameSession.Settings.MotionMode;
		if (effectKind != HitEffectKind.Mine && mode != UiMotionMode.Off)
		{
			GameplayHitParticles particles;
			if (_hitParticlePool.Count > 0)
				particles = _hitParticlePool.Pop();
			else
			{
				particles = _activeHitParticles[0];
				_activeHitParticles.RemoveAt(0);
			}
			particles.ResetFor(effectPosition, effectRotation, effectAccent, effectKind, effectStrength, mode);
			particles.GetParent().MoveChild(particles, -1);
			_activeHitParticles.Add(particles);
		}
	}

	private NoteView TakeNoteView(Note note, bool accent = true)
	{
		var type = VisualPoolPlanning.PoolType(note.Type);
		if (!_noteViewPools.TryGetValue(type, out var pool))
			_noteViewPools[type] = pool = new Stack<NoteView>();
		var view = pool.Count > 0 ? pool.Pop() : NoteView.Create(note, SizeFor(note), ColorFor(note));
		if (view.GetParent() != _noteRoot)
		{
			if (view.GetParent() == null) _noteRoot.AddChild(view);
			else view.Reparent(_noteRoot);
		}
		view.Reuse(note, SizeFor(note), ColorFor(note), accent && _loaded.SyncAccentRuntimeIds.Contains(note.Id));
		_noteRoot.MoveChild(view, -1);
		return view;
	}

	private void ReturnNoteView(NoteView view)
	{
		if (view.GetParent() != _noteRoot) view.Reparent(_noteRoot);
		var type = VisualPoolPlanning.PoolType(view.Model.Type);
		view.ResetForPool();
		if (!_noteViewPools.TryGetValue(type, out var pool))
			_noteViewPools[type] = pool = new Stack<NoteView>();
		pool.Push(view);
	}

	private void EmitBloom(Vector2 position, float rotation, Color accent,
		HitEffectKind kind, float span, float strength)
	{
		GameplayHitBloom bloom;
		if (_bloomPool.Count > 0)
			bloom = _bloomPool.Pop();
		else
		{
			bloom = _activeBlooms[0];
			_activeBlooms.RemoveAt(0);
		}
		bloom.ResetFor(position, rotation, accent, kind, span, strength);
		bloom.ZIndex = 3;
		_noteRoot.MoveChild(bloom, -1);
		_activeBlooms.Add(bloom);
	}

	private void RecycleExpiredBlooms()
	{
		for (var i = _activeBlooms.Count - 1; i >= 0; i--)
		{
			var bloom = _activeBlooms[i];
			if (!bloom.IsExpired) continue;
			_activeBlooms.RemoveAt(i);
			bloom.ResetForPool();
			_bloomPool.Push(bloom);
		}
		for (var i = _activeHitParticles.Count - 1; i >= 0; i--)
		{
			var particles = _activeHitParticles[i];
			if (!particles.IsExpired) continue;
			_activeHitParticles.RemoveAt(i);
			particles.ResetForPool();
			_hitParticlePool.Push(particles);
		}
		for (var i = _drainingParticles.Count - 1; i >= 0; i--)
		{
			var particles = _drainingParticles[i];
			if (!particles.IsDrained) continue;
			_drainingParticles.RemoveAt(i);
			particles.ResetForPool();
			_sustainParticlePool.Push(particles);
		}
	}

	private void RecycleNoteViewImmediately(int noteId)
	{
		if (!_viewByNoteId.Remove(noteId, out var view))
			return;
		_active.Remove(view);
		ReturnNoteView(view);
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
		return GameplayVisualMapper.LinkHalfFor(n);
	/*
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
	*/ }

	private static Color LinkColorFor(Note n) => GameplayVisualMapper.LinkColorFor(n);

	private static string GradeText(JudgeResult r) => GradeText(r.Grade, r.Timing);

	private static string GradeText(JudgeGrade grade, HitTiming timing) => grade switch
	{
		JudgeGrade.Perfect => "PERFECT",
		JudgeGrade.Great => $"GREAT {(timing == HitTiming.Early ? "E" : "L")}",
		JudgeGrade.Good => $"GOOD {(timing == HitTiming.Early ? "E" : "L")}",
		_ => "MISS",
	};
}

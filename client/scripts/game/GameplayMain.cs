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
		public bool SliderInitialized;
		public double SliderPosition;
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
	private readonly List<(ActivePointer Pointer, double Time)> _frameReleases = new();
	private readonly V2InputProtection _v2InputProtection = new();
	private readonly Dictionary<(int Id, Track Track), TouchSample> _mixerSamplesScratch = new();
	private readonly Dictionary<int, SustainRuntime> _sustainStates = new();
	private int _pressWindowStart;
	private int _pressWindowEnd;
	private int _sweepCursor;
	private int _judgedTheoreticalMax;
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
		RefreshJudgeWindow(t);
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
		// Auto 演示不接受任何会导致判定的输入：结果严格全 Prefect，触点一律不进入判定管线。
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
		_frameReleases.Clear();
		foreach (var pointer in _activePointers.Values)
		{
			pointer.Phase = ContactPhase.Stationary;
			AddProjectedTouches(_frameTouchSamples, pointer.Id,
				pointer.ScreenPosition, pointer.Phase);
			AddProjectedTouches(_frameStartTouchSamples, pointer.Id,
				pointer.ScreenPosition, pointer.Phase);
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
						ScreenPosition = input.ScreenPosition,
						Phase = ContactPhase.Began,
					};
					_activePointers[input.Id] = pointer;
					ReplaceFrameTouches(input.Id, input.ScreenPosition, ContactPhase.Began);
					AddProjectedPresses(_framePresses, input.Id, input.ScreenPosition, input.Time);
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
						ScreenPosition = input.ScreenPosition,
					};
					pointer.ScreenPosition = input.ScreenPosition;
					pointer.Phase = phase;
					_activePointers[input.Id] = pointer;
					ReplaceFrameTouches(input.Id, input.ScreenPosition, phase);
					break;
				}
				case PointerAction.Release:
					if (_activePointers.TryGetValue(input.Id, out var released))
						_frameReleases.Add((released, input.Time));
					_activePointers.Remove(input.Id);
					RemoveFrameTouches(input.Id);
					break;
			}
		}

		_v2InputProtection.Reset();
		foreach (var press in _framePresses)
			ProcessPress(press.Touch, press.Time, preRun: true);
		foreach (var press in _framePresses)
			ProcessPress(press.Touch, press.Time, preRun: false);
		foreach (var release in _frameReleases)
			RecordHoldRelease(release.Pointer, release.Time);

		_pendingPointerInputs.Clear();
	}

	private void RefreshJudgeWindow(double currentTime)
	{
		if (_plan.Units.Count == 0)
			return;
		var scanMs = V2InputProtection.ToMilliseconds(JudgeScanWindowSec);
		var currentMs = V2InputProtection.ToMilliseconds(currentTime);
		var lowerMs = currentMs - scanMs;
		var upperMs = currentMs + scanMs;
		while (_pressWindowEnd < _plan.Units.Count &&
			V2InputProtection.ToMilliseconds(_plan.Units[_pressWindowEnd].Time) <= upperMs)
			_pressWindowEnd++;
		while (_pressWindowStart < _pressWindowEnd &&
			V2InputProtection.ToMilliseconds(_plan.Units[_pressWindowStart].Time) < lowerMs)
			_pressWindowStart++;
	}

	/// <summary>
	/// 按键候选的扫描半窗：取设置里的 Miss 窗再乘最宽的窗口系数（EX-Tap 1.5×），
	/// 这样 EX 的加宽窗口不会被扫描范围截断。
	/// </summary>
	private double JudgeScanWindowSec =>
		_engine.Settings.MissSec * JudgePlan.ExTapWindowScale;

	private void RecordHoldRelease(ActivePointer pointer, double releaseTime)
	{
		var trackMask = ProjectedTrackMask(pointer.ScreenPosition);
		foreach (var state in _sustainStates.Values)
		{
			// 屏幕区域 → 显示轨道：镜像下谱面 Left 的 Hold 显示在右轨，掩码必须按**显示**轨道比。
			var displayTrack = GameplayMirror.DisplayTrack(state.Path.Track);
			if (state.Path.Kind != SustainKind.Hold || !state.StartResolved ||
				state.EndResolved || (trackMask & MaskFor(displayTrack)) == 0)
				continue;
			var judgedReleaseTime = Math.Min(releaseTime, state.Path.EndTime);
			var bounds = state.Path.BoundsAt(judgedReleaseTime);
			if (InputJudgeRules.Overlaps(bounds,
				ChartPointOf(displayTrack, pointer.ScreenPosition).Position,
				CommunityTouchWidth))
				state.Contact = state.Contact.BeginLoss(
					judgedReleaseTime, _chart.BpmAtSeconds(judgedReleaseTime), _engine.Settings);
		}
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
				_engine.Settings.PrefectSec * unit.WindowScale, ContactPhase.Began);
		return unit.Kind == UnitKind.Input &&
			Math.Abs(unit.Time - time) <= _engine.Settings.MissSec * unit.WindowScale;
	}

	private void ProcessPress(TouchSample touch, double t, bool preRun)
	{
		// JudgePlan is time-sorted. Commit the shared timestamp immediately after
		// each accepted Note, matching the original Manual update.
		var inputMs = V2InputProtection.ToMilliseconds(t);
		for (var i = _pressWindowStart; i < _pressWindowEnd; i++)
		{
			var u = _plan.Units[i];
			if (u.Judged || u.Track != touch.Track ||
				u.Kind is not (UnitKind.Input or UnitKind.Mine) ||
				!_noteById.TryGetValue(u.NoteId, out var note) ||
				!PressTimeCanJudge(u, t) ||
				!InputJudgeRules.Overlaps(
					InputJudgeRules.Bounds(note.Position, note.Width),
					touch.Position, CommunityTouchWidth,
					expandByTouchWidth: u.Kind != UnitKind.Mine))
				continue;

			var noteMs = V2InputProtection.ToMilliseconds(u.Time);
			if (!_v2InputProtection.CanUse(noteMs, inputMs))
				continue;

			if (u.Kind == UnitKind.Mine)
			{
				if (preRun)
				{
					_v2InputProtection.CommitResolved(noteMs, inputMs);
					continue;
				}
				_engine.ApplyMine(touched: true);
				_judgedTheoreticalMax += JudgeEngine.ScoreDelta(u.Category, JudgeGrade.Prefect);
				u.Judged = true;
				_v2InputProtection.CommitResolved(noteMs, inputMs);
				ResolveNoteView(u, JudgeGrade.Miss);
				FlashJudge("MINE!");
				continue;
			}

			var r = _engine.JudgePress(u.Time, t, u.WindowScale, u.IsExTap);
			if (r.Resolution == JudgeResolution.Pending)
				continue;
			if (r.Grade == JudgeGrade.Miss && r.Timing == HitTiming.Early)
				_v2InputProtection.ProtectEarlyMiss(noteMs, inputMs);
			else
				_v2InputProtection.CommitResolved(noteMs, inputMs);
			if (preRun)
				continue;
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
		state.LastContactUpdateTime = Math.Max(state.Path.StartTime, resolvedTime);
		state.LostContactSecond = 0.0;
		state.Contact = HoldContactState.Contact;
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
		var from = Math.Max(state.LastContactUpdateTime, state.Path.StartTime);
		var bounds = state.Path.BoundsAt(activeUntil);
		var touch = FindNearestTouch(state.Path.Track, bounds.Center,
			bounds, expandByTouchWidth: true);
		var touching = _auto || touch.HasValue;
		UpdateHoldHeadContactView(state, touching, bounds);
		UpdateSustainEffect(state, touching, bounds, bounds.Center);
		if (activeUntil <= from)
			return;

		if (touching)
		{
			state.LostContactSecond = 0.0;
			state.Contact = HoldContactState.Contact;
		}
		else
		{
			state.Contact = state.Contact.BeginLoss(
				from, _chart.BpmAtSeconds(from), _engine.Settings);
			state.LostContactSecond = Math.Max(0.0,
				activeUntil - state.Contact.ContactLostAt!.Value);
		}

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
		UpdateMixerHeadView(state, bounds);
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

	private void UpdateMixerHeadView(SustainRuntime state, NoteBounds bounds)
	{
		// 到线或提前接头后只保留接触驱动的动态头，避免静态头留在起点。
		RecycleNoteViewImmediately(state.Path.HeadId);
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
		UpdateSustainHeadGeometry(state.MixerHeadView, bounds, state.SliderPosition);
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
		if (!GameSession.Settings.GameplayEffectsEnabled)
		{
			ReleaseSustainEffect(state);
			return;
		}
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
				Accent = NoteVisualSpec.HitPerfect,
				Rotation = state.Path.Track == Track.Center ? 0f : Mathf.Pi * 0.5f,
				ZIndex = 4,
			};
			_noteRoot.AddChild(state.ContactEffect);
		}

		state.ContactEffect.Position = TrackPositionAtLine(state.Path.Track, position);
		state.ContactEffect.SpanPx = SustainSpanPx(state.Path.Track, bounds);

		// GPU 粒子拖尾层：跟随动态头位置，断开时由 ReleaseSustainEffect 停发回收。
		var particleMode = GameSession.Settings.MotionMode;
		if (particleMode != UiMotionMode.Off)
		{
			if (state.ContactParticles == null ||
				!GodotObject.IsInstanceValid(state.ContactParticles))
			{
				state.ContactParticles = GameplaySustainParticles.Spawn(_noteRoot,
					// 侧轨旋转带符号：配合持续层局部 (0,1,0) 的方向，把粒子推向面板外侧。
					// 镜像时按显示轨道取符号，否则粒子会朝面板内侧喷。
					GameplayMirror.DisplayTrack(state.Path.Track) switch
					{
						Track.Center => 0f,
						Track.Left => Mathf.Pi * 0.5f,
						_ => -Mathf.Pi * 0.5f,
					},
					state.Path.Kind == SustainKind.Hold
						? HitEffectKind.Hold : HitEffectKind.Mixer,
					particleMode, SustainSpanPx(state.Path.Track, bounds));
			}

			state.ContactParticles.SetEmitting(true);
			state.ContactParticles.Position = TrackPositionAtLine(state.Path.Track, position);
		}
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
		state.ContactParticles?.SetEmitting(false);
		state.ContactParticles = null;
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
		_mixerSamplesScratch.Clear();
		foreach (var sample in _frameStartTouchSamples)
			_mixerSamplesScratch[(sample.Id, sample.Track)] = sample;
		foreach (var input in _framePointerInputs)
		{
			if (input.Time > tickTime)
				continue;
			switch (input.Action)
			{
				case PointerAction.Press:
					ReplaceMixerSamples(input.Id, input.ScreenPosition, ContactPhase.Began);
					break;
				case PointerAction.Move:
				{
					var phase = MixerPointerBegan(input.Id)
						? ContactPhase.Began : ContactPhase.Moved;
					ReplaceMixerSamples(input.Id, input.ScreenPosition, phase);
					break;
				}
				case PointerAction.Release:
					RemoveMixerSamples(input.Id);
					break;
			}
		}
		return FindNearestTouchIn(_mixerSamplesScratch.Values, state.Path.Track,
			bounds.Center, bounds, expandByTouchWidth: true).HasValue;
	}

	private bool MixerPointerBegan(int id) =>
		_mixerSamplesScratch.TryGetValue((id, Track.Center), out var center) &&
			center.Phase == ContactPhase.Began ||
		_mixerSamplesScratch.TryGetValue((id, Track.Left), out var left) &&
			left.Phase == ContactPhase.Began ||
		_mixerSamplesScratch.TryGetValue((id, Track.Right), out var right) &&
			right.Phase == ContactPhase.Began;

	private void ReplaceMixerSamples(int id, Vector2 pos, ContactPhase phase)
	{
		RemoveMixerSamples(id);
		var mask = ProjectedTrackMask(pos);
		if ((mask & TouchTrackMask.Center) != 0)
			_mixerSamplesScratch[(id, Track.Center)] =
				ChartTouchOf(id, Track.Center, pos, phase);
		if ((mask & TouchTrackMask.Left) != 0)
			_mixerSamplesScratch[(id, Track.Left)] =
				ChartTouchOf(id, Track.Left, pos, phase);
		if ((mask & TouchTrackMask.Right) != 0)
			_mixerSamplesScratch[(id, Track.Right)] =
				ChartTouchOf(id, Track.Right, pos, phase);
	}

	private void RemoveMixerSamples(int id)
	{
		_mixerSamplesScratch.Remove((id, Track.Center));
		_mixerSamplesScratch.Remove((id, Track.Left));
		_mixerSamplesScratch.Remove((id, Track.Right));
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
			_judgedTheoreticalMax += JudgeEngine.ScoreDelta(
				settlement.Unit.Category, JudgeGrade.Prefect);
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
		foreach (var unit in SustainJudgementRules.FailRemainingHold(
			_plan.Units, state.Path.HeadId, _engine))
		{
			_judgedTheoreticalMax += JudgeEngine.ScoreDelta(
				unit.Category, JudgeGrade.Prefect);
		}
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
					var r = _engine.Judge(u.Time, u.Time, u.WindowScale, u.IsExTap); // Auto：精确时刻判定
					ApplyUnit(u, r.Grade, r.Resolution);
					u.Judged = true;
					ResolveSustainStart(u, r.Grade, null, u.Time);
					ResolveNoteView(u, JudgeGrade.Prefect);
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
		var prefect = _engine.Settings.PrefectSec * unit.WindowScale;
		var miss = _engine.Settings.MissSec * unit.WindowScale;
		var good = _engine.Settings.GoodSec * unit.WindowScale;
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
		if (t > unit.Time + good)
			return;
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
		var prefect = _engine.Settings.PrefectSec * unit.WindowScale;
		var bounds = InputJudgeRules.Bounds(note.Position, note.Width);
		foreach (var touch in _frameTouchSamples)
		{
			if (touch.Track != unit.Track ||
				!InputJudgeRules.AcceptsMinePhase(unit.Time, t, prefect, touch.Phase) ||
				!InputJudgeRules.Overlaps(bounds, touch.Position, CommunityTouchWidth,
					expandByTouchWidth: false))
				continue;
			_engine.ApplyMine(touched: true);
			_judgedTheoreticalMax += JudgeEngine.ScoreDelta(unit.Category, JudgeGrade.Prefect);
			unit.Judged = true;
			ResolveNoteView(unit, JudgeGrade.Miss);
			FlashJudge("MINE!");
			return;
		}
		if (t >= unit.Time)
		{
			_engine.ApplyMine(touched: false);
			_judgedTheoreticalMax += JudgeEngine.ScoreDelta(unit.Category, JudgeGrade.Prefect);
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
				(state.Holding || state.Contact.HasLoss)))
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
		if (_auto || state is { StartResolved: true, Contact.ContactLostAt: null,
			Holding: true, Broken: false })
		{
			grade = JudgeGrade.Prefect;
			resolution = JudgeResolution.Prefect;
		}
		else if (state is { StartResolved: true, Contact.ContactLostAt: { } releaseTime,
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
			FinishHoldViews(state, grade);
		ResolveNoteView(unit, grade);
		FlashJudge(GradeText(grade, timing));
	}

	private void ApplyUnit(JudgeUnit unit, JudgeGrade grade,
		JudgeResolution? resolution = null)
	{

		if (!unit.Judged)
			_judgedTheoreticalMax += JudgeEngine.ScoreDelta(unit.Category, JudgeGrade.Prefect);
		_engine.Apply(unit.Category, grade, unit.AffectsCombo,
			unit.AffectsJudgeCounts, resolution);
	}

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
			if (_failedHoldNoteIds.Contains(n.Id))
				continue;
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
				v.QueueFree();
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
			l.Poly.QueueFree();
			l.Frame?.QueueFree();
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
				link.Poly.QueueFree();
				link.Frame?.QueueFree();
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
		return GameplayVisualMapper.ClipToJudgeLine(track, ref a, ref b, ref wa, ref wb);
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
		=> GameplayVisualMapper.PositionAt(n, rem);
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
			JudgeGrade.Prefect => 1f,
			JudgeGrade.Great => 0.82f,
			JudgeGrade.Good => 0.68f,
			_ => 0.75f,
		};

		_noteRoot.AddChild(new GameplayHitBloom
		{
			Position = effectPosition,
			Rotation = effectRotation,
			Accent = effectAccent,
			Kind = effectKind,
			SpanPx = note.Track == Track.Center ? size.X : size.Y,
			Strength = effectStrength,
			ZIndex = 3,
		});

		// GPU 粒子质感层：与爆发同点同向，ZIndex 2 夹在 Note 与爆发之间。
		GameplayHitParticles.Spawn(_noteRoot, effectPosition, effectRotation, effectAccent,
			effectKind, effectStrength, GameSession.Settings.MotionMode);
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
		JudgeGrade.Prefect => "PREFECT",
		JudgeGrade.Great => $"GREAT {(timing == HitTiming.Early ? "E" : "L")}",
		JudgeGrade.Good => $"GOOD {(timing == HitTiming.Early ? "E" : "L")}",
		_ => "MISS",
	};
}

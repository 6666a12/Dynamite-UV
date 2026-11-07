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
	//      x 向常量 = 1440 值 ×4/3，共享 y 基准见下述客户端偏移。几何依据见 docs/gameplay-spec.md §9、
	//      docs/video-geometry-analysis.md §8、_rev/notes/NOTE_POSITION_ANALYSIS.md）----
	// 客户端在共享基准几何上整体下移 40px；顶部 HUD 下移 8px，判定栏居中于屏幕顶部与蓝线之间。
	internal const float StageOffsetY = 40f;
	internal const float TopHudOffsetY = 8f;
	// Center 判定线：共享基准 y=861，客户端实际 y=901（谱面确认视频直出实测：亮线体 y 860–862；
	// 21:9 时代的拍板下移值 985 已废弃，勿回退）。
	// 粉色横条（基准 y=632, x 675..1244）是 Mixer 装饰 UI，不是判定线（同视频实测）。
	private const float CenterLineY = GameplayStageGeometry.CenterLineY;
	private const float CenterTrackX = GameplayStageGeometry.CenterTrackX; // Center 轨横向中心（P=2.0 处）
	// Center x 映射（用户拍板：note 在边上的位置 = 谱面数值的比例映射，分辨率无关）：
	// P 为条左缘，跨域 [P, P+W]，P∈[0,4] 铺满整条下边 [CenterX0, CenterX0+4·PosUnitPx]。
	// 数值来源：手元视频星爆拟合 x_hit = 312.5+204.9·P @1440（n=249，r²=0.9968），
	// 16:9 下 ×4/3。
	private const float CenterX0 = GameplayStageGeometry.CenterX0;
	private const float PosUnitPx = GameplayStageGeometry.CenterUnitPx;
	// 侧轨：竖条从屏幕中央附近生成、向左右边缘外移（实机验证的运动方式）。
	// 命中点 x：iPad 实测 138/1302 @1440 ×4/3 = 184/1736 @1920。
	private const float LeftLineX = GameplayStageGeometry.LeftLineX;
	private const float RightLineX = GameplayStageGeometry.RightLineX;
	// 侧轨流速 = 正面 × 行程比（691/790 ≈ 0.8747，实验性改动，用户拍板试手）：
	// 生成提前量因此与正面一致（691/(691/790)=790px ≈0.77s），正侧同刻 note 同时入场。
	private const float SideDistScale = GameplayStageGeometry.SideDistanceScale;
	private const float SideLeadPx = GameplayStageGeometry.SideLeadPx; // = 790，与 TravelPx 相同
	// 侧轨命中 y 使用当前固定的左右对称二维映射：y = 880 - 115 * (P + W/2)。
	private const float SideY0 = GameplayStageGeometry.SideY0;
	private const float SideUnit = GameplayStageGeometry.SideUnitPx;
	// 侧小节线（T9）视觉长度仍按满跨域渲染的换算单位（装饰，非实测映射）
	private const float SidePosUnitPx = GameplayStageGeometry.SidePositionUnitPx;
	// 侧轨条的**视觉长度**单位（≠ 位置单位）：谱面确认视频实测 W=2.0 → 长 ≈204px
	// @1440 ≈ 102px/W（video-geometry-analysis §4/§8，单侧样本但与用户观感一致）。
	private const float SideNoteLenUnitPx = GameplayStageGeometry.SideNoteLengthUnitPx;
	// 下落使用固定二维映射，不做透视投影。Lv10、DropSpeed=1 时恒为
	// 150 BPM 基准的 1026px/s，不再随谱面 BPM 改变。
	private const float BaseFallSpeedPx = GameplayStageGeometry.BaseFallSpeedPx;
	private const float TravelPx = GameplayStageGeometry.TravelPx;
	private const double PostHitViewLifetimeSec = 0.77;
	// 音符视觉宽 = Width × 1 Position 单位 × NoteVisualScale（用户拍板条宽压到 95%；
	// 原值 1.0 依据：手元视频实测 W=1 条宽 211±16px @1440 ≈ 位置单位 204.9px @1440）
	private const float NoteVisualScale = GameplayStageGeometry.NoteVisualScale;

	// Touch projection intentionally allows overlapping track candidates.
	// A lower corner can therefore judge both the Center and adjacent side track.
	private const float CenterRegionMinY = 771f + StageOffsetY; // 判定线上 90px
	private const float LeftRegionMaxX = 400f;
	private const float RightRegionMinX = 1520f;
	// 原版空间扩边公式已确认，但 NSTouchWidth 的设备运行值仍未知。这里集中使用一个
	// 试玩后提高的社区值（谱面 Position 单位）；每侧扩 0.20，Mine 仍不扩边。
	private const double CommunityTouchWidth = 0.40;
	private const int MousePointerId = -1;
	private static readonly Rect2 PauseButtonRect = new(900f, 60f + TopHudOffsetY, 120f, 64f);
	private static readonly Color JudgeLineColor = new(0.9f, 0.9f, 0.9f);

	private DynamiteUniverse.Shared.Chart.Chart _chart = null!;
	private LoadedChart _loaded = null!;
	private JudgePlan.Plan _plan = null!;
	private JudgeEngine _engine = null!;
	// BLEED 血量：仅 BleedEffective 时创建（HARDCORE 强制开启并取 Hardcore 数值）。
	private BleedHealth? _bleed;
	private SongPlayback _playback = null!;
	// 正常结束双锁存：谱末 +2 秒与音频自然结束分别单调锁存，两者都到才结算。
	private GameplayEndCondition _endCondition = null!;
	private V2IntegrationVerification? _verification;
	private V2IntegrationTrace? _trace;

	// Current pack/chart presentation and score identity stay immutable after startup.
	private GameplayRunContext _run = GameplayRunContext.InternalFallback;

	private List<Note> _notesByTime = new();
	private Dictionary<int, Note> _noteById = new();
		private bool _auto;
		private bool _finished;
		private bool _playbackStarted;
		private double _startSecond;
		private double _nextLogSec = 10.0;

	public override void _Ready()
	{
		GameSession.EnsureInit();
		// 配色在进游玩时定格：中途改设置/预设不会影响正在跑的谱面。
		NoteVisualSpec.Active = GameSession.Settings.ActiveNotePalette.Clone();
		NoteVisualSpec.BurstFollowsNote = GameSession.Settings.BurstColorFollowsNote;

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
			_loaded = selection.LoadedChart is { } cached &&
				cached.Preset == V2Integration.PresetFor(diff.Difficulty) ? cached : pack.LoadChart(diff);
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
		_bleed = GameSession.Settings.BleedEffective
			? new BleedHealth(GameSession.Settings.GameplayMode == GameplayMode.Hardcore
				? BleedRule.Hardcore : BleedRule.Standard)
			: null;
		foreach (var path in _plan.Sustains.Values)
		{
			_sustainStates[path.HeadId] = new SustainRuntime
			{
				Path = path,
				LastContactUpdateTime = path.StartTime,
				Mixer = path.Kind == SustainKind.Mixer
					? new MixerRuntime(path, MixerTimeline.BarAt(_chart, path)) : null,
			};
		}
		foreach (var unit in _plan.Units)
			if (unit.Category == ScoreCategory.MixerHolding && unit.SustainHeadId is { } headId)
				_sustainStates[headId].MixerTicks.Add(unit);
		_notesByTime = _chart.AllNotes.OrderBy(n => n.Second).ToList();
		_noteById = _notesByTime.ToDictionary(n => n.Id);
		_noteRenderer = new GameplayNoteRenderer(_noteRoot, _chart, GameSession.Settings, _loaded.SyncAccentRuntimeIds,
			_notesByTime, _noteById, _runtimeNoteSeconds, _plan.Sustains,
			VisualPoolPlanning.Analyze(_plan.Units, _notesByTime, _plan.Sustains.Values), ReadSustainVisual);
		GD.Print($"chart '{_chart.Title}' notes={_notesByTime.Count} " +
				 $"units={_plan.Units.Count} headline={_plan.HeadlineUnitCount} " +
				 $"theoreticalMax={_plan.TheoreticalMax} speed={GameSession.Settings.FallSpeedLevel}");

		_playback = new SongPlayback(this, songPath, GameSession.Settings.TimingOffsetMs,
			_verification);
		_endCondition = new GameplayEndCondition(_plan.EndTime + 2.0, _playback.TracksNaturalEnd);
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
		// Pause state is owned by the overlay; the short-circuit keeps it unread before _Ready.
		if (!_playbackStarted || _finished || _pause.IsPaused)
		{
			_pointerBuffer.DiscardPending();
			return;
		}
		if (_verification is { FixedClockEnabled: true } verification &&
			!verification.CanAdvance)
		{
			_pointerBuffer.DiscardPending();
			return;
		}
		_playback.AdvanceFixedFrame();
		var t = _playback.GetSongTime();
		var currentBar = _noteRenderer.CurrentBarAt(t);

		// Frame order is a behavior contract. Input snapshots, sustain updates, sweeping, mixer
		// finalization and the trace write must remain in this exact order during refactoring.
		_noteRenderer.SpawnNotes(t, currentBar);
		_noteRenderer.UpdateViews(t, currentBar, delta);
		FlushPendingInputs(t);
		UpdateSustainStates(t);
		SweepJudges(t);
		FinalizeEndedMixers(t);
		// BLEED 耗尽：本帧判定与 Mixer 收尾都已完成，先暂停音乐再立即进入失败字标前奏，
		// 不等待谱末/音频结束；专用失败页与血条 UI 仍是后续独立设计。
		if (_bleed is { IsDepleted: true })
		{
			_playback.Pause();
			ShowResults(stageFailure: true);
			return;
		}
		UpdateHud(t);
		_trace?.Write(t, currentBar, _noteRenderer.ActiveViews, _plan, _engine);
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
					 $"hp={_engine.Health} activeViews={_noteRenderer.ActiveViews.Count}");
		}

		// 结束检测（取晚，不取早）：谱末 +2s 与音频自然结束分别锁存，两者都满足才结算。
		// 音频自然结束后 SongTime 会冻结/归零，因此由 GameplayEndCondition 用音频结束后的
		// 真实帧 delta 补足到谱末 +2s；回退/固定帧时钟只走可测试的谱末路径，不会死锁。
		_endCondition.Observe(t, _playback.HasNaturallyEnded, delta);
		if (_endCondition.ShouldFinish)
			ShowResults();
	}
}

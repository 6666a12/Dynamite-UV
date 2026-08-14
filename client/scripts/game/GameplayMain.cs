using Godot;
using DuxCommunity.Audio;
using DuxCommunity.Ui;
using DuxShared.Chart;
using DuxShared.Judge;

namespace DuxCommunity.Game;

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
	// 下落使用固定二维映射，不做透视投影。Lv10 在 150 BPM、DropSpeed=1 时仍等价于
	// 1026px/s；内部换算为每 BarTime 的像素距离，以适配 BarTime 变速模型。
	private const float StandardBpm = 150f;
	private const float BaseFallSpeedPx = 1026f;
	private const float BaseFallDistancePxPerBar = BaseFallSpeedPx * 240f / StandardBpm;
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

	private static readonly Color ColBackground = new(0.08f, 0.08f, 0.12f);
	private static readonly Color ColJudgeLine = new(0.9f, 0.9f, 0.9f);
	// 用户拍板色板（§4 权威映射）：1 蓝 Tap、2 绿 Drag、3/4 琥珀 Hold（视频实证）、
	// 5 蓝 EX-Tap、6/7 粉 Mixer、8 地雷、9 小节线（暗灰细线）；Baked_SyncNote≠0 金色描边（仅 T1）。
	private static readonly Color ColTap = new(0.30f, 0.75f, 1.00f);
	private static readonly Color ColExTap = new(0.60f, 0.88f, 1.00f);
	private static readonly Color ColDrag = new(0.35f, 1.00f, 0.55f);
	private static readonly Color ColHold = new(1.00f, 0.72f, 0.30f); // 琥珀（两段视频实证；曾按用户类型表用红，视频对照后用户批准改琥珀）
	private static readonly Color ColMine = new(0.50f, 0.16f, 0.20f);
	private static readonly Color ColMixer = new(1.00f, 0.45f, 0.72f);
	private static readonly Color ColBarLine = new(0.55f, 0.55f, 0.62f, 0.45f);
	private static readonly Color ColSyncGold = new(1.00f, 0.82f, 0.30f);
	private static readonly Color ColMixerBar = new(0.9f, 0.3f, 0.55f);

	private DuxShared.Chart.Chart _chart = null!;
	private JudgePlan.Plan _plan = null!;
	private JudgeEngine _engine = null!;
	private SongClock _clock = null!;
	private AudioStreamPlayer _player = null!;

	// 当前谱面包信息（成绩写回 / 结算展示用）
	private string _packId = "tablear";
	private string _diffKey = "giga";
	private int _diffLevel = 15;
	private string _songTitle = "Tablear";
	private string? _coverPath;

	private List<Note> _notesByTime = new();
	private int _nextSpawn;
	private readonly List<NoteView> _active = new();
	private readonly Dictionary<int, NoteView> _viewByNoteId = new();
	private readonly HashSet<int> _holdTailIds = new();
	private readonly Dictionary<int, (JudgeUnit Unit, JudgeGrade Grade)> _deferredNoteViews = new();

	// 同一帧所有 note 都使用当前 BarTime 的流速：
	// visualDistance = (noteBar - currentBar) * speed(currentBar) * playerScale。
	// 该距离可随流速变化而增加，因此已生成 note 必须允许回退出屏并继续保留。
	private DropSpeedMap _dropSpeedMap = DropSpeedMap.Empty;
	private float _fallDistancePxPerBar = BaseFallDistancePxPerBar;

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
		public bool SliderInitialized;
		public double SliderPosition;
		public NoteView? MixerHeadView;
		public GameplaySustainEffect? ContactEffect;
	}
	private readonly List<PendingPointerInput> _pendingPointerInputs = new();
	private readonly Dictionary<int, ActivePointer> _activePointers = new();
	private readonly HashSet<int> _ignoredTouchIds = new();
	private readonly List<TouchSample> _frameTouchSamples = new();
	private readonly InputTimeGroupGate _inputTimeGroupGate = new();
	private readonly Dictionary<int, SustainRuntime> _sustainStates = new();
	private bool _auto;
	private bool _finished;
	private bool _paused;
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
	// 暂停菜单
	private Control _pauseLayer = null!;
	// 结算
	private Control _resultLayer = null!;
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

		// 谱面来源：GameSession 选中包；编辑器/Internal 构建才允许回退 testdata。
		string chartPath, songPath;
		if (GameSession.SelectedPack is { } pack && GameSession.SelectedDiff is { } diff)
		{
			chartPath = pack.ChartPathFor(diff);
			songPath = pack.AudioPath;
			_packId = pack.Id;
			_diffKey = diff.Diff;
			_diffLevel = diff.Level;
			_songTitle = pack.Title;
			_coverPath = pack.CoverPath;
			_auto = false;
		}
		else if (OS.HasFeature("internal_testdata") || OS.HasFeature("editor") ||
			OS.HasFeature("editor_runtime"))
		{
			chartPath = FallbackChartPath;
			songPath = FallbackSongPath;
			_auto = true; // 编辑器/Internal 演示默认 AUTO（F1 可切回手动）
		}
		else
		{
			GD.PushError("Public build entered gameplay without a selected chart pack.");
			GetTree().ChangeSceneToFile("res://scenes/song_select.tscn");
			return;
		}

		BuildStage();

		_chart = DynamixChartLoader.Load(Godot.FileAccess.GetFileAsString(chartPath));
		var preset = PresetForDifficulty(_diffKey);
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
		_fallDistancePxPerBar = BaseFallDistancePxPerBar *
			(float)GameSession.Settings.FallSpeedMultiplier;
		_notesByTime = _chart.AllNotes.OrderBy(n => n.Second).ToList();
		_noteById = _notesByTime.ToDictionary(n => n.Id);
		_dropSpeedMap = new DropSpeedMap(_chart.DropSpeeds);
		GD.Print($"chart '{_chart.Title}' notes={_notesByTime.Count} " +
				 $"units={_plan.Units.Count} headline={_plan.HeadlineUnitCount} " +
				 $"theoreticalMax={_plan.TheoreticalMax} speed={GameSession.Settings.FallSpeedLevel}");

		_player = new AudioStreamPlayer
		{
			Stream = Res.LoadAudio(songPath),
			Bus = "Music",
		};
		AddChild(_player);
		_clock = new SongClock();
		_clock.Attach(_player);
		_clock.UserOffsetMs = GameSession.Settings.TimingOffsetMs;
		AddChild(_clock);
		// 调试：DUX_START_SEC=起始秒（用于与录屏做同刻对比截图）
		var startSec = 0.0;
		if (double.TryParse(System.Environment.GetEnvironmentVariable("DUX_START_SEC"),
				out var envStart) && envStart > 0)
			startSec = envStart;
		_clock.Play(startSec);

		RefreshTitleTag();
	}

	public override void _Process(double delta)
	{
		if (_finished || _paused)
		{
			_pendingPointerInputs.Clear();
			return;
		}
		var t = _clock.GetSongTime();
		var currentBar = CurrentBarAt(t);

		SpawnNotes(currentBar);
		UpdateViews(t, currentBar, delta);
		FlushPendingInputs(t);
		UpdateSustainStates(t);
		SweepJudges(t);
		FinalizeEndedMixers(t);
		UpdateHud(t);

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
		var audioEnded = !_clock.ManualFallback && _clock.IsPlaying && !_player.Playing;
		if (t > _plan.EndTime + 2.0 || audioEnded)
			ShowResults();
	}

	public override void _Input(InputEvent e)
	{
		if (_finished || _paused)
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
		if (_finished) return;

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
				_clock.Seek(Math.Max(0.0, _plan.EndTime - 3.0));
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
		var t = _clock.GetSongTime();
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
		var framePresses = new List<(TouchSample Touch, double Time)>();
		var frameReleases = new List<(ActivePointer Pointer, double Time)>();
		foreach (var pointer in _activePointers.Values)
		{
			pointer.Phase = ContactPhase.Stationary;
			_frameTouchSamples.Add(new TouchSample(
				pointer.Id, pointer.Track, pointer.Position, pointer.Phase));
		}

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
		}
		else
		{
			state.ContactLostAt ??= from;
			state.LostContactSecond = Math.Max(0.0,
				activeUntil - state.ContactLostAt.Value);
		}

		state.LastContactUpdateTime = activeUntil;
		var grace = _engine.Settings.HoldContactGraceSeconds(
			_chart.BpmAtSeconds(activeUntil));
		if (state.LostContactSecond > grace)
		{
			BreakHold(state, state.ContactLostAt ?? activeUntil);
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
				SizeFor(state.Path.Head), ColMixer);
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
		// 当前素材方案只管理侧轨 Mixer；Hold 三轨共用同一份旋转后的效果。
		if (state.Path.Kind == SustainKind.Mixer && state.Path.Track == Track.Center)
			connected = false;
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
				Accent = state.Path.Kind == SustainKind.Hold ? ColHold : ColMixer,
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
		NoteBounds? requiredBounds = null, bool expandByTouchWidth = true)
	{
		TouchSample? best = null;
		var bestDistance = double.MaxValue;
		foreach (var touch in _frameTouchSamples)
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
			if (_auto || (state.StartResolved && state.Holding && !state.Broken))
				ApplyUnit(unit, JudgeGrade.Prefect, JudgeResolution.Prefect);
			else
				ApplyUnit(unit, JudgeGrade.Miss, JudgeResolution.AutoMiss);
			return;
		}

		unit.Judged = true;
		var ok = _auto || state.Holding;
		ApplyUnit(unit, ok ? JudgeGrade.Prefect : JudgeGrade.Miss,
			ok ? JudgeResolution.Prefect : JudgeResolution.AutoMiss);
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
					headView.MarkJudged(ColHold);
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

	private void SpawnNotes(double currentBar)
	{
		while (_nextSpawn < _notesByTime.Count)
		{
			var peek = _notesByTime[_nextSpawn];
			// 侧轨流速 75% 但行程不变 → 生成提前量更短（SideLeadPx），晚些生成
			var threshold = peek.Track == Track.Center ? TravelPx : SideLeadPx;
			if (VisualDistanceFor(peek, currentBar) > threshold)
				break;
			var n = _notesByTime[_nextSpawn++];
			if (ShouldRenderStaticNote(n))
			{
				var view = NoteView.Create(n, SizeFor(n), ColorFor(n),
					n.SyncNote != 0 && n.Type == NoteType.Tap); // 金框只在 Tap（用户拍板）
				view.Position = PositionFor(n, currentBar);
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
				var remaining = VisualDistanceFor(v.Model, currentBar);
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
					: PositionForActiveView(v.Model, currentBar);
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
			var rA = VisualDistanceFor(from, currentBar);
			var rB = VisualDistanceFor(to, currentBar);
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
		_chart.Sections.Count > 0 ? _chart.SecondsToBarTime(second) : second;

	private float VisualDistanceFor(Note n, double currentBar)
	{
		// 空时间线只能使用 Baked_Second，因此把秒作为后备的单调视觉坐标。
		var noteBar = _chart.Sections.Count > 0 ? n.BarTime : n.Second;
		return (float)(_dropSpeedMap.RemainingDistance(noteBar, currentBar) *
			_fallDistancePxPerBar);
	}

	private Vector2 PositionFor(Note n, double currentBar)
	{
		// 生成时尚未到线；保留钳制只防止跳转启动时把新视图放到线外。
		var rem = Mathf.Max(0f, VisualDistanceFor(n, currentBar));
		return PositionAt(n, rem);
	}

	private Vector2 PositionForActiveView(Note n, double currentBar)
	{
		var rem = VisualDistanceFor(n, currentBar);
		if (!MovesPastJudgeLineContinuously(n.Type))
			rem = Mathf.Max(0f, rem);
		return PositionAt(n, rem);
	}

	private static bool MovesPastJudgeLineContinuously(NoteType type) =>
		UsesOrdinaryMissFallthrough(type);

	private float MissFallSpeedPx(Note note, double t, double currentBar)
	{
		var bpm = (float)_chart.BpmAtSeconds(t);
		var dropSpeed = (float)Math.Abs(_dropSpeedMap.SpeedAt(currentBar));
		var speed = _fallDistancePxPerBar * bpm / 240f * dropSpeed;
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

	private static Color ColorFor(Note n) => n.Type switch
	{
		NoteType.Tap => ColTap,
		NoteType.ExTap => ColExTap,
		NoteType.Drag => ColDrag,
		NoteType.HoldHead or NoteType.HoldNode => ColHold,
		NoteType.Mine => ColMine,
		NoteType.BarLine => ColBarLine,
		_ => ColMixer, // Mixer 6/7 粉缎带
	};

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
				note.SyncNote != 0 && note.Type == NoteType.Tap);
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

	private static Color HitAccentFor(NoteType type) => type switch
	{
		NoteType.Tap => ColTap,
		NoteType.ExTap => ColExTap,
		NoteType.Drag => ColDrag,
		NoteType.HoldHead or NoteType.HoldNode => ColHold,
		NoteType.MixerHead or NoteType.MixerNode => ColMixer,
		NoteType.Mine => new Color(0.93f, 0.24f, 0.33f),
		_ => ColBarLine,
	};

	private static HitEffectKind HitEffectKindFor(NoteType type) => type switch
	{
		NoteType.ExTap => HitEffectKind.ExTap,
		NoteType.Drag => HitEffectKind.Drag,
		NoteType.HoldHead or NoteType.HoldNode => HitEffectKind.Hold,
		NoteType.MixerHead or NoteType.MixerNode => HitEffectKind.Mixer,
		NoteType.Mine => HitEffectKind.Mine,
		_ => HitEffectKind.Tap,
	};

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

	private static Color LinkColorFor(Note n) => n.Type switch
	{
		NoteType.HoldHead or NoteType.HoldNode => new Color(1.0f, 0.70f, 0.45f, 0.28f), // 亮琥珀半透明（实机可透出背景）
		NoteType.Drag => new Color(ColDrag, 0.35f),
		NoteType.MixerHead or NoteType.MixerNode => new Color(ColMixer, 0.35f),
		_ => new Color(ColTap, 0.35f),
	};

	// ---- UI ----

	private void BuildStage()
	{
		_stageRoot = new Node2D { Name = "StageVisuals" };
		_noteRoot = new Node2D { Name = "NoteVisuals" };
		_hudRoot = new Node2D { Name = "GameplayHud", ZIndex = 20 };
		AddChild(_stageRoot);
		AddChild(_noteRoot);
		AddChild(_hudRoot);
		_stageRoot.AddChild(new GameplayBackdrop { ZIndex = -2 });

		// 判定线（实机为隐形线，此处用暗色细线示意）：三线构成 ∪ 形——
		// 底部横线贯通左右，与两侧竖线在两端相接；Mixer 粉条 + 中央光标。
		var lineCol = new Color(0.35f, 0.35f, 0.4f);
		AddLine(new Vector2(60, CenterLineY - 2), new Vector2(1800, 4), lineCol); // 底线延长超出侧线、接近屏边（原版如此）
		AddLine(new Vector2(LeftLineX - 2, 70), new Vector2(4, CenterLineY - 70), lineCol); // 顶栏下到线底（用户拍板延长）
		AddLine(new Vector2(RightLineX - 2, 70), new Vector2(4, CenterLineY - 70), lineCol);
		AddLine(new Vector2(CenterTrackX - 4, MixerBarY - 14), new Vector2(8, 28),
			new Color(0.5f, 0.5f, 1.0f));

		// Mixer 装饰 UI：粉色固定横条 + 中央光标（实机装饰元素，T6/T7 粉缎带
		// 现在是下落音符，由常规视图/连接体渲染）
		_mixerBar = new CutPanel
		{
			Position = new Vector2(MixerBarLeft, MixerBarY - 12),
			Size = new Vector2(MixerBarLen, 24),
			Cut = 4,
			Fill = new Color(ColMixerBar, 0.15f),
			Border = new Color(ColMixerBar, 0.68f),
			BorderWidth = 2f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_stageRoot.AddChild(_mixerBar);

		BuildHud();
		BuildPauseMenu();
		BuildResultLayer();
	}

	private void BuildHud()
	{
		// 原版风 HUD（2340 直出截图换算到 1440）：顶栏细青线框（中央缺口放暂停钮）、
		// 底线上的发光进度段、左下曲名+难度、右下分数、顶部 CLEAR、
		// 中央偏下 COMBO + 判定字。
		var frameCol = new Color(UiFonts.Cyan, 0.45f);
		AddHudLine(new Vector2(53, 64), new Vector2(773, 2), frameCol);
		AddHudLine(new Vector2(1093, 64), new Vector2(773, 2), frameCol);

		// 顶部中央：六个紧凑 pill；CLEAR 是得分/理论满分，不是 timing accuracy。
		_pillP = MakePill(461, UiFonts.Cyan);
		_pillGr = MakePill(629, UiFonts.Hard);
		_pillGo = MakePill(797, UiFonts.Casual);
		_pillM = MakePill(965, new Color(0.53f, 0.53f, 0.53f));
		_pillAcc = MakePill(1133, new Color(0.64f, 0.90f, 0.21f));
		_pillMc = MakePill(1301, UiFonts.Text);

		// 顶部 pill 行下方正中间：暂停按钮（用户拍板：放大，嵌在顶栏缺口）
		_pauseBtn = new CutButton
		{
			Position = new Vector2(900, 60),
			Size = new Vector2(120, 64),
			Text = "II",
			FontSize = 30,
			TechFont = true,
			ZIndex = 2,
		};
		_pauseBtn.Pressed += TogglePause;
		_hudRoot.AddChild(_pauseBtn);

		// 底线上的发光进度段（从中央向两侧生长）
		_hudRoot.AddChild(new ColorRect
		{
			Color = new Color(1, 1, 1, 0.045f),
			Position = new Vector2(493, CenterLineY - 2),
			Size = new Vector2(933, 3),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		});
		_progressFill = new ColorRect
		{
			Color = new Color(UiFonts.Cyan, 0.68f),
			Position = new Vector2(960, CenterLineY - 2),
			Size = new Vector2(0, 3),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		_hudRoot.AddChild(_progressFill);

		// 左下：曲名 + 难度（Dynamit 样例布局；实时评级已按用户要求删除）
		_titleTag = new RichTextLabel
		{
			Position = new Vector2(67, 920),
			Size = new Vector2(620, 44),
			BbcodeEnabled = true,
			ScrollActive = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		_titleTag.AddThemeFontOverride("normal_font", UiFonts.Cjk);
		_titleTag.AddThemeFontSizeOverride("normal_font_size", 24);
		_hudRoot.AddChild(_titleTag);

		// 右下：分数（CLEAR 收进顶部 pill 行）
		_scoreLabel = new Label
		{
			Position = new Vector2(1290, 879),
			Size = new Vector2(550, 74),
			HorizontalAlignment = HorizontalAlignment.Right,
			Text = "0",
			ZIndex = 2,
		};
		_scoreLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_scoreLabel.AddThemeFontSizeOverride("font_size", 58);
		_scoreLabel.AddThemeColorOverride("font_color", UiFonts.Text);
		_hudRoot.AddChild(_scoreLabel);

		// 判定反馈在线上方；Combo 放到判定线下方安全区（用户 2026-08-10 拍板）。
		_comboLabel = new Label
		{
			Position = new Vector2(693, 872),
			Size = new Vector2(533, 72),
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = "",
			ZIndex = 2,
		};
		_comboLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_comboLabel.AddThemeFontSizeOverride("font_size", 62);
		_comboLabel.AddThemeColorOverride("font_color", UiFonts.Text);
		_hudRoot.AddChild(_comboLabel);
		_comboSub = new Label
		{
			Position = new Vector2(826.5f, 944),
			Size = new Vector2(267, 28),
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = "",
			ZIndex = 2,
		};
		_comboSub.AddThemeFontOverride("font", UiFonts.Tech);
		_comboSub.AddThemeFontSizeOverride("font_size", 18);
		_comboSub.AddThemeColorOverride("font_color", UiFonts.Dim);
		_hudRoot.AddChild(_comboSub);

		_judgeFlash = new Label
		{
			Position = new Vector2(CenterTrackX - 150, 696),
			Size = new Vector2(300, 44),
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1, 1, 1, 0),
			ZIndex = 2,
		};
		_judgeFlash.AddThemeFontOverride("font", UiFonts.TechBold);
		_judgeFlash.AddThemeFontSizeOverride("font_size", 34);
		_judgeFlash.AddThemeColorOverride("font_color", UiFonts.Cyan);
		_hudRoot.AddChild(_judgeFlash);
	}

	private void AddHudLine(Vector2 pos, Vector2 size, Color color)
	{
		_hudRoot.AddChild(new ColorRect
		{
			Color = color,
			Position = pos,
			Size = size,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		});
	}

	// 顶部 pill：切角暗底 + 居中彩色文字，返回值标签由 UpdateHud 刷新
	private Label MakePill(float x, Color color)
	{
		_hudRoot.AddChild(new CutPanel
		{
			Position = new Vector2(x, 12),
			Size = new Vector2(158, 40),
			Cut = 12,
			Fill = new Color(0.03f, 0.05f, 0.10f, 0.72f),
			ZIndex = 2,
		});
		var label = new Label
		{
			Position = new Vector2(x, 17),
			Size = new Vector2(158, 32),
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		label.AddThemeFontOverride("font", UiFonts.Tech);
		label.AddThemeFontSizeOverride("font_size", 16);
		label.AddThemeColorOverride("font_color", color);
		_hudRoot.AddChild(label);
		return label;
	}

	private RichTextLabel _titleTag = null!;

	private void RefreshTitleTag()
	{
		var hex = UiFonts.DiffColor(_diffKey).ToHtml(false);
		var auto = _auto ? "   [color=#7c88b0]AUTO (F1)[/color]" : "";
		var diff = _diffLevel > 0
			? $"{UiFonts.DiffName(_diffKey)} · Lv {_diffLevel}"
			: UiFonts.DiffName(_diffKey);
		_titleTag.Text =
			$"{_songTitle}   [color=#{hex}]{diff}[/color]{auto}";
	}

	private void BuildPauseMenu()
	{
		_pauseLayer = new Control { Visible = false, ZIndex = 60 };
		AddChild(_pauseLayer);

		var dim = new ColorRect
		{
			Color = new Color(0.02f, 0.03f, 0.07f, 0.72f),
			Size = new Vector2(1920, 1080),
		};
		_pauseLayer.AddChild(dim);

		_pauseLayer.AddChild(new CutPanel
		{
			Position = new Vector2(610, 300),
			Size = new Vector2(700, 480),
		});

		var title = new Label
		{
			Position = new Vector2(610, 340),
			Size = new Vector2(700, 70),
			Text = "PAUSED",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontOverride("font", UiFonts.Tech);
		title.AddThemeFontSizeOverride("font_size", 52);
		title.AddThemeColorOverride("font_color", UiFonts.Text);
		_pauseLayer.AddChild(title);

		var resume = new CutButton
		{
			Position = new Vector2(770, 450),
			Size = new Vector2(380, 76),
			Text = "继续",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 28,
		};
		resume.Pressed += TogglePause;
		_pauseLayer.AddChild(resume);

		var retry = new CutButton
		{
			Position = new Vector2(770, 546),
			Size = new Vector2(380, 76),
			Text = "重开",
			FontSize = 28,
		};
		retry.Pressed += Restart;
		_pauseLayer.AddChild(retry);

		var quit = new CutButton
		{
			Position = new Vector2(770, 642),
			Size = new Vector2(380, 76),
			Text = "返回选曲",
			FontSize = 28,
		};
		quit.Pressed += ExitToSelect;
		_pauseLayer.AddChild(quit);
	}

	private void BuildResultLayer()
	{
		_resultLayer = new Control { Visible = false, ZIndex = 100 };
		AddChild(_resultLayer);

		// 先铺不透明 clean-room fallback；无封面时也绝不会透出游玩层。
		_resultLayer.AddChild(new ColorRect
		{
			Color = new Color(0.018f, 0.026f, 0.060f),
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		// 封面拉伸铺满 + 压暗（样式稿为模糊，Godot 侧先用压暗近似）。
		_resultCover = new TextureRect
		{
			Position = Vector2.Zero,
			Size = new Vector2(1920, 1080),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			Modulate = new Color(0.34f, 0.34f, 0.38f),
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_resultLayer.AddChild(_resultCover);
		var dim = new ColorRect
		{
			Color = new Color(0.04f, 0.05f, 0.10f, 0.58f),
			Size = new Vector2(1920, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_resultLayer.AddChild(dim);

		_resultLayer.AddChild(new CutPanel
		{
			Position = new Vector2(380, 240),
			Size = new Vector2(1160, 600),
			Fill = new Color(0.078f, 0.106f, 0.20f, 0.78f),
		});

		_songLine = new Label { Position = new Vector2(450, 276), Size = new Vector2(1020, 34) };
		_songLine.AddThemeFontSizeOverride("font_size", 24);
		_songLine.AddThemeColorOverride("font_color", UiFonts.Dim);
		_resultLayer.AddChild(_songLine);

		_gradeLabel = new Label { Position = new Vector2(480, 330), Size = new Vector2(220, 180) };
		_gradeLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_gradeLabel.AddThemeFontSizeOverride("font_size", 140);
		_gradeLabel.AddThemeColorOverride("font_color", UiFonts.Cyan);
		_resultLayer.AddChild(_gradeLabel);

		_newRecChip = new Control { Position = new Vector2(490, 530), Size = new Vector2(200, 40) };
		var chipBg = new CutPanel
		{
			Size = new Vector2(200, 40),
			Cut = 8,
			Fill = UiFonts.Pink,
			Border = UiFonts.Pink,
		};
		_newRecChip.AddChild(chipBg);
		var chipText = new Label
		{
			Size = new Vector2(200, 40),
			Text = "NEW RECORD",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		chipText.AddThemeFontOverride("font", UiFonts.Tech);
		chipText.AddThemeFontSizeOverride("font_size", 18);
		chipText.AddThemeColorOverride("font_color", new Color("1a0510"));
		_newRecChip.AddChild(chipText);
		_resultLayer.AddChild(_newRecChip);

		_resultScore = new Label { Position = new Vector2(750, 320), Size = new Vector2(720, 90) };
		_resultScore.AddThemeFontOverride("font", UiFonts.TechBold);
		_resultScore.AddThemeFontSizeOverride("font_size", 76);
		_resultScore.AddThemeColorOverride("font_color", UiFonts.Text);
		_resultLayer.AddChild(_resultScore);

		_resultAcc = new Label { Position = new Vector2(750, 424), Size = new Vector2(720, 36) };
		_resultAcc.AddThemeFontOverride("font", UiFonts.Tech);
		_resultAcc.AddThemeFontSizeOverride("font_size", 26);
		_resultAcc.AddThemeColorOverride("font_color", UiFonts.Dim);
		_resultLayer.AddChild(_resultAcc);

		// 判定分布条（4 段彩色，宽度按占比）
		const float barX = 750, barY = 486, barH = 14;
		_distP = DistBar(barX, barY, UiFonts.Cyan);
		_distGr = DistBar(barX, barY, UiFonts.Hard);
		_distGo = DistBar(barX, barY, UiFonts.Casual);
		_distM = DistBar(barX, barY, new Color(0.33f, 0.33f, 0.33f));

		_resultCountP = MakeResultStatCell(750, "P", UiFonts.Cyan);
		_resultCountGr = MakeResultStatCell(930, "GR", UiFonts.Hard);
		_resultCountGo = MakeResultStatCell(1110, "GD", UiFonts.Casual);
		_resultCountM = MakeResultStatCell(1290, "M", new Color(0.52f, 0.52f, 0.56f));

		var back = new CutButton
		{
			Position = new Vector2(450, 720),
			Size = new Vector2(280, 72),
			Text = "返回选曲",
			FontSize = 26,
		};
		back.Pressed += ExitToSelect;
		_resultLayer.AddChild(back);

		var next = new CutButton
		{
			Position = new Vector2(750, 720),
			Size = new Vector2(280, 72),
			Text = "下一首",
			FontSize = 26,
		};
		next.Pressed += NextSong;
		_resultLayer.AddChild(next);

		var retry = new CutButton
		{
			Position = new Vector2(1050, 720),
			Size = new Vector2(280, 72),
			Text = "再来一次",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 26,
		};
		retry.Pressed += Restart;
		_resultLayer.AddChild(retry);

		ColorRect DistBar(float x, float y, Color c)
		{
			var r = new ColorRect
			{
				Color = c,
				Position = new Vector2(x, y),
				Size = new Vector2(0, barH),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			_resultLayer.AddChild(r);
			return r;
		}
	}

	private Label MakeResultStatCell(float x, string title, Color color)
	{
		_resultLayer.AddChild(new CutPanel
		{
			Position = new Vector2(x, 520),
			Size = new Vector2(160, 72),
			Cut = 10,
			Fill = new Color(0.025f, 0.04f, 0.09f, 0.84f),
			Border = new Color(color, 0.48f),
			BorderWidth = 1.5f,
		});
		var tag = new Label
		{
			Position = new Vector2(x + 14, 530),
			Size = new Vector2(42, 48),
			Text = title,
			VerticalAlignment = VerticalAlignment.Center,
		};
		tag.AddThemeFontOverride("font", UiFonts.TechBold);
		tag.AddThemeFontSizeOverride("font_size", 19);
		tag.AddThemeColorOverride("font_color", color);
		_resultLayer.AddChild(tag);

		var value = new Label
		{
			Position = new Vector2(x + 50, 530),
			Size = new Vector2(94, 48),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
		};
		value.AddThemeFontOverride("font", UiFonts.TechBold);
		value.AddThemeFontSizeOverride("font_size", 24);
		value.AddThemeColorOverride("font_color", UiFonts.Text);
		_resultLayer.AddChild(value);
		return value;
	}

	private Label _songLine = null!;

	private void TogglePause()
	{
		if (_finished) return;
		_paused = !_paused;
		if (_paused)
		{
			ClearPointerState();
			_clock.Pause();
			_pauseLayer.Visible = true;
		}
		else
		{
			_pauseLayer.Visible = false;
			_clock.Resume();
		}
	}

	private void ClearPointerState()
	{
		_pendingPointerInputs.Clear();
		_activePointers.Clear();
		_frameTouchSamples.Clear();
		_ignoredTouchIds.Clear();
		_inputTimeGroupGate.Reset();
	}

	private void Restart()
	{
		_clock.Pause();
		_paused = false;
		GetTree().ChangeSceneToFile("res://scenes/gameplay.tscn");
	}

	private void ExitToSelect()
	{
		_clock.Pause();
		_paused = false;
		GetTree().ChangeSceneToFile("res://scenes/song_select.tscn");
	}

	private void NextSong()
	{
		if (!GameSession.SelectNextSong())
			return;
		_clock.Pause();
		GetTree().ChangeSceneToFile("res://scenes/gameplay.tscn");
	}

	private double _flashTimer;
	private const double JudgeFlashDuration = 0.45;

	private void FlashJudge(string text)
	{
		_judgeFlash.Text = text;
		_judgeFlash.Modulate = Colors.White;
		_judgeFlash.Position = new Vector2(CenterTrackX - 150, 696);
		_flashTimer = JudgeFlashDuration;
	}

	private void UpdateHud(double t)
	{
		if (_flashTimer > 0)
		{
			_flashTimer -= GetProcessDeltaTime();
			if (_flashTimer <= 0)
				_judgeFlash.Modulate = new Color(1, 1, 1, 0);
			else
			{
				var progress = 1f - (float)(_flashTimer / JudgeFlashDuration);
				var alpha = Mathf.Clamp((float)(_flashTimer / 0.14), 0f, 1f);
				_judgeFlash.Position = new Vector2(CenterTrackX - 150, 696 - progress * 24f);
				_judgeFlash.Modulate = new Color(1, 1, 1, alpha);
			}
		}

		_scoreLabel.Text = $"{_engine.NormalizedScore(_plan.TheoreticalMax):N0}";
		// 实时 CLEAR = 当前得分 / 已判定单元的理论满分（未判定不计入）。
		var maxSoFar = 0;
		foreach (var u in _plan.Units)
			if (u.Judged)
				maxSoFar += JudgeEngine.ScoreDelta(u.Category, JudgeGrade.Prefect);
		var liveClear = maxSoFar > 0 ? 100.0 * _engine.Score / maxSoFar : 100.0;
		_pillP.Text = $"PERFECT {_engine.CountPrefect}";
		_pillGr.Text = $"GREAT {_engine.CountGreat}";
		_pillGo.Text = $"GOOD {_engine.CountGood}";
		_pillM.Text = $"MISS {_engine.CountMiss}";
		_pillAcc.Text = $"CLEAR {liveClear:F2}%";
		_pillMc.Text = $"M.COMBO {_engine.MaxCombo}";
		_comboLabel.Text = _engine.Combo > 1 ? $"{_engine.Combo}" : "";
		_comboSub.Text = _engine.Combo > 1 ? "COMBO" : "";
		// 底线上的进度段：从中央向两侧生长
		var pw = 933f * Mathf.Clamp((float)(t / Math.Max(0.01, _plan.EndTime)), 0f, 1f);
		_progressFill.Position = new Vector2(960 - pw / 2f, CenterLineY - 2);
		_progressFill.Size = new Vector2(pw, 3);
	}

	private void ShowResults()
	{
		_finished = true;
		_clock.Pause();
		var pct = _engine.Percent(_plan.TheoreticalMax);
		var normalizedScore = _engine.NormalizedScore(_plan.TheoreticalMax);

		// 成绩写回（AUTO 演示不计入成绩库）
		var isNewRecord = false;
		var grade = ScoreStore.GradeOf(pct);
		if (!_auto)
		{
			isNewRecord = GameSession.Scores.TryUpdate(_packId, _diffKey, new ScoreRecord
			{
				Score = normalizedScore,
				Acc = pct,
				MaxCombo = _engine.MaxCombo,
				Grade = grade,
				Perfect = _engine.CountPrefect,
				Great = _engine.CountGreat,
				Good = _engine.CountGood,
				Miss = _engine.CountMiss,
			});
		}

		// 结算面板（样式稿 #result）
		if (_coverPath is { } cp && Res.LoadTexture(cp) is { } tex)
		{
			_resultCover.Texture = tex;
			_resultCover.Visible = true;
		}
		var resultDiff = _diffLevel > 0
			? $"{UiFonts.DiffName(_diffKey)} {_diffLevel}"
			: UiFonts.DiffName(_diffKey);
		_songLine.Text = $"RESULT · {_songTitle} · {resultDiff}" +
						 (_auto ? " · AUTO（不计成绩）" : "");
		_gradeLabel.Text = grade;
		_gradeLabel.AddThemeColorOverride("font_color", grade switch
		{
			"Ω" => UiFonts.Pink,
			"S" => UiFonts.Cyan,
			"A" => UiFonts.Hard,
			"B" => UiFonts.Normal,
			_ => UiFonts.Dim,
		});
		_newRecChip.Visible = isNewRecord;
		_resultScore.Text = $"{normalizedScore:N0}";
		_resultAcc.Text = $"CLEAR {pct:F2}% · MAX COMBO {_engine.MaxCombo:N0} / {_plan.HeadlineUnitCount:N0}";

		// 判定分布条
		var total = Math.Max(1, _engine.CountPrefect + _engine.CountGreat +
			_engine.CountGood + _engine.CountMiss);
		const float barX = 750, barW = 720;
		var wP = barW * _engine.CountPrefect / total;
		var wGr = barW * _engine.CountGreat / total;
		var wGo = barW * _engine.CountGood / total;
		var wM = barW - wP - wGr - wGo;
		_distP.Position = new Vector2(barX, _distP.Position.Y);
		_distP.Size = new Vector2(wP, _distP.Size.Y);
		_distGr.Position = new Vector2(barX + wP, _distGr.Position.Y);
		_distGr.Size = new Vector2(wGr, _distGr.Size.Y);
		_distGo.Position = new Vector2(barX + wP + wGr, _distGo.Position.Y);
		_distGo.Size = new Vector2(wGo, _distGo.Size.Y);
		_distM.Position = new Vector2(barX + wP + wGr + wGo, _distM.Position.Y);
		_distM.Size = new Vector2(wM, _distM.Size.Y);

		_resultCountP.Text = $"{_engine.CountPrefect:N0}";
		_resultCountGr.Text = $"{_engine.CountGreat:N0}";
		_resultCountGo.Text = $"{_engine.CountGood:N0}";
		_resultCountM.Text = $"{_engine.CountMiss:N0}";

		_stageRoot.Visible = false;
		_noteRoot.Visible = false;
		_hudRoot.Visible = false;
		_pauseLayer.Visible = false;
		_resultLayer.Visible = true;

		GD.Print($"=== RESULT === {_songTitle} [{_diffKey} Lv{_diffLevel}] " +
				 $"score={normalizedScore} rawScore={_engine.Score} clear={pct:F2}% grade={grade} " +
				 $"P={_engine.CountPrefect} Gr={_engine.CountGreat} " +
				 $"Gd={_engine.CountGood} M={_engine.CountMiss} " +
				 $"maxCombo={_engine.MaxCombo} newRecord={isNewRecord}");
	}

	private void AddLine(Vector2 pos, Vector2 size, Color? color = null)
	{
		var line = new ColorRect
		{
			Color = color ?? ColJudgeLine,
			Position = pos,
			Size = size,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = -1, // 背景之上、连接体/音符之下
		};
		_stageRoot.AddChild(line);
	}

	private static JudgePreset PresetForDifficulty(string diff) => diff.ToLowerInvariant() switch
	{
		"tutorial" => JudgePreset.Tutorial,
		"casual" => JudgePreset.Casual,
		"normal" => JudgePreset.Normal,
		_ => JudgePreset.Hard, // Hard / Mega / Giga 共用 Hard 档
	};

	private static string GradeText(JudgeResult r) => GradeText(r.Grade, r.Timing);

	private static string GradeText(JudgeGrade grade, HitTiming timing) => grade switch
	{
		JudgeGrade.Prefect => "PREFECT",
		JudgeGrade.Great => $"GREAT {(timing == HitTiming.Early ? "E" : "L")}",
		JudgeGrade.Good => $"GOOD {(timing == HitTiming.Early ? "E" : "L")}",
		_ => "MISS",
	};
}

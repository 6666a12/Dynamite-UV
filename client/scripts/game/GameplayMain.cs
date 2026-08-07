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
/// 谱面/音频来自 GameSession 选中的谱面包；无选择时回退 testdata 演示谱。
/// 音符本体仍为占位几何图形（clean-room，无原版素材），皮肤素材管线后续替换。
/// </summary>
public partial class GameplayMain : Node2D
{
	private const string FallbackChartPath = "res://testdata/chart_tablear.json";
	private const string FallbackSongPath = "res://testdata/song_tablear.wav";

	// ---- 布局（2340x1080 横屏；几何经两段视频逐帧实测交叉验证，
	//      见 docs/gameplay-spec.md §8.6 与 docs/video-geometry-analysis.md §8）----
	// Center 判定线：隐形水平线 y=955（手机原生 2340×1080 截图实测 ≈88.5% 屏高；
	// 858 来自 1440×1080 iPad 谱面确认视频，不适用手机舞台）。
	// 粉色横条（y≈655, x 875..1465）是 Mixer UI，不是判定线。
	private const float CenterLineY = 955f;
	private const float MixerBarY = 655f;
	private const float MixerBarLeft = 875f;
	private const float MixerBarLen = 590f;
	private const float CenterTrackX = 1170f; // Center 轨横向中心（P=2.0 处）
	// Center x 映射（实机固定映射，无漂移）：x = CenterX0 + PosUnitPx * P
	// 2340 实机截图线性拟合 x≈619+276P（r²≈1）；
	// 1440 实机视频 x=312.5+204.9P（r²=0.9968）——按舞台分辨率用前者。
	private const float CenterX0 = 619f;
	private const float PosUnitPx = 276f;
	// 侧轨：竖条从屏幕中央附近生成、向左右边缘外移（实机验证的运动方式）。
	// 命中点 x：手机截图实测 421/1942（18.2%），iPad 视频换算 268/2072（11.5%）；
	// 用户拍板取偏边缘的 280/2060（≈12%）。
	private const float LeftLineX = 280f;
	private const float RightLineX = 2060f;
	// 侧轨行程（中央 1066/1274 → 侧线 280/2060，≈890px）
	// 与 Center 行程 ≈790px 之比作为侧轨距离换算
	private const float SideDistScale = 890f / 790f;
	// 侧轨 y 映射【占位，实机未复测，spec §8.6】：y = SideTrackY + (P-2)×SidePosUnitPx。
	// 205px/P 时 P=0 落在 y=50 贴顶溢出；压缩到 170px/P → P∈[0,4] 落 y∈[120,800]。
	private const float SideTrackY = 460f;
	private const float SidePosUnitPx = 170f;
	// 下落：匀速平面复现（实机为 3D 隧道透视加速、x 恒定，观感差异来自相机）。
	// 手机原生实测 Hold 从屏幕顶部 y≈160 生成，行程 ≈790px，生成→命中 ≈0.77s。
	private const float FallSpeedPx = 1026f;
	private const double LeadTimeSec = 0.77;
	private const float TravelPx = FallSpeedPx * (float)LeadTimeSec; // ≈790
	// 音符视觉宽 = Width × 1 Position 单位（1440 实测 204px/W vs 204.9px/P；
	// 2340 截图 ~270px/W vs 276px/P，两处都 ≈1:1）
	private const float NoteVisualScale = 1.0f;

	// 点击区域划分（MVP：按 X 粗分）
	private const float LeftRegionMaxX = 700f;
	private const float RightRegionMinX = 1640f;

	private static readonly Color ColBackground = new(0.08f, 0.08f, 0.12f);
	private static readonly Color ColJudgeLine = new(0.9f, 0.9f, 0.9f);
	private static readonly Color ColTap = new(0.95f, 0.95f, 0.95f);
	private static readonly Color ColBurst = new(1.0f, 0.6f, 0.2f);
	private static readonly Color ColChain = new(0.3f, 0.85f, 1.0f);
	private static readonly Color ColHold = new(0.4f, 1.0f, 0.5f);
	private static readonly Color ColMine = new(0.7f, 0.7f, 0.75f);
	private static readonly Color ColMixer = new(0.8f, 0.4f, 1.0f);
	private static readonly Color ColMixerBar = new(0.9f, 0.3f, 0.55f);
	private static readonly Color ColMixerBarFlash = new(1.0f, 0.75f, 0.95f);
	private static readonly Color ColHitFlash = new(1.0f, 1.0f, 0.4f);
	private static readonly Color ColMissFlash = new(0.5f, 0.5f, 0.5f);

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

	// 变速（§8.3）：DropSpeeds 阶跃倍率，下落位置 = 累计积分距离（px）。
	// 命中秒不变，只影响视觉速度；音符距判定线的像素距离 = D(命中) - D(现在)。
	private readonly List<(double Sec, double Mult)> _speedSegs = new();
	private readonly Dictionary<int, float> _dHitById = new();

	private double FallDist(double t)
	{
		double d = 0;
		for (var i = 0; i < _speedSegs.Count; i++)
		{
			var (s0, m) = _speedSegs[i];
			if (t <= s0) break;
			var s1 = i + 1 < _speedSegs.Count ? _speedSegs[i + 1].Sec : double.MaxValue;
			d += m * (Math.Min(t, s1) - s0);
		}
		return d * FallSpeedPx;
	}

	// 链/长条连接体（SubNoteId 链接的可视化）：梯形面板段。Hold/Chain 为刚性
	// 形状整体下落，过线部分被判定线裁剪（连续吞噬），见 UpdateViews/ClipToLine
	private sealed class NoteLink
	{
		public int FromId;
		public int ToId;
		public Track Track;
		public float Wa; // 起点半宽（Center=面板半宽，侧轨=缎带半厚）
		public float Wb; // 终点半宽
		public Polygon2D Poly = null!;
	}
	private readonly List<NoteLink> _links = new();
	private Dictionary<int, Note> _noteById = new();
	private readonly List<double> _mixerFlashTimes = new();
	private ColorRect _mixerBar = null!;
	private ColorRect _mixerHi = null!;

	private readonly bool[] _pressed = new bool[3];
	private readonly bool[] _holdActive = new bool[3];
	private bool _auto;
	private bool _finished;
	private bool _paused;
	private double _nextLogSec = 10.0;

	// ---- HUD / 菜单节点 ----
	private ColorRect _progressFill = null!;
	private Label _scoreLabel = null!;
	private Label _accLabel = null!;
	private RichTextLabel _countsLabel = null!;
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
	private Label _countsResult = null!;

	public override void _Ready()
	{
		GameSession.EnsureInit();

		// 谱面来源：GameSession 选中包；无选择（直接启动演示）时回退 testdata
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
		else
		{
			chartPath = FallbackChartPath;
			songPath = FallbackSongPath;
			_auto = true; // 演示默认 AUTO（F1 可切回手动）
		}

		BuildStage();

		_chart = DynamixChartLoader.Load(Godot.FileAccess.GetFileAsString(chartPath));
		_engine = new JudgeEngine(JudgePreset.Hard); // Hard 谱 -> Hard 档（§6.1）
		_plan = JudgePlan.Build(_chart, _engine.Settings);
		_notesByTime = _chart.AllNotes.OrderBy(n => n.Second).ToList();
		_noteById = _notesByTime.ToDictionary(n => n.Id);

		// 变速段（BarTime→秒，同秒后者覆盖）；无事件则全程 1.0
		var segs = new SortedDictionary<double, double>();
		foreach (var (bar, mult) in _chart.DropSpeeds)
		{
			var sec = _chart.Sections.Count > 0 ? _chart.BarTimeToSeconds(bar) : bar;
			segs[Math.Max(0.0, sec)] = mult;
		}
		_speedSegs.Add((0.0, 1.0));
		foreach (var (sec, mult) in segs)
		{
			if (_speedSegs[^1].Sec == sec) _speedSegs[^1] = (sec, mult);
			else _speedSegs.Add((sec, mult));
		}
		foreach (var n in _notesByTime)
			_dHitById[n.Id] = (float)FallDist(n.Second);
		GD.Print($"chart '{_chart.Title}' notes={_notesByTime.Count} " +
				 $"units={_plan.Units.Count} theoreticalMax={_plan.TheoreticalMax}");

		_player = new AudioStreamPlayer
		{
			Stream = Res.LoadAudio(songPath),
			Bus = "Master",
		};
		AddChild(_player);
		_clock = new SongClock();
		_clock.Attach(_player);
		AddChild(_clock);
		_clock.Play(0.0);

		RefreshTitleTag();
	}

	public override void _Process(double delta)
	{
		if (_finished || _paused)
			return;
		var t = _clock.GetSongTime();

		SpawnNotes(t);
		UpdateViews(t, delta);
		SweepJudges(t);
		UpdateHud(t);

		// Mixer 事件（Type 8/9）：高亮块提前 1s 从粉条左端滑向中央光标，
		// 到点停在光标处并与粉条一起闪亮 0.3s（原版为粉条上的移动高亮段）
		var ev = double.NaN;
		foreach (var s in _mixerFlashTimes)
			if (t >= s - 1.0 && t < s + 0.3) { ev = s; break; }
		if (!double.IsNaN(ev))
		{
			_mixerHi.Visible = true;
			if (t < ev)
			{
				var k = (float)(t - (ev - 1.0)); // 0→1 滑向光标
				_mixerHi.Position = new Vector2(
					Mathf.Lerp(MixerBarLeft, CenterTrackX - 28, k), MixerBarY - 12);
				_mixerHi.Color = new Color(0.3f, 0.9f, 1.0f);
				_mixerBar.Color = ColMixerBar;
			}
			else
			{
				_mixerHi.Position = new Vector2(CenterTrackX - 28, MixerBarY - 12);
				_mixerHi.Color = ColMixerBarFlash;
				_mixerBar.Color = ColMixerBarFlash;
			}
		}
		else
		{
			_mixerHi.Visible = false;
			_mixerBar.Color = ColMixerBar;
		}

		if (t >= _nextLogSec) // 周期性日志，便于 headless 验证判定在跑
		{
			_nextLogSec = Math.Floor(t / 10) * 10 + 10;
			GD.Print($"t={t:F1}s score={_engine.Score} combo={_engine.Combo} " +
					 $"hp={_engine.Health} activeViews={_active.Count}");
		}

		// 结束检测：时间超过谱面末尾 +2s，或音频流自然播完（播放器停止后
		// GetPlaybackPosition 冻结，单靠时间判断会永远进不了结算）
		var audioEnded = !_clock.ManualFallback && !_player.Playing && t > 1.0;
		if (t > _plan.EndTime + 2.0 || audioEnded)
			ShowResults();
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

		Vector2 pos;
		bool pressed;
		switch (e)
		{
			case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
				pos = mb.Position;
				pressed = mb.Pressed;
				break;
			case InputEventScreenTouch touch:
				pos = touch.Position;
				pressed = touch.Pressed;
				break;
			default:
				return;
		}

		var track = RegionOf(pos);
		var t = _clock.GetSongTime();
		if (pressed)
			OnPress(track, t);
		else
			OnRelease(track, t);
	}

	// ---- 输入 ----

	private static Track RegionOf(Vector2 pos) =>
		pos.X < LeftRegionMaxX ? Track.Left :
		pos.X > RightRegionMinX ? Track.Right :
		Track.Center;

	private void OnPress(Track track, double t)
	{
		_pressed[(int)track] = true;

		// 该区域内最近未判定的可输入单元（含 Mine——碰到即误触）
		JudgeUnit? best = null;
		var bestAbs = double.MaxValue;
		foreach (var u in _plan.Units)
		{
			if (u.Judged || u.Track != track) continue;
			if (u.Kind is not (UnitKind.Input or UnitKind.Mine)) continue;
			if (!_engine.InWindow(u.Time, t)) continue;
			var abs = Math.Abs(u.Time - t);
			if (abs < bestAbs) { bestAbs = abs; best = u; }
		}
		if (best == null) return;

		if (best.Kind == UnitKind.Mine)
		{
			_engine.ApplyMine(touched: true); // TODO(§9#7 推测)：误触 = Miss
			best.Judged = true;
			FlashView(best, ColMissFlash);
			FlashJudge("MINE!");
			return;
		}

		var r = _engine.Judge(best.Time, t);
		_engine.Apply(best.Category, r.Grade);
		best.Judged = true;
		if (r.Grade != JudgeGrade.Miss &&
			best.Category is ScoreCategory.HoldStart or ScoreCategory.MixerStart)
			_holdActive[(int)track] = true;
		if (best.Category is ScoreCategory.HoldEnd or ScoreCategory.MixerEnd)
			_holdActive[(int)track] = false;
		FlashView(best, r.Grade == JudgeGrade.Miss ? ColMissFlash : ColHitFlash);
		FlashJudge(GradeText(r));
	}

	private void OnRelease(Track track, double t)
	{
		_pressed[(int)track] = false;
		if (!_holdActive[(int)track]) return;
		_holdActive[(int)track] = false;

		// 提前松手：该轨最近的未判定 HoldEnd/MixerEnd 立即按松手时刻判定
		foreach (var u in _plan.Units)
		{
			if (u.Judged || u.Track != track || u.Kind != UnitKind.Input) continue;
			if (u.Category is not (ScoreCategory.HoldEnd or ScoreCategory.MixerEnd)) continue;
			if (t > u.Time + _engine.Settings.MissSec) continue; // 交给 Miss 扫尾
			var r = _engine.Judge(u.Time, t); // 过早松手超出窗口即 Miss
			_engine.Apply(u.Category, r.Grade);
			u.Judged = true;
			FlashView(u, r.Grade == JudgeGrade.Miss ? ColMissFlash : ColHitFlash);
			FlashJudge(GradeText(r));
			break;
		}
	}

	// ---- 判定扫尾 ----

	private void SweepJudges(double t)
	{
		// 单元按时间排序；每帧全量扫描（数千单元，开销可忽略）。
		foreach (var u in _plan.Units)
		{
			if (u.Judged) continue;
			if (u.Time - t > _engine.Settings.MissSec)
				break; // 之后的单元都还远在窗口外
			JudgeOne(u, t);
		}
	}

	private void JudgeOne(JudgeUnit u, double t)
	{
		var miss = _engine.Settings.MissSec;
		switch (u.Kind)
		{
			case UnitKind.Auto: // 链体节点自动命中（§4 推测）
				if (t >= u.Time)
				{
					_engine.Apply(u.Category, JudgeGrade.Prefect);
					u.Judged = true;
					FlashView(u, ColHitFlash);
				}
				break;

			case UnitKind.HoldPoint:
				if (t >= u.Time)
				{
					// 按住（且 Hold 有效）= Prefect，否则 Miss
					var ok = _auto || (_pressed[(int)u.Track] && _holdActive[(int)u.Track]);
					_engine.Apply(u.Category, ok ? JudgeGrade.Prefect : JudgeGrade.Miss);
					u.Judged = true;
				}
				break;

			case UnitKind.Mine:
				// TODO(§9#7 推测)：不触碰 = Prefect 给分；误触在 OnPress 处理
				if (t >= u.Time)
				{
					_engine.ApplyMine(touched: false);
					u.Judged = true;
					FlashView(u, ColMine);
				}
				break;

			case UnitKind.Input:
				if (_auto && t >= u.Time)
				{
					var r = _engine.Judge(u.Time, u.Time); // Auto：精确时刻判定
					_engine.Apply(u.Category, r.Grade);
					u.Judged = true;
					FlashView(u, ColHitFlash);
				}
				else if (u.Category is ScoreCategory.HoldEnd or ScoreCategory.MixerEnd &&
						 _pressed[(int)u.Track] && _holdActive[(int)u.Track] && t >= u.Time)
				{
					// TODO(MVP 简化)：按住到尾即按精确时刻判定 HoldEnd
					var r = _engine.Judge(u.Time, u.Time);
					_engine.Apply(u.Category, r.Grade);
					u.Judged = true;
					_holdActive[(int)u.Track] = false;
					FlashView(u, ColHitFlash);
					FlashJudge(GradeText(r));
				}
				else if (t > u.Time + miss)
				{
					_engine.Apply(u.Category, JudgeGrade.Miss); // 超窗未击中
					u.Judged = true;
					if (u.Category is ScoreCategory.HoldEnd or ScoreCategory.MixerEnd)
						_holdActive[(int)u.Track] = false;
					FlashView(u, ColMissFlash);
					FlashJudge("MISS");
				}
				break;
		}
	}

	// ---- 音符生成与移动 ----

	private void SpawnNotes(double t)
	{
		var d = (float)FallDist(t);
		while (_nextSpawn < _notesByTime.Count &&
			   _dHitById[_notesByTime[_nextSpawn].Id] - d <= TravelPx)
		{
			var n = _notesByTime[_nextSpawn++];
			// Type 8/9（Mixer）：不是下落音符——原版为 Mixer 粉条上的高亮段
			// （video-geometry-analysis §5），MVP 不生成视图，仅到点闪一下粉条
			if (n.Type is NoteType.MixerStart or NoteType.MixerNode)
			{
				_mixerFlashTimes.Add(n.Second);
				continue;
			}
			var view = NoteView.Create(n, SizeFor(n), ColorFor(n));
			view.Position = PositionFor(n, t);
			AddChild(view);
			_viewByNoteId[n.Id] = view;
			_active.Add(view);
			if (n.SubNoteId != -1 && _noteById.TryGetValue(n.SubNoteId, out var toNote))
			{
				var poly = new Polygon2D
				{
					Color = LinkColorFor(n),
					Visible = false, // ZIndex 0：判定线之上、音符之下
				};
				AddChild(poly);
				_links.Add(new NoteLink
				{
					FromId = n.Id,
					ToId = n.SubNoteId,
					Track = n.Track,
					Wa = LinkHalfFor(n),
					Wb = LinkHalfFor(toNote),
					Poly = poly,
				});
			}
		}
	}

	private void UpdateViews(double t, double delta)
	{
		for (var i = _active.Count - 1; i >= 0; i--)
		{
			var v = _active[i];
			if (v.TickTtl(delta) || t - v.Model.Second > LeadTimeSec)
			{
				_viewByNoteId.Remove(v.Model.Id);
				v.QueueFree();
				_active.RemoveAt(i);
				continue;
			}
			if (t - v.Model.Second > 0.4)
				v.MarkExpired(); // 过线未回收的（如长条中间节点）淡出
			v.Position = PositionFor(v.Model, t);
		}

		// 更新链/长条连接体：Hold/Chain 是**刚性形状**整体对着判定线下落
		// （节点只是形状顶点，端点位置不钳制）；过线部分被判定线裁剪，
		// 裁剪点沿线连续滑动＝连续吞噬；整段过线后销毁。全程可见，
		// 不要求节点视图在场。
		for (var i = _links.Count - 1; i >= 0; i--)
		{
			var l = _links[i];
			var a = PositionForRaw(_noteById[l.FromId], t);
			var b = PositionForRaw(_noteById[l.ToId], t);
			var wa = l.Wa;
			var wb = l.Wb;
			if (!ClipToLine(l.Track, ref a, ref b, ref wa, ref wb))
			{
				l.Poly.QueueFree();
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
			l.Poly.Visible = true;
		}
	}

	// 将段 a→b（中心线端点，半宽 wa/wb）按判定线裁剪：a 端已过线时用
	// 中心线与判定线的交点取而代之（面板边缘沿线滑动）；整段过线返回 false。
	// 链按时间有序，from 端恒领先于 b 端，故 b 不会单独过线。
	private static bool ClipToLine(Track track, ref Vector2 a, ref Vector2 b, ref float wa, ref float wb)
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

	private Vector2 PositionFor(Note n, double t)
	{
		// 距判定线的像素距离（变速积分，钳 0——原版音符到线即爆，不下沉）
		var rem = Mathf.Max(0f, _dHitById[n.Id] - (float)FallDist(t));
		return PositionAt(n, rem);
	}

	// 连接体端点：不钳制——形状刚性下落，过线部分由 ClipToLine 裁剪（连续吞噬）
	private Vector2 PositionForRaw(Note n, double t)
	{
		var rem = _dHitById[n.Id] - (float)FallDist(t);
		return PositionAt(n, rem);
	}

	private static Vector2 PositionAt(Note n, float rem) => n.Track switch
	{
		// Center：匀速落向底部隐形判定线（y=858），x = 619 + 276P 全程恒定
		Track.Center => new Vector2(
			CenterX0 + (float)n.Position * PosUnitPx,
			CenterLineY - rem),
		// Left：竖条从中央（x≈1066）向左缘判定线（x=268）外移
		Track.Left => new Vector2(
			LeftLineX + rem * SideDistScale,
			SideTrackY + ((float)n.Position - 2.0f) * SidePosUnitPx),
		// Right：竖条从中央（x≈1274）向右缘判定线（x=2072）外移
		_ => new Vector2(
			RightLineX - rem * SideDistScale,
			SideTrackY + ((float)n.Position - 2.0f) * SidePosUnitPx),
	};

	private static Vector2 SizeFor(Note n)
	{
		// 长轴 = Width × 1 Position 单位（NoteVisualScale≈1，见顶部注释）；
		// Center 轨为横条，侧轨为竖条（长轴换到 y）。
		var axis = Mathf.Max(12f, (float)n.Width * PosUnitPx * NoteVisualScale);
		if (n.Track != Track.Center)
		{
			// 侧轨竖条：W=2.0 实测长 ≈204px@1440 宽（≈331px@2340），
			// 换算约 165px/W（与 Center 的 276px/W 不同，单侧样本，spec §8.6）
			var len = Mathf.Max(12f, (float)n.Width * 165f);
			return new Vector2(20f, len); // 厚 17–24px 实测
		}
		return n.Type switch
		{
			NoteType.HoldHead or NoteType.HoldNode => new Vector2(axis, 26f),
			NoteType.MixerStart or NoteType.MixerNode => new Vector2(axis, 40f),
			NoteType.ChainNode => new Vector2(Mathf.Min(axis, 40f), 20f),
			_ => new Vector2(axis, 24f), // Tap：实测高 22–24px
		};
	}

	private static Color ColorFor(Note n) => n.Type switch
	{
		NoteType.Tap => ColTap,
		NoteType.Burst => ColBurst,
		NoteType.ChainHead or NoteType.ChainNode => ColChain,
		NoteType.Mine => ColMine, // Type 5：视频实锤以普通下落横条呈现（§9#1），仅稍暗以示待定
		NoteType.HoldHead or NoteType.HoldNode => ColHold,
		_ => ColMixer, // Mixer 8/9（TODO：MVP 按 Hold 处理）
	};

	private void FlashView(JudgeUnit u, Color c)
	{
		if (!_viewByNoteId.TryGetValue(u.NoteId, out var v)) return;
		// 节点到线即爆（节点只是 Hold/Chain 形状的顶点）；长条体的保留
		// 由连接体裁剪负责，不需要锚定视图
		v.MarkJudged(c);
	}

	// 连接体半宽：Center 长条面板为音符条的 0.8 倍宽，侧轨滑链缎带半厚 7px
	private static float LinkHalfFor(Note n) =>
		n.Track == Track.Center ? Mathf.Max(4f, (float)n.Width * PosUnitPx * 0.4f) : 7f;

	private static Color LinkColorFor(Note n) => n.Type switch
	{
		NoteType.HoldHead or NoteType.HoldNode => new Color(ColHold, 0.35f),
		_ => new Color(ColChain, 0.35f),
	};

	// ---- UI ----

	private void BuildStage()
	{
		var bg = new ColorRect
		{
			Color = ColBackground,
			Size = new Vector2(2340, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = -2, // 压在最底（连接体 0、音符 1）
		};
		AddChild(bg);

		// 判定线（实机为隐形线，此处用暗色细线示意）：三线构成 ∪ 形——
		// 底部横线贯通左右，与两侧竖线在两端相接
		AddLine(new Vector2(LeftLineX, CenterLineY - 2), new Vector2(RightLineX - LeftLineX, 4),
			new Color(0.35f, 0.35f, 0.4f));
		AddLine(new Vector2(LeftLineX - 2, 60), new Vector2(4, CenterLineY - 60), new Color(0.35f, 0.35f, 0.4f));
		AddLine(new Vector2(RightLineX - 2, 60), new Vector2(4, CenterLineY - 60), new Color(0.35f, 0.35f, 0.4f));
		// Mixer 滑条 UI：粉色固定横条（y≈634, x 875..1465，实测）+ 中央光标
		_mixerBar = new ColorRect
		{
			Color = ColMixerBar,
			Position = new Vector2(MixerBarLeft, MixerBarY - 12),
			Size = new Vector2(MixerBarLen, 24),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(_mixerBar);
		_mixerHi = new ColorRect // Mixer 接近段高亮块（滑向中央光标）
		{
			Color = new Color(0.3f, 0.9f, 1.0f),
			Position = new Vector2(MixerBarLeft, MixerBarY - 12),
			Size = new Vector2(56, 24),
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(_mixerHi);
		AddLine(new Vector2(CenterTrackX - 4, MixerBarY - 14), new Vector2(8, 28),
			new Color(0.5f, 0.5f, 1.0f));

		BuildHud();
		BuildPauseMenu();
		BuildResultLayer();
	}

	private void BuildHud()
	{
		// 顶部进度条
		AddChild(new ColorRect
		{
			Color = new Color(1, 1, 1, 0.07f),
			Size = new Vector2(2340, 5),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		});
		_progressFill = new ColorRect
		{
			Color = UiFonts.Cyan,
			Size = new Vector2(0, 5),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		AddChild(_progressFill);

		// 左上：实时判定统计
		_countsLabel = new RichTextLabel
		{
			Position = new Vector2(60, 30),
			Size = new Vector2(1000, 44),
			BbcodeEnabled = true,
			ScrollActive = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		_countsLabel.AddThemeFontOverride("normal_font", UiFonts.Tech);
		_countsLabel.AddThemeFontSizeOverride("normal_font_size", 24);
		AddChild(_countsLabel);

		// 右上：分数 + ACC
		_scoreLabel = new Label
		{
			Position = new Vector2(1740, 24),
			Size = new Vector2(540, 62),
			HorizontalAlignment = HorizontalAlignment.Right,
			Text = "0",
			ZIndex = 2,
		};
		_scoreLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_scoreLabel.AddThemeFontSizeOverride("font_size", 50);
		_scoreLabel.AddThemeColorOverride("font_color", UiFonts.Text);
		AddChild(_scoreLabel);
		_accLabel = new Label
		{
			Position = new Vector2(1740, 92),
			Size = new Vector2(540, 30),
			HorizontalAlignment = HorizontalAlignment.Right,
			ZIndex = 2,
		};
		_accLabel.AddThemeFontOverride("font", UiFonts.Tech);
		_accLabel.AddThemeFontSizeOverride("font_size", 20);
		_accLabel.AddThemeColorOverride("font_color", UiFonts.Dim);
		AddChild(_accLabel);

		// 顶部中央：暂停按钮（样式稿：下移+放大）
		_pauseBtn = new CutButton
		{
			Position = new Vector2(1121, 72),
			Size = new Vector2(98, 66),
			Text = "II",
			FontSize = 26,
			TechFont = true,
			ZIndex = 2,
		};
		_pauseBtn.Pressed += TogglePause;
		AddChild(_pauseBtn);

		// 中央：COMBO（样式稿：几何中心，稳视觉重心）
		_comboLabel = new Label
		{
			Position = new Vector2(970, 380),
			Size = new Vector2(400, 90),
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = "",
			ZIndex = 2,
		};
		_comboLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_comboLabel.AddThemeFontSizeOverride("font_size", 68);
		_comboLabel.AddThemeColorOverride("font_color", UiFonts.Text);
		AddChild(_comboLabel);
		_comboSub = new Label
		{
			Position = new Vector2(1070, 478),
			Size = new Vector2(200, 28),
			HorizontalAlignment = HorizontalAlignment.Center,
			Text = "",
			ZIndex = 2,
		};
		_comboSub.AddThemeFontOverride("font", UiFonts.Tech);
		_comboSub.AddThemeFontSizeOverride("font_size", 18);
		_comboSub.AddThemeColorOverride("font_color", UiFonts.Dim);
		AddChild(_comboSub);

		// 底线上方：判定反馈字
		_judgeFlash = new Label
		{
			Position = new Vector2(CenterTrackX - 150, 768),
			Size = new Vector2(300, 44),
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1, 1, 1, 0),
			ZIndex = 2,
		};
		_judgeFlash.AddThemeFontOverride("font", UiFonts.TechBold);
		_judgeFlash.AddThemeFontSizeOverride("font_size", 34);
		_judgeFlash.AddThemeColorOverride("font_color", UiFonts.Cyan);
		AddChild(_judgeFlash);

		// 左下：曲名 + 难度（样式稿：填底线以下空间）
		_titleTag = new RichTextLabel
		{
			Position = new Vector2(60, 1006),
			Size = new Vector2(1400, 48),
			BbcodeEnabled = true,
			ScrollActive = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 2,
		};
		_titleTag.AddThemeFontOverride("normal_font", UiFonts.Cjk);
		_titleTag.AddThemeFontSizeOverride("normal_font_size", 28);
		AddChild(_titleTag);
	}

	private RichTextLabel _titleTag = null!;

	private void RefreshTitleTag()
	{
		var hex = UiFonts.DiffColor(_diffKey).ToHtml(false);
		var auto = _auto ? "   [color=#7c88b0]AUTO (F1)[/color]" : "";
		_titleTag.Text =
			$"{_songTitle}   [color=#{hex}]{UiFonts.DiffName(_diffKey)} · Lv {_diffLevel}[/color]{auto}";
	}

	private void BuildPauseMenu()
	{
		_pauseLayer = new Control { Visible = false, ZIndex = 60 };
		AddChild(_pauseLayer);

		var dim = new ColorRect
		{
			Color = new Color(0.02f, 0.03f, 0.07f, 0.72f),
			Size = new Vector2(2340, 1080),
		};
		_pauseLayer.AddChild(dim);

		_pauseLayer.AddChild(new CutPanel
		{
			Position = new Vector2(820, 300),
			Size = new Vector2(700, 480),
		});

		var title = new Label
		{
			Position = new Vector2(820, 340),
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
			Position = new Vector2(980, 450),
			Size = new Vector2(380, 76),
			Text = "继续",
			StyleKind = CutButton.ButtonStyle.Solid,
			FontSize = 28,
		};
		resume.Pressed += TogglePause;
		_pauseLayer.AddChild(resume);

		var retry = new CutButton
		{
			Position = new Vector2(980, 546),
			Size = new Vector2(380, 76),
			Text = "重开",
			FontSize = 28,
		};
		retry.Pressed += Restart;
		_pauseLayer.AddChild(retry);

		var quit = new CutButton
		{
			Position = new Vector2(980, 642),
			Size = new Vector2(380, 76),
			Text = "返回选曲",
			FontSize = 28,
		};
		quit.Pressed += ExitToSelect;
		_pauseLayer.AddChild(quit);
	}

	private void BuildResultLayer()
	{
		_resultLayer = new Control { Visible = false, ZIndex = 50 };
		AddChild(_resultLayer);

		// 背景：封面拉伸铺满 + 压暗（样式稿为模糊，Godot 侧先用压暗近似，模糊后续上 shader）
		_resultCover = new TextureRect
		{
			Position = Vector2.Zero,
			Size = new Vector2(2340, 1080),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			Modulate = new Color(0.5f, 0.5f, 0.55f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_resultLayer.AddChild(_resultCover);
		_resultLayer.AddChild(new ColorRect
		{
			Color = new Color(0.04f, 0.05f, 0.10f, 0.45f),
			Size = new Vector2(2340, 1080),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		_resultLayer.AddChild(new CutPanel
		{
			Position = new Vector2(590, 240),
			Size = new Vector2(1160, 600),
			Fill = new Color(0.078f, 0.106f, 0.20f, 0.78f),
		});

		_songLine = new Label { Position = new Vector2(660, 276), Size = new Vector2(1020, 34) };
		_songLine.AddThemeFontSizeOverride("font_size", 24);
		_songLine.AddThemeColorOverride("font_color", UiFonts.Dim);
		_resultLayer.AddChild(_songLine);

		_gradeLabel = new Label { Position = new Vector2(690, 330), Size = new Vector2(220, 180) };
		_gradeLabel.AddThemeFontOverride("font", UiFonts.TechBold);
		_gradeLabel.AddThemeFontSizeOverride("font_size", 140);
		_gradeLabel.AddThemeColorOverride("font_color", UiFonts.Cyan);
		_resultLayer.AddChild(_gradeLabel);

		_newRecChip = new Control { Position = new Vector2(700, 530), Size = new Vector2(200, 40) };
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

		_resultScore = new Label { Position = new Vector2(960, 320), Size = new Vector2(720, 90) };
		_resultScore.AddThemeFontOverride("font", UiFonts.TechBold);
		_resultScore.AddThemeFontSizeOverride("font_size", 76);
		_resultScore.AddThemeColorOverride("font_color", UiFonts.Text);
		_resultLayer.AddChild(_resultScore);

		_resultAcc = new Label { Position = new Vector2(960, 424), Size = new Vector2(720, 36) };
		_resultAcc.AddThemeFontOverride("font", UiFonts.Tech);
		_resultAcc.AddThemeFontSizeOverride("font_size", 26);
		_resultAcc.AddThemeColorOverride("font_color", UiFonts.Dim);
		_resultLayer.AddChild(_resultAcc);

		// 判定分布条（4 段彩色，宽度按占比）
		const float barX = 960, barY = 486, barH = 14;
		_distP = DistBar(barX, barY, UiFonts.Cyan);
		_distGr = DistBar(barX, barY, UiFonts.Hard);
		_distGo = DistBar(barX, barY, UiFonts.Casual);
		_distM = DistBar(barX, barY, new Color(0.33f, 0.33f, 0.33f));

		_countsResult = new Label { Position = new Vector2(960, 516), Size = new Vector2(720, 36) };
		_countsResult.AddThemeFontOverride("font", UiFonts.Tech);
		_countsResult.AddThemeFontSizeOverride("font_size", 24);
		_countsResult.AddThemeColorOverride("font_color", UiFonts.Text);
		_resultLayer.AddChild(_countsResult);

		var back = new CutButton
		{
			Position = new Vector2(660, 720),
			Size = new Vector2(280, 72),
			Text = "返回选曲",
			FontSize = 26,
		};
		back.Pressed += ExitToSelect;
		_resultLayer.AddChild(back);

		var next = new CutButton
		{
			Position = new Vector2(960, 720),
			Size = new Vector2(280, 72),
			Text = "下一首",
			FontSize = 26,
		};
		next.Pressed += NextSong;
		_resultLayer.AddChild(next);

		var retry = new CutButton
		{
			Position = new Vector2(1260, 720),
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

	private Label _songLine = null!;

	private void TogglePause()
	{
		if (_finished) return;
		_paused = !_paused;
		if (_paused)
		{
			_clock.Pause();
			_pauseLayer.Visible = true;
		}
		else
		{
			_pauseLayer.Visible = false;
			_clock.Resume();
		}
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

	private void FlashJudge(string text)
	{
		_judgeFlash.Text = text;
		_judgeFlash.Modulate = Colors.White;
		_flashTimer = 0.4;
	}

	private void UpdateHud(double t)
	{
		if (_flashTimer > 0)
		{
			_flashTimer -= GetProcessDeltaTime();
			if (_flashTimer <= 0)
				_judgeFlash.Modulate = new Color(1, 1, 1, 0);
		}

		_scoreLabel.Text = $"{_engine.Score:N0}";
		// 实时 ACC = 当前得分 / 已判定单元的理论满分（未判定不计入）
		var maxSoFar = 0;
		foreach (var u in _plan.Units)
			if (u.Judged)
				maxSoFar += JudgeEngine.ScoreDelta(u.Category, JudgeGrade.Prefect);
		_accLabel.Text = maxSoFar > 0 ? $"ACC {100.0 * _engine.Score / maxSoFar:F2}%" : "ACC --";
		_countsLabel.Text =
			$"[color=#35e0ff]PERFECT {_engine.CountPrefect}[/color]   " +
			$"[color=#fbbf24]GREAT {_engine.CountGreat}[/color]   " +
			$"[color=#4ade80]GOOD {_engine.CountGood}[/color]   " +
			$"[color=#888888]MISS {_engine.CountMiss}[/color]";
		_comboLabel.Text = _engine.Combo > 1 ? $"{_engine.Combo}" : "";
		_comboSub.Text = _engine.Combo > 1 ? "COMBO" : "";
		_progressFill.Size = new Vector2(
			2340f * Mathf.Clamp((float)(t / Math.Max(0.01, _plan.EndTime)), 0f, 1f), 5);
	}

	private void ShowResults()
	{
		_finished = true;
		_clock.Pause();
		var pct = _engine.Percent(_plan.TheoreticalMax);

		// 成绩写回（AUTO 演示不计入成绩库）
		var isNewRecord = false;
		var grade = ScoreStore.GradeOf(pct);
		if (!_auto)
		{
			isNewRecord = GameSession.Scores.TryUpdate(_packId, _diffKey, new ScoreRecord
			{
				Score = _engine.Score,
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
			_resultCover.Texture = tex;
		_songLine.Text = $"RESULT · {_songTitle} · {UiFonts.DiffName(_diffKey)} {_diffLevel}" +
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
		_resultScore.Text = $"{_engine.Score:N0}";
		_resultAcc.Text = $"ACC {pct:F2}% · MAX COMBO {_engine.MaxCombo:N0} / {_plan.Units.Count:N0}";

		// 判定分布条
		var total = Math.Max(1, _engine.CountPrefect + _engine.CountGreat +
			_engine.CountGood + _engine.CountMiss);
		const float barX = 960, barW = 720;
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

		_countsResult.Text =
			$"P {_engine.CountPrefect}   GR {_engine.CountGreat}   " +
			$"GD {_engine.CountGood}   M {_engine.CountMiss}";

		_resultLayer.Visible = true;

		GD.Print($"=== RESULT === {_songTitle} [{_diffKey} Lv{_diffLevel}] " +
				 $"score={_engine.Score} acc={pct:F2}% grade={grade} " +
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
		AddChild(line);
	}

	private static string GradeText(JudgeResult r) => r.Grade switch
	{
		JudgeGrade.Prefect => r.Timing == HitTiming.Exact ? "PREFECT" :
			$"PREFECT {(r.Timing == HitTiming.Early ? "E" : "L")}",
		JudgeGrade.Great => $"GREAT {(r.Timing == HitTiming.Early ? "E" : "L")}",
		JudgeGrade.Good => $"GOOD {(r.Timing == HitTiming.Early ? "E" : "L")}",
		_ => "MISS",
	};
}

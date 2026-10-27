using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class InputJudgementTests
{

    // 4. 偏移输入（窗口数值按规格书 §6.3：Hard = ±62.5/±112.5/±162.5/±250ms，
    //    Normal = ±62.5/±150/±200/±250ms）。
    //    注意：任务书给的 ±120ms→Great、±170ms→Good 与 Hard 档窗口不符
    //    （Hard 下分别为 Good/Miss），该组数值只在 Normal 档窗口下成立——
    //    这里两档都断言，Hard 档严格按规格书数值。
    internal static void TestWindowOffsets()
    {
        Console.WriteLine("[4] Window offsets");
        var hard = new JudgeEngine(JudgePreset.Hard);
        var normal = new JudgeEngine(JudgePreset.Normal);
        const double t = 10.0;

        // ±50ms -> Perfect（Hard/Normal 均为 ±62.5ms 内）
        foreach (var offset in new[] { -0.050, 0.050 })
        {
            Check(hard.Judge(t, t + offset).Grade == JudgeGrade.Perfect, $"Hard ±50ms -> Perfect ({offset:+0.000;-0.000})");
            Check(normal.Judge(t, t + offset).Grade == JudgeGrade.Perfect, $"Normal ±50ms -> Perfect ({offset:+0.000;-0.000})");
        }

        // ±120ms：Normal 档（±150ms 内）-> Great；Hard 档（超 ±112.5ms）-> Good
        foreach (var offset in new[] { -0.120, 0.120 })
        {
            Check(normal.Judge(t, t + offset).Grade == JudgeGrade.Great, $"Normal ±120ms -> Great ({offset:+0.000;-0.000})");
            Check(hard.Judge(t, t + offset).Grade == JudgeGrade.Good, $"Hard ±120ms -> Good（规格书 Hard Great 窗仅 ±112.5ms）({offset:+0.000;-0.000})");
        }

        // ±170ms：Normal 档（±200ms 内）-> Good；Hard 档（超 ±162.5ms）-> Miss
        foreach (var offset in new[] { -0.170, 0.170 })
        {
            Check(normal.Judge(t, t + offset).Grade == JudgeGrade.Good, $"Normal ±170ms -> Good ({offset:+0.000;-0.000})");
            Check(hard.Judge(t, t + offset).Grade == JudgeGrade.Miss, $"Hard ±170ms -> Miss（规格书 Hard Good 窗仅 ±162.5ms）({offset:+0.000;-0.000})");
        }

        // ±260ms -> Miss（两档 Miss 窗均 ±250ms）
        foreach (var offset in new[] { -0.260, 0.260 })
        {
            Check(hard.Judge(t, t + offset).Grade == JudgeGrade.Miss, $"Hard ±260ms -> Miss ({offset:+0.000;-0.000})");
            Check(normal.Judge(t, t + offset).Grade == JudgeGrade.Miss, $"Normal ±260ms -> Miss ({offset:+0.000;-0.000})");
        }

        Check(hard.Judge(t, t - 0.050).Timing == HitTiming.Early, "-50ms = Early");
        Check(hard.Judge(t, t + 0.050).Timing == HitTiming.Late, "+50ms = Late");
        Check(hard.Judge(t, t).Timing == HitTiming.Exact, "0ms = Exact");
        Check(hard.InWindow(t, t + 0.250), "250ms 在 Miss 窗口内");
        Check(!hard.InWindow(t, t + 0.251), "251ms 超出 Miss 窗口");

        // EX-Tap 与原版一致：窗口倍率 1.0（与 Tap 同窗），级差由二值判定覆盖（见 [7b]）
        const double ex = JudgePlan.ExTapWindowScale;
        Check(Math.Abs(ex - 1.0) < 1e-12, "EX-Tap 窗口倍率 = 1.0（与 Tap 同窗）");
        Check(hard.Judge(t, t + 0.150, ex).Grade == JudgeGrade.Good,
            "EX-Tap ±150ms -> Good（同 Tap Good 窗）");
        Check(hard.Judge(t, t + 0.200, ex).Grade == JudgeGrade.Miss,
            "EX-Tap ±200ms -> Miss（超 Good 窗）");
        Check(hard.InWindow(t, t + 0.250, ex), "EX-Tap 250ms 仍在 Miss 窗内");
        Check(!hard.InWindow(t, t + 0.251, ex), "EX-Tap 251ms 超出 Miss 窗");

        // 窗口换算值本身（bar * 240/StandardBPM=150，规格书 §6.3）
        var s = hard.Settings;
        Check(Math.Abs(s.PerfectSec - 0.0625) < 1e-12, "Hard Perfect 窗 = ±62.5ms");
        Check(Math.Abs(s.GreatSec - 0.1125) < 1e-12, "Hard Great 窗 = ±112.5ms");
        Check(Math.Abs(s.GoodSec - 0.1625) < 1e-12, "Hard Good 窗 = ±162.5ms");
        Check(Math.Abs(s.MissSec - 0.250) < 1e-12, "Hard Miss 窗 = ±250ms");
		Check(Math.Abs(s.HoldContactGraceSec - 0.200) < 1e-12,
			"StandardBPM 下 Holding 间隔 = 200ms");
		Check(Math.Abs(s.HoldContactGraceSeconds(90) - 0.250) < 1e-12,
			"Holding BPM 90 钳到 120 -> 250ms");
		Check(Math.Abs(s.HoldContactGraceSeconds(240) - 0.150) < 1e-12,
			"Holding BPM 240 钳到 200 -> 150ms");
        var sn = normal.Settings;
        Check(Math.Abs(sn.GreatSec - 0.150) < 1e-12, "Normal Great 窗 = ±150ms");
        Check(Math.Abs(sn.GoodSec - 0.200) < 1e-12, "Normal Good 窗 = ±200ms");

        // 原版 4 个 JudgeSettings 实例的精确换算（barTime × 240 / StandardBPM 150 = ×1.6）
        var casual = JudgeSettings.ForPreset(JudgePreset.Casual);
        var tutorial = JudgeSettings.ForPreset(JudgePreset.Tutorial);
        Check(Math.Abs(casual.PerfectSec - 0.100) < 1e-12, "Casual Perfect 窗 = ±100ms");
        Check(Math.Abs(casual.GreatSec - 0.150) < 1e-12, "Casual Great 窗 = ±150ms");
        Check(Math.Abs(casual.GoodSec - 0.200) < 1e-12, "Casual Good 窗 = ±200ms");
        Check(Math.Abs(casual.MissSec - 0.250) < 1e-12, "Casual Miss 窗 = ±250ms");
        Check(Math.Abs(tutorial.PerfectSec - casual.PerfectSec) < 1e-12 &&
              Math.Abs(tutorial.GreatSec - casual.GreatSec) < 1e-12 &&
              Math.Abs(tutorial.GoodSec - casual.GoodSec) < 1e-12 &&
              Math.Abs(tutorial.MissSec - casual.MissSec) < 1e-12,
            "Tutorial 实例 = Casual 数值");

        // 难度键 -> 原版实例（hard 及以上含 mega/giga/tech/自定义一律 Hard）
        Check(JudgeSettings.PresetForDifficulty("casual") == JudgePreset.Casual, "casual -> Casual");
        Check(JudgeSettings.PresetForDifficulty("normal") == JudgePreset.Normal, "normal -> Normal");
        Check(JudgeSettings.PresetForDifficulty("tutorial") == JudgePreset.Tutorial,
            "tutorial -> Tutorial（数值等同 Casual）");
        foreach (var key in new[] { "hard", "mega", "giga", "tech", "extra", "unknown", "GIGA" })
            Check(JudgeSettings.PresetForDifficulty(key) == JudgePreset.Hard, $"{key} -> Hard");

        // Hardcore 实例 = Hard × 0.5（31.25 / 56.25 / 81.25 / 125 ms）
        var hc = JudgeSettings.ForPreset(JudgePreset.Hardcore);
        Check(Math.Abs(hc.PerfectSec - 0.03125) < 1e-12, "Hardcore Perfect 窗 = ±31.25ms");
        Check(Math.Abs(hc.GreatSec - 0.05625) < 1e-12, "Hardcore Great 窗 = ±56.25ms");
        Check(Math.Abs(hc.GoodSec - 0.08125) < 1e-12, "Hardcore Good 窗 = ±81.25ms");
        Check(Math.Abs(hc.MissSec - 0.125) < 1e-12, "Hardcore Miss 窗 = ±125ms");
        var hcEngine = new JudgeEngine(JudgePreset.Hardcore);
        Check(hcEngine.Judge(t, t + 0.030).Grade == JudgeGrade.Perfect, "Hardcore ±30ms -> Perfect");
        Check(hcEngine.Judge(t, t + 0.050).Grade == JudgeGrade.Great, "Hardcore ±50ms -> Great");
        Check(hcEngine.Judge(t, t + 0.080).Grade == JudgeGrade.Good, "Hardcore ±80ms -> Good");
        Check(hcEngine.Judge(t, t + 0.100).Grade == JudgeGrade.Miss, "Hardcore ±100ms -> Miss");
        Check(hcEngine.InWindow(t, t + 0.125) && !hcEngine.InWindow(t, t + 0.126),
            "Hardcore Miss 窗 = ±125ms");

        // 模式覆盖：Hardcore 对任何难度强制 Hardcore 实例；Standard/Bleed 走难度映射
        foreach (var key in new[] { "casual", "normal", "hard", "giga", "tech", "unknown" })
            Check(JudgeSettings.ResolvePreset(key, GameplayMode.Hardcore) == JudgePreset.Hardcore,
                $"Hardcore 模式强制 {key} -> Hardcore");
        Check(JudgeSettings.ResolvePreset("casual", GameplayMode.Standard) == JudgePreset.Casual,
            "Standard 模式 casual -> Casual");
        Check(JudgeSettings.ResolvePreset("giga", GameplayMode.Standard) == JudgePreset.Hard,
            "Standard 模式 giga -> Hard");
        Check(JudgeSettings.ResolvePreset("normal", GameplayMode.Bleed) == JudgePreset.Normal,
            "Bleed 预留：暂等同 Standard（normal -> Normal）");
    }

    // 5. Mine：不碰给分（Perfect），危险窗内接触为 Miss。
    internal static void TestMine()
    {
        Console.WriteLine("[5] Mine behavior");
        var engine = new JudgeEngine(JudgePreset.Hard);

        engine.ApplyMine(touched: false);
        Check(engine.Score == 100, "不触碰 Mine -> +100 分");
        Check(engine.Combo == 1, "不触碰 Mine -> 连击 +1");
        Check(engine.CountPerfect == 1, "不触碰 Mine -> 计 Perfect");

        var healthBefore = engine.Health;
        engine.ApplyMine(touched: true);
        Check(engine.Score == 100, "误触 Mine -> 不得分");
        Check(engine.Combo == 0, "误触 Mine -> 断连");
        Check(engine.CountMiss == 1, "误触 Mine -> 计 Miss");
        Check(engine.Health == healthBefore - 1500 || engine.Health == 0,
            $"误触 Mine -> Hard 档血量 -1500（{healthBefore} -> {engine.Health}）");
    }

    // 6. 所有谱面统一 1,000,000 满分，保留原始判定权重比例。
    internal static void TestNormalizedScore()
    {
        Console.WriteLine("[6] Normalized score ceiling");
        var engine = new JudgeEngine(JudgePreset.Hard);
        engine.Apply(ScoreCategory.Tap, JudgeGrade.Great); // 70 / 100
        Check(engine.NormalizedScore(100) == 700_000,
            "Great Tap 70/100 -> 700,000");

        var perfect = new JudgeEngine(JudgePreset.Hard);
        perfect.Apply(ScoreCategory.Tap, JudgeGrade.Perfect);
        Check(perfect.NormalizedScore(100) == 1_000_000,
            "Perfect Tap 100/100 -> 1,000,000");
        Check(perfect.NormalizedScore(0) == 0, "空谱理论满分 0 -> 分数 0");
    }

		// 7. 每帧精确 note 时间批次锁；成功结算不区分 Early/Late，Mine 独立。
		internal static void TestV2InputTimeLock()
		{
			Console.WriteLine("[7] Exact-time frame batch lock");
			var gate = new V2InputProtection();
			Check(gate.CanUse(84.231), "未武装时任意候选通过");
			gate.CommitResolved(84.231);
			Check(gate.IsArmed && gate.LockedTime == 84.231, "结算记录精确锁定时间");
			Check(gate.CanUse(84.231), "同刻多押放行");
			Check(!gate.CanUse(84.350), "同帧异时候选被拒");

			// 早侧 Miss 与晚侧成功都使用同一个结算入口并武装锁。
			gate.Reset();
			gate.CommitResolved(84.231);
			Check(!gate.CanUse(84.232), "早侧 Miss 后不同时间被拒");
			gate.Reset();
			gate.CommitResolved(83.900);
			Check(!gate.CanUse(84.231), "晚侧成功后不同时间同样被拒");

			gate.Reset();
			Check(!gate.IsArmed && gate.CanUse(84.350), "跨帧重置解除锁");
		}

	// 7b. EX-Tap 二值判定（原版 Burst：窗内恒 Perfect，出窗即输入 Miss）。
	internal static void TestExTapBinaryJudgement()
	{
		Console.WriteLine("[7b] EX-Tap binary judgement");
		var engine = new JudgeEngine(JudgePreset.Hard);
		var settings = engine.Settings;
		const double ex = JudgePlan.ExTapWindowScale;   // 与原版一致：1.0（同 Tap 窗）
		const double t = 10.0;
		var good = settings.GoodSec * ex;   // Hard: 0.1625
		var miss = settings.MissSec * ex;   // Hard: 0.25

		// 窗内（含 Great/Good 档位区间）一律 grade 5 = Perfect。
		foreach (var offset in new[] { 0.0, 0.05, -0.05, 0.10, -0.10, 0.16, -0.16 })
		{
			var r = engine.JudgePress(t, t + offset, ex, binaryExTap: true);
			Check(r.Grade == JudgeGrade.Perfect && r.Resolution == JudgeResolution.Perfect,
				$"EX 窗内 {offset:+0.00;-0.00}s -> Perfect（无 GR/GD 档）");
		}

		// 出窗（早侧或晚侧）在输入窗内 -> grade 2 = 输入 Miss，不留 Pending。
		foreach (var offset in new[] { good + 0.01, -(good + 0.01), 0.20, -0.20, miss - 0.001, -(miss - 0.001) })
		{
			var r = engine.JudgePress(t, t + offset, ex, binaryExTap: true);
			Check(r.Grade == JudgeGrade.Miss && r.Resolution == JudgeResolution.InputMiss,
				$"EX 出窗 {offset:+0.000;-0.000}s -> 输入 Miss");
		}
		Check(engine.JudgePress(t, t + 0.20, ex, binaryExTap: true).Resolution != JudgeResolution.Pending,
			"EX 晚侧出窗不再保持 Pending");
		Check(engine.JudgePress(t, t + 0.20, ex, binaryExTap: true).Timing == HitTiming.Late,
			"EX 晚侧出窗标记 Late");
		Check(engine.JudgePress(t, t - 0.20, ex, binaryExTap: true).Timing == HitTiming.Early,
			"EX 早侧出窗标记 Early");

		// 超时（无触点）走 AutoMiss = grade 1：Miss + AutoMiss 结算。
		var timeout = new JudgeEngine(JudgePreset.Hard);
		timeout.Apply(ScoreCategory.Tap, JudgeGrade.Miss, resolution: JudgeResolution.AutoMiss);
		Check(timeout.CountAutoMiss == 1 && timeout.CountMiss == 1, "超时 -> grade 1（AutoMiss）");

		// Tap 等其他类型分级不变（窗口倍率 1.0）。
		Check(engine.JudgePress(t, t + 0.05).Grade == JudgeGrade.Perfect,
			"非二值路径 ±50ms 仍为 Perfect");
		Check(engine.JudgePress(t, t + 0.10).Grade == JudgeGrade.Great,
			"非二值路径 ±100ms 仍为 Great（GR 档未被二值化）");
		Check(engine.JudgePress(t, t + 0.15).Grade == JudgeGrade.Good,
			"非二值路径 ±150ms 仍为 Good（GD 档未被二值化）");
		Check(engine.JudgePress(t, t - 0.20).Grade == JudgeGrade.Miss &&
			engine.JudgePress(t, t - 0.20).Resolution == JudgeResolution.InputMiss,
			"非二值路径早侧超 Good 为输入 Miss");
		Check(engine.JudgePress(t, t + 0.20).Resolution == JudgeResolution.Pending,
			"非二值路径晚侧超 Good 仍保持 Pending");

		// T5 单元带显式 IsExTap 标志（不用 WindowScale 浮点相等判断）。
		var chart = new Chart
		{
			Name = "ex-binary",
			Title = "ex-binary",
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = new[] { new BarSection { Bpm = 120, BarTime = 0.0, Seconds = 0.0 } },
			NotesLeft = Array.Empty<Note>(),
			NotesCenter = new[]
			{
				TestNote(10, 0, NoteType.ExTap, 1.0, 0.0, 1.0, 10.0),
				TestNote(11, 0, NoteType.Tap, 1.5, 0.0, 1.0, 10.5),
			},
			NotesRight = Array.Empty<Note>(),
		};
		var plan = JudgePlan.Build(chart, settings);
		var exUnit = plan.Units.Single(u => u.NoteId == 10);
		var tapUnit = plan.Units.Single(u => u.NoteId == 11);
		Check(exUnit.IsExTap, "T5 单元带 IsExTap 标志");
		Check(Math.Abs(exUnit.WindowScale - 1.0) < 1e-12, "T5 单元窗口倍率 = 1.0（与原版同窗）");
		Check(!tapUnit.IsExTap && Math.Abs(tapUnit.WindowScale - 1.0) < 1e-12, "Tap 单元不带 EX 标志");
	}

	// 8. 逐触点空间、重叠复用和 phase 规则。
	internal static void TestInputJudgeRules()
	{
		Console.WriteLine("[8] Touch overlap and phase rules");
		var bounds = InputJudgeRules.Bounds(1.0, 1.0);
		Check(InputJudgeRules.Overlaps(bounds, 0.9, 0.2),
			"普通音符按触摸半宽扩边");
		Check(!InputJudgeRules.Overlaps(bounds, 0.89, 0.2),
			"扩边外不重叠");
		Check(!InputJudgeRules.Overlaps(bounds, 0.99, 0.2,
			expandByTouchWidth: false), "Mine 不使用触摸扩边");
		var overlapping = InputJudgeRules.Bounds(1.5, 1.0);
		var chord = new[]
		{
			(Track.Center, bounds),
			(Track.Center, overlapping),
			(Track.Left, bounds),
		};
		var chordMatches = InputJudgeRules.MatchingCandidates(
			chord,
			new TouchSample(1, Track.Center, 1.75, ContactPhase.Began),
			candidate => candidate.Item1,
			candidate => candidate.Item2);
		Check(chordMatches.Count == 2,
			"one touch independently matches two overlapping same-track notes");
		var mixed = new[]
		{
			(Bounds: bounds, Expand: true),
			(Bounds: bounds, Expand: false),
		};
		var edgeMatches = InputJudgeRules.MatchingCandidates(
			mixed,
			new TouchSample(2, Track.Center, 0.95, ContactPhase.Began),
			_ => Track.Center,
			candidate => candidate.Bounds,
			touchWidth: 0.2,
			expandByTouchWidthFor: candidate => candidate.Expand);
		Check(edgeMatches.Count == 1 && edgeMatches[0].Expand,
			"touch expansion applies per candidate and never expands mines");

		const double noteTime = 10.0;
		var lowerLeftMask = InputJudgeRules.ProjectedTrackMask(
			184, 800, 771, 400, 1520);
		Check(lowerLeftMask == (TouchTrackMask.Center | TouchTrackMask.Left),
			nameof(lowerLeftMask));
		Check(InputJudgeRules.ProjectedTrackMask(184, 700, 771, 400, 1520) ==
			TouchTrackMask.Left, nameof(TouchTrackMask.Left));
		Check(InputJudgeRules.ProjectedTrackMask(960, 700, 771, 400, 1520) ==
			TouchTrackMask.Center, nameof(TouchTrackMask.Center));
		var lowerRightMask = InputJudgeRules.ProjectedTrackMask(
			1736, 800, 771, 400, 1520);
		Check(lowerRightMask == (TouchTrackMask.Center | TouchTrackMask.Right),
			nameof(lowerRightMask));
		Check(InputJudgeRules.ProjectedTrackMask(960, 800, 771, 400, 1520) ==
			TouchTrackMask.Center, nameof(TouchTrackMask.Center));
		var projectedSamples = new Dictionary<(int Id, Track Track), TouchSample>
		{
			[(7, Track.Center)] = new TouchSample(7, Track.Center, 1.0, ContactPhase.Began),
			[(7, Track.Left)] = new TouchSample(7, Track.Left, 0.5, ContactPhase.Began),
		};
		Check(projectedSamples.Count == 2 &&
			projectedSamples.ContainsKey((7, Track.Center)) &&
			projectedSamples.ContainsKey((7, Track.Left)), nameof(projectedSamples));
		const double perfect = 0.1;
		Check(InputJudgeRules.AcceptsContactPhase(noteTime, 9.925, perfect,
			ContactPhase.Began), "Drag 早侧外半窗接受 phase 1");
		Check(!InputJudgeRules.AcceptsContactPhase(noteTime, 9.925, perfect,
			ContactPhase.Moved), "Drag 早侧外半窗拒绝持续接触");
		Check(InputJudgeRules.AcceptsContactPhase(noteTime, 9.975, perfect,
			ContactPhase.Stationary), "Drag 最后半窗接受 phase 1..3");
		Check(InputJudgeRules.AcceptsContactPhase(noteTime, 10.05, perfect,
			ContactPhase.Stationary), "Drag 晚侧接受持续接触");
		Check(!InputJudgeRules.AcceptsContactPhase(noteTime, 9.89, perfect,
			ContactPhase.Began), "Drag Perfect 早窗之外保持 Pending");

		Check(InputJudgeRules.AcceptsMinePhase(noteTime, 9.95, perfect,
			ContactPhase.Began), "Mine 外侧危险窗接受 phase 1");
		Check(!InputJudgeRules.AcceptsMinePhase(noteTime, 9.95, perfect,
			ContactPhase.Moved), "Mine 外侧危险窗拒绝持续接触");
		Check(InputJudgeRules.AcceptsMinePhase(noteTime, 9.98, perfect,
			ContactPhase.Stationary), "Mine 最后 Perfect/4 接受 phase 1..3");
		Check(!InputJudgeRules.AcceptsMinePhase(noteTime, 10.001, perfect,
			ContactPhase.Began), "Mine 到点后不再触发");
	}

	// 11. AutoMiss/InputMiss 分源，以及 Health/Boost 按主判定数缩放。
	internal static void TestResolutionAndScaledVitals()
	{
		Console.WriteLine("[11] Resolution and scaled Health/Boost");
		var result = new JudgeEngine(JudgePreset.Hard).Judge(10.0, 10.3);
		Check(result.Resolution == JudgeResolution.InputMiss,
			"有效超 Good 输入标记为 InputMiss");

		var scaled = new JudgeEngine(JudgePreset.Hard, totalMainNote: 1200);
		Check(scaled.HealthDelta(ScoreCategory.Tap, JudgeGrade.Miss) == -250,
			"Health = floor(-500*600/1200)");
		Check(scaled.HealthDelta(ScoreCategory.Mine, JudgeGrade.Miss) == -750,
			"Mine Health = floor(-1500*600/1200)");
		Check(scaled.BoostDelta(ScoreCategory.Tap, JudgeGrade.Perfect) == 8,
			"Boost = floor(100*100/1200)");
		scaled.Apply(ScoreCategory.Tap, JudgeGrade.Miss,
			resolution: JudgeResolution.AutoMiss);
		scaled.Apply(ScoreCategory.Tap, JudgeGrade.Miss,
			resolution: JudgeResolution.InputMiss);
		Check(scaled.CountAutoMiss == 1 && scaled.CountInputMiss == 1,
			"两种 Miss 来源分别统计");
		for (var i = 0; i < 10000; i++)
			scaled.Apply(ScoreCategory.Tap, JudgeGrade.Perfect);
		Check(scaled.Boost == JudgeEngine.MaxBoost, "Boost 上限钳到 3000");

		Check(Math.Abs(OriginalJudgeMath.ComboMultiplier(0) - 1.0) < 1e-12,
			"原版 Combo 0 = 1.0x");
		Check(Math.Abs(OriginalJudgeMath.ComboMultiplier(150) - 1.25) < 1e-12,
			"原版 Combo 150 = 1.25x");
		Check(Math.Abs(OriginalJudgeMath.ComboMultiplier(300) - 1.5) < 1e-12,
			"原版 Combo 300 封顶 1.5x");
		Check(OriginalJudgeMath.RawScoreDelta(100, 300) == 1500,
			"原版 raw score 使用命中前 Combo 倍率和固定 ×10");
		Check(OriginalJudgeMath.ClearPercent100(100, 0, 0, 100) == 10000,
			"原版 CLEAR 全 Perfect 快速路径 = 10000");
		Check(OriginalJudgeMath.ClearPercent100(0, 100, 0, 100) == 6999,
			"原版 CLEAR Great 使用实际 float 0.699999988 并截断");
		Check(OriginalJudgeMath.MixerEndGrade(7, 10) == JudgeGrade.Great &&
			OriginalJudgeMath.MixerEndGrade(5, 10) == JudgeGrade.Good &&
			OriginalJudgeMath.MixerEndGrade(4, 10) == JudgeGrade.Miss,
			"Mixer 尾判 70%/50% 边界");
	}
}

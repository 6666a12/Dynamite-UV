using DuxShared.Chart;
using DuxShared.Judge;

namespace CoreTests;

/// <summary>
/// 断言式核心逻辑测试（无测试框架，失败时非零退出）。
/// 跑法：dotnet run --project tools/core-tests/CoreTests.csproj --configuration Release
/// 可选本地开发谱：追加 -- --dev-testdata &lt;packs-dir&gt;
/// </summary>
public static class Program
{
    private static int _failures;

    public static int Main(string[] args)
    {
        _failures = 0;

        string? devTestdataDirectory;
        try
        {
            devTestdataDirectory = ParseArguments(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Argument error: {ex.Message}");
            Console.Error.WriteLine(
                "Usage: dotnet run --project tools/core-tests/CoreTests.csproj -- " +
                "[--dev-testdata <packs-dir>]");
            return 2;
        }

        try
        {
            var chartPath = FindFixturePath();
            Console.WriteLine($"fixture: {chartPath}");

            TestLoad(chartPath, out var chart);
            TestBarTimeConversion(chart!);
            TestDropSpeedVisualModel(chart!);
            TestAutoFullCombo(chart!);
            if (devTestdataDirectory is not null)
                TestHeadlineCountsAcrossDevPacks(devTestdataDirectory);
            TestWindowOffsets();
            TestMine();
            TestNormalizedScore();
            TestInputTimeGroupGate();
            TestInputJudgeRules();
            TestDerivedSustainJudgements();
            TestHoldFailureSettlement();
            TestReleasedHoldSettlement();
            TestSustainInterpolation();
            TestResolutionAndScaledVitals();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Core test error: {ex.Message}");
            return 1;
        }

        Console.WriteLine();
        if (_failures > 0)
        {
            Console.WriteLine($"FAILED: {_failures} assertion(s) failed.");
            return 1;
        }
        Console.WriteLine("ALL TESTS PASSED");
        return 0;
    }

    // 1. 加载人工 clean-room fixture，精确验证元数据、三轨、类型与链结构。
    private static void TestLoad(string path, out Chart? chart)
    {
        Console.WriteLine("[1] Load clean-room synthetic fixture");
        chart = DynamixChartLoader.LoadFile(path);
        var notes = chart.AllNotes.ToArray();
        var total = notes.Length;
        Console.WriteLine($"    name={chart.Name} difficulty={chart.Difficulty} title={chart.Title}");
        Console.WriteLine($"    sections={chart.Sections.Count} notes: L={chart.NotesLeft.Count} " +
                          $"C={chart.NotesCenter.Count} R={chart.NotesRight.Count} total={total}");
        Check(chart.Name == "CleanRoom_0001.3 - Synthetic Core Fixture",
            "fixture name 精确匹配");
        Check(chart.Title == "Synthetic Core Fixture", "title 解析精确匹配");
        Check(chart.Difficulty == 3, "难度号精确为 3");
        Check(chart.Sections.Count == 2, "恰有两个 BPM 段");
        Check(chart.Sections[0].Bpm == 120.0 && chart.Sections[0].BarTime == 0.0 &&
              chart.Sections[0].Seconds == 0.0, "第一 BPM 段 = 120@bar 0/sec 0");
        Check(chart.Sections[1].Bpm == 240.0 && chart.Sections[1].BarTime == 2.0 &&
              chart.Sections[1].Seconds == 4.0, "第二 BPM 段 = 240@bar 2/sec 4");
        Check(chart.DropSpeeds.SequenceEqual(new[]
        {
            (0.0, 0.5),
            (1.0, 1.5),
            (2.0, 1.0),
        }), "DropSpeed 三事件精确加载");
        Check(chart.NotesLeft.Count == 5 && chart.NotesCenter.Count == 6 &&
              chart.NotesRight.Count == 3 && total == 14,
            "三轨 note 数精确为 L=5/C=6/R=3/总计14");
        Check(Enum.GetValues<NoteType>().All(type => notes.Any(note => note.Type == type)),
            "fixture 覆盖 Type 1–9");
        Check(chart.TotalMainNote == 15, "Baked_TotalMainNote fixture 值精确为 15");

        var hold = chart.NotesCenter.Where(note => note.Id is >= 201 and <= 203).ToArray();
        Check(hold.Select(note => note.Id).SequenceEqual(new[] { 201, 202, 203 }) &&
              hold.Select(note => note.SubNoteId).SequenceEqual(new[] { 202, 203, -1 }) &&
              hold.Select(note => note.Type).SequenceEqual(new[]
              {
                  NoteType.HoldHead, NoteType.HoldNode, NoteType.HoldNode,
              }), "Hold 链精确为 201->202->203->-1");
        var mixer = chart.NotesLeft.Where(note => note.Id is >= 103 and <= 105).ToArray();
        Check(mixer.Select(note => note.Id).SequenceEqual(new[] { 103, 104, 105 }) &&
              mixer.Select(note => note.SubNoteId).SequenceEqual(new[] { 104, 105, -1 }) &&
              mixer.Select(note => note.Type).SequenceEqual(new[]
              {
                  NoteType.MixerHead, NoteType.MixerNode, NoteType.MixerNode,
              }), "Mixer 链精确为 103->104->105->-1");
    }

    // 2. 对人工 fixture 做精确的分段时间换算、反向换算与 BPM 查询。
    private static void TestBarTimeConversion(Chart chart)
    {
        Console.WriteLine("[2] Exact BarTime/Second conversion");
        var expected = new Dictionary<int, double>
        {
            [101] = 0.5,
            [102] = 1.5,
            [103] = 3.5,
            [104] = 4.0,
            [105] = 4.25,
            [201] = 1.0,
            [202] = 2.0,
            [203] = 3.0,
            [204] = 4.5,
            [205] = 4.75,
            [206] = 5.0,
            [301] = 2.5,
            [302] = 4.125,
            [303] = 5.25,
        };
        foreach (var note in chart.AllNotes)
        {
            var expectedSecond = expected[note.Id];
            Check(Math.Abs(note.Second - expectedSecond) < 1e-12,
                $"note {note.Id} loader Second = {expectedSecond:F3}s");
            Check(Math.Abs(note.BakedSecond - expectedSecond) < 1e-12,
                $"note {note.Id} synthetic Baked_Second = {expectedSecond:F3}s");
            Check(Math.Abs(chart.BarTimeToSeconds(note.BarTime) - expectedSecond) < 1e-12,
                $"note {note.Id} BarTimeToSeconds = {expectedSecond:F3}s");
            Check(Math.Abs(chart.SecondsToBarTime(expectedSecond) - note.BarTime) < 1e-12,
                $"note {note.Id} Second/BarTime round trip");
        }
        Check(Math.Abs(chart.BarTimeToSeconds(1.5) - 3.0) < 1e-12,
            "120 BPM 段 bar 1.5 = 3.0s");
        Check(Math.Abs(chart.BarTimeToSeconds(2.5) - 4.5) < 1e-12,
            "240 BPM 段 bar 2.5 = 4.5s");
        Check(Math.Abs(chart.SecondsToBarTime(4.75) - 2.75) < 1e-12,
            "240 BPM 段 4.75s = bar 2.75");
        Check(chart.BpmAtBarTime(1.999) == 120.0 && chart.BpmAtBarTime(2.0) == 240.0,
            "BpmAtBarTime 在 bar 2 精确切段");
        Check(chart.BpmAtSeconds(3.999) == 120.0 && chart.BpmAtSeconds(4.0) == 240.0,
            "BpmAtSeconds 在 4.0s 精确切段");
    }

    // 2b. fixture DropSpeed 线性插值；另保留回溯换向与重复事件逻辑测试。
    private static void TestDropSpeedVisualModel(Chart chart)
    {
        Console.WriteLine(nameof(TestDropSpeedVisualModel));
        var fixtureSpeed = new DropSpeedMap(chart.DropSpeeds);
        Check(Math.Abs(fixtureSpeed.SpeedAt(-1.0) - 0.5) < 1e-12,
            "fixture DropSpeed 首事件前 = 0.5");
        Check(Math.Abs(fixtureSpeed.SpeedAt(0.5) - 1.0) < 1e-12,
            "fixture DropSpeed bar 0.5 插值 = 1.0");
        Check(Math.Abs(fixtureSpeed.SpeedAt(1.5) - 1.25) < 1e-12,
            "fixture DropSpeed bar 1.5 插值 = 1.25");
        Check(Math.Abs(fixtureSpeed.SpeedAt(3.0) - 1.0) < 1e-12,
            "fixture DropSpeed 末事件后 = 1.0");
        Check(Math.Abs(fixtureSpeed.RemainingDistance(2.5, 1.5) - 1.25) < 1e-12,
            "fixture RemainingDistance = (2.5-1.5)*1.25 = 1.25");

        var speed = new DropSpeedMap(new[]
        {
            (0.0, 0.2),
            (1.0, 1.1),
        });

        Check(Math.Abs(speed.SpeedAt(0.5) - 0.65) < 1e-12,
            nameof(DropSpeedMap.SpeedAt));
        var peakBar = 7.0 / 18.0;
        var peak = speed.RemainingDistance(1.0, peakBar);
        Check(peak > speed.RemainingDistance(1.0, 0.0),
            nameof(DropSpeedMap.RemainingDistance));
        Check(peak > speed.RemainingDistance(1.0, peakBar - 0.001) &&
            peak > speed.RemainingDistance(1.0, peakBar + 0.001),
            nameof(TestDropSpeedVisualModel));
        Check(Math.Abs(speed.RemainingDistance(1.0, 1.0)) < 1e-12,
            nameof(TestDropSpeedVisualModel));

        var duplicate = new DropSpeedMap(new[] { (2.0, 0.5), (2.0, 0.8) });
        Check(Math.Abs(duplicate.SpeedAt(2.0) - 0.8) < 1e-12,
            nameof(TestDropSpeedVisualModel));
    }

    // 3. Auto-FC：fixture 的 15 个主判定精确命中。
    private static void TestAutoFullCombo(Chart chart)
    {
        Console.WriteLine("[3] Auto-FC simulation");
        var engine = new JudgeEngine(JudgePreset.Hard);
        var plan = JudgePlan.Build(chart, engine.Settings);
        Console.WriteLine($"    units={plan.Units.Count} headline={plan.HeadlineUnitCount} " +
                          $"theoreticalMax={plan.TheoreticalMax} end={plan.EndTime:F3}s");

        Check(plan.Units.Count == 15, "fixture JudgePlan 恰有 15 个单元");
        Check(plan.HeadlineUnitCount == 15, "fixture 主判定数 = 15");
        Check(plan.TheoreticalMax == 1500, "fixture 理论满分 = 1,500 raw");
        Check(Math.Abs(plan.EndTime - 5.25) < 1e-12, "fixture 结束时间 = 5.25s");
        Check(plan.Sustains.Count == 2 && plan.Sustains.ContainsKey(201) &&
              plan.Sustains.ContainsKey(103), "fixture 展开一条 Hold 和一条 Mixer");
        Check(plan.Units.Count(unit => unit.Kind == UnitKind.Input) == 6,
            "fixture Input 单元 = 6");
        Check(plan.Units.Count(unit => unit.Kind == UnitKind.Contact) == 2,
            "fixture Contact 单元 = 2");
        Check(plan.Units.Count(unit => unit.Kind == UnitKind.HoldPoint) == 5,
            "fixture HoldPoint 单元 = 5（Hold 1 + Mixer 4）");
        Check(plan.Units.Count(unit => unit.Kind == UnitKind.HoldEnd) == 1,
            "fixture HoldEnd 单元 = 1");
        Check(plan.Units.Count(unit => unit.Kind == UnitKind.Mine) == 1,
            "fixture Mine 单元 = 1，BarLine 不产生单元");
        var holdUnits = plan.Units.Where(unit => unit.SustainHeadId == 201).ToArray();
        Check(holdUnits.Length == 3 &&
              holdUnits.Select(unit => unit.NoteId).SequenceEqual(new[] { 201, 202, 203 }) &&
              holdUnits.Select(unit => unit.Time).SequenceEqual(new[] { 1.0, 2.0, 3.0 }),
            "fixture Hold 判定精确为 201@1s、202@2s、203@3s");
        var mixerUnits = plan.Units.Where(unit => unit.SustainHeadId == 103).ToArray();
        Check(mixerUnits.Length == 5 &&
              mixerUnits.Select(unit => unit.Time).SequenceEqual(
                  new[] { 3.5, 3.75, 4.0, 4.125, 4.25 }),
            "fixture Mixer 判定精确跨 BPM 为 3.5/3.75/4/4.125/4.25s");

        foreach (var u in plan.Units)
        {
            switch (u.Kind)
            {
                case UnitKind.Input:
                    var r = engine.Judge(u.Time, u.Time); // 精确时刻命中
                    Check(r.Grade == JudgeGrade.Prefect, "精确命中 = Prefect");
                    engine.Apply(u.Category, r.Grade,
                        u.AffectsCombo, u.AffectsJudgeCounts);
                    break;
                case UnitKind.Auto:
                case UnitKind.HoldPoint:
                case UnitKind.Contact: // Drag 接触判定：模拟按住 → Prefect
                case UnitKind.HoldEnd:
                    engine.Apply(u.Category, JudgeGrade.Prefect,
                        u.AffectsCombo, u.AffectsJudgeCounts);
                    break;
                case UnitKind.Mine:
                    engine.ApplyMine(touched: false);
                    break;
            }
            u.Judged = true;
        }

        Console.WriteLine($"    score={engine.Score} maxCombo={engine.MaxCombo} " +
                          $"health={engine.Health}/{engine.MaxHealth} " +
                          $"percent={engine.Percent(plan.TheoreticalMax):F2}%");
        Check(engine.CountPrefect == 15, "Auto 精确得到 15 Prefect");
        Check(engine.CountMiss == 0 && engine.CountGreat == 0 && engine.CountGood == 0,
            "Auto 无其他等级");
        Check(engine.MaxCombo == 15, "Auto MaxCombo = 15");
        Check(engine.Score == 1500, "Auto raw score = 1,500");
        Check(engine.NormalizedScore(plan.TheoreticalMax) == JudgeEngine.NormalizedScoreMax,
            "Auto 归一化分数 = 1,000,000");
        Check(engine.Health == engine.MaxHealth, "Auto Health 满");
        Check(Math.Abs(engine.Percent(plan.TheoreticalMax) - 100.0) < 1e-12,
            "Auto 结算 = 100%");
    }

    // 3b. 仅在 --dev-testdata 显式启用时递归验证本地开发谱。
    private static void TestHeadlineCountsAcrossDevPacks(string packsDirectory)
    {
        Console.WriteLine($"[3b] Optional dev testdata: {packsDirectory}");
        string[] files;
        try
        {
            files = Directory.GetFiles(
                packsDirectory, "chart_*.json", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Cannot scan --dev-testdata directory '{packsDirectory}': {ex.Message}", ex);
        }
        if (files.Length == 0)
            throw new InvalidOperationException(
                $"No chart_*.json files found recursively under --dev-testdata directory: " +
                packsDirectory);

        foreach (var file in files.OrderBy(path => path, StringComparer.Ordinal))
        {
            var chart = DynamixChartLoader.LoadFile(file);
            var engine = new JudgeEngine(JudgePreset.Hard);
            var plan = JudgePlan.Build(chart, engine.Settings);
            foreach (var unit in plan.Units)
                engine.Apply(unit.Category, JudgeGrade.Prefect,
                    unit.AffectsCombo, unit.AffectsJudgeCounts);
            Console.WriteLine($"    {Path.GetRelativePath(packsDirectory, file)}: " +
                              $"units={plan.Units.Count} headline={plan.HeadlineUnitCount} " +
                              $"baked={chart.TotalMainNote} " +
                              $"score={engine.NormalizedScore(plan.TheoreticalMax):N0}");
            Check(plan.Units.All(unit => unit.AffectsCombo && unit.AffectsJudgeCounts),
                $"{Path.GetFileName(file)} 全部派生单元都是主判定");
            Check(engine.NormalizedScore(plan.TheoreticalMax) == 1_000_000,
                $"{Path.GetFileName(file)} 全 Prefect 满分为 1,000,000");
        }
    }

    // 4. 偏移输入（窗口数值按规格书 §6.3：Hard = ±62.5/±112.5/±162.5/±250ms，
    //    Normal = ±62.5/±150/±200/±250ms）。
    //    注意：任务书给的 ±120ms→Great、±170ms→Good 与 Hard 档窗口不符
    //    （Hard 下分别为 Good/Miss），该组数值只在 Normal 档窗口下成立——
    //    这里两档都断言，Hard 档严格按规格书数值。
    private static void TestWindowOffsets()
    {
        Console.WriteLine("[4] Window offsets");
        var hard = new JudgeEngine(JudgePreset.Hard);
        var normal = new JudgeEngine(JudgePreset.Normal);
        const double t = 10.0;

        // ±50ms -> Prefect（Hard/Normal 均为 ±62.5ms 内）
        foreach (var offset in new[] { -0.050, 0.050 })
        {
            Check(hard.Judge(t, t + offset).Grade == JudgeGrade.Prefect, $"Hard ±50ms -> Prefect ({offset:+0.000;-0.000})");
            Check(normal.Judge(t, t + offset).Grade == JudgeGrade.Prefect, $"Normal ±50ms -> Prefect ({offset:+0.000;-0.000})");
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

        // EX-Tap 宽窗口（JudgePlan.ExTapWindowScale = 1.5，用户拍板"判定更宽松"）
        const double ex = JudgePlan.ExTapWindowScale;
        Check(hard.Judge(t, t + 0.200, ex).Grade == JudgeGrade.Good,
            "EX-Tap ±200ms -> Good（Good 窗 162.5×1.5=243.75ms）");
        Check(hard.Judge(t, t + 0.300, ex).Grade == JudgeGrade.Miss,
            "EX-Tap ±300ms -> Miss（超 Good 窗）");
        Check(hard.InWindow(t, t + 0.350, ex), "EX-Tap 350ms 仍在 Miss 窗内（250×1.5=375ms）");
        Check(!hard.InWindow(t, t + 0.380, ex), "EX-Tap 380ms 超出 Miss 窗");

        // 窗口换算值本身（bar * 240/StandardBPM=150，规格书 §6.3）
        var s = hard.Settings;
        Check(Math.Abs(s.PrefectSec - 0.0625) < 1e-12, "Hard Prefect 窗 = ±62.5ms");
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
    }

    // 5. Mine：不碰给分（Prefect），危险窗内接触为 Miss。
    private static void TestMine()
    {
        Console.WriteLine("[5] Mine behavior");
        var engine = new JudgeEngine(JudgePreset.Hard);

        engine.ApplyMine(touched: false);
        Check(engine.Score == 100, "不触碰 Mine -> +100 分");
        Check(engine.Combo == 1, "不触碰 Mine -> 连击 +1");
        Check(engine.CountPrefect == 1, "不触碰 Mine -> 计 Prefect");

        var healthBefore = engine.Health;
        engine.ApplyMine(touched: true);
        Check(engine.Score == 100, "误触 Mine -> 不得分");
        Check(engine.Combo == 0, "误触 Mine -> 断连");
        Check(engine.CountMiss == 1, "误触 Mine -> 计 Miss");
        Check(engine.Health == healthBefore - 1500 || engine.Health == 0,
            $"误触 Mine -> Hard 档血量 -1500（{healthBefore} -> {engine.Health}）");
    }

    // 6. 所有谱面统一 1,000,000 满分，保留原始判定权重比例。
    private static void TestNormalizedScore()
    {
        Console.WriteLine("[6] Normalized score ceiling");
        var engine = new JudgeEngine(JudgePreset.Hard);
        engine.Apply(ScoreCategory.Tap, JudgeGrade.Great); // 70 / 100
        Check(engine.NormalizedScore(100) == 700_000,
            "Great Tap 70/100 -> 700,000");

        var perfect = new JudgeEngine(JudgePreset.Hard);
        perfect.Apply(ScoreCategory.Tap, JudgeGrade.Prefect);
        Check(perfect.NormalizedScore(100) == 1_000_000,
            "Perfect Tap 100/100 -> 1,000,000");
        Check(perfect.NormalizedScore(0) == 0, "空谱理论满分 0 -> 分数 0");
    }

	// 7. 同一输入批次只接受同一 float32 目标时刻；同刻多押继续放行。
	private static void TestInputTimeGroupGate()
	{
		Console.WriteLine("[7] Input timestamp group gate");
		var gate = new InputTimeGroupGate();

		Check(!gate.IsLocked, "新门控器未锁定");
		Check(gate.TryLock(10.0), "首个目标时刻成功锁定");
		Check(gate.IsLocked, "锁定后 IsLocked = true");
		Check(gate.Matches(10.0), "完全同刻允许多押");
		Check(gate.Matches(10.0 + 1e-10), "转换为同一 float32 的时间属于同组");
		Check(gate.TryLock(10.0 + 1e-10), "同一 float32 时间可重复通过");
		Check(!gate.Matches(10.001), "不同目标时刻不匹配");
		Check(!gate.TryLock(10.001), "锁定后拒绝异时目标");
		Check(!gate.Allows(10.001, 10.0), "异时 early 目标受共享时间锁约束");
		Check(gate.Allows(9.999, 10.0), "late 输入不受共享 early 时间锁约束");

		gate.Reset();
		Check(!gate.IsLocked, "Reset 清除锁定");
		Check(gate.TryLock(10.001) && gate.Matches(10.001),
			"Reset 后可以锁定新的目标时刻");
	}

	// 8. 逐触点空间、重叠复用和 phase 规则。
	private static void TestInputJudgeRules()
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
		const double prefect = 0.1;
		Check(InputJudgeRules.AcceptsContactPhase(noteTime, 9.925, prefect,
			ContactPhase.Began), "Drag 早侧外半窗接受 phase 1");
		Check(!InputJudgeRules.AcceptsContactPhase(noteTime, 9.925, prefect,
			ContactPhase.Moved), "Drag 早侧外半窗拒绝持续接触");
		Check(InputJudgeRules.AcceptsContactPhase(noteTime, 9.975, prefect,
			ContactPhase.Stationary), "Drag 最后半窗接受 phase 1..3");
		Check(InputJudgeRules.AcceptsContactPhase(noteTime, 10.05, prefect,
			ContactPhase.Stationary), "Drag 晚侧接受持续接触");
		Check(!InputJudgeRules.AcceptsContactPhase(noteTime, 9.89, prefect,
			ContactPhase.Began), "Drag Prefect 早窗之外保持 Pending");

		Check(InputJudgeRules.AcceptsMinePhase(noteTime, 9.95, prefect,
			ContactPhase.Began), "Mine 外侧危险窗接受 phase 1");
		Check(!InputJudgeRules.AcceptsMinePhase(noteTime, 9.95, prefect,
			ContactPhase.Moved), "Mine 外侧危险窗拒绝持续接触");
		Check(InputJudgeRules.AcceptsMinePhase(noteTime, 9.98, prefect,
			ContactPhase.Stationary), "Mine 最后 Prefect/4 接受 phase 1..3");
		Check(!InputJudgeRules.AcceptsMinePhase(noteTime, 10.001, prefect,
			ContactPhase.Began), "Mine 到点后不再触发");
	}

	// 9. Hold 按真实节点判定；Mixer 从头相位每 1/8 bar 派生判定。
	private static void TestDerivedSustainJudgements()
	{
		Console.WriteLine("[9] Derived sustain judgements");
		var holdHead = TestNote(1, 2, NoteType.HoldHead, 0.0, 0.0, 1.0, 0.0);
		var holdNode = TestNote(2, 3, NoteType.HoldNode, 0.25, 0.5, 1.0, 0.5);
		var holdTail = TestNote(3, -1, NoteType.HoldNode, 0.75, 1.0, 1.0, 1.5);
		var mixerHead = TestNote(10, 11, NoteType.MixerHead, 2.0, 1.0, 1.0, 4.0,
			Track.Left);
		var mixerTail = TestNote(11, -1, NoteType.MixerNode, 3.0, 2.0, 1.0, 6.0,
			Track.Left);
		var shortMixerHead = TestNote(20, 21, NoteType.MixerHead, 4.0, 1.0, 1.0, 8.0,
			Track.Right);
		var shortMixerTail = TestNote(21, -1, NoteType.MixerNode, 4.3, 2.0, 1.0, 8.6,
			Track.Right);
		var chart = new Chart
		{
			Name = "test",
			Title = "test",
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = new[]
			{
				new BarSection { Bpm = 120, BarTime = 0.0, Seconds = 0.0 },
			},
			NotesLeft = new[] { mixerHead, mixerTail },
			NotesCenter = new[] { holdHead, holdNode, holdTail },
			NotesRight = new[] { shortMixerHead, shortMixerTail },
		};
		var plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));

		var holdUnits = plan.Units
			.Where(unit => unit.SustainHeadId == holdHead.Id).ToArray();
		Check(holdUnits.Length == 3, "三节点 Hold 正好生成头、中间节点和尾三个主判定");
		Check(holdUnits.Select(unit => unit.NoteId).SequenceEqual(new[] { 1, 2, 3 }),
			"Hold 判定严格对应实际路径节点");

		var mixerUnits = plan.Units
			.Where(unit => unit.SustainHeadId == mixerHead.Id).ToArray();
		Check(mixerUnits.Length == 9, "1 bar Mixer 从头到尾生成 9 个主判定");
		for (var i = 0; i < mixerUnits.Length; i++)
			Check(Math.Abs(mixerUnits[i].Time - (4.0 + i * 0.25)) < 1e-9,
				$"Mixer tick[{i}] 位于头部相位 + {i}/8 bar");

		var shortMixerUnits = plan.Units
			.Where(unit => unit.SustainHeadId == shortMixerHead.Id).ToArray();
		Check(shortMixerUnits.Length == 3,
			"0.3 bar Mixer 只生成 0、1/8、2/8 三个判定，不补非网格尾");
		Check(shortMixerUnits.All(unit => Math.Abs(unit.Time - 8.6) > 1e-9),
			"非网格尾不产生额外判定");
		Check(Math.Abs(plan.EndTime - 8.6) < 1e-9,
			"非网格尾仍决定 sustain 和谱面结束时间");

		Check(plan.Units.All(unit => unit.AffectsCombo && unit.AffectsJudgeCounts),
			"Hold 节点和 Mixer 八分点全部进入 Combo 与 P/GR/GD/M");
		Check(plan.Units.All(unit =>
			JudgeEngine.ScoreDelta(unit.Category, JudgeGrade.Prefect) == 100),
			"Hold 节点和 Mixer 八分点 Prefect 权重均为 100 分");
		Check(JudgeEngine.ScoreDelta(ScoreCategory.HoldHolding, JudgeGrade.Great) == 70 &&
			JudgeEngine.ScoreDelta(ScoreCategory.HoldHolding, JudgeGrade.Good) == 50 &&
			JudgeEngine.ScoreDelta(ScoreCategory.MixerHolding, JudgeGrade.Great) == 70 &&
			JudgeEngine.ScoreDelta(ScoreCategory.MixerHolding, JudgeGrade.Good) == 50,
			"派生主判定沿用完整 100/70/50/0 权重");
	}

	private static void TestHoldFailureSettlement()
	{
		Console.WriteLine(nameof(TestHoldFailureSettlement));
		Check(JudgeEngine.ScoreDelta(ScoreCategory.MixerHolding,
			JudgeGrade.Miss) == 0, nameof(JudgeGrade.Miss));
		var head = TestNote(1, 2, NoteType.HoldHead, 0.0, 0.0, 1.0, 0.0);
		var middle = TestNote(2, 3, NoteType.HoldNode, 0.5, 0.0, 1.0, 1.0);
		var tail = TestNote(3, -1, NoteType.HoldNode, 1.0, 0.0, 1.0, 2.0);
		var chart = new Chart
		{
			Name = string.Empty,
			Title = string.Empty,
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = Array.Empty<BarSection>(),
			NotesLeft = Array.Empty<Note>(),
			NotesCenter = new[] { head, middle, tail },
			NotesRight = Array.Empty<Note>(),
		};
		var plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));
		var engine = new JudgeEngine(JudgePreset.Hard, plan.HeadlineUnitCount);
		var headUnit = plan.Units.Single(
			unit => unit.Category == ScoreCategory.HoldStart);
		engine.Apply(headUnit.Category, JudgeGrade.Prefect);
		headUnit.Judged = true;
		var failed = SustainJudgementRules.FailRemainingHold(
			plan.Units, head.Id, engine);
		Check(failed.Count == 2, nameof(TestHoldFailureSettlement));
		Check(failed.All(unit => unit.Judged),
			nameof(SustainJudgementRules.FailRemainingHold));
		Check(engine.CountPrefect == 1 && engine.CountMiss == 2 &&
			engine.Combo == 0, nameof(JudgeEngine.Combo));
		var score = engine.Score;
		var misses = engine.CountMiss;
		var repeated = SustainJudgementRules.FailRemainingHold(
			plan.Units, head.Id, engine);
		Check(repeated.Count == 0 && engine.Score == score &&
			engine.CountMiss == misses, nameof(JudgeUnit.Judged));
	}

	private static void TestReleasedHoldSettlement()
	{
		Console.WriteLine(nameof(TestReleasedHoldSettlement));
		var head = TestNote(1, 2, NoteType.HoldHead, 0.0, 0.0, 1.0, 0.0);
		var middle = TestNote(2, 3, NoteType.HoldNode, 0.5, 0.0, 1.0, 1.0);
		var tail = TestNote(3, -1, NoteType.HoldNode, 1.0, 0.0, 1.0, 2.0);
		var chart = new Chart
		{
			Name = string.Empty,
			Title = string.Empty,
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = Array.Empty<BarSection>(),
			NotesLeft = Array.Empty<Note>(),
			NotesCenter = new[] { head, middle, tail },
			NotesRight = Array.Empty<Note>(),
		};
		var plan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));
		var engine = new JudgeEngine(JudgePreset.Hard, plan.HeadlineUnitCount);
		var headUnit = plan.Units.Single(
			unit => unit.Category == ScoreCategory.HoldStart);
		engine.Apply(headUnit.Category, JudgeGrade.Prefect);
		headUnit.Judged = true;

		var settled = SustainJudgementRules.SettleReleasedHold(
			plan.Units, head.Id, 1.9, engine);
		Check(settled.Count == 2, nameof(TestReleasedHoldSettlement));
		Check(settled.Single(item => item.Unit.Category == ScoreCategory.HoldHolding)
			.Grade == JudgeGrade.Miss, "提前松手后未结算的 Hold 中间节点为 Miss");
		Check(settled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
			.Grade == JudgeGrade.Great, "Hold 尾按实际松手时刻进入 Great 档");
		Check(settled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
			.Timing == HitTiming.Early, "提前松手的 Hold 尾保留 Early 时序");
		Check(engine.CountPrefect == 1 && engine.CountGreat == 1 &&
			engine.CountMiss == 1, "提前松手结算统计正确");

		var afterTailPlan = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard));
		var afterTail = new JudgeEngine(JudgePreset.Hard, afterTailPlan.HeadlineUnitCount);
		var afterTailHead = afterTailPlan.Units.Single(
			unit => unit.Category == ScoreCategory.HoldStart);
		afterTail.Apply(afterTailHead.Category, JudgeGrade.Prefect);
		afterTailHead.Judged = true;
		var afterTailSettled = SustainJudgementRules.SettleReleasedHold(
			afterTailPlan.Units, head.Id, 2.2, afterTail);
		Check(afterTailSettled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
			.Grade == JudgeGrade.Prefect &&
			afterTailSettled.Single(item => item.Unit.Category == ScoreCategory.HoldEnd)
				.Timing == HitTiming.Exact,
			"共享 Hold 结算将尾后 release 钳到尾时刻 Prefect");
	}

	// 10. Hold/Mixer 身体左右边缘分别线性插值。
	private static void TestSustainInterpolation()
	{
		Console.WriteLine("[10] Sustain bounds interpolation");
		var a = TestNote(1, 2, NoteType.HoldHead, 0, 1, 1, 0);
		var b = TestNote(2, -1, NoteType.HoldNode, 1, 3, 2, 10);
		var path = new SustainPath
		{
			Kind = SustainKind.Hold,
			HeadId = 1,
			Track = Track.Center,
			Nodes = new[] { a, b },
		};
		var mid = path.BoundsAt(5.0);
		Check(Math.Abs(mid.Left - 2.0) < 1e-12, "中点左缘线性插值");
		Check(Math.Abs(mid.Right - 3.5) < 1e-12, "中点右缘线性插值");
	}

	// 11. AutoMiss/InputMiss 分源，以及 Health/Boost 按主判定数缩放。
	private static void TestResolutionAndScaledVitals()
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
		Check(scaled.BoostDelta(ScoreCategory.Tap, JudgeGrade.Prefect) == 8,
			"Boost = floor(100*100/1200)");
		scaled.Apply(ScoreCategory.Tap, JudgeGrade.Miss,
			resolution: JudgeResolution.AutoMiss);
		scaled.Apply(ScoreCategory.Tap, JudgeGrade.Miss,
			resolution: JudgeResolution.InputMiss);
		Check(scaled.CountAutoMiss == 1 && scaled.CountInputMiss == 1,
			"两种 Miss 来源分别统计");
		for (var i = 0; i < 10000; i++)
			scaled.Apply(ScoreCategory.Tap, JudgeGrade.Prefect);
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
			"原版 CLEAR 全 Prefect 快速路径 = 10000");
		Check(OriginalJudgeMath.ClearPercent100(0, 100, 0, 100) == 6999,
			"原版 CLEAR Great 使用实际 float 0.699999988 并截断");
		Check(OriginalJudgeMath.MixerEndGrade(7, 10) == JudgeGrade.Great &&
			OriginalJudgeMath.MixerEndGrade(5, 10) == JudgeGrade.Good &&
			OriginalJudgeMath.MixerEndGrade(4, 10) == JudgeGrade.Miss,
			"Mixer 尾判 70%/50% 边界");
	}

	private static Note TestNote(int id, int subId, NoteType type,
		double bar, double position, double width, double second,
		Track track = Track.Center) => new()
	{
		Id = id,
		SubNoteId = subId,
		Type = type,
		Track = track,
		BarTime = bar,
		Position = position,
		Width = width,
		Second = second,
		BakedSecond = second,
	};

    // ---- helpers ----

    private static void Check(bool cond, string what)
    {
        if (!cond)
        {
            _failures++;
            Console.WriteLine($"    FAIL: {what}");
        }
    }

    private static string? ParseArguments(string[] args)
    {
        if (args.Length == 0)
            return null;
        if (args.Length != 2 || !string.Equals(
                args[0], "--dev-testdata", StringComparison.Ordinal))
            throw new ArgumentException(
                "Expected no arguments or exactly --dev-testdata <packs-dir>.");
        if (string.IsNullOrWhiteSpace(args[1]))
            throw new ArgumentException("--dev-testdata requires a non-empty <packs-dir>.");

        string directory;
        try
        {
            directory = Path.GetFullPath(args[1]);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(
                $"Invalid --dev-testdata directory '{args[1]}': {ex.Message}");
        }
        if (!Directory.Exists(directory))
            throw new ArgumentException(
                $"--dev-testdata directory does not exist: {directory}");
        return directory;
    }

    private static string FindFixturePath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "synthetic_chart.json");
        if (File.Exists(candidate))
            return candidate;
        throw new FileNotFoundException(
            "Missing copied clean-room fixture. Expected: " + candidate, candidate);
    }
}

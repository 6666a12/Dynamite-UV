using DuxShared.Chart;
using DuxShared.Judge;

namespace CoreTests;

/// <summary>
/// 断言式核心逻辑测试（无测试框架，失败时非零退出）。
/// 跑法：dotnet run --project community/tools/core-tests
/// </summary>
public static class Program
{
    private static int _failures;

    public static int Main()
    {
        var chartPath = FindChartPath();
        Console.WriteLine($"chart: {chartPath}");

        TestLoad(chartPath, out var chart);
        TestBarTimeConversion(chart!);
        TestAutoFullCombo(chart!);
        TestWindowOffsets();
        TestMine();

        Console.WriteLine();
        if (_failures > 0)
        {
            Console.WriteLine($"FAILED: {_failures} assertion(s) failed.");
            return 1;
        }
        Console.WriteLine("ALL TESTS PASSED");
        return 0;
    }

    // 1. 加载 chart_test.json 成功，音符总数合理
    private static void TestLoad(string path, out Chart? chart)
    {
        Console.WriteLine("[1] Load chart_test.json");
        chart = DynamixChartLoader.LoadFile(path);
        var total = chart.AllNotes.Count();
        Console.WriteLine($"    name={chart.Name} difficulty={chart.Difficulty} title={chart.Title}");
        Console.WriteLine($"    sections={chart.Sections.Count} notes: L={chart.NotesLeft.Count} " +
                          $"C={chart.NotesCenter.Count} R={chart.NotesRight.Count} total={total}");
        Check(chart.Sections.Count >= 1, "时间线非空（该谱有 BakedBarSections）");
        Check(total > 2000 && total < 2500, $"音符总数合理（{total}）");
        Check(chart.Difficulty == 3, $"难度号=3 Hard（{chart.Difficulty}）");
        Check(chart.AllNotes.All(n => n.Second > 0), "全部音符命中秒为正");
    }

    // 2. 全部音符：§3.2 换算秒 vs JSON Baked_Second 误差 ≤1ms
    private static void TestBarTimeConversion(Chart chart)
    {
        Console.WriteLine("[2] BarTime->Second vs Baked_Second (<=1ms)");
        var maxErr = 0.0;
        Note? worst = null;
        foreach (var n in chart.AllNotes)
        {
            var sec = chart.BarTimeToSeconds(n.BarTime);
            var err = Math.Abs(sec - n.BakedSecond);
            if (err > maxErr) { maxErr = err; worst = n; }
        }
        Console.WriteLine($"    max error = {maxErr * 1000.0:F4} ms (note Id={worst?.Id})");
        Check(maxErr <= 0.001, "全部音符换算误差 ≤1ms");
    }

    // 3. Auto-FC：全部判定单元精确命中 → 全 Prefect、combo 最大、得分=理论满分、满血、100%
    private static void TestAutoFullCombo(Chart chart)
    {
        Console.WriteLine("[3] Auto-FC simulation");
        var engine = new JudgeEngine(JudgePreset.Hard);
        var plan = JudgePlan.Build(chart, engine.Settings);
        Console.WriteLine($"    units={plan.Units.Count} theoreticalMax={plan.TheoreticalMax} " +
                          $"end={plan.EndTime:F2}s");

        foreach (var u in plan.Units)
        {
            switch (u.Kind)
            {
                case UnitKind.Input:
                    var r = engine.Judge(u.Time, u.Time); // 精确时刻命中
                    Check(r.Grade == JudgeGrade.Prefect, "精确命中 = Prefect");
                    engine.Apply(u.Category, r.Grade);
                    break;
                case UnitKind.Auto:
                case UnitKind.HoldPoint:
                    engine.Apply(u.Category, JudgeGrade.Prefect);
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
        Check(engine.CountPrefect == plan.Units.Count, "全部 Prefect");
        Check(engine.CountMiss == 0 && engine.CountGreat == 0 && engine.CountGood == 0, "无其他等级");
        Check(engine.MaxCombo == plan.Units.Count, "combo = 判定单元总数");
        Check(engine.Score == plan.TheoreticalMax, "得分 = 理论满分");
        Check(engine.Health == engine.MaxHealth, "Health 满");
        Check(Math.Abs(engine.Percent(plan.TheoreticalMax) - 100.0) < 1e-9, "结算 100%");
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

        // 窗口换算值本身（bar * 240/StandardBPM=150，规格书 §6.3）
        var s = hard.Settings;
        Check(Math.Abs(s.PrefectSec - 0.0625) < 1e-12, "Hard Prefect 窗 = ±62.5ms");
        Check(Math.Abs(s.GreatSec - 0.1125) < 1e-12, "Hard Great 窗 = ±112.5ms");
        Check(Math.Abs(s.GoodSec - 0.1625) < 1e-12, "Hard Good 窗 = ±162.5ms");
        Check(Math.Abs(s.MissSec - 0.250) < 1e-12, "Hard Miss 窗 = ±250ms");
        Check(Math.Abs(s.HoldHoldingSec - 0.200) < 1e-12, "Holding 间隔 = 200ms");
        var sn = normal.Settings;
        Check(Math.Abs(sn.GreatSec - 0.150) < 1e-12, "Normal Great 窗 = ±150ms");
        Check(Math.Abs(sn.GoodSec - 0.200) < 1e-12, "Normal Good 窗 = ±200ms");
    }

    // 5. Mine【TODO 规格书 §9#7 推测】：不碰给分（Prefect），误触 Miss 扣分
    private static void TestMine()
    {
        Console.WriteLine("[5] Mine behavior (speculated direction, TODO §9#7)");
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

    // ---- helpers ----

    private static void Check(bool cond, string what)
    {
        if (!cond)
        {
            _failures++;
            Console.WriteLine($"    FAIL: {what}");
        }
    }

    private static string FindChartPath()
    {
        // 从程序输出目录向上找 community/client/testdata/chart_test.json
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName,
                "community", "client", "testdata", "chart_test.json");
            if (File.Exists(candidate))
                return candidate;
            // 也兼容直接在 community/ 之内运行的情况
            if (dir.Name == "community")
            {
                candidate = Path.Combine(dir.FullName, "client", "testdata", "chart_test.json");
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("找不到 community/client/testdata/chart_test.json");
    }
}

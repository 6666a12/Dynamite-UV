using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class LegacyChartTests
{

    // 1. 加载人工 clean-room fixture，精确验证元数据、三轨、类型与链结构。
    internal static void TestLoad(string path, out Chart? chart)
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
    internal static void TestBarTimeConversion(Chart chart)
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

    // 3. Auto-FC：fixture 的 15 个主判定精确命中。
    internal static void TestAutoFullCombo(Chart chart)
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
                    Check(r.Grade == JudgeGrade.Perfect, "精确命中 = Perfect");
                    engine.Apply(u.Category, r.Grade,
                        u.AffectsCombo, u.AffectsJudgeCounts);
                    break;
                case UnitKind.Auto:
                case UnitKind.HoldPoint:
                case UnitKind.Contact: // Drag 接触判定：模拟按住 → Perfect
                case UnitKind.HoldEnd:
                    engine.Apply(u.Category, JudgeGrade.Perfect,
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
        Check(engine.CountPerfect == 15, "Auto 精确得到 15 Perfect");
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
    internal static void TestHeadlineCountsAcrossDevPacks(string packsDirectory)
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
                engine.Apply(unit.Category, JudgeGrade.Perfect,
                    unit.AffectsCombo, unit.AffectsJudgeCounts);
            Console.WriteLine($"    {Path.GetRelativePath(packsDirectory, file)}: " +
                              $"units={plan.Units.Count} headline={plan.HeadlineUnitCount} " +
                              $"baked={chart.TotalMainNote} " +
                              $"score={engine.NormalizedScore(plan.TheoreticalMax):N0}");
            Check(plan.Units.All(unit => unit.AffectsCombo && unit.AffectsJudgeCounts),
                $"{Path.GetFileName(file)} 全部派生单元都是主判定");
            Check(engine.NormalizedScore(plan.TheoreticalMax) == 1_000_000,
                $"{Path.GetFileName(file)} 全 Perfect 满分为 1,000,000");
        }
    }
}

using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

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
            TestConstantBpmVisualSpeed(chart!);
            TestGameplayStageGeometry();
            TestGameplayMirrorMapping();
            TestAutoFullCombo(chart!);
            if (devTestdataDirectory is not null)
                TestHeadlineCountsAcrossDevPacks(devTestdataDirectory);
            TestWindowOffsets();
            TestMine();
            TestNormalizedScore();
            TestV2InputTimeLock();
            TestExTapBinaryJudgement();
            TestInputJudgeRules();
            TestDerivedSustainJudgements();
            TestHoldFailureSettlement();
            TestReleasedHoldSettlement();
            TestSustainInterpolation();
            TestResolutionAndScaledVitals();
            TestV2ExactBarTimeAndLegacyConversion();
            TestV2GeometryJudgementAndSync();
            TestV2CurvesMixerAndDigest();
            TestD4CFrozenGrace();
            TestHoldReleaseFreezesGrace();
            TestScoreStoreMigrationAndIdentity();
            ResultRevealTests.Run(Check);
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

    private static void TestConstantBpmVisualSpeed(Chart chart)
    {
        Console.WriteLine(nameof(TestConstantBpmVisualSpeed));
        const double basePixelsPerSecond = 1026.0;

        var slowCurrentSecond = 3.0;
        var fastCurrentSecond = 4.5;
        var slowCurrentBar = chart.SecondsToBarTime(slowCurrentSecond);
        var fastCurrentBar = chart.SecondsToBarTime(fastCurrentSecond);
        Check(chart.BpmAtSeconds(slowCurrentSecond) == 120.0 &&
            chart.BpmAtSeconds(fastCurrentSecond) == 240.0,
            "visual speed fixture samples 120 and 240 BPM segments");

        var slowScroll = new DropSpeedMap(new[] { (0.0, 1.0) }).SpeedAt(slowCurrentBar);
        var fastScroll = new DropSpeedMap(new[] { (0.0, 1.0) }).SpeedAt(fastCurrentBar);
        var slowDistance = VisualScrollMath.DistancePixels(
            slowCurrentSecond + 0.5, slowCurrentSecond, basePixelsPerSecond,
            slowScroll, 1.0);
        var fastDistance = VisualScrollMath.DistancePixels(
            fastCurrentSecond + 0.5, fastCurrentSecond, basePixelsPerSecond,
            fastScroll, 1.0);
        Check(Math.Abs(slowDistance - 513.0) < 1e-12 &&
            Math.Abs(fastDistance - 513.0) < 1e-12,
            "same 0.5s lead stays 513px at both 120 and 240 BPM");

        Check(Math.Abs(VisualScrollMath.DistancePixels(
            2.0, 1.0, basePixelsPerSecond, 1.25, 0.5) - 641.25) < 1e-12,
            "scroll and player speed multiply the fixed 1026px/s baseline");
        Check(Math.Abs(VisualScrollMath.DistancePixels(
            2.0, 1.0, basePixelsPerSecond, -0.5, 1.0) + 513.0) < 1e-12,
            "legacy negative DropSpeed preserves signed approach distance");
        Check(Math.Abs(VisualScrollMath.FallthroughSpeedPixelsPerSecond(
            basePixelsPerSecond, -0.5, 1.0) - 513.0) < 1e-12,
            "fallthrough keeps moving outward at the absolute scroll speed");
    }

    private static void TestGameplayStageGeometry()
    {
        Console.WriteLine(nameof(TestGameplayStageGeometry));
        var center = GameplayStageGeometry.PositionAt(Track.Center, 2.5, 100);
        var left = GameplayStageGeometry.PositionAt(Track.Left, 2.5, 100);
        var right = GameplayStageGeometry.PositionAt(Track.Right, 2.5, 100);
        Check(Math.Abs(center.X - 963f) < .001f && Math.Abs(center.Y - 761f) < .001f,
            "shared stage center mapping preserves fixed gameplay geometry");
        Check(Math.Abs(left.X - 259f) < .001f && Math.Abs(right.X - 1661f) < .001f &&
              Math.Abs(left.Y - right.Y) < .001f,
            "shared stage side mappings are symmetric");
        Check(Math.Abs(GameplayStageGeometry.CenterWidthPx(1) - 259.54f) < .001f &&
              Math.Abs(GameplayStageGeometry.StageXToCenter(GameplayStageGeometry.CenterToStageX(2.5)) - 2.5) < .00001,
            "shared stage center width and inverse mapping remain deterministic");
        Check(Math.Abs(GameplayStageGeometry.PreviewDistanceForBars(Track.Center, 8) - 790f) < .001f &&
              Math.Abs(GameplayStageGeometry.PreviewDistanceForBars(Track.Left, 8) *
                  GameplayStageGeometry.SideDistanceScale - 691f) < .001f &&
              Math.Abs(GameplayStageGeometry.PreviewDistanceForBars(Track.Right, 8) -
                  GameplayStageGeometry.PreviewDistanceForBars(Track.Left, 8)) < .001f,
            "shared editor preview keeps gameplay center and side travel scales");
        Check(Math.Abs(GameplayStageGeometry.PreviewBarAtStagePoint(Track.Left,
                  GameplayStageGeometry.LeftLineX + GameplayStageGeometry.SidePreviewTravelPx,
                  GameplayStageGeometry.SideY0, 3) - 11d) < .00001 &&
              Math.Abs(GameplayStageGeometry.PreviewBarAtStagePoint(Track.Right,
                  GameplayStageGeometry.RightLineX - GameplayStageGeometry.SidePreviewTravelPx,
                  GameplayStageGeometry.SideY0, 3) - 11d) < .00001 &&
              Math.Abs(GameplayStageGeometry.CenterAtStagePoint(Track.Left,
                  GameplayStageGeometry.LeftLineX, GameplayStageGeometry.SideY0 - 2.5f * GameplayStageGeometry.SideUnitPx) - 2.5d) < .00001 &&
              Math.Abs(GameplayStageGeometry.WidthAtStageDrag(Track.Right, 0, 100, 0, 330) - 2d) < .00001,
            "shared editor placement maps both side rails to time center and vertical width");
    }

    // 2b. MIRROR 开关的纯几何不变量：直接打 shared 里的映射本体
    // （GameplayStageGeometry.MirroredDisplay / MirroredTrack，客户端 GameplayMirror 只是读设置）。
    // 侧轨 Left↔Right 互换且沿判定线坐标不动（关于屏幕中轴的横向反射）；
    // Center 水平坐标关于**面板中轴**反射、宽度不变；映射自反
    // （屏幕 → 谱面的输入反查复用同一函数，不自反则输入归属与 note 视觉分叉）。
    private static void TestGameplayMirrorMapping()
    {
        Console.WriteLine(nameof(TestGameplayMirrorMapping));
        var axisX = GameplayStageGeometry.CenterX0 +
                    (float)GameplayStageGeometry.MirrorAxis * GameplayStageGeometry.CenterUnitPx;

        // 面板中轴必须是 2.5（屏幕 x=963），不是 [0,4] 左缘带的中点 2.0（x=826.4）：
        // 后者会把整个中轨镜像整体左移 136.6px。全部发行谱面的 note 中心都落在 [0,5]。
        Check(Math.Abs(GameplayStageGeometry.MirrorAxis - 2.5) < .00001 &&
              Math.Abs(axisX - 963f) < .001f &&
              Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Center, 0d, true).Center - 5d) < .00001,
            "center mirror axis is the panel midpoint 2.5 (screen x=963), mapping center 0 onto 5");

        var offCenter = GameplayStageGeometry.PositionAt(Track.Center, 2.5, 100);
        var mappedCenter = GameplayStageGeometry.MirroredDisplay(Track.Center, 2.5, true);
        var mirroredCenter = GameplayStageGeometry.PositionAt(mappedCenter.Track, mappedCenter.Center, 100);
        Check(Math.Abs(mirroredCenter.X - (2f * axisX - offCenter.X)) < .001f &&
              Math.Abs(mirroredCenter.Y - offCenter.Y) < .001f,
            "mirrored center note reflects about the panel axis and keeps its distance to the judge line");
        Check(Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Center, mappedCenter.Center, true)
                  .Center - 2.5) < .00001 &&
              Math.Abs(GameplayStageGeometry.CenterWidthPx(1) - 259.54f) < .001f,
            "center mirror is self-inverse and leaves note width untouched");

        // 对称用例：中心 ±2.0、宽 3.0 的两条互为镜像。Position 是左缘，所以对应
        // 中心 4.5 / 0.5 → 跨度 [3,6] 与 [-1,2]；镜像后右缘 6 → 左缘 -1（端点对调），
        // 两者的 screenX 相加必须正好等于 2·963。
        const double halfWidth = 1.5;
        var upper = 2.5 + 2.0;
        var lower = 2.5 - 2.0;
        Check(Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Center, upper, true).Center - lower)
                  < .00001 &&
              Math.Abs(2d * GameplayStageGeometry.MirrorAxis - (upper + halfWidth) -
                       (lower - halfWidth)) < .00001 &&
              Math.Abs(GameplayStageGeometry.CenterToStageX(upper) +
                       GameplayStageGeometry.CenterToStageX(lower) - 2f * axisX) < .001f,
            "center error +2.0/-2.0 with width 3.0 map onto each other symmetric about the panel axis");

        // 输入逆映射：触点在**显示坐标**上 → 判定侧用同一函数反查谱面坐标 → 必须落进 note 跨度。
        // 这也是"镜像开了但点在镜像后位置不判"的根因断言：判定只认谱面坐标，
        // 若在别处再镜像一次（两次施加互相抵消），反查值就会跑回显示坐标、落在跨度之外。
        const double noteP = 3.0;
        const double noteW = 3.0;                            // 谱面跨度 [3, 6]，中心 4.5
        var noteDisplayCenter = GameplayStageGeometry.MirroredDisplay(
            Track.Center, noteP + noteW / 2, true).Center;   // 显示中心 0.5
        var hitChart = GameplayStageGeometry.MirroredDisplay(
            Track.Center, noteDisplayCenter, true).Center;    // 反查回 4.5
        var originalChart = GameplayStageGeometry.MirroredDisplay(
            Track.Center, noteP + noteW / 2, true).Center;    // 点在原位置反查得到 0.5
        Check(hitChart >= noteP && hitChart <= noteP + noteW,
            "mirrored touch at the displayed center maps back inside the chart note bounds");
        Check(!(originalChart >= noteP && originalChart <= noteP + noteW),
            "touch at the un-mirrored position maps outside the chart note bounds with mirror on");

        // 侧轨：显示在右轨的触点必须反查成谱面 Left，且沿判定线坐标不变。
        var sideTouch = GameplayStageGeometry.MirroredDisplay(Track.Right, 2.5, true);
        Check(sideTouch.Track == Track.Left && Math.Abs(sideTouch.Center - 2.5) < .00001 &&
              GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, false).Track == Track.Left,
            "touch on the displayed right rail maps back to the chart Left track");

        var mappedLeft = GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, true);
        var mappedRight = GameplayStageGeometry.MirroredDisplay(Track.Right, 2.5, true);
        var mirroredLeft = GameplayStageGeometry.PositionAt(mappedLeft.Track, mappedLeft.Center, 100);
        var mirroredRight = GameplayStageGeometry.PositionAt(mappedRight.Track, mappedRight.Center, 100);
        Check(mappedLeft.Track == Track.Right && mappedRight.Track == Track.Left &&
              Math.Abs(mappedLeft.Center - 2.5) < .00001 && Math.Abs(mappedRight.Center - 2.5) < .00001,
            "side mirror swaps rails and leaves the along-rail coordinate untouched");
        Check(Math.Abs(mirroredLeft.X - 1661f) < .001f && Math.Abs(mirroredRight.X - 259f) < .001f &&
              Math.Abs(mirroredLeft.Y - 552.5f) < .001f && Math.Abs(mirroredRight.Y - 552.5f) < .001f &&
              GameplayStageGeometry.MirroredTrack(GameplayStageGeometry.MirroredTrack(Track.Left, true),
                  true) == Track.Left,
            "mirrored side notes land on the opposite rail at the same height and the swap is self-inverse");
        Check(GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, false).Track == Track.Left &&
              Math.Abs(GameplayStageGeometry.MirroredDisplay(Track.Left, 2.5, false).Center - 2.5) < .00001 &&
              Math.Abs(GameplayStageGeometry.LeftLineX + GameplayStageGeometry.RightLineX -
                  GameplayStageGeometry.DesignWidth) < .001f,
            "mirror off is the identity and the two rails are symmetric about the screen axis");
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

        // 原版 4 个 JudgeSettings 实例的精确换算（barTime × 240 / StandardBPM 150 = ×1.6）
        var casual = JudgeSettings.ForPreset(JudgePreset.Casual);
        var tutorial = JudgeSettings.ForPreset(JudgePreset.Tutorial);
        Check(Math.Abs(casual.PrefectSec - 0.100) < 1e-12, "Casual Prefect 窗 = ±100ms");
        Check(Math.Abs(casual.GreatSec - 0.150) < 1e-12, "Casual Great 窗 = ±150ms");
        Check(Math.Abs(casual.GoodSec - 0.200) < 1e-12, "Casual Good 窗 = ±200ms");
        Check(Math.Abs(casual.MissSec - 0.250) < 1e-12, "Casual Miss 窗 = ±250ms");
        Check(Math.Abs(tutorial.PrefectSec - casual.PrefectSec) < 1e-12 &&
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
        Check(Math.Abs(hc.PrefectSec - 0.03125) < 1e-12, "Hardcore Prefect 窗 = ±31.25ms");
        Check(Math.Abs(hc.GreatSec - 0.05625) < 1e-12, "Hardcore Great 窗 = ±56.25ms");
        Check(Math.Abs(hc.GoodSec - 0.08125) < 1e-12, "Hardcore Good 窗 = ±81.25ms");
        Check(Math.Abs(hc.MissSec - 0.125) < 1e-12, "Hardcore Miss 窗 = ±125ms");
        var hcEngine = new JudgeEngine(JudgePreset.Hardcore);
        Check(hcEngine.Judge(t, t + 0.030).Grade == JudgeGrade.Prefect, "Hardcore ±30ms -> Prefect");
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

	// 7. 同帧目标时间锁（原版 JudgeState 语义）：只锁早侧、同刻放行、晚侧放行、EX 豁免。
	private static void TestV2InputTimeLock()
	{
		Console.WriteLine("[7] V2 input time lock (early side only)");
		var gate = new V2InputProtection();

		// 未武装时早侧任意目标可判定。
		gate.Reset();
		Check(gate.CanUse(84_231, 83_981), "未武装时未来 Note 可判定");
		Check(gate.CanUse(84_231, 84_231), "未武装时同刻可判定");

		// 早侧命中武装锁，取本批最早目标；完全同刻继续放行。
		gate.CommitResolved(84_231, 83_981);
		Check(gate.Xm == 84_231, "早侧命中武装 Xm");
		Check(gate.CanUse(84_231, 83_981), "同刻多押放行");
		Check(gate.CanUse(84_231, 84_231), "同刻（Exact）多押放行");
		Check(!gate.CanUse(84_350, 83_981), "异时早侧目标被锁拦截");
		Check(gate.CanUse(84_231, 84_231, enforceProbability: false), "关闭概率保护后同刻仍放行");

		// H1 回归：晚侧候选既不检查锁，也被放行。
		Check(gate.CanUse(83_900, 84_000), "晚侧候选不检查锁（H1）");
		Check(gate.CanUse(500, 84_000), "远晚于输入的目标仍然放行");

		// 晚侧命中不武装锁。
		gate.Reset();
		gate.CommitResolved(83_900, 84_000);
		Check(gate.Xm == V2InputProtection.Infinity, "晚侧命中不武装时间锁");
		Check(gate.CanUse(84_231, 83_981), "晚侧结算后早侧目标不被锁（无武装）");

		// 武装值取最早目标，与事件到达顺序无关。
		gate.Reset();
		gate.CommitResolved(84_500, 83_981);
		gate.CommitResolved(84_231, 83_981);
		gate.CommitResolved(84_400, 83_981);
		Check(gate.Xm == 84_231, "本批锁 = 最早已结算目标（与顺序无关）");
		Check(!gate.CanUse(84_400, 83_981), "异时早侧仍被拦截");
		Check(gate.CanUse(83_500, 84_000), "晚侧不受最早目标影响");

		// 早侧 Miss 保护：被保护的未来目标可在同批继续使用，超出上限的早侧目标被拒。
		gate.Reset();
		gate.ProtectEarlyMiss(84_231, 83_981);
		Check(gate.ProbXm == 84_231, "早侧 Miss 记录保护上限");
		Check(gate.CanUse(84_231, 83_981), "受保护上限内的早侧目标可判定");
		Check(!gate.CanUse(84_400, 83_981), "超出保护上限的早侧目标被拒");
		Check(gate.CanUse(83_900, 84_000), "保护上限不影响晚侧候选");

		// EX-Tap（Burst）与 Tap 逐指令一致：同样参与时间锁（检查 + 命中武装）。
		gate.Reset();
		gate.CommitResolved(84_231, 83_981);
		Check(!gate.CanUse(84_900, 83_981), "EX 异时早侧目标被锁拦截");
		Check(gate.CanUse(84_231, 83_981), "EX 与已武装时刻同刻放行");
		Check(gate.Xm == 84_231, "EX 早侧命中武装锁");
		Check(gate.CanUse(83_900, 84_000), "EX 晚侧候选仍不受锁限制");

		// 毫秒换算保持原值的四舍五入语义。
		Check(V2InputProtection.ToMilliseconds(83.981) == 83_981, "秒 -> 毫秒换算");
	}

	// 7b. EX-Tap 二值判定（原版 Burst：窗内恒 Prefect，出窗即输入 Miss）。
	private static void TestExTapBinaryJudgement()
	{
		Console.WriteLine("[7b] EX-Tap binary judgement");
		var engine = new JudgeEngine(JudgePreset.Hard);
		var settings = engine.Settings;
		const double ex = JudgePlan.ExTapWindowScale;   // 与原版一致：1.0（同 Tap 窗）
		const double t = 10.0;
		var good = settings.GoodSec * ex;   // Hard: 0.1625
		var miss = settings.MissSec * ex;   // Hard: 0.25

		// 窗内（含 Great/Good 档位区间）一律 grade 5 = Prefect。
		foreach (var offset in new[] { 0.0, 0.05, -0.05, 0.10, -0.10, 0.16, -0.16 })
		{
			var r = engine.JudgePress(t, t + offset, ex, binaryExTap: true);
			Check(r.Grade == JudgeGrade.Prefect && r.Resolution == JudgeResolution.Prefect,
				$"EX 窗内 {offset:+0.00;-0.00}s -> Prefect（无 GR/GD 档）");
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
		Check(engine.JudgePress(t, t + 0.05).Grade == JudgeGrade.Prefect,
			"非二值路径 ±50ms 仍为 Prefect");
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

	private static void TestV2ExactBarTimeAndLegacyConversion()
	{
		Console.WriteLine("[12] v2 exact binary64 legacy conversion");
		var sevenTenths = ExactBarTime.FromDouble(0.7);
		Check(sevenTenths.ImproperNumerator == 3152519739159347 &&
			sevenTenths.Denominator == 4503599627370496,
			"ExactBarTime.FromDouble preserves the exact 0.7 binary64 rational");
		Check(sevenTenths.ToDouble() == 0.7, "exact binary64 rational round trips to 0.7");

		var head = TestNote(1, 2, NoteType.HoldHead, 0.7, -2.0, 7.0, 0.0);
		var tail = TestNote(2, -1, NoteType.HoldNode, 0.875, -1.0, 8.0, 0.0);
		var tap = TestNote(3, -1, NoteType.Tap, 0.75, 4.5, 2.0, 0.0,
			Track.Right);
		var legacy = new Chart
		{
			Name = "legacy",
			Title = "legacy",
			Difficulty = 3,
			TotalMainNote = 0,
			Sections = new[]
			{
				new BarSection { Bpm = 120.0, BarTime = 0.25, Seconds = 1.0 },
				new BarSection { Bpm = 240.0, BarTime = 1.25, Seconds = 3.0 },
			},
			NotesLeft = Array.Empty<Note>(),
			NotesCenter = new[] { head, tail },
			NotesRight = new[] { tap },
			DropSpeeds = new[] { (0.5, 2.0), (0.5, 3.0), (1.0, 4.0) },
		};
		var converted = V2Integration.ConvertLegacyChart(legacy, "legacy-chart");
		Check(converted.Bpms.Count == 3 && converted.Bpms[0].Time == ExactBarTime.Zero &&
			converted.Bpms[0].Bpm == 120.0,
			"legacy first BPM is extrapolated back to bar 0");
		Check(Math.Abs(converted.AudioOffsetSec - 0.5) < 1e-12,
			"legacy audio offset is extrapolated from first section");
		Check(converted.ScrollSpeeds.Count == 3 &&
			converted.ScrollSpeeds[0].Time == ExactBarTime.Zero &&
			converted.ScrollSpeeds[0].Value == 3.0 &&
			converted.ScrollSpeeds[1].Time == ExactBarTime.FromDouble(0.5) &&
			converted.ScrollSpeeds[1].Value == 3.0,
			"duplicate DropSpeed is later-wins and first event is prefixed to bar 0");
		var convertedHold = (V2PathNote)converted.NotesCenter.Single();
		Check(convertedHold.Time == sevenTenths,
			"in-memory converter uses exact binary64 BarTime authority");
		Check(convertedHold.Center == 1.5 && convertedHold.Width == 7.0 &&
			convertedHold.Nodes[0].Center == 3.0 && convertedHold.Nodes[0].Width == 8.0,
			"legacy Position to center retains overscan without clamping");

		// 回归：Hardcore 预设曾命中 "Tutorial cannot be converted to v2" 的 ArgumentException，
		// 导致 Hardcore 模式下全部 legacy 谱面加载失败。Hardcore 按 Hard 归档、Tutorial 按 Casual。
		var hcLoaded = V2Integration.ConvertLegacyPackageChart(legacy,
			"legacy.pack", "legacy-chart", JudgePreset.Hardcore);
		Check(hcLoaded.SemanticPack.Charts[0].Difficulty == V2Difficulty.Hard,
			"Hardcore 预设 legacy->v2 转换按 Hard 难度归档");
		var tutorialLoaded = V2Integration.ConvertLegacyPackageChart(legacy,
			"legacy.pack", "legacy-chart", JudgePreset.Tutorial);
		Check(tutorialLoaded.SemanticPack.Charts[0].Difficulty == V2Difficulty.Casual,
			"Tutorial 预设 legacy->v2 转换按 Casual 难度归档");
	}

	private static void TestV2GeometryJudgementAndSync()
	{
		Console.WriteLine("[13] v2 geometry, Hold judge, and sync derivation");
		var hold = new V2PathNote
		{
			Id = "hold",
			Type = V2NoteType.Hold,
			Time = Bar(1),
			Center = -2.0,
			Width = 7.0,
			CurveToNext = V2PathCurve.Linear,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "shape", Time = Bar(2), Center = 7.0, Width = 8.0,
					CurveToNext = V2PathCurve.Linear, Judge = false,
					JudgeWasExplicit = true,
				},
				new V2PathNode
				{
					Id = "tail", Time = Bar(3), Center = 2.0, Width = 1.0,
					Judge = true, JudgeWasExplicit = true,
				},
			},
		};
		var mixer = new V2PathNote
		{
			Id = "mixer",
			Type = V2NoteType.Mixer,
			Time = Bar(0),
			Center = -3.0,
			Width = 9.0,
			CurveToNext = V2PathCurve.Linear,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "mixer-tail", Time = ExactBarTime.FromFraction(3, 8),
					Center = 6.0, Width = 7.0,
				},
			},
		};
		var chart = new V2Chart
		{
			ChartId = "geometry",
			AudioOffsetSec = 0.0,
			Bpms = new[] { new V2BpmEvent(Bar(0), 120.0) },
			NotesLeft = new V2Note[]
			{
				new V2BasicNote
				{
					Id = "tap-left", Type = V2NoteType.Tap, Time = Bar(0),
					Center = 1.0, Width = 1.0,
				},
			},
			NotesCenter = new V2Note[] { mixer, hold },
			NotesRight = new V2Note[]
			{
				new V2BasicNote
				{
					Id = "tap-right-near", Type = V2NoteType.Tap,
					Time = ExactBarTime.FromFraction(1, 9),
					Center = 1.0, Width = 1.0,
				},
			},
		};
		V2SemanticValidator.ValidateChart(chart);
		Check(V2SemanticValidator.CountMainJudgements(chart) == 8,
			"Hold judge:false omits one unit while center Mixer retains exact ticks");
		var adapted = V2RuntimeAdapter.Adapt(chart);
		var plan = JudgePlan.Build(adapted.RuntimeChart,
			JudgeSettings.ForPreset(JudgePreset.Hard));
		Check(plan.Units.All(unit => adapted.Metadata.SourceIdsByRuntimeId[unit.NoteId] != "shape"),
			"Hold shape-only node is absent from runtime JudgePlan");
		Check(adapted.RuntimeChart.AllNotes.Count(note =>
			note.V2Metadata?.SourceId.Contains(".__shape.", StringComparison.Ordinal) == true) == 0,
			"linear paths add no hidden render subdivision and stay legacy-equivalent");
		var holdRuntime = adapted.RuntimeChart.NotesCenter.Single(note =>
			note.V2Metadata?.SourceId == "hold");
		Check(holdRuntime.Position == -5.5 && holdRuntime.Width == 7.0,
			"runtime adapter preserves overscan left edge and width");
		Check(adapted.SyncAccentRuntimeIds.SetEquals(new[]
		{
			adapted.RuntimeIdsBySourceId["tap-left"],
		}), "exact cross-track Mixer/Tap time accents only the Tap");
		Check(!adapted.SyncAccentRuntimeIds.Contains(
			adapted.RuntimeIdsBySourceId["tap-right-near"]),
			"near but unequal exact BarTime is not merged for sync accent");
	}

	private static void TestV2CurvesMixerAndDigest()
	{
		Console.WriteLine("[14] v2 curves, Mixer ticks, and golden digest");
		var path = new V2PathNote
		{
			Id = "curve",
			Type = V2NoteType.Hold,
			Time = Bar(0),
			Center = 0.0,
			Width = 1.0,
			CurveToNext = V2PathCurve.EaseInQuad,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "curve-tail", Time = Bar(1), Center = 4.0, Width = 3.0,
					Judge = true, JudgeWasExplicit = true,
				},
			},
		};
		var sample = new V2PathEvaluator(path).Evaluate(ExactBarTime.FromFraction(1, 2));
		Check(Math.Abs(sample.Center - 1.0) < 1e-12 &&
			Math.Abs(sample.Width - 1.5) < 1e-12,
			"easeInQuad path evaluates center and width at u^2");
		var curveChart = new V2Chart
		{
			ChartId = "curve-chart", AudioOffsetSec = 0.0,
			Bpms = new[] { new V2BpmEvent(Bar(0), 120.0) },
			NotesLeft = Array.Empty<V2Note>(),
			NotesCenter = new V2Note[] { path },
			NotesRight = Array.Empty<V2Note>(),
		};
		var curveRuntime = V2RuntimeAdapter.Adapt(curveChart).RuntimeChart;
		var hidden = curveRuntime.NotesCenter.Where(note =>
			note.V2Metadata?.SourceId.Contains(".__shape.", StringComparison.Ordinal) == true)
			.ToArray();
		Check(hidden.Length > 0 && hidden.All(note => note.V2Metadata?.HoldJudge == false),
			"curved body gets adaptive hidden render points with no Hold judgements");
		var midpoint = hidden.Single(note => note.V2Metadata!.ExactTime ==
			ExactBarTime.FromFraction(1, 2));
		Check(Math.Abs(midpoint.Position - 0.25) < 1e-12 &&
			Math.Abs(midpoint.Width - 1.5) < 1e-12,
			"hidden midpoint follows the authoritative eased center/width sample");
		var scroll = new V2ScrollMap(new[]
		{
			new V2ScrollEvent
			{
				Time = Bar(0), Value = 1.0, CurveToNext = V2ScrollCurve.EaseOutQuad,
			},
			new V2ScrollEvent { Time = Bar(1), Value = 3.0 },
		});
		Check(Math.Abs(scroll.SpeedAt(ExactBarTime.FromFraction(1, 2)) - 2.5) < 1e-12,
			"easeOutQuad scroll midpoint is exact known value");

		var mixer = new V2PathNote
		{
			Id = "ticks", Type = V2NoteType.Mixer, Time = ExactBarTime.FromFraction(1, 10),
			Center = 0.0, Width = 1.0, CurveToNext = V2PathCurve.Linear,
			Nodes = new[]
			{
				new V2PathNode
				{
					Id = "ticks-tail", Time = ExactBarTime.FromFraction(49, 100),
					Center = 0.0, Width = 1.0,
				},
			},
		};
		var ticks = V2MixerTicks.Enumerate(mixer).ToArray();
		Check(ticks.Length == 3 && ticks[0] == ExactBarTime.FromFraction(9, 40) &&
			ticks[2] == ExactBarTime.FromFraction(19, 40),
			"Mixer exact grid is head-relative k/8 and omits non-grid tail");

		var root = FindRepositoryRoot();
		var vectorPath = Path.Combine(root, "schemas", "chart-format-v2", "vectors",
			"gameplay-digest-v1.json");
		using var vector = System.Text.Json.JsonDocument.Parse(File.ReadAllText(vectorPath));
		var vectorRoot = vector.RootElement;
		var expected = vectorRoot.GetProperty("expectedSha256").GetString();
		var projection = vectorRoot.GetProperty("projection");
		var actual = V2GameplayDigest.Sha256Hex(
			System.Text.Encoding.UTF8.GetBytes(JsonCanonicalizer.Canonicalize(projection)));
		Check(actual == expected, "C# RFC8785 canonicalizer matches golden gameplay digest vector");
	}

	private static void TestD4CFrozenGrace()
	{
		Console.WriteLine("[15] D4-C frozen Hold grace");
		var settings = JudgeSettings.ForPreset(JudgePreset.Hard);
		var slow = SustainJudgementRules.BeginHoldContactLoss(10.0, 90.0, settings);
		Check(Math.Abs(slow.GraceDuration - 0.25) < 1e-12 &&
			Math.Abs(slow.Deadline - 10.25) < 1e-12,
			"loss BPM clamps to 120 and freezes a 250ms deadline");
		Check(!SustainJudgementRules.ShouldBreakHold(10.24, 12.0, slow) &&
			SustainJudgementRules.ShouldBreakHold(10.25, 12.0, slow),
			"Hold breaks exactly at the frozen deadline, independent of later BPM");
		Check(SustainJudgementRules.HoldSettlementTime(10.2, slow) == 10.2 &&
			SustainJudgementRules.HoldSettlementTime(12.0, slow) == 10.25,
			"D4-C settlement time is min(tail, deadline)");
		var fast = SustainJudgementRules.BeginHoldContactLoss(20.0, 240.0, settings);
		Check(Math.Abs(fast.GraceDuration - 0.15) < 1e-12,
			"loss BPM clamps to 200 and freezes a 150ms deadline");
	}

	private static void TestHoldReleaseFreezesGrace()
	{
		Console.WriteLine("[15b] Hold release freezes grace at first contact loss");
		var settings = JudgeSettings.ForPreset(JudgePreset.Hard);

		// A judged Hold head starts in steady contact.
		var contact = HoldContactState.Contact;
		Check(!contact.HasLoss, "successful Hold head starts with steady contact");

		// Releasing while overlapping before the tail must freeze the deadline.
		var released = contact.BeginLoss(10.0, 90.0, settings);
		Check(released.HasLoss && released.ContactLostAt == 10.0 &&
			released.GraceDeadline is { } deadline && Math.Abs(deadline - 10.25) < 1e-12,
			"hold release records the frozen grace deadline");

		// Later losses or BPM changes must not move the original deadline.
		var repeated = released.BeginLoss(11.0, 240.0, settings);
		Check(repeated == released, "later losses or BPM changes keep the original deadline");

		// Advancing beyond the frozen grace must break the Hold before its tail.
		var loss = new SustainJudgementRules.HoldContactLoss(released.ContactLostAt!.Value,
			released.GraceDeadline!.Value - released.ContactLostAt!.Value,
			released.GraceDeadline!.Value);
		Check(!SustainJudgementRules.ShouldBreakHold(10.24, 12.0, loss) &&
			SustainJudgementRules.ShouldBreakHold(10.25, 12.0, loss),
			"released Hold breaks at the frozen grace deadline");
	}

	private static void TestScoreStoreMigrationAndIdentity()
	{
		Console.WriteLine("[16] score migration and four-part identity");
		const string legacyJson = """
		{
		  "pack:giga": {
		    "score": 700000,
		    "acc": 70,
		    "maxCombo": 10,
		    "grade": "C",
		    "perfect": 7,
		    "great": 2,
		    "good": 1,
		    "miss": 0,
		    "date": "2026-01-01 00:00:00"
		  }
		}
		""";
		var store = new ScoreStoreCore();
		store.LoadJson(legacyJson);
		var identity = new ScoreIdentity("pack", "giga",
			V2Format.RulesetId, new string('a', 64));
		Check(store.GetLegacy("pack", "giga")?.Score == 700000,
			"legacy dictionary migrates into historical records");
		Check(store.Get(identity) == null,
			"legacy record never masquerades as current gameplay identity");
		var acceptedAt = new DateTime(2026, 8, 14, 12, 34, 56);
		Check(store.TryUpdate(identity, new ScoreRecord
		{
			Score = 800000, Acc = 91.0, MaxCombo = 20,
		}, acceptedAt), "new four-part score is accepted");
		Check(store.Get(identity)?.Grade == "A" &&
			store.Get(identity)?.Date == "2026-08-14 12:34:56",
			"accepted score resolves grade and stable timestamp");
		Check(!store.TryUpdate(identity, new ScoreRecord
		{
			Score = 799999, Acc = 100.0,
		}, acceptedAt), "lower score cannot overwrite current BEST");
		var changedDigest = new ScoreIdentity("pack", "giga",
			V2Format.RulesetId, new string('b', 64));
		Check(store.Get(changedDigest) == null,
			"gameplay digest change isolates BEST");
		var metadataOnly = new ScoreIdentity("pack", "giga",
			V2Format.RulesetId, new string('a', 64));
		Check(store.Get(metadataOnly)?.Score == 800000,
			"same four-part identity preserves BEST across metadata-only changes");

		var saved = store.SaveJson();
		Check(saved.Contains("\"formatVersion\": 2", StringComparison.Ordinal) &&
			!saved.Contains("\"version\"", StringComparison.Ordinal),
			"score codec writes formatVersion:2");
		var roundTrip = new ScoreStoreCore();
		roundTrip.LoadJson(saved);
		Check(roundTrip.Get(identity)?.Score == 800000 &&
			roundTrip.GetLegacy("pack", "giga")?.Score == 700000,
			"versioned round trip preserves current and historical records");
	}

	private static ExactBarTime Bar(long value) =>
		ExactBarTime.FromJsonComponents(value, 0, 1);

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
				return directory.FullName;
			directory = directory.Parent;
		}
		throw new DirectoryNotFoundException("Cannot find repository root from core-tests output.");
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

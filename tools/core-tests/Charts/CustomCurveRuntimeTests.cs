using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using static CoreTests.CoreTestSupport;

namespace CoreTests;

internal static class CustomCurveRuntimeTests
{
    private const string Swing = "t+8*t*(1-t)";
    private static readonly ExactBarTime Zero = ExactBarTime.Zero, One = ExactBarTime.One;

    public static void Run()
    {
        Console.WriteLine("[v2.1] Custom path/scroll integration, tempo split and real Mixer replay");
        var curve = CustomCurve.Compile(Swing);
        var map = new V2ScrollMap([
            new() { Time = Zero, Value = 5, CurveToNext = V2ScrollCurve.Custom, CustomExpr = Swing },
            new() { Time = One, Value = 4 }]);
        var note = Path();
        var evaluator = new V2PathEvaluator(note);
        var times = curve.Knots.Concat(Enumerable.Range(0, 97).Select(i => i / 96.0));
        Check(times.All(t =>
        {
            var exact = ExactBarTime.FromDouble(t);
            var p = curve.Evaluate(t);
            var point = evaluator.Evaluate(exact);
            return Near(point.Center, p) && Near(point.Width, 2 + .25 * p) &&
                point == evaluator.EvaluateRuntime(t) && Near(map.SpeedAt(exact), 5 - p) &&
                map.RuntimeSpeedAt(t) == map.SpeedAt(exact);
        }), "Custom exact/runtime scroll and path use the compiled polyline at knots and between them");
        Check(Near(map.MinimumSpeedFor(0), 5 - curve.Maximum) && map.MinimumSpeedFor(0) < 4 &&
            map.MinimumSpeedFor(1) == 4, "guide minimum includes legal downward scroll overshoot");
        var invalid = new V2PathEvaluator(note with { Width = 1, Nodes = [note.Nodes[0] with { Width = .4 }] });
        Check(!invalid.IsFiniteAndPositive(out var interval, out _) && interval == 0,
            "custom segment with positive endpoints but negative interior width is rejected");
        var badScroll = Chart(note) with { ScrollSpeeds = [new() { Time = Zero, Value = 2,
            CurveToNext = V2ScrollCurve.Custom, CustomExpr = Swing }, new() { Time = One, Value = 1 }] };
        var threw = false;
        try { V2SemanticValidator.ValidateChart(badScroll); }
        catch (V2DiagnosticException e) { threw = e.Message.Contains("/customExpr", StringComparison.Ordinal); }
        Check(threw, "invalid actual speed during overshoot is rejected at the expression");
        TestNonuniformKnots();
        TestLargeOrigin();
        TestMixer(note, curve, evaluator);
        TestLongHold();
        CustomHoldReplayTests.Run();
    }

    private static void TestNonuniformKnots()
    {
        const string formula = "1-(1-t)^1.5*cos(3*pi*t)";
        var curve = CustomCurve.Compile(formula);
        var chart = Chart(Path() with { CustomExpr = formula });
        var adapted = V2RuntimeAdapter.Adapt(chart);
        var points = adapted.RuntimeChart.AllNotes.Where(n => n.V2Metadata!.IsShapeSample).ToArray();
        var expected = curve.Knots.Skip(1).SkipLast(1).Select(ExactBarTime.FromDouble)
            .Append(chart.Bpms[1].Time).Distinct().Order().ToArray();
        Check(curve.Knots.Zip(curve.Knots.Skip(1), (a, b) => b - a).Distinct().Count() > 1 &&
            expected.SequenceEqual(points.Select(n => n.V2Metadata!.ExactTime)) &&
            points.All(n => Near(n.Position + n.Width * .5, curve.Evaluate(n.BarTime))),
            "asymmetric bounce retains its exact nonuniform polyline instead of resampling a uniform grid");
    }

    private static void TestLargeOrigin()
    {
        // The fractional endpoints round away in binary64 at this origin. A representable
        // interior bar must still use its true phase, not the difference of rounded endpoints.
        var origin = Bar(1L << 52);
        var start = origin + ExactBarTime.FromFraction(1, 4);
        var end = start + One * 2;
        var note = Path() with { Time = start, Nodes = [Path().Nodes[0] with { Time = end }] };
        var evaluator = new V2PathEvaluator(note);
        var map = new V2ScrollMap([new() { Time = start, Value = 5,
            CurveToNext = V2ScrollCurve.Custom, CustomExpr = Swing }, new() { Time = end, Value = 4 }]);
        Check(new[] { origin + One, origin + One * 2 }.All(bar =>
            evaluator.EvaluateRuntime(bar.ToDouble()) == evaluator.Evaluate(bar) &&
            map.RuntimeSpeedAt(bar.ToDouble()) == map.SpeedAt(bar)),
            "large-origin custom intervals retain the local phase, including a rounded endpoint collision");

        // Exactly two millibars apart on a multi-billion-bar timeline; endpoints of both
        // boundaries are integral in dyadic space, but not on the chart's 1/7-bar lattice.
        var far = Bar(1L << 32) + ExactBarTime.FromFraction(1, 7);
        var linear = note with { Time = far, CustomExpr = "t", Center = 0, Width = 1.0 / 512,
            Nodes = [note.Nodes[0] with { Time = far + One, Center = 1, Width = 1.0 / 512 }] };
        var small = new V2PathEvaluator(linear);
        var cuts = new List<double>();
        small.AddReceiverBoundaries(far.ToDouble(), (far + One).ToDouble(), .5, cuts);
        var lo = (far + ExactBarTime.FromFraction(511, 1024)).ToDouble();
        var hi = (far + ExactBarTime.FromFraction(513, 1024)).ToDouble();
        Check(cuts.Distinct().Order().SequenceEqual(new[] { lo, hi }),
            "narrow receiver entry and exit at a large fractional origin retain both rounded crossings");
    }

    private static void TestMixer(V2PathNote note, CustomCurve curve, V2PathEvaluator evaluator)
    {
        var chart = Chart(note);
        var adapted = V2RuntimeAdapter.Adapt(chart);
        var clock = adapted.Metadata.BpmTimeline;
        var plan = JudgePlan.Build(adapted.RuntimeChart, JudgeSettings.ForPreset(JudgePreset.Hard));
        var path = plan.Sustains.Values.Single();
        var extras = path.Nodes.Where(n => n.V2Metadata?.IsShapeSample == true).ToArray();
        var wanted = curve.Knots.Skip(1).SkipLast(1).Select(ExactBarTime.FromDouble)
            .Append(chart.Bpms[1].Time).Distinct().Order().ToArray();
        Check(extras.Select(n => n.V2Metadata!.ExactTime).SequenceEqual(wanted) &&
            extras.All(n => !adapted.RuntimeIdsBySourceId.ContainsKey(n.V2Metadata!.SourceId)) && plan.Units.Count == 9,
            "render-only samples follow actual knots plus tempo break, never authored nodes or Mixer ticks");
        Check(path.Nodes.Zip(path.Nodes.Skip(1)).All(edge =>
        {
            var mid = edge.First.Second + (edge.Second.Second - edge.First.Second) * .5;
            var shape = evaluator.EvaluateRuntime(clock.RuntimeBarAt(mid));
            return Near(shape.Left, (edge.First.Position + edge.Second.Position) * .5) &&
                Near(shape.Width, (edge.First.Width + edge.Second.Width) * .5);
        }), "every displayed segment agrees with judging in audio seconds across a non-knot BPM change");

        var cuts = new List<double>();
        MixerContactGeometry.AddBoundaries(path, path.StartTime, path.EndTime, 0, cuts);
        Check(cuts.Distinct().Count() >= 2 && cuts.Any(s => clock.RuntimeBarAt(s) > .5),
            "stationary Mixer receiver has distinct exit/reentry roots across the tempo change");
        var peak = curve.Knots.MaxBy(t => evaluator.EvaluateRuntime(t).Right);
        var tangent = evaluator.EvaluateRuntime(peak).Right;
        var grazes = new List<double>();
        evaluator.AddReceiverBoundaries(0, 1, tangent, grazes);
        Check(grazes.Any(x => Near(x, peak)), "isolated contact at a turning knot survives with no sign change");
        var ticks = V2MixerTicks.Enumerate(note).Select(clock.ToSeconds).ToArray();
        var coarse = Replay([path.EndTime]);
        var fine = Replay(Enumerable.Range(1, 120).Select(i => path.EndTime * i / 120.0).Append(path.EndTime));
        Check(coarse.Length == 8 && coarse.Contains(JudgeGrade.Miss) && coarse[^1] == JudgeGrade.Perfect &&
            coarse.SequenceEqual(fine), "real Mixer multiple crossings, grace and re-entry give identical coarse/fine grades");

        JudgeGrade[] Replay(IEnumerable<double> frames)
        {
            var mixer = new MixerRuntime(path, clock.RuntimeBarAt);
            var grades = new List<JudgeGrade>();
            var previous = double.NegativeInfinity;
            foreach (var frame in frames)
            {
                mixer.AdvanceFrame(frame, [], previous < 0 ? [new(0, 1, 0), new(0, 1, null)] : [],
                    ticks.Where(t => previous < t && t <= frame).ToArray(), (_, g) => grades.Add(g));
                previous = frame;
            }
            return grades.ToArray();
        }
    }

    private static void TestLongHold()
    {
        const int count = 5002;
        var nodes = Enumerable.Range(1, count).Select(i => new V2PathNode {
            Id = $"n{i}", Time = ExactBarTime.FromFraction(i, count), Center = i % 2, Width = 1,
            Judge = i == count, JudgeWasExplicit = true, CurveToNext = i == count ? null : V2PathCurve.Hold }).ToArray();
        var hold = Path() with { Type = V2NoteType.Hold, CustomExpr = "t", Nodes = nodes };
        var runtime = V2RuntimeAdapter.Adapt(Chart(hold)).RuntimeChart;
        var plan = JudgePlan.Build(runtime, JudgeSettings.ForPreset(JudgePreset.Hard));
        var path = plan.Sustains.Values.Single();
        Check(path.Nodes.Count > 10000 && path.End.V2Metadata?.SourceId == nodes[^1].Id && plan.Units.Count == 2,
            "a valid v2.1 Hold past 10,000 flattened links reaches its authored scored tail without extra judgements");
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-10;
    private static V2PathNote Path() => new() { Id = "m", Type = V2NoteType.Mixer, Center = 0, Width = 2,
        Time = Zero, CurveToNext = V2PathCurve.Custom, CustomExpr = Swing,
        Nodes = [new() { Id = "tail", Time = One, Center = 1, Width = 2.25 }] };
    private static V2Chart Chart(V2PathNote path) => new() { ChartId = "custom-runtime", FormatMinor = 1,
        AudioOffsetSec = 0, Bpms = [new(Zero, 150), new(ExactBarTime.FromFraction(1, 3), 300)],
        NotesLeft = [], NotesCenter = [path], NotesRight = [] };
}

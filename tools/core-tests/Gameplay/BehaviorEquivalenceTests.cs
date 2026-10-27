using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;

namespace CoreTests;

public static class BehaviorEquivalenceTests
{
    public static void Run(Action<bool, string> check)
    {
        TestTimeBudget(check);
        TestPchipCache(check);
        TestPressScan(check);
        TestVisualPoolPlanning(check);
        TestStealablePool(check);
    }

    private static void TestVisualPoolPlanning(Action<bool, string> check)
    {
        Console.WriteLine("[17e] Visual pool peak planning equivalence");
        var uniform = Enumerable.Range(0, 20).Select(i => i * 0.1).ToArray();
        var uniformNotes = uniform.Select(t => new VisualPoolPlanning.ActivityInterval(t, t + 0.5));
        var uniformSustains = new[]
        {
            new VisualPoolPlanning.ActivityInterval(0.0, 0.8),
            new VisualPoolPlanning.ActivityInterval(0.4, 1.2),
        };
        var planned = VisualPoolPlanning.Analyze(uniform, uniformNotes, uniformSustains, 0.5);
        check(planned.PeakHits == NaivePeakWindow(uniform, 0.5), "uniform peak hits equals naive");
        check(planned.PeakNotes == NaivePeakIntervals(uniformNotes), "uniform peak notes equals naive");
        check(planned.PeakSustains == NaivePeakIntervals(uniformSustains), "sustain peak equals naive");

        var spike = Enumerable.Repeat(3.0, 50).ToArray();
        var spikePlan = VisualPoolPlanning.Analyze(spike, [], [], 0.5);
        check(spikePlan.PeakHits == 50, "same-frame 50-hit spike is preserved");
        check(spikePlan.HitCapacity == VisualPoolPlanning.PoolMaximumCapacity,
            "peak hit capacity clamps to maximum");

        var longTailNotes = new[]
        {
            new VisualPoolPlanning.ActivityInterval(0.0, 3.0),
            new VisualPoolPlanning.ActivityInterval(0.5, 4.0),
            new VisualPoolPlanning.ActivityInterval(1.0, 5.0),
        };
        var longTailSustains = new[]
        {
            new VisualPoolPlanning.ActivityInterval(0.0, 10.0),
            new VisualPoolPlanning.ActivityInterval(2.0, 8.0),
            new VisualPoolPlanning.ActivityInterval(4.0, 12.0),
        };
        var mixedTimes = uniform.Concat(new[] { 2.5, 2.5, 2.5 }).ToArray();
        var mixedPlan = VisualPoolPlanning.Analyze(mixedTimes, longTailNotes, longTailSustains, 0.5);
        check(mixedPlan.PeakHits == NaivePeakWindow(mixedTimes.OrderBy(x => x).ToArray(), 0.5),
            "long-tail mixed peak hits equals naive");
        check(mixedPlan.PeakNotes == NaivePeakIntervals(longTailNotes),
            "long-tail mixed peak notes equals naive");
        check(mixedPlan.PeakSustains == NaivePeakIntervals(longTailSustains),
            "long-tail mixed peak sustains equals naive");
        check(VisualPoolPlanning.ClampCapacity(0) == 8, "capacity lower clamp");
        check(VisualPoolPlanning.ClampCapacity(7) == 8, "capacity lower boundary");
        check(VisualPoolPlanning.ClampCapacity(12) == 12, "capacity interior preserved");
        check(VisualPoolPlanning.ClampCapacity(50) == 32, "capacity upper clamp");
    }

    private static void TestStealablePool(Action<bool, string> check)
    {
        Console.WriteLine("[17f] Stealable pool sequence and reset");
        var reset = new List<int>();
        var next = 0;
        var pool = new StealablePool<TestPoolItem>(2,
            () => new TestPoolItem { Id = next++ }, item =>
            {
                item.State = 0;
                reset.Add(item.Id);
            });
        var first = pool.Acquire();
        first.State = 11;
        var second = pool.Acquire();
        second.State = 22;
        var stolen = pool.Acquire();
        check(ReferenceEquals(stolen, first), "full pool steals oldest item");
        check(stolen.State == 0, "stolen item is reset before reuse");
        check(pool.InUseCount == 2, "steal keeps hard capacity");
        check(pool.Release(stolen), "release returns borrowed item");
        check(stolen.State == 0, "release resets item state");
        check(reset.Count >= 2, "reset callback runs on steal and release");
    }

    private sealed class TestPoolItem
    {
        public int Id { get; init; }
        public int State { get; set; }
    }

    private static int NaivePeakWindow(IReadOnlyList<double> values, double window)
    {
        var peak = 0;
        for (var i = 0; i < values.Count; i++)
        {
            var count = values.Count(t => t >= values[i] && t <= values[i] + window);
            peak = Math.Max(peak, count);
        }
        return peak;
    }

    private static int NaivePeakIntervals(IEnumerable<VisualPoolPlanning.ActivityInterval> source)
    {
        var intervals = source.ToArray();
        if (intervals.Length == 0) return 0;
        var points = intervals.SelectMany(i => new[] { i.Start, i.End }).Distinct().OrderBy(x => x);
        return points.Max(point => intervals.Count(i => i.Start <= point && point <= i.End));
    }

    private static void TestTimeBudget(Action<bool, string> check)
    {
        Console.WriteLine("[17a] Runtime time/scroll budget equivalence");
        var timeline = new V2BpmTimeline(new[]
        {
            new V2BpmEvent(Bar(0), 120.0),
            new V2BpmEvent(Bar(2), 180.0),
            new V2BpmEvent(Bar(5), 97.5),
        }, -0.1375);
        var notes = new[] { Bar(0), Fraction(1, 2), Bar(2), Fraction(9, 4), Bar(5), Fraction(23, 4) };
        foreach (var note in notes)
        {
            var exact = timeline.ToSeconds(note);
            var budget = timeline.RuntimeSecondsAt(note);
            check(Math.Abs(exact - budget) <= 1e-9,
                $"runtime seconds equals exact conversion at {note}");
        }

        var scroll = new V2ScrollMap(new[]
        {
            new V2ScrollEvent { Time = Bar(0), Value = 0.5, CurveToNext = V2ScrollCurve.Linear },
            new V2ScrollEvent { Time = Bar(2), Value = 2.5, CurveToNext = V2ScrollCurve.EaseInOutCubic },
            new V2ScrollEvent { Time = Bar(5), Value = 1.0 },
        });
        foreach (var bar in notes)
        {
            var exactSpeed = scroll.SpeedAt(bar);
            var runtimeSpeed = scroll.RuntimeSpeedAt(bar.ToDouble());
            check(Math.Abs(exactSpeed - runtimeSpeed) <= 1e-9,
                $"runtime scroll equals exact interpolation at {bar}");
        }
        foreach (var second in notes.Select(t => timeline.ToSeconds(t)))
        {
            var exactBar = timeline.ToBarTime(second).ToDouble();
            var runtimeBar = timeline.RuntimeBarAt(second);
            check(Math.Abs(exactBar - runtimeBar) <= 1e-9,
                $"runtime bar inverse equals exact conversion at {second:R}");
        }
    }

    private static void TestPchipCache(Action<bool, string> check)
    {
        Console.WriteLine("[17b] Cached PCHIP equivalence");
        var path = new V2PathNote
        {
            Id = "smooth-cache", Type = V2NoteType.Hold, Time = Bar(0),
            Center = 0.0, Width = 1.0, CurveToNext = V2PathCurve.Smooth,
            Nodes = new[]
            {
                Node("n1", Fraction(1, 2), 2.5, 1.8, V2PathCurve.Smooth),
                Node("n2", Bar(1), -1.0, 2.2, V2PathCurve.Smooth),
                Node("n3", Fraction(3, 2), 4.0, 1.4, null),
            },
        };
        var evaluator = new V2PathEvaluator(path);
        var center = ReferenceSlopes(new[] { 0.0, 2.5, -1.0, 4.0 }, new[] { 0.0, 0.5, 1.0, 1.5 });
        var width = ReferenceSlopes(new[] { 1.0, 1.8, 2.2, 1.4 }, new[] { 0.0, 0.5, 1.0, 1.5 });
        var cachedCenter = evaluator.CenterSlopesFor(0);
        var cachedWidth = evaluator.WidthSlopesFor(0);
        check(cachedCenter.Count == center.Length && cachedWidth.Count == width.Length,
            "cached PCHIP slope lengths");
        for (var i = 0; i < center.Length; i++)
        {
            check(cachedCenter[i] == center[i], $"cached center slope[{i}] equals reference");
            check(cachedWidth[i] == width[i], $"cached width slope[{i}] equals reference");
        }
        foreach (var u in new[] { 0.1, 0.25, 0.49, 0.75, 1.1, 1.4 })
        {
            var time = ExactBarTime.FromDouble(u);
            var interval = u < 0.5 ? 0 : u < 1.0 ? 1 : 2;
            var local = (u - interval * 0.5) / 0.5;
            var expectedCenter = Hermite(
                new[] { 0.0, 2.5, -1.0 }[interval],
                new[] { 2.5, -1.0, 4.0 }[interval],
                center[interval], center[interval + 1], 0.5, local);
            var expectedWidth = Hermite(
                new[] { 1.0, 1.8, 2.2 }[interval],
                new[] { 1.8, 2.2, 1.4 }[interval],
                width[interval], width[interval + 1], 0.5, local);
            var actual = evaluator.Evaluate(time);
            check(Math.Abs(actual.Center - expectedCenter) <= 1e-12 &&
                  Math.Abs(actual.Width - expectedWidth) <= 1e-12,
                $"smooth Evaluate equals reference at {u:R}");
        }
    }

    private readonly record struct PressCandidate(int Id, Track Track, double Time,
        double Position, UnitKind Kind, bool ExTap);
    private readonly record struct PressResult(int Id, Track Track, double NoteTime, JudgeGrade Grade);

    private static void TestPressScan(Action<bool, string> check)
    {
        Console.WriteLine("[17c] Single-pass bucket scan equivalence");
        var protection = new V2InputProtection();
        var candidates = new[]
        {
            new PressCandidate(1, Track.Center, 10.000, 1.0, UnitKind.Input, false),
            new PressCandidate(2, Track.Center, 10.000, 1.4, UnitKind.Input, false),
            new PressCandidate(3, Track.Left, 10.000, 1.0, UnitKind.Input, false),
            new PressCandidate(4, Track.Right, 10.041, 1.0, UnitKind.Input, true),
            new PressCandidate(5, Track.Center, 10.162, 1.0, UnitKind.Input, false),
            new PressCandidate(6, Track.Center, 10.250, 1.0, UnitKind.Mine, false),
        };
        var presses = new[]
        {
            (Track.Center, 1.0, 9.850), // early boundary / same-ms chord
            (Track.Left, 1.0, 10.000),  // exact on another track
            (Track.Right, 1.0, 10.041), // exact EX-Tap
            (Track.Center, 1.0, 10.251), // late candidates do not lock
        };
        var reference = RunPresses(candidates, presses, doubleScan: true);
        var optimized = RunPresses(candidates, presses, doubleScan: false);
        check(reference.SequenceEqual(optimized),
            "single-pass bucket scan matches naive double scan sequence");
        check(reference.Count(item => item.Track == Track.Center) >= 2 &&
              reference.Any(item => item.Track == Track.Left),
            "dense scan covers same-track chord and multi-track exact cases");
        check(reference.SequenceEqual(optimized),
            "early lock and late candidates preserve the reference result sequence");

        var sameBarTimeline = new V2BpmTimeline(new[] { new V2BpmEvent(Bar(0), 120.0) }, 0.0);
        var sameBarSeconds = new[] { Bar(1), Bar(1), Bar(1) }
            .Select(sameBarTimeline.ToSeconds).ToArray();
        check(sameBarSeconds[0] == sameBarSeconds[1] && sameBarSeconds[1] == sameBarSeconds[2],
            "v2 same exact BarTime converts to bit-equal seconds for chord lock");

        var mineFirst = RunPresses(
            new[]
            {
                new PressCandidate(10, Track.Center, 10.020, 1.0, UnitKind.Mine, false),
                new PressCandidate(11, Track.Center, 10.040, 1.0, UnitKind.Input, false),
            },
            new[] { (Track.Center, 1.0, 10.020) }, doubleScan: false);
        check(mineFirst.Any(item => item.Id == 10) && mineFirst.Any(item => item.Id == 11),
            "Mine hit does not arm the lock against a different-time legal note");

        var noteFirst = RunPresses(
            new[]
            {
                new PressCandidate(12, Track.Center, 10.000, 1.0, UnitKind.Input, false),
                new PressCandidate(13, Track.Center, 10.020, 1.0, UnitKind.Mine, false),
            },
            new[] { (Track.Center, 1.0, 10.020) }, doubleScan: false);
        check(noteFirst.Any(item => item.Id == 12) && noteFirst.Any(item => item.Id == 13),
            "Mine remains observable after a different-time legal note locks the batch");
    }

    private static IReadOnlyList<PressResult> RunPresses(
        IReadOnlyList<PressCandidate> candidates,
        IReadOnlyList<(Track Track, double Position, double Time)> presses,
        bool doubleScan)
    {
        var settings = JudgeSettings.ForPreset(JudgePreset.Hard);
        var engine = new JudgeEngine(JudgePreset.Hard, candidates.Count);
        var protection = new V2InputProtection();
        protection.Reset();
        var result = new List<PressResult>();
        var judged = new HashSet<int>();
        foreach (var press in presses)
        {
            var bucket = candidates.Where(c => c.Track == press.Track).ToArray();
            if (doubleScan)
            {
                foreach (var candidate in bucket)
                    ResolvePressCandidate(candidate, press, engine, protection, result, apply: false, settings, judged);
                foreach (var candidate in bucket)
                    ResolvePressCandidate(candidate, press, engine, protection, result, apply: true, settings, judged);
            }
            else
            {
                foreach (var candidate in bucket)
                    ResolvePressCandidate(candidate, press, engine, protection, result, apply: true, settings, judged);
            }
        }
        return result;
    }

    private static void ResolvePressCandidate(PressCandidate candidate,
        (Track Track, double Position, double Time) press, JudgeEngine engine,
        V2InputProtection protection, List<PressResult> result, bool apply,
        JudgeSettings settings, HashSet<int> judged)
    {
        var miss = settings.MissSec;
        if (judged.Contains(candidate.Id) ||
            Math.Abs(candidate.Time - press.Time) > miss ||
            Math.Abs(candidate.Position - press.Position) > 1.2)
            return;
        if (candidate.Kind != UnitKind.Mine && !protection.CanUse(candidate.Time))
            return;
        if (candidate.Kind == UnitKind.Mine)
        {
            if (apply)
            {
                result.Add(new PressResult(candidate.Id, candidate.Track, candidate.Time, JudgeGrade.Miss));
                judged.Add(candidate.Id);
            }
            return;
        }
        var judgement = engine.JudgePress(candidate.Time, press.Time, 1.0, candidate.ExTap);
        if (judgement.Resolution == JudgeResolution.Pending)
            return;
        protection.CommitResolved(candidate.Time);
        if (apply)
        {
            result.Add(new PressResult(candidate.Id, candidate.Track, candidate.Time, judgement.Grade));
            judged.Add(candidate.Id);
        }
    }

    private static double[] ReferenceSlopes(double[] values, double[] times)
    {
        var h = new double[values.Length - 1];
        var d = new double[h.Length];
        for (var i = 0; i < h.Length; i++)
        {
            h[i] = times[i + 1] - times[i];
            d[i] = (values[i + 1] - values[i]) / h[i];
        }
        var m = new double[values.Length];
        m[0] = EndpointSlope(h[0], h[1], d[0], d[1]);
        for (var i = 1; i < values.Length - 1; i++)
        {
            if (d[i - 1] == 0.0 || d[i] == 0.0 || Math.Sign(d[i - 1]) != Math.Sign(d[i]))
                m[i] = 0.0;
            else
            {
                var w1 = 2.0 * h[i] + h[i - 1];
                var w2 = h[i] + 2.0 * h[i - 1];
                m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
        }
        m[^1] = EndpointSlope(h[^1], h[^2], d[^1], d[^2]);
        return m;
    }

    private static double EndpointSlope(double h0, double h1, double d0, double d1)
    {
        var slope = ((2.0 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
        if (Math.Sign(slope) != Math.Sign(d0)) return 0.0;
        if (Math.Sign(d0) != Math.Sign(d1) && Math.Abs(slope) > Math.Abs(3.0 * d0))
            return 3.0 * d0;
        return slope;
    }

    private static double Hermite(double y0, double y1, double m0, double m1,
        double h, double u)
    {
        var u2 = u * u;
        var u3 = u2 * u;
        return (2.0 * u3 - 3.0 * u2 + 1.0) * y0 +
               (u3 - 2.0 * u2 + u) * h * m0 +
               (-2.0 * u3 + 3.0 * u2) * y1 +
               (u3 - u2) * h * m1;
    }

    private static ExactBarTime Bar(int value) => ExactBarTime.FromFraction(value, 1);
    private static ExactBarTime Fraction(int numerator, int denominator) =>
        ExactBarTime.FromFraction(numerator, denominator);
    private static V2PathNode Node(string id, ExactBarTime time, double center,
        double width, V2PathCurve? curve) => new()
    {
        Id = id, Time = time, Center = center, Width = width,
        CurveToNext = curve, Judge = true, JudgeWasExplicit = true,
    };
}

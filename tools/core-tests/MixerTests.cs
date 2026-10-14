using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;

namespace CoreTests;

public static class MixerTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("[20] Placed Mixer receiver, musical grace and timestamp replay");
        TestClockRegression(check);
        var flat = Path((0, 0.0), (2, 0.0));
        var mixer = new MixerRuntime(flat, t => t / 1.6);
        var grades = new List<JudgeGrade>();
        mixer.AdvanceFrame(1, [], [new(0, 1, 0), new(.01, 1, null),
            new(.2, 2, .4), new(.21, 2, null), new(.3, 3, 4), new(.31, 3, null)],
            [.125, .25, .5, 1], (_, g) => grades.Add(g));
        check(mixer.Placed && mixer.Position == .4 && mixer.Controller == null && mixer.Catching,
            "short click relocates; release preserves head; invalid normal click does not clamp or move it");
        check(grades.All(g => g == JudgeGrade.Perfect), "parked receiver scores without any held fingers");
        check(InputJudgeRules.Overlaps(new NoteBounds(.3, .5), .4, .4),
            "same placement click remains eligible for ordinary note overlap");
        mixer.ClearControls();
        mixer.AdvanceFrame(1.5, [], [], [1.5], (_, g) => check(g == JudgeGrade.Perfect,
            "focus/pause control cleanup preserves parked reception"));

        var moving = Path((0, 0.0), (2, 2.0)); // radius .5; exit t=.5
        mixer = new MixerRuntime(moving, t => t / 1.6);
        grades.Clear();
        mixer.AdvanceFrame(1, [], [new(0, 1, 0), new(.01, 1, null)],
            [.49, .6, .699, .7, .8], (_, g) => grades.Add(g));
        check(grades.SequenceEqual(new[] { JudgeGrade.Perfect, JudgeGrade.Perfect,
            JudgeGrade.Perfect, JudgeGrade.Miss, JudgeGrade.Miss }),
            "loss starts at geometric crossing, grace <1/8 bar; exact deadline fails");
        check(Math.Abs(mixer.LossBar!.Value - .3125) < 1e-9,
            "coarse frame finds exact loss rather than frame-end loss");
        mixer.AdvanceFrame(1.1, [], [new(1.1, 2, 1.1)], [1.1], (_, g) =>
            check(g == JudgeGrade.Perfect, "valid input at tick restores after expiry; misses are not retroactive"));

        mixer = new MixerRuntime(flat, t => t / 1.6);
        mixer.AdvanceFrame(.2, [], [new(0, 1, 0), new(.1, 1, 3, ContactPhase.Moved)], [], (_, _) => { });
        check(mixer.Position == 0 && mixer.Catching, "invalid drag retains last valid head position");
        mixer.AdvanceFrame(.3, [new(1, Track.Center, 3, ContactPhase.Stationary)],
            [new(.3, 1, .25, ContactPhase.Moved)], [], (_, _) => { });
        check(mixer.Position == .25, "controlling finger can return and move receiver");
        mixer.AdvanceFrame(.4, [new(1, Track.Center, .25, ContactPhase.Stationary)],
            [new(.35, 2, -.25), new(.36, 1, .4, ContactPhase.Moved)], [], (_, _) => { });
        check(mixer.Position == -.25 && mixer.Controller == 2,
            "new valid click takes control; old controller cannot steal by moving while receiver catches");
        mixer = new MixerRuntime(flat, t => t / 1.6);
        mixer.AdvanceFrame(.5, [], [new(.1, 99, 0, ContactPhase.Moved)], [.5], (_, g) =>
            check(g == JudgeGrade.Miss, "unknown Move cannot create a placed receiver"));

        var relay = new[] { new MixerInput(0, 1, 0), new MixerInput(0, 2, 1), new MixerInput(0, 3, 2) };
        foreach (var frames in new[] { new[] { 2.0 }, Enumerable.Range(1, 240).Select(i => i / 120.0).ToArray() })
        {
            var (runtime, result) = Replay(moving, relay, frames, [.25, .5, .75, 1, 1.5, 2]);
            check(result.All(g => g == JudgeGrade.Perfect) && runtime.Position == 2,
                "stationary multi-finger relay works independently of render frame size");
        }
        var events = new[] { new MixerInput(0, 1, 0), new MixerInput(.01, 1, null),
            new MixerInput(.7, 2, .7), new MixerInput(.701, 2, null),
            new MixerInput(.9, 3, 8), new MixerInput(.91, 3, null) };
        var ticks = new[] { .2, .4, .6, .7, .8, 1.2, 1.4, 1.6, 2.0 };
        var baseline = Replay(moving, events, [2], ticks);
        foreach (var fps in new[] { 30, 60, 144 })
        {
            var result = Replay(moving, events, Enumerable.Range(1, fps * 2).Select(i => (double)i / fps).ToArray(), ticks);
            check(result.Grades.SequenceEqual(baseline.Grades) && result.Runtime.Position == baseline.Runtime.Position,
                $"{fps}fps matches one coarse frame including input/deadline/tick ordering");
        }

        var excursion = Path((0, 0.0), (.4, 0.0), (.5, 2.0), (.6, 0.0), (1, 0.0));
        var excursionResult = Replay(excursion, [new(0, 1, 0), new(.01, 1, null)], [1], [.5, .8]);
        check(excursionResult.Grades.All(g => g == JudgeGrade.Perfect) && excursionResult.Runtime.LossBar == null,
            "whole exit and reentry within one frame is found and short loss is forgiven");

        // Independent receivers share the same physical input, not a consumed pointer.
        foreach (var track in Enum.GetValues<Track>())
        {
            var side = new SustainPath { Kind = SustainKind.Mixer, Track = track,
                HeadId = flat.HeadId, Nodes = flat.Nodes };
            var one = Replay(side, [new(0, 1, .3), new(.01, 1, null)], [1], [.5, 1]);
            var two = Replay(side, [new(0, 1, .3), new(.01, 1, null)], [1], [.5, 1]);
            check(one.Grades.SequenceEqual(two.Grades) && one.Grades.All(g => g == JudgeGrade.Perfect),
                $"{track}: independent simultaneous paths may share placement input");
        }

        foreach (var curve in Enum.GetValues<V2PathCurve>())
        {
            var source = new V2PathNote
            {
                Id = "m", Type = V2NoteType.Mixer, Time = ExactBarTime.Zero, Center = 0, Width = 1,
                CurveToNext = curve,
                Nodes = [new V2PathNode { Id = "n", Time = ExactBarTime.FromDouble(1), Center = 2, Width = 1 }],
            };
            var bpm = new V2BpmTimeline([new V2BpmEvent(ExactBarTime.Zero, 150)], 0);
            var path = new SustainPath { Kind = SustainKind.Mixer, Track = Track.Center,
                HeadId = 1, Nodes = Path((0, 0.0), (1.6, 2.0)).Nodes,
                V2Evaluator = new V2PathEvaluator(source), V2BpmTimeline = bpm };
            var coarse = Replay(path, [new(0, 1, 0), new(.01, 1, null)], [1.6], [.2, .4, .6, .8, 1, 1.4, 1.6]);
            var fine = Replay(path, [new(0, 1, 0), new(.01, 1, null)],
                Enumerable.Range(1, 160).Select(i => i / 100.0).ToArray(), [.2, .4, .6, .8, 1, 1.4, 1.6]);
            check(coarse.Grades.SequenceEqual(fine.Grades), $"{curve}: curved/jump boundaries do not depend on frames");
        }

        // Smooth center and width can have opposing derivatives: both ends outside
        // does not imply the whole interval is outside. Check every polynomial cell.
        var smoothSource = new V2PathNote
        {
            Id = "smooth", Type = V2NoteType.Mixer, Time = ExactBarTime.Zero,
            Center = 0, Width = 1, CurveToNext = V2PathCurve.Smooth,
            Nodes = [new V2PathNode { Id = "a", Time = ExactBarTime.FromDouble(.2), Center = 2, Width = .4,
                CurveToNext = V2PathCurve.Smooth },
                new V2PathNode { Id = "b", Time = ExactBarTime.FromDouble(.4), Center = 0, Width = 2,
                    CurveToNext = V2PathCurve.Smooth },
                new V2PathNode { Id = "c", Time = ExactBarTime.FromDouble(1), Center = 3, Width = .8 }],
        };
        var evaluator = new V2PathEvaluator(smoothSource);
        foreach (var position in new[] { -.25, .5, 1.0, 1.5, 2.5 })
        {
            var cuts = new List<double> { 0, 1 };
            evaluator.AddReceiverBoundaries(0, 1, position, cuts);
            cuts.Sort();
            var constant = true;
            for (var i = 1; i < cuts.Count; i++)
            {
                if (cuts[i] - cuts[i - 1] < 1e-10) continue;
                bool At(double u)
                {
                    var sample = evaluator.EvaluateRuntime(cuts[i - 1] + (cuts[i] - cuts[i - 1]) * u);
                    return MixerContactGeometry.CanCatch(new NoteBounds(sample.Left, sample.Right), position);
                }
                var expected = At(.5);
                for (var j = 1; j < 100; j++) constant &= At(j / 100.0) == expected;
            }
            check(constant, "PCHIP variable-width roots partition every contact transition");
        }

        // At .5s loss begins; tempo doubles at .6s. Remaining musical grace ends at .65s.
        mixer = new MixerRuntime(moving, t => t <= .6 ? t / 1.6 : .375 + (t - .6) / .8);
        grades.Clear();
        mixer.AdvanceFrame(1, [], [new(0, 1, 0), new(.01, 1, null)], [.64, .65], (_, g) => grades.Add(g));
        check(grades.SequenceEqual(new[] { JudgeGrade.Perfect, JudgeGrade.Miss }),
            "grace follows music across tempo changes rather than frozen seconds/BPM clamp");

        mixer = new MixerRuntime(flat, t => t / 1.6);
        mixer.AdvanceFrame(-.05, [], [new(-.1, 1, 0, AllowEarlyPlacement: true), new(-.08, 1, null)], [], (_, _) => { });
        mixer.AdvanceFrame(.2, [], [], [.2], (_, g) => check(g == JudgeGrade.Perfect,
            "successful early head can be placed and released before chart start"));
        mixer = new MixerRuntime(flat, t => t / 1.6);
        mixer.AdvanceFrame(.2, [], [new(-.1, 1, 0), new(-.08, 1, null)], [.2], (_, g) =>
            check(g == JudgeGrade.Miss, "unaccepted pre-start click does not create a receiver"));
        mixer = new MixerRuntime(moving, t => t / 1.6);
        mixer.AdvanceFrame(2, [], [], [.5, 1, 2], (_, g) => check(g == JudgeGrade.Perfect, "Auto remains Perfect"), true);
    }

    private static void TestClockRegression(Action<bool, string> check)
    {
        var clock = new MonotonicSongPosition();
        check(clock.Observe(-.08) == -.08, "audio filter preserves initial negative latency compensation");
        var reads = new[] { .5, .51, .503, .506, .52 }.Select(clock.Observe).ToArray();
        check(reads.SequenceEqual(new[] { .5, .51, .51, .51, .52 }),
            "mix readback jitter holds time without adding drift; input and frame reads share floor");
        clock.Reset(.52);
        check(clock.Observe(.44) == .52 && clock.Observe(.53) == .53,
            "resume retains paused floor until restarted audio catches up");
        clock.Reset();
        check(clock.Observe(.1) == .1, "explicit play/seek starts a new clock epoch");

        var path = Path((0, 0.0), (2, 2.0));
        var mixer = new MixerRuntime(path, t => t / 1.6);
        var grades = new List<JudgeGrade>();
        mixer.AdvanceFrame(.6, [], [new(0, 1, 0), new(.01, 1, null)], [.6], (_, g) => grades.Add(g));
        var loss = mixer.LossBar;
        mixer.AdvanceFrame(.57, [], [], [], (_, g) => grades.Add(g));
        check(mixer.CurrentTime == .6 && mixer.LossBar == loss && mixer.Position == 0,
            "backward render sample preserves committed receiver and original grace deadline");
        // Late raw timestamps must still apply release; neither throw nor discard controls.
        mixer.AdvanceFrame(.58, [], [new(.575, 2, .6), new(.576, 2, null)], [], (_, g) => grades.Add(g));
        check(mixer.Position == .6 && mixer.Controller == null && mixer.Catching,
            "press/release during clock regression applies in arrival order at committed time");
        mixer.AdvanceFrame(.8, [], [], [.7, .8], (_, g) => grades.Add(g));
        check(grades.Count == 3 && grades.All(g => g == JudgeGrade.Perfect),
            "clock recovery neither drops nor duplicates queued scoring ticks");

        mixer = new MixerRuntime(path, t => t / 1.6);
        mixer.AdvanceFrame(.6, [], [new(0, 1, 0), new(.01, 1, null)], [], (_, _) => { });
        mixer.AdvanceFrame(.58, [], [new(.57, 2, 9), new(.575, 2, null)], [], (_, _) => { });
        mixer.AdvanceFrame(.7, [], [], [.7], (_, grade) => check(grade == JudgeGrade.Miss,
            "jitter and rejected late clicks cannot extend grace"));
    }

    private static (MixerRuntime Runtime, List<JudgeGrade> Grades) Replay(SustainPath path,
        MixerInput[] events, double[] frames, double[] ticks)
    {
        var runtime = new MixerRuntime(path, path.V2BpmTimeline is { } bpm ? bpm.RuntimeBarAt : t => t / 1.6);
        var active = new Dictionary<int, double>();
        var result = new List<JudgeGrade>();
        var previous = double.NegativeInfinity;
        foreach (var frame in frames)
        {
            var inputs = events.Where(e => e.Time > previous && e.Time <= frame).ToArray();
            runtime.AdvanceFrame(frame, active.Select(p => new TouchSample(p.Key, path.Track, p.Value, ContactPhase.Stationary)),
                inputs, ticks.Where(t => t > previous && t <= frame).ToArray(), (_, g) => result.Add(g));
            foreach (var input in inputs)
                if (input.Position is { } p) active[input.Id] = p; else active.Remove(input.Id);
            previous = frame;
        }
        return (runtime, result);
    }

    private static SustainPath Path(params (double Time, double Center)[] points) => new()
    {
        Kind = SustainKind.Mixer, HeadId = 1, Track = Track.Center,
        Nodes = points.Select((p, i) => new Note { Id = i + 1, SubNoteId = i + 1 < points.Length ? i + 2 : -1,
            Type = i == 0 ? NoteType.MixerHead : NoteType.MixerNode, Track = Track.Center,
            Second = p.Time, BakedSecond = p.Time, BarTime = p.Time / 1.6, Position = p.Center - .5, Width = 1 }).ToArray(),
    };
}

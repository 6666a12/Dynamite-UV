using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using static CoreTests.CoreTestSupport;

namespace CoreTests;

internal static class CustomHoldReplayTests
{
    private static readonly JudgeSettings Settings = JudgeSettings.ForPreset(JudgePreset.Hard);
    private static readonly int[] Rates = [4, 30, 60, 64, 1000];
    private readonly record struct Input(double Time, int Id, double? Position);
    private static ExactBarTime F(int n, int d = 1) => ExactBarTime.FromFraction(n, d);

    public static void Run()
    {
        Console.WriteLine("[v2.1 Hold] stationary sweeps, touch handoff and frozen settlement at 4/30/60/64/1000 FPS");
        var report = Build(Note("t+24*t*(1-t)", F(17,96), 2, 2.2, .16));
        var loss = Replay(report, 4, [new(0, 1, 2)]);
        Console.WriteLine($"    17/96 bar: loss={loss.ContactLostAt:R}s, deadline={loss.GraceDeadline:R}s, tail={report.EndTime:R}s");
        Check(loss.ContactLostAt is > 0 and < .016 && loss.GraceDeadline < report.EndTime,
            $"review repro 17/96: geometric exit is before first 60Hz update; L={loss.ContactLostAt:R}, D={loss.GraceDeadline:R}");
        foreach (var fps in Rates)
        {
            var actual = Replay(report, fps, [new(0, 1, 2)]);
            Check(actual == loss && Tail(report, actual) == JudgeGrade.Miss,
                $"17/96 overshoot: identical frozen loss, break and Miss tail at {fps} fps");
        }

        var line = Build(Note("t", F(1)));
        var tempo = Build(Note("t", F(1)), [new(F(0),120), new(F(1,8),240), new(F(5,16),120)]);
        var flat = Build(Note("t", F(1), 0, 0));
        // Each touch overlaps by half the note width plus half the touch width (.125+.125).
        V2PathNode Node(string id, int ticks, double center, V2PathCurve? curve = null) =>
            new() { Id = id, Time = F(ticks,8), Center = center, Width = .25, CurveToNext = curve, Judge = true, JudgeWasExplicit = true };
        var jumps = Build(Note("t", F(1)) with { Nodes = [Node("middle",1,0,V2PathCurve.Hold), Node("jump",2,1,V2PathCurve.Hold), Node("tail",8,1)] });
        var apex = Build(Note("min(4*t,3-2*t)", F(1,4)), [new(F(0),197)]);
        var atPeak = apex.V2BpmTimeline!.RuntimeSecondsAt(.125);
        var grazingPosition = apex.V2Evaluator!.EvaluateRuntime(.125).Right + .125;
        var shortPath = Build(Note("t", F(1,32), 0, 0));
        var tailAtDeadline = Build(Note("t", F(11,40), 0, 0));
        var deadline = .125 + Settings.HoldContactGraceSeconds(240);
        foreach (var fps in Rates)
        {
            Check(!Replay(line, fps, [new(0,1,0),new(0,2,.5),new(0,3,1)], .25).HasLoss,
                $"{fps}: union of finite-width stationary fingers hands off across moving edges");
            Check(!Replay(flat, fps, [new(0,1,0),new(.13,1,null),new(.13,2,0),new(.18,2,.6),new(.21,2,0)], .25).HasLoss,
                $"{fps}: release/replacement at one instant and move out/back within grace");
            Check(Replay(line, fps, [new(0,1,0),new(.4,1,.5)], .25).ContactLostAt == .25,
                $"{fps}: moving back exactly at frozen deadline cannot revive a Hold");
            var change = Replay(tempo, fps, [new(0,1,0)], .25);
            Check(change.ContactLostAt == .375 && change.GraceDeadline == .375 + .15,
                $"{fps}: interpolated exit uses tempo at loss, not the later tempo at observation");
            Check(Replay(jumps, fps, [new(0,1,0)], .25).ContactLostAt == .25,
                $"{fps}: prior built-in hold's right-continuous jump starts loss exactly at authored node");
            var graze = Replay(apex, fps, [new(0,1,0),new(0,2,grazingPosition)], .25);
            Check(graze.ContactLostAt == atPeak && graze.GraceDeadline == atPeak + Settings.HoldContactGraceSeconds(197),
                $"{fps}: an isolated zero-duration contact at the turning knot resets grace before immediate loss");
            Check(!Replay(flat, fps, [new(0,1,0),new(.125,1,null),new(Math.BitDecrement(deadline),1,0)]).HasLoss,
                $"{fps}: re-entry just before the deadline is legal");
            Check(Replay(flat, fps, [new(0,1,0),new(.125,1,null),new(deadline,1,0)]).ContactLostAt == .125,
                $"{fps}: re-entry exactly at the deadline leaves the first loss frozen");
            var atTail = Replay(tailAtDeadline, fps, [new(0,1,0),new(.125,1,null)]);
            Check(atTail.GraceDeadline == tailAtDeadline.EndTime && Tail(tailAtDeadline, atTail) ==
                  new JudgeEngine(JudgePreset.Hard).Judge(tailAtDeadline.EndTime, .125).Grade,
                $"{fps}: a deadline equal to the tail settles at tail using the original release window");
            Check(!Replay(shortPath, fps, [new(0,1,0),new(.01,1,null),new(.04,2,0)], accepted:.04).HasLoss,
                $"{fps}: late accepted short head after tail never invents pre-head loss");
            Check(Replay(shortPath, fps, [new(-.04,1,0),new(-.02,1,null)], accepted:-.04).ContactLostAt == -.02,
                $"{fps}: early accepted head honors a pre-chart release");
            Check(!Replay(shortPath, fps, [new(0,1,0),new(.04,1,null)]).HasLoss &&
                  Replay(shortPath, fps, [new(0,1,0),new(shortPath.EndTime,1,null)]).ContactLostAt == shortPath.EndTime,
                $"{fps}: tail is inclusive; input after tail is ignored");
        }
        // Even in a custom chart, a Hold with no custom segment retains event/frame-only replay.
        var builtIn = Note("t", F(1)) with { CurveToNext = V2PathCurve.Linear, CustomExpr = null };
        Check(!Build(builtIn, minor:0).ReplaysHoldGeometry && !Build(builtIn).ReplaysHoldGeometry &&
              Replay(Build(builtIn, minor:0), 4, [new(0,1,0)], .25).ContactLostAt == .5,
            "2.0 and built-in-only 2.1 Holds do not acquire new contact semantics");
        // One large frame spans nodes both before and after D; compare to already-swept fine frames.
        var cutoff = .125 + .15;
        foreach (var fine in new[] { true, false })
        {
            var engine = new JudgeEngine(JudgePreset.Hard);
            var nodes = new[] { Unit(.1), Unit(.2), Unit(cutoff), Unit(.5), Unit(1, true) };
            if (fine) foreach (var n in nodes.Where(n => n.Time < cutoff))
            { engine.Apply(n.Category, JudgeGrade.Perfect); n.Judged = true; }
            var grades = SustainJudgementRules.SettleReleasedHold(nodes, 1, .125, engine, cutoff);
            Check(engine.CountPerfect == 2 && engine.CountMiss == 3 && grades.Count == (fine ? 3 : 5),
                $"{(fine ? "fine" : "coarse")}: nodes before D perfect; at/after D miss; tail graded at L");
        }
    }

    private static JudgeGrade? Tail(SustainPath path, HoldContactState state) =>
        SustainJudgementRules.JudgeHoldTail(path.EndTime, path.EndTime, true, true,
            state.GraceDeadline < path.EndTime, state, new JudgeEngine(JudgePreset.Hard))?.Grade;

    private static V2PathNote Note(string expression, ExactBarTime tail, double center=0, double end=1, double width=.25) => new()
    {
        Id="hold", Type=V2NoteType.Hold, Time=F(0), Center=center, Width=width, CurveToNext=V2PathCurve.Custom, CustomExpr=expression,
        Nodes=[new() { Id="tail", Time=tail, Center=end, Width=width, Judge=true, JudgeWasExplicit=true }]
    };
    private static SustainPath Build(V2PathNote note, IReadOnlyList<V2BpmEvent>? bpms=null, int minor=1)
    {
        var chart = new V2Chart { ChartId="review", FormatMinor=minor, AudioOffsetSec=0,
            Bpms=bpms ?? [new(F(0),240)], NotesCenter=[note], NotesLeft=[], NotesRight=[] };
        return JudgePlan.Build(V2RuntimeAdapter.Adapt(chart).RuntimeChart, Settings).Sustains.Values.Single();
    }
    private static JudgeUnit Unit(double time, bool tail=false) => new() { Time=time, NoteId=1, Track=Track.Center, SustainHeadId=1,
        Category=tail?ScoreCategory.HoldEnd:ScoreCategory.HoldHolding, Kind=tail?UnitKind.HoldEnd:UnitKind.HoldPoint };

    private static HoldContactState Replay(SustainPath path, int fps, Input[] inputs, double width=.4, double accepted=0)
    {
        var active = new Dictionary<int, HoldTouchProjection>(); var frame = new HoldContactFrame();
        var state = HoldContactState.Contact; int index=0; var from=accepted;
        for (var i=0;;i++)
        {
            var now=accepted+i/(double)fps; frame.Clear();
            foreach (var (id,p) in active) frame.Seed(id,p);
            while (index<inputs.Length && inputs[index].Time<=now)
            {
                var e=inputs[index++]; HoldTouchProjection? touch=null;
                if (e.Position is {} x) active[e.Id]=new(new(e.Id,Track.Center,x,ContactPhase.Stationary),null,null);
                if (e.Position.HasValue) touch=active[e.Id]; else active.Remove(e.Id);
                frame.Record(e.Time,e.Id,touch);
            }
            state=frame.Observe(path,from,now,state,Settings,path.V2BpmTimeline!.RuntimeBpmAt,width);
            if(now>=path.EndTime) return state;
            from=now;
        }
    }
}

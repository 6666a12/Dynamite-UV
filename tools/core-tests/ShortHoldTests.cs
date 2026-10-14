using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace CoreTests;

public static class ShortHoldTests
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("[18] Short Hold late heads, timestamped contact and live accuracy");
        foreach (var preset in new[] { JudgePreset.Hard, JudgePreset.Hardcore })
        foreach (var duration in new[] { .025, .050, .100 })
        {
            var engine = new JudgeEngine(preset);
            var tail = 1 + duration;
            var pending = SustainJudgementRules.JudgeHoldTail(tail, tail + .001,
                false, false, false, default, engine);
            check(pending == null, $"{preset}/{duration}: tail waits for unresolved head");
            foreach (var offset in new[] { -.020, .0, .020, .060, .080, .150 })
            {
                engine = new JudgeEngine(preset);
                var head = engine.JudgePress(1, 1 + offset);
                if (head.Resolution == JudgeResolution.Pending || head.Grade == JudgeGrade.Miss)
                    continue;
                engine.Apply(ScoreCategory.HoldStart, head.Grade);
                var result = SustainJudgementRules.JudgeHoldTail(tail, Math.Max(tail, 1 + offset),
                    true, true, false, default, engine);
                check(result?.Grade == JudgeGrade.Perfect,
                    $"{preset}/{duration}/{offset}: legal head retains full late window");
                engine.Apply(ScoreCategory.HoldEnd, result!.Value.Grade);
                check(engine.JudgedTheoreticalMax == 200 && engine.CountMiss == 0,
                    "short Hold counts head and tail once");
            }
        }

        var path = Path(1.05);
        var settings = JudgeSettings.ForPreset(JudgePreset.Hard);
        // These event sequences must give the same tail across coarse and fine render frames.
        VerifyFrames(check, path, 1.0,
            [new(1.0, 1, Touch(1)), new(1.06, 1, null)], JudgeGrade.Perfect, "release after tail");
        VerifyFrames(check, path, .95,
            [new(.95, 1, Touch(1)), new(.97, 1, null)], JudgeGrade.Great,
            "early head and release before chart start preserve release timestamp");
        VerifyFrames(check, path, 1.0,
            [new(1.0, 1, Touch(1)), new(1.05, 1, null)], JudgeGrade.Perfect, "release exactly at tail");
        VerifyFrames(check, path, 1.06,
            [new(.99, 9, Touch(9)), new(1.0, 9, null), new(1.06, 1, Touch(1)), new(1.07, 1, null)],
            JudgeGrade.Perfect, "late head ignores pre-head release");
        VerifyFrames(check, Path(1.2), 1.0,
            [new(1.0, 1, Touch(1)), new(1.1, 1, null)], JudgeGrade.Great, "early release keeps timing grade");
        VerifyFrames(check, Path(1.2), 1.0,
            [new(1.0, 1, Touch(1)), new(1.04, 2, Touch(2)), new(1.05, 1, null), new(1.25, 2, null)],
            JudgeGrade.Perfect, "replacement finger covers tail");
        VerifyFrames(check, Path(1.2), 1.0,
            [new(1.0, 1, Touch(1)), new(1.05, 1, null), new(1.05, 2, Touch(2))],
            JudgeGrade.Perfect, "same-time release and replacement");
        VerifyFrames(check, Path(1.2), 1.0,
            [new(1.0, 1, Touch(1)), new(1.04, 1, Touch(1, 3)), new(1.09, 1, Touch(1))],
            JudgeGrade.Perfect, "move out and reconnect within grace");

        var frame = new HoldContactFrame();
        frame.Seed(1, Touch(1));
        frame.Record(1.02, 1, null);
        frame.Record(1.25, 1, Touch(1));
        var loss = frame.Observe(Path(2), 1, 1.3, default, settings, _ => 150, .4);
        check(loss.ContactLostAt == 1.02 && loss.GraceDeadline == 1.22,
            "coarse frame cannot reconnect after frozen grace deadline");
        check(SustainJudgementRules.JudgeHoldTail(2, 2, true, true, true, loss,
            new JudgeEngine(JudgePreset.Hard))?.Grade == JudgeGrade.Miss,
            "broken Hold cannot be revived by a late contact");

        var lateEngine = new JudgeEngine(JudgePreset.Hard);
        check(lateEngine.JudgePress(1, 1.2).Resolution == JudgeResolution.Pending &&
              !lateEngine.InWindow(1, 1.251), "late-head fix does not widen Good or Miss input windows");

        // One physical pointer projected onto two tracks; no per-track consumption.
        frame.Clear();
        frame.Seed(1, new HoldTouchProjection(
            new TouchSample(1, Track.Center, .5, ContactPhase.Stationary),
            new TouchSample(1, Track.Left, .5, ContactPhase.Stationary), null));
        check(!frame.Observe(Path(1.1, Track.Left), 1, 1.1, default, settings, _ => 150, .4).HasLoss,
            "multi-track contact observes the chart-side projection");

        var failedEngine = new JudgeEngine(JudgePreset.Hard);
        var nodes = new[] { Unit(ScoreCategory.HoldHolding), Unit(ScoreCategory.HoldEnd) };
        failedEngine.Apply(ScoreCategory.HoldStart, JudgeGrade.Miss);
        var failed = SustainJudgementRules.FailRemainingHold(nodes, 1, failedEngine);
        check(failed.Count == 2 && failedEngine.CountMiss == 3 && failedEngine.JudgedTheoreticalMax == 300,
            "failed head settles remaining Hold exactly once, including live denominator");
        check(SustainJudgementRules.FailRemainingHold(nodes, 1, failedEngine).Count == 0 &&
              failedEngine.JudgedTheoreticalMax == 300, "repeated failure does not change counters");

        var scored = new JudgeEngine(JudgePreset.Hard);
        scored.Apply(ScoreCategory.HoldStart, JudgeGrade.Great);
        scored.Apply(ScoreCategory.HoldHolding, JudgeGrade.Perfect);
        scored.Apply(ScoreCategory.MixerHolding, JudgeGrade.Miss);
        scored.Apply(ScoreCategory.HoldEnd, JudgeGrade.Perfect);
        check(scored.JudgedTheoreticalMax == 400 && scored.Percent(scored.JudgedTheoreticalMax) == 67.5 &&
              scored.Percent(400) == 67.5, "live ACC includes Hold/Mixer nodes and equals final CLEAR");
        var released = new JudgeEngine(JudgePreset.Hard);
        released.Apply(ScoreCategory.HoldStart, JudgeGrade.Perfect);
        SustainJudgementRules.SettleReleasedHold(
            [Unit(ScoreCategory.HoldHolding), Unit(ScoreCategory.HoldEnd)], 1, 1.9, released);
        check(released.JudgedTheoreticalMax == 300, "release batch contributes its full denominator");
    }

    private readonly record struct Input(double Time, int Id, HoldTouchProjection? Touch);

    private static void VerifyFrames(Action<bool, string> check, SustainPath path,
        double headTime, Input[] inputs, JudgeGrade expected, string name)
    {
        foreach (var step in new[] { .005, 1.0 / 30, .4 })
        {
            var engine = new JudgeEngine(JudgePreset.Hard);
            var frame = new HoldContactFrame();
            var active = new Dictionary<int, HoldTouchProjection>();
            var contact = HoldContactState.Contact;
            var cursor = 0;
            var from = headTime;
            var started = false;
            JudgeResult? tail = null;
            for (var now = .98; now < 2 && tail == null; now += step)
            {
                frame.Clear();
                foreach (var (id, touch) in active) frame.Seed(id, touch);
                while (cursor < inputs.Length && inputs[cursor].Time <= now)
                {
                    var e = inputs[cursor++];
                    frame.Record(e.Time, e.Id, e.Touch);
                    if (e.Touch is { } touch) active[e.Id] = touch;
                    else active.Remove(e.Id);
                }
                started |= now >= headTime;
                if (started)
                {
                    contact = frame.Observe(path, from, now, contact, engine.Settings, _ => 150, .4);
                    from = Math.Max(from, Math.Min(now, path.EndTime));
                }
                tail = SustainJudgementRules.JudgeHoldTail(path.EndTime, now, started,
                    started, false, contact, engine);
            }
            check(tail?.Grade == expected, $"{name}: frame step {step:R} -> {expected}");
        }
    }

    private static HoldTouchProjection Touch(int id, double position = .5) =>
        new(new TouchSample(id, Track.Center, position, ContactPhase.Stationary), null, null);

    private static SustainPath Path(double tail, Track track = Track.Center) => new()
    {
        Kind = SustainKind.Hold, HeadId = 1, Track = track,
        Nodes = [Note(1, 1, track), Note(2, tail, track)],
    };

    private static Note Note(int id, double second, Track track) => new()
    {
        Id = id, SubNoteId = id == 1 ? 2 : -1,
        Type = id == 1 ? NoteType.HoldHead : NoteType.HoldNode,
        BarTime = second, Second = second, BakedSecond = second, Position = 0, Width = 1, Track = track,
    };

    private static JudgeUnit Unit(ScoreCategory category) => new()
    {
        Time = 2, Category = category, Track = Track.Center, NoteId = (int)category,
        Kind = category == ScoreCategory.HoldEnd ? UnitKind.HoldEnd : UnitKind.HoldPoint,
        SustainHeadId = 1,
    };
}

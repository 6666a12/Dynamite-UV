using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using static EditorTestSupport;

internal static class EditorAutoTimelineTests
{
    internal static void Run()
    {
        var chart = new V2Chart
        {
            ChartId = "auto-timeline", AudioOffsetSec = .25,
            Bpms = [new(ExactBarTime.Zero, 120), new(ExactBarTime.FromFraction(1, 1), 240)],
            NotesLeft = [], NotesRight = [],
            NotesCenter =
            [
                new V2BasicNote { Id = "tap", Type = V2NoteType.Tap, Time = ExactBarTime.FromFraction(1, 3), Center = 2, Width = 1 },
                new V2PathNote
                {
                    Id = "hold", Type = V2NoteType.Hold, Time = ExactBarTime.FromFraction(1, 1), Center = 1, Width = 1,
                    Nodes = [new V2PathNode { Id = "tail", Time = ExactBarTime.FromFraction(3, 1), Center = 4, Width = 2,
                        Judge = true, JudgeWasExplicit = true }],
                },
            ],
        };
        var adapted = V2RuntimeAdapter.Adapt(chart);
        var timeline = new EditorAutoTimeline(adapted.RuntimeChart);
        var expected = JudgePlan.Build(adapted.RuntimeChart, JudgeSettings.ForPreset(JudgePreset.Hard));
        var events = new List<JudgeUnit>();
        timeline.Reset(-10);
        timeline.Advance(20, events.Add);
        Check(events.Select(unit => (unit.Time, unit.Kind, unit.NoteId)).SequenceEqual(
            expected.Units.Select(unit => (unit.Time, unit.Kind, unit.NoteId))), "Auto timeline shares exact BPM/offset judge plan and handles frame jumps");
        timeline.Advance(20, events.Add);
        Check(events.Count == expected.Units.Count, "paused and duplicate clock do not emit duplicate hits");
        var path = timeline.Plan.Sustains.Values.Single();
        timeline.Reset((path.StartTime + path.EndTime) / 2);
        events.Clear();
        Check(timeline.IsActive(path), "seek into Hold restores active interval");
        timeline.Advance(path.EndTime, events.Add);
        Check(!timeline.IsActive(path) && events.Count == 1 && events[0].Kind == UnitKind.HoldEnd,
            "seek consumes history and keeps only future tail judgement");
        timeline.Reset(path.StartTime);
        events.Clear();
        timeline.Advance(path.StartTime, events.Add);
        Check(events.Count == 0 && timeline.IsActive(path), "exact seek boundary restores sustain without replaying head");
        timeline.Reset(-10);
        timeline.Advance(20, events.Add);
        Check(events.Count == expected.Units.Count, "backward seek re-arms all future events");
        Check(timeline.Plan.Units.All(unit => !unit.Judged), "preview cursor does not mutate shared judgement units");
        var empty = V2RuntimeAdapter.Adapt(chart with { NotesCenter = [] }, requireMainJudgement: false);
        var cleared = new EditorAutoTimeline(empty.RuntimeChart);
        cleared.Reset(3);
        cleared.Advance(4, _ => throw new Exception("empty edit emitted a hit"));
        Check(cleared.Plan.Sustains.Count == 0, "playing deletion to empty chart clears all sustain state");
    }
}

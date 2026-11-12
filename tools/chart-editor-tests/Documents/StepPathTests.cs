using System.Diagnostics;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using static EditorTestSupport;

internal static class StepPathTests
{
    internal static void Run()
    {
        foreach (var type in new[] { V2NoteType.Hold, V2NoteType.Mixer })
        foreach (var track in Enum.GetValues<EditorTrack>())
        {
            var document = CreateCleanDocument("step.test", "hard", "Step", "Tests");
            var note = new EditableNote
            {
                Id = "step", Type = type, Track = track, Time = ExactBarTime.Zero,
                Center = 1, Width = .6, CurveToNext = V2PathCurve.Hold,
            };
            note.Nodes.Add(new EditablePathNode
            {
                Id = "tail", Time = ExactBarTime.FromFraction(2, 1), Center = 3, Width = 1.2,
                Judge = type == V2NoteType.Hold ? true : null,
            });
            document.Execute(new AddNoteCommand(note));
            var projected = EditorPathProjection.Flatten(EditorPathProjection.ShapeOf(note), 273.2, 273.2);
            Check(projected.Count == 3 && projected[1].Time == projected[2].Time &&
                  !projected[1].IsControlPoint && projected[2].IsControlPoint,
                "editor projection keeps the step left limit distinct from its authored node");
            var snapshot = document.BuildChartSnapshot("hard");
            var before = V2JsonEncoder.EncodeChart(snapshot);
            var adapted = V2RuntimeAdapter.Adapt(snapshot);
            var nodes = adapted.RuntimeChart.AllNotes.ToArray();
            Check(nodes.Length == 3 && nodes.Count(n => n.V2Metadata!.IsShapeSample) == 1,
                $"{track}/{type} step uses exactly head + left limit + tail, not 257 nodes");
            var plan = JudgePlan.Build(adapted.RuntimeChart, JudgeSettings.ForPreset(JudgePreset.Hard));
            var path = plan.Sustains.Values.Single();
            Check(path.Nodes.Count == 3 && path.End.V2Metadata!.SourceId == "tail",
                "same-time left limit does not replace authored tail");
            Check(Math.Abs(path.BoundsAtRuntime(path.EndTime - .0001).Center - 1) < 1e-12 &&
                  Math.Abs(path.BoundsAtRuntime(path.EndTime).Center - 3) < 1e-12,
                "step keeps exact pre-jump and endpoint judgement bounds");
            Check(!plan.Units.Any(u => u.NoteId == nodes.Single(n => n.V2Metadata!.IsShapeSample).Id),
                "shape-only step sample never creates a judgement unit");
            Check(before.SequenceEqual(V2JsonEncoder.EncodeChart(document.BuildChartSnapshot("hard"))),
                "runtime optimization does not change authored bytes");
        }
        var dense = CreateCleanDocument("step.benchmark", "hard", "Steps", "Tests");
        for (var i = 0; i < 100; i++)
        {
            var note = new EditableNote { Id = $"h{i}", Type = V2NoteType.Hold, Track = EditorTrack.Center,
                Time = ExactBarTime.FromFraction(i, 1), Center = 1, Width = 1, CurveToNext = V2PathCurve.Hold };
            note.Nodes.Add(new EditablePathNode { Id = $"t{i}", Time = ExactBarTime.FromFraction(i + 1, 1),
                Center = 3, Width = 1, Judge = true });
            dense.Execute(new AddNoteCommand(note));
        }
        var chart = dense.BuildChartSnapshot("hard");
        _ = V2RuntimeAdapter.Adapt(chart); // warm up
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var runtime = V2RuntimeAdapter.Adapt(chart);
        watch.Stop();
        Console.WriteLine($"STEP BENCH 100 paths: nodes={runtime.RuntimeChart.AllNotes.Count()} elapsed={watch.Elapsed.TotalMilliseconds:F2}ms allocated={GC.GetAllocatedBytesForCurrentThread() - bytes} bytes; not a device FPS test");
        Check(runtime.RuntimeChart.AllNotes.Count() == 300, "100 step paths have 300 runtime objects instead of 25700");
    }
}

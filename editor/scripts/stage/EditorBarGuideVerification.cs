using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using G = DynamiteUniverse.Shared.Chart.GameplayStageGeometry;

namespace DynamiteUniverse.Editor;

internal static class EditorBarGuideVerification
{
    internal static void Run(Node host)
    {
        var stage = new EditorStagePreview { Size = new Vector2(1920, 1080) };
        host.AddChild(stage);
        var overlay = stage.GetNode<EditorBarGuides>("GameplayStage/BarGuides");
        var chart = Chart([new(At(0), 240)], [], .375);
        stage.SetChart(chart);
        Check(overlay.BuildFrame().Count == 0, "all tracks initially off");
        stage.SetBarGuides(1, 0, 2);
        var both = overlay.BuildFrame().ToArray();
        Check(both.Length > 0 && both.All(m => m.Track != Track.Center), "independent tracks");
        var left = both.Where(m => m.Track == Track.Left).ToArray();
        var right = both.Where(m => m.Track == Track.Right).ToArray();
        Check(left.Select(m => (m.Tick, m.Label, m.Width)).SequenceEqual(right.Select(m => (m.Tick, m.Label, m.Width))), "same density and labels at dim and bright");
        Check(left.Zip(right).All(pair => pair.First.Alpha * 2 == pair.Second.Alpha), "brightness is alpha only");
        Check(left.All(m => m.From.X == m.To.X && m.From.Y < m.To.Y) &&
            left.Single(m => m.Tick == 8).From.X > G.LeftLineX && right.Single(m => m.Tick == 8).From.X < G.RightLineX, "side orientation");
        stage.SetBarGuides(0, 2, 0);
        var marks = overlay.BuildFrame().ToArray();
        Check(marks.Single(m => m.Tick == 0).Label == "0" && marks.Any(m => m.Label == "1/4"), "whole bars and reduced fractions");
        Check(marks.Single(m => m.Tick == 8).Width > marks.Single(m => m.Tick == 4).Width &&
            marks.Single(m => m.Tick == 4).Width > marks.Single(m => m.Tick == 1).Width, "three musical stroke weights");
        Check(marks.All(m => m.Track == Track.Center && m.From.Y == m.To.Y && m.From.X < m.To.X), "center orientation");
        var y = marks.Single(m => m.Tick == 8).From.Y;
        Near(y, G.CenterLineY - G.BaseFallSpeedPx * .25, "center projection at current audio offset");
        stage.SetPlayerSpeed(2);
        Near(overlay.BuildFrame().Single(m => m.Tick == 8).From.Y, G.CenterLineY - 2 * (G.CenterLineY - y), "player speed");
        stage.CurrentBar = 1;
        Check(overlay.BuildFrame().Single(m => m.Tick == 32).Label == "1", "seek moves the grid");
        stage.SetGamePreview(true);
        Check(!overlay.Visible && overlay.BuildFrame().Count == 0, "hidden from game preview");
        stage.SetGamePreview(false);
        Check(overlay.BuildFrame().Count > 0, "return to authoring keeps configured state");
        overlay.SetViewport(new Rect2(300, 350, 500, 300));
        Check(overlay.BuildFrame().Count > 0 && overlay.BuildFrame().All(m => m.From.X == 300 && m.To.X == 800 &&
            m.From.Y >= 350 && m.From.Y < 650 && m.Label is null), "local PAN viewport clips strokes and labels");
        Check(!chart.RuntimeChart.AllNotes.Any(), "guides never insert runtime notes");
        stage.Free();
        VerifyTiming();
        GD.Print("PASS bar guides: independent levels, 1/32 hierarchy, exact labels, BPM/offset/scroll, PAN clipping and sparse budget");
    }

    private static void VerifyTiming()
    {
        // First leave the viewport, then re-enter as the outgoing speed eases down. A single linear inverse fails here.
        var chart = Chart([new(At(0), 240), new(At(24), 120)],
            [new() { Time = At(0), Value = 4, CurveToNext = V2ScrollCurve.Hold },
             new() { Time = At(16), Value = 4, CurveToNext = V2ScrollCurve.EaseInOutSine },
             new() { Time = At(32), Value = .125 }], -.375);
        var grid = new EditorBarGuideGrid(chart.Metadata);
        const double now = -.375;
        var output = new List<EditorBarGuideGrid.Sample>();
        grid.Collect(now, 1, G.TravelPx, output);
        var ticks = output.Select(s => s.Tick).Order().ToArray();
        var expected = new List<long>();
        for (var i = 0; i <= 256; ++i)
        {
            var bar = i / 32d;
            var sec = -.375 + (bar <= .75 ? bar : .75 + 2 * (bar - .75));
            var scroll = bar < .5 ? 4 : bar >= 1 ? .125 : 4 + (.125 - 4) * (1 - Math.Cos(Math.PI * (bar - .5) * 2)) / 2;
            var distance = (sec - now) * G.BaseFallSpeedPx * scroll;
            if (distance is >= 0 and <= G.TravelPx)
            {
                expected.Add(i);
                Near(output.Single(s => s.Tick == i).Distance, distance, "nonlinear distance");
            }
        }
        Check(ticks.SequenceEqual(expected) && !ticks.Contains(16L) && ticks.Contains(32L), "later returning visible window after first offscreen tick");
        var allSlow = Chart([new(At(0), 1000)], [new() { Time = At(0), Value = .000001 }], .25);
        grid = new(allSlow.Metadata);
        var evaluated = grid.Collect(30, .1, G.TravelPx, output);
        Check(evaluated <= EditorBarGuideGrid.SampleBudget && output.Count > 1 && output.All(s => s.Tick % 32 == 0), "tiny legal speed: bounded, musically aligned decimation");
        grid = new(chart.Metadata);
        grid.Collect(-.475, 1, G.TravelPx, output);
        Near(output.Single(s => s.Tick == 0).Distance, .1 * G.BaseFallSpeedPx * 4, "audio offset displaces bar zero during pre-roll");
        Check(output.All(s => s.Tick >= 0 && s.Distance <= G.TravelPx), "pre-roll never creates negative chart ticks");
    }

    private static ExactBarTime At(long tick) => ExactBarTime.FromFraction(tick, 32);
    private static V2RuntimeAdapterResult Chart(V2BpmEvent[] bpms, V2ScrollEvent[] scrolls, double offset) =>
        V2RuntimeAdapter.Adapt(new V2Chart { ChartId = "bar-guides", Bpms = bpms, ScrollSpeeds = scrolls,
            AudioOffsetSec = offset, NotesLeft = [], NotesCenter = [], NotesRight = [] }, requireMainJudgement: false);
    private static void Near(double actual, double expected, string name) => Check(Math.Abs(actual - expected) < .002, name);
    private static void Check(bool okay, string name) { if (!okay) throw new InvalidOperationException(name); }
}

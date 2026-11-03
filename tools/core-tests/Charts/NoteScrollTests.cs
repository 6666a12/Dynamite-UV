using System.Text;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using static CoreTests.CoreTestSupport;

namespace CoreTests;

internal static class NoteScrollTests
{
    internal static void Run()
    {
        Console.WriteLine("Note scroll speed multiplier");
        var tap = new V2BasicNote
        {
            Id = "tap", Type = V2NoteType.Tap, Time = Bar(1), Center = 2.5, Width = 1,
            ScrollSpeedMultiplier = .5,
        };
        var hold = new V2PathNote
        {
            Id = "hold", Type = V2NoteType.Hold, Time = Bar(2), Center = 2, Width = 1,
            ScrollSpeedMultiplier = 2, CurveToNext = V2PathCurve.EaseInQuad,
            Nodes =
            [
                new V2PathNode
                {
                    Id = "hold-tail", Time = Bar(3), Center = 3, Width = 1.5,
                    Judge = true, JudgeWasExplicit = true,
                },
            ],
        };
        var chart = Chart(tap, hold);
        var encoded = Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(chart));
        var decoded = V2JsonDecoder.DecodeChart(encoded);
        Check(decoded.NotesCenter[0].ScrollSpeedMultiplier == .5 &&
              decoded.NotesCenter[1].ScrollSpeedMultiplier == 2,
            "strict JSON round trip preserves per-note multipliers");
        Check(encoded.Contains("\"scrollSpeedMultiplier\": 0.5", StringComparison.Ordinal) &&
              encoded.Contains("\"scrollSpeedMultiplier\": 2", StringComparison.Ordinal),
            "encoder writes non-default multipliers on parent notes");

        var defaultChart = Chart(new V2BasicNote
        {
            Id = "default", Type = V2NoteType.Tap, Time = Bar(1), Center = 2.5, Width = 1,
        });
        var defaultJson = Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(defaultChart));
        Check(!defaultJson.Contains("scrollSpeedMultiplier", StringComparison.Ordinal) &&
              V2JsonDecoder.DecodeChart(defaultJson).NotesCenter[0].ScrollSpeedMultiplier == 1,
            "omitted multiplier is canonical default 1");
        var defaultProjection = V2GameplayDigest.BuildProjection(defaultChart, JudgePreset.Hard,
            new string('0', 64));
        var projectedNotes = (IReadOnlyDictionary<string, object?>)defaultProjection["notes"]!;
        var projectedCenter = (object?[])projectedNotes["center"]!;
        var projectedTap = (IReadOnlyDictionary<string, object?>)projectedCenter[0]!;
        Check(!projectedTap.ContainsKey("scrollSpeedMultiplier"),
            "default multiplier stays out of gameplay projection for old-chart digest stability");

        var defaultRuntime = V2RuntimeAdapter.Adapt(defaultChart).RuntimeChart;
        var runtime = V2RuntimeAdapter.Adapt(chart).RuntimeChart;
        Check(runtime.AllNotes.All(note => note.V2Metadata != null) &&
              runtime.NotesCenter.Where(note => note.V2Metadata!.ParentSourceId == "hold")
                  .All(note => note.V2Metadata!.ScrollSpeedMultiplier == 2) &&
              runtime.NotesCenter.Single(note => note.V2Metadata!.ParentSourceId == "tap")
                  .V2Metadata!.ScrollSpeedMultiplier == .5,
            "parent multiplier reaches every flattened Hold path point");
        Check(runtime.NotesCenter.Single(note => note.V2Metadata!.ParentSourceId == "tap").Second ==
              defaultRuntime.NotesCenter[0].Second,
            "multiplier does not change authoritative hit seconds");

        var audioHash = new string('0', 64);
        Check(V2GameplayDigest.Compute(defaultChart, JudgePreset.Hard, audioHash).Sha256 !=
              V2GameplayDigest.Compute(chart, JudgePreset.Hard, audioHash).Sha256,
            "non-default multipliers change gameplay digest");

        foreach (var invalid in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
        {
            var rejected = false;
            try
            {
                V2SemanticValidator.ValidateChart(Chart(new V2BasicNote
                {
                    Id = "invalid", Type = V2NoteType.Tap, Time = Bar(1), Center = 2.5, Width = 1,
                    ScrollSpeedMultiplier = invalid,
                }));
            }
            catch (V2DiagnosticException)
            {
                rejected = true;
            }
            Check(rejected, $"invalid note multiplier {invalid} is rejected");
        }
    }

    private static V2Chart Chart(params V2Note[] notes) => new()
    {
        ChartId = "note-scroll", AudioOffsetSec = 0,
        Bpms = [new V2BpmEvent(Bar(0), 120)],
        NotesLeft = [], NotesCenter = notes, NotesRight = [],
    };
}

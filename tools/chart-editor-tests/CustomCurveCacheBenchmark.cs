using System.Diagnostics;
using System.Text;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using static EditorTestSupport;

/// <summary>Opt-in Release measurement, not a wall-clock assertion. Actual workbench core
/// stages, excluding IPC transport, rendering, I/O and GPU. For cold loads, displace the
/// fallback cache to model the stage's separate process.</summary>
internal static class CustomCurveCacheBenchmark
{
    public static void Run()
    {
        Console.WriteLine("sources,round,command_ms,snapshot_encode_ms,preview_decode_adapt_ms,command_compiles,encode_compiles,preview_compiles");
        foreach (int count in new[] { 64, 65, 128 })
        {
            var d = CreateCleanDocument("bench.curves", "hard", "Benchmark", "Community");
            var chart = d.SelectedChart;
            for (int i = 0; i < count; i++) chart.ScrollSpeeds.Add(new()
            {
                Time = ExactBarTime.FromFraction(i,1), Value = 1, CurveToNext = V2ScrollCurve.Custom,
                CustomExpr = FormattableString.Invariant($"1-(1-t)^{1.5 + i * .001:R}*cos(3*pi*t)"),
            });
            chart.ScrollSpeeds.Add(new() { Time = ExactBarTime.FromFraction(count,1), Value = 1 });
            var stage = new EditorPreviewChartLoader();
            for (int round = 0; round < 4; round++)
            {
                if (round == 0) DisplaceFallback();
                long n = CustomCurve.CompilationCount; var sw = Stopwatch.StartNew();
                d.Execute(new EditChartOffsetCommand(round * .01));
                double command = sw.Elapsed.TotalMilliseconds; long cmds = CustomCurve.CompilationCount - n;
                n = CustomCurve.CompilationCount; sw.Restart();
                var json = Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(d.BuildChartSnapshot(chart.Id), false));
                double encode = sw.Elapsed.TotalMilliseconds; long encs = CustomCurve.CompilationCount - n;
                if (round == 0) DisplaceFallback();
                n = CustomCurve.CompilationCount; sw.Restart();
                stage.Load(json, "Benchmark", chart.Key.ToString());
                Console.WriteLine(FormattableString.Invariant($"{count},{round},{command:F2},{encode:F2},{sw.Elapsed.TotalMilliseconds:F2},{cmds},{encs},{CustomCurve.CompilationCount - n}"));
            }
        }
    }

    private static void DisplaceFallback()
    {
        for (int i = 0; i <= 64; i++) CustomCurve.Compile($"t + 0*{i}");
    }
}

using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

/// <summary>
/// 断言式核心逻辑测试（无测试框架，失败时非零退出）。
/// 跑法：dotnet run --project tools/core-tests/CoreTests.csproj --configuration Release
/// 可选本地开发谱：追加 -- --dev-testdata &lt;packs-dir&gt;
/// </summary>
public static class Program
{

    public static int Main(string[] args)
    {
        Failures = 0;

        string? devTestdataDirectory;
        try
        {
            devTestdataDirectory = ParseArguments(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Argument error: {ex.Message}");
            Console.Error.WriteLine(
                "Usage: dotnet run --project tools/core-tests/CoreTests.csproj -- " +
                "[--dev-testdata <packs-dir>]");
            return 2;
        }

        try
        {
            var chartPath = FindFixturePath();
            Console.WriteLine($"fixture: {chartPath}");

            LegacyChartTests.TestLoad(chartPath, out var chart);
            LegacyChartTests.TestBarTimeConversion(chart!);
            GameplayGeometryTests.TestDropSpeedVisualModel(chart!);
            GameplayGeometryTests.TestConstantBpmVisualSpeed(chart!);
            GameplayGeometryTests.TestGameplayStageGeometry();
            GameplayGeometryTests.TestGameplayMirrorMapping();
            LegacyChartTests.TestAutoFullCombo(chart!);
            if (devTestdataDirectory is not null)
                LegacyChartTests.TestHeadlineCountsAcrossDevPacks(devTestdataDirectory);
            InputJudgementTests.TestWindowOffsets();
            InputJudgementTests.TestMine();
            InputJudgementTests.TestNormalizedScore();
            InputJudgementTests.TestV2InputTimeLock();
            InputJudgementTests.TestExTapBinaryJudgement();
            InputJudgementTests.TestInputJudgeRules();
            SustainJudgementTests.TestDerivedSustainJudgements();
            SustainJudgementTests.TestHoldFailureSettlement();
            SustainJudgementTests.TestReleasedHoldSettlement();
            SustainJudgementTests.TestSustainInterpolation();
            InputJudgementTests.TestResolutionAndScaledVitals();
            V2ChartTests.TestV2ExactBarTimeAndLegacyConversion();
            V2ChartTests.TestV2GeometryJudgementAndSync();
            V2ChartTests.TestV2CurvesMixerAndDigest();
            SustainJudgementTests.TestD4CFrozenGrace();
            SustainJudgementTests.TestHoldReleaseFreezesGrace();
            ScoreIdentityTests.TestScoreStoreMigrationAndIdentity();
            ResultRevealTests.Run(Check);
            ResultOutcomeTests.Run(Check);
            ResultPreludeTimelineTests.Run(Check);
            TrackHandoffStateTests.Run(Check);
            GameSettingsCodecTests.Run(Check);
            CalibrationStatsTests.Run(Check);
            CalibrationSamplingTests.Run(Check);
            NotePaletteDraftTests.Run(Check);
            SettingsFilePersistenceTests.Run(Check);
            ScoreFilePersistenceTests.Run(Check);
            PathResourceCacheTests.Run(Check);
            PointerInputBufferTests.Run(Check);
            LatestRequestLoaderTests.Run(Check);
            SongBrowseStateTests.Run(Check);
            SongListScrollStateTests.Run(Check);
            SongCardMotionTests.Run(Check);
            SongDetailRequestTests.Run(Check);
            BehaviorEquivalenceTests.Run(Check);
            ShortHoldTests.Run(Check);
            GradeTests.Run(Check);
            MixerTests.Run(Check);
            BleedHealthTests.Run(Check);
            GameplayEndConditionTests.Run(Check);
            NoteScrollTests.Run();
            FlacDecoderTests.Run(Check);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Core test error: {ex.Message}");
            return 1;
        }

        Console.WriteLine();
        if (Failures > 0)
        {
            Console.WriteLine($"FAILED: {Failures} assertion(s) failed.");
            return 1;
        }
        Console.WriteLine("ALL TESTS PASSED");
        return 0;
    }

    private static string? ParseArguments(string[] args)
    {
        if (args.Length == 0)
            return null;
        if (args.Length != 2 || !string.Equals(
                args[0], "--dev-testdata", StringComparison.Ordinal))
            throw new ArgumentException(
                "Expected no arguments or exactly --dev-testdata <packs-dir>.");
        if (string.IsNullOrWhiteSpace(args[1]))
            throw new ArgumentException("--dev-testdata requires a non-empty <packs-dir>.");

        string directory;
        try
        {
            directory = Path.GetFullPath(args[1]);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(
                $"Invalid --dev-testdata directory '{args[1]}': {ex.Message}");
        }
        if (!Directory.Exists(directory))
            throw new ArgumentException(
                $"--dev-testdata directory does not exist: {directory}");
        return directory;
    }

    private static string FindFixturePath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "synthetic_chart.json");
        if (File.Exists(candidate))
            return candidate;
        throw new FileNotFoundException(
            "Missing copied clean-room fixture. Expected: " + candidate, candidate);
    }
}

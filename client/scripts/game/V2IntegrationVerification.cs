using System.Text.Json;
using Godot;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

/// <summary>Editor/Internal-only deterministic package verification environment.</summary>
internal sealed class V2IntegrationVerification
{
    public string PackPath { get; }
    public string ChartId { get; }
    public ChartLoadMode Mode { get; }
    public bool Auto { get; }
    public int FixedFps { get; }
    public string? TracePath { get; }
    public string? StartGatePath { get; }
    public string? ReadyPath { get; }
    public int FrameCount { get; }

    private int _completedFrames;

    public bool FixedClockEnabled => FixedFps > 0;
    public bool CanAdvance =>
        (StartGatePath == null || File.Exists(StartGatePath)) &&
        (FrameCount <= 0 || _completedFrames < FrameCount);

    private V2IntegrationVerification(string packPath, string chartId,
        ChartLoadMode mode, bool auto, int fixedFps, string? tracePath,
        string? startGatePath, string? readyPath, int frameCount)
    {
        PackPath = packPath;
        ChartId = chartId;
        Mode = mode;
        Auto = auto;
        FixedFps = fixedFps;
        TracePath = tracePath;
        StartGatePath = startGatePath;
        ReadyPath = readyPath;
        FrameCount = frameCount;
    }

    public static V2IntegrationVerification? FromEnvironment()
    {
        // No feature or environment variable can enable verification in Public builds.
        if (!ChartPack.IsEditorOrInternal)
            return null;
        var pack = System.Environment.GetEnvironmentVariable("DUV_VERIFY_PACK");
        if (string.IsNullOrWhiteSpace(pack))
            return null;
        var chartId = System.Environment.GetEnvironmentVariable("DUV_VERIFY_CHART_ID");
        if (string.IsNullOrWhiteSpace(chartId))
            throw new InvalidOperationException("DUV_VERIFY_CHART_ID is required with DUV_VERIFY_PACK");
        var modeText = System.Environment.GetEnvironmentVariable("DUV_VERIFY_MODE");
        var mode = modeText switch
        {
            "legacy-direct" => ChartLoadMode.LegacyDirect,
            "v2" => ChartLoadMode.V2,
            _ => throw new InvalidOperationException(
                "DUV_VERIFY_MODE must be legacy-direct or v2"),
        };
        var fixedFps = int.TryParse(System.Environment.GetEnvironmentVariable("DUV_VERIFY_FIXED_FPS"),
            out var parsedFps) && parsedFps > 0 ? parsedFps : 0;
        var tracePath = OptionalPath("DUV_VERIFY_TRACE");
        var startGatePath = OptionalPath("DUV_VERIFY_START_GATE");
        var readyPath = OptionalPath("DUV_VERIFY_READY");
        var frameCount = int.TryParse(
            System.Environment.GetEnvironmentVariable("DUV_VERIFY_FRAME_COUNT"),
            out var parsedFrameCount) && parsedFrameCount > 0 ? parsedFrameCount : 0;
        return new V2IntegrationVerification(pack, chartId, mode,
            ParseBool(System.Environment.GetEnvironmentVariable("DUV_VERIFY_AUTO")), fixedFps,
            tracePath, startGatePath, readyPath, frameCount);
    }

    public (ChartPack Pack, ChartDiff Diff, LoadedChart Loaded) Load()
    {
        var godotPackPath = ToGodotPath(PackPath);
        var pack = ChartPack.Load(godotPackPath) ??
            throw new InvalidDataException($"verification package could not be loaded: {PackPath}");
        var diff = pack.ChartOf(ChartId) ??
            throw new InvalidDataException(
                $"verification chart id '{ChartId}' was not found in package '{pack.Id}'");
        return (pack, diff, pack.LoadChart(diff, Mode));
    }

    public V2IntegrationTrace? CreateTrace(LoadedChart loaded) => TracePath == null
        ? null
        : new V2IntegrationTrace(TracePath, loaded);

    public void SignalReady()
    {
        if (ReadyPath is null)
            return;
        var resolved = ResolveLocalPath(ReadyPath);
        var directory = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(resolved, "ready\n", new System.Text.UTF8Encoding(false));
    }

    public bool CompleteFrame()
    {
        _completedFrames++;
        return FrameCount > 0 && _completedFrames >= FrameCount;
    }

    private static bool ParseBool(string? value) => value is "1" or "true" or "yes" or "on";

    private static string? OptionalPath(string name)
    {
        var value = System.Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ResolveLocalPath(string path)
    {
        if (path.StartsWith("user://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return ProjectSettings.GlobalizePath(path);
        return Path.GetFullPath(path);
    }

    private static string ToGodotPath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            return path.Replace('\\', '/');
        // External local packs stay absolute; LocalizePath only converts paths inside the project.
        return Path.GetFullPath(path).Replace('\\', '/');
    }
}

/// <summary>Stable one-object-per-frame JSONL trace used by legacy/v2 A/B comparisons.</summary>
internal sealed class V2IntegrationTrace : IDisposable
{
    private readonly LoadedChart _loaded;
    private readonly StreamWriter _writer;
    private ulong _frame;

    public V2IntegrationTrace(string path, LoadedChart loaded)
    {
        _loaded = loaded;
        var resolved = ResolveLocalPath(path);
        var directory = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        // 常驻 writer（UTF-8 no BOM，截断模式）：Write 只写缓冲，退出时 Dispose 冲刷，
        // 取代每帧 File.AppendAllText 的完整 open/seek/write/close。
        _writer = new StreamWriter(resolved, append: false,
            new System.Text.UTF8Encoding(false));
    }

    public void Write(double time, double bar, IEnumerable<NoteView> activeViews,
        JudgePlan.Plan plan, JudgeEngine engine)
    {
        var active = activeViews
            .Where(view => !view.IsQueuedForDeletion())
            .OrderBy(view => view.Model.Track)
            .ThenBy(view => view.Model.Second)
            .ThenBy(view => view.Model.Id)
            .Select(view => new
            {
                runtimeId = LegacyComparableId(view.Model),
                track = view.Model.Track.ToString().ToLowerInvariant(),
                barTime = view.Model.BarTime,
                second = view.Model.Second,
                chartPosition = view.Model.Position,
                width = view.Model.Width,
                screenX = view.Position.X,
                screenY = view.Position.Y,
            })
            .ToArray();
        var value = new
        {
            frame = _frame++,
            time,
            active,
            judgement = new
            {
                resolved = plan.Units.Count(unit => unit.Judged),
                total = plan.Units.Count,
                pending = plan.Units.Count(unit => !unit.Judged),
                prefect = engine.CountPrefect,
                great = engine.CountGreat,
                good = engine.CountGood,
                miss = engine.CountMiss,
                combo = engine.Combo,
                maxCombo = engine.MaxCombo,
            },
            score = new
            {
                raw = engine.Score,
                normalized = engine.NormalizedScore(plan.TheoreticalMax),
            },
        };
        _writer.Write(JsonSerializer.Serialize(value));
        _writer.Write('\n');
    }

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }

    private string LegacyComparableId(Note note)
    {
        if (!_loaded.SourceIdsByRuntimeId.TryGetValue(note.Id, out var sourceId))
            return note.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return sourceId.StartsWith("legacy-", StringComparison.Ordinal)
            ? sourceId["legacy-".Length..]
            : sourceId;
    }

    private static string ResolveLocalPath(string path)
    {
        if (path.StartsWith("user://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return ProjectSettings.GlobalizePath(path);
        return Path.GetFullPath(path);
    }
}

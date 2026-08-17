using System.Text.Json;
using Godot;
using DuxShared.Chart;
using DuxShared.Chart.V2;
using DuxShared.Judge;
using DuxShared.Score;

namespace DuxCommunity.Game;

public enum ChartPackageFormat
{
    Legacy,
    V2,
}

public enum ChartLoadMode
{
    Default,
    LegacyDirect,
    V2,
}

/// <summary>Optional preview interval inherited from either the pack or chart entry.</summary>
public readonly record struct ChartPreview(double StartSec, double DurationSec);

/// <summary>Format-neutral metadata for one playable chart entry.</summary>
public sealed class ChartDiff
{
    public required string ChartId { get; init; }
    public required string Difficulty { get; init; }
    public string? DifficultyKey { get; init; }
    public required string Display { get; init; }
    public int? Level { get; init; }
    public bool Unrated { get; init; }
    public required string File { get; init; }
    public IReadOnlyList<string> Charters { get; init; } = [];
    public string? Audio { get; init; }
    public ChartPreview? Preview { get; init; }

    /// <summary>Compatibility alias used by the current HUD and legacy ScoreStore.</summary>
    public string Diff => Difficulty;

    public string CharterDisplay => Charters.Count == 0 ? "-" : string.Join(", ", Charters);

    /// <summary>
    /// ScoreStore is still owned by the legacy client. Keep its old difficulty key for legacy
    /// packages, while v2 callers can at least avoid custom-difficulty display-key collisions.
    /// </summary>
    public string LegacyScoreKey(ChartPackageFormat format) =>
        format == ChartPackageFormat.V2 ? ChartId : Difficulty;
}

/// <summary>
/// Runtime chart data grouped by responsibility: the compatibility graph, optional strict-v2 timing
/// capability, and stable score identity. Godot callers should not depend on semantic source records.
/// </summary>
public sealed class LoadedChart
{
    public required Chart RuntimeChart { get; init; }
    public V2Pack? V2Pack { get; init; }
    public required string PackId { get; init; }
    public required string ChartId { get; init; }
    public string? RulesetId { get; init; }
    public string? GameplayDigest { get; init; }
    public required string ResolvedAudioPath { get; init; }
    public required int NoteCount { get; init; }
    public required double DurationSec { get; init; }
    public required JudgePreset Preset { get; init; }
    public required IReadOnlySet<int> SyncAccentRuntimeIds { get; init; }
    public IReadOnlyDictionary<int, string> SourceIdsByRuntimeId { get; init; } =
        new Dictionary<int, string>();
    public IReadOnlyDictionary<int, ExactBarTime> ExactTimesByRuntimeId { get; init; } =
        new Dictionary<int, ExactBarTime>();
    public DuxShared.Chart.V2.V2BpmTimeline? V2Timeline { get; init; }
    public DuxShared.Chart.V2.V2ScrollMap? V2Scroll { get; init; }

    public bool TryGetScoreIdentity(out ScoreIdentity identity)
    {
        if (RulesetId is { Length: > 0 } rulesetId &&
            GameplayDigest is { Length: > 0 } gameplayDigest)
        {
            identity = new ScoreIdentity(PackId, ChartId, rulesetId, gameplayDigest);
            return true;
        }
        identity = default;
        return false;
    }
}

/// <summary>
/// Chart package directory. Metadata parsing is format-specific, while loading is delegated to
/// ChartLoadService. Unknown explicit package formats or versions fail closed.
/// </summary>
public sealed class ChartPack
{
    public const string V2PackFormat = "dynamite-uv-pack";
    public const int V2FormatVersion = 2;
    public static readonly string[] DiffOrder =
        { "casual", "normal", "hard", "mega", "giga", "tech", "custom" };

    public required ChartPackageFormat PackageFormat { get; init; }
    public string? Format { get; init; }
    public int? FormatVersion { get; init; }
    public long? Revision { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string DirPath { get; init; }
    public string? Audio { get; init; }
    public string Cover { get; init; } = "";
    public ChartPreview? Preview { get; init; }
    public List<ChartDiff> Charts { get; init; } = new();

    public bool IsV2 => PackageFormat == ChartPackageFormat.V2;
    public string Charter => Charts.SelectMany(chart => chart.Charters).Distinct()
        .DefaultIfEmpty("-").Aggregate((a, b) => $"{a}, {b}");

    public string ChartPathFor(ChartDiff diff) => ResolvePath(diff.File);
    public string AudioPathFor(ChartDiff diff) => ResolvePath(
        diff.Audio ?? Audio ?? throw new InvalidDataException(
            $"Package '{Id}' chart '{diff.ChartId}' has no resolved audio."));
    public string? CoverPath => Cover.Length > 0 ? ResolvePath(Cover) : null;
    public ChartPreview? PreviewFor(ChartDiff diff) => diff.Preview ?? Preview;

    public ChartDiff? DiffOf(string difficulty) =>
        Charts.FirstOrDefault(chart => chart.Difficulty == difficulty);
    public ChartDiff? ChartOf(string chartId) =>
        Charts.FirstOrDefault(chart => chart.ChartId == chartId);

    public LoadedChart LoadChart(ChartDiff diff, ChartLoadMode mode = ChartLoadMode.Default) =>
        ChartLoadService.Load(this, diff, mode);

    public bool TryLoadChart(ChartDiff diff, out LoadedChart? loaded,
        ChartLoadMode mode = ChartLoadMode.Default)
    {
        try
        {
            loaded = LoadChart(diff, mode);
            return true;
        }
        catch (Exception e)
        {
            loaded = null;
            GD.PushWarning($"ChartPack: loading {Id}/{diff.ChartId} failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Derived gameplay duration; never reads legacy Baked_TotalMainNote.</summary>
    public double LoadDurationSec(ChartDiff diff) => LoadChart(diff).DurationSec;

    /// <summary>Derived main judgement count; never reads legacy Baked_TotalMainNote.</summary>
    public int LoadNoteCount(ChartDiff diff) => LoadChart(diff).NoteCount;

    public static List<ChartPack> ScanAll() => ChartCatalog.ScanAll();

    public static ChartPack? Load(string dirPath)
    {
        var metaPath = JoinPath(dirPath, "meta.json");
        if (!Godot.FileAccess.FileExists(metaPath))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(metaPath));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("meta.json root must be an object.");

            var hasFormat = root.TryGetProperty("format", out var formatElement);
            var hasVersion = root.TryGetProperty("formatVersion", out var versionElement);
            string? declaredFormat = hasFormat && formatElement.ValueKind == JsonValueKind.String
                ? formatElement.GetString()
                : null;
            int? declaredVersion = hasVersion && versionElement.ValueKind == JsonValueKind.Number &&
                versionElement.TryGetInt32(out var parsedVersion)
                ? parsedVersion
                : null;
            ChartPackageFormat packageFormat;
            if (hasFormat || hasVersion)
            {
                if (declaredFormat != V2PackFormat || declaredVersion != V2FormatVersion)
                    throw new InvalidDataException(
                        $"unsupported explicit format/version '{declaredFormat ?? "<invalid>"}'/" +
                        $"'{declaredVersion?.ToString() ?? "<invalid>"}'");
                packageFormat = ChartPackageFormat.V2;
            }
            else
            {
                packageFormat = ChartPackageFormat.Legacy;
            }

            if (packageFormat == ChartPackageFormat.V2)
            {
                var strictPack = DuxShared.Chart.V2.V2JsonDecoder.DecodePack(
                    Godot.FileAccess.GetFileAsBytes(metaPath), metaPath);
                return V2ChartMetadataAdapter.ToChartPack(dirPath, strictPack);
            }

            var packAudio = LegacyChartMetadata.OptionalString(root, "audio");
            var charts = LegacyChartMetadata.ParseCharts(root,
                LegacyChartMetadata.OptionalString(root, "charter"), packAudio);
            if (charts.Count == 0)
                throw new InvalidDataException("package has no chart entries");

            // Legacy keeps its established tier order. V2 preserves the recommended charts[] order.
            charts = charts.OrderBy(chart =>
            {
                var index = Array.IndexOf(DiffOrder, chart.Difficulty);
                return index >= 0 ? index : int.MaxValue;
            }).ToList();

            var legacyCover = LegacyChartMetadata.OptionalString(root, "cover") ?? "";
            if (legacyCover.Length > 0 &&
                !IsSafeLegacyCoverPath(dirPath, legacyCover, out var coverReason))
            {
                GD.PushWarning(
                    $"ChartPack: ignoring unsafe optional legacy cover in {metaPath}: {coverReason}");
                legacyCover = "";
            }

            return new ChartPack
            {
                PackageFormat = ChartPackageFormat.Legacy,
                Format = declaredFormat,
                FormatVersion = declaredVersion,
                Revision = LegacyChartMetadata.OptionalInt64(root, "revision"),
                Id = LegacyChartMetadata.RequiredString(root, "id"),
                Title = LegacyChartMetadata.RequiredString(root, "title"),
                Artist = LegacyChartMetadata.OptionalString(root, "artist") ?? "-",
                DirPath = dirPath.TrimEnd('/', '\\'),
                Audio = packAudio,
                Cover = legacyCover,
                Preview = LegacyChartMetadata.OptionalPreview(root, "preview"),
                Charts = charts,
            };
        }
        catch (Exception e)
        {
            GD.PushWarning($"ChartPack: parsing {metaPath} failed: {e.Message}");
            return null;
        }
    }

    public static bool IsEditorOrInternal => ChartCatalog.IsEditorOrInternal;

    private string ResolvePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException($"Package '{Id}' contains an empty path.");
        return JoinPath(DirPath, relative);
    }

    private static bool IsSafeLegacyCoverPath(string packageDirectory, string path,
        out string reason)
    {
        if (string.IsNullOrEmpty(path))
        {
            reason = "path is empty";
            return false;
        }
        if (path.Length > 512 || path.Contains('\0'))
        {
            reason = "path is too long or contains a NUL character";
            return false;
        }
        if (path.Contains('\\'))
        {
            reason = "backslashes are not allowed";
            return false;
        }
        if (path.StartsWith('/') || path.EndsWith('/'))
        {
            reason = "absolute paths and empty path segments are not allowed";
            return false;
        }
        if (path.Contains(':') || Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            reason = "URI schemes and drive-qualified paths are not allowed";
            return false;
        }
        if (path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            reason = "empty, '.' and '..' path segments are not allowed";
            return false;
        }

        var root = Path.GetFullPath(ProjectSettings.GlobalizePath(packageDirectory));
        var candidate = Path.GetFullPath(Path.Combine(root,
            path.Replace('/', Path.DirectorySeparatorChar)));
        var rootedPrefix = root.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            reason = "resolved path escapes the package root";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string JoinPath(string directory, string relative) =>
        $"{directory.TrimEnd('/', '\\')}/{relative.TrimStart('/', '\\')}";
}

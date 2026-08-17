using DuxShared.Chart.V2;

namespace DuxCommunity.Game;

/// <summary>Converts strict shared v2 metadata into stable client directory models.</summary>
internal static class V2ChartMetadataAdapter
{
    public static ChartPack ToChartPack(string dirPath, V2Pack pack) => new()
    {
        PackageFormat = ChartPackageFormat.V2,
        Format = V2Format.PackFormat,
        FormatVersion = V2Format.FormatVersion,
        Revision = pack.Revision,
        Id = pack.Id,
        Title = pack.Title,
        Artist = pack.Artist,
        DirPath = dirPath.TrimEnd('/', '\\'),
        Audio = pack.Audio,
        Cover = pack.Cover ?? "",
        Preview = ToPreview(pack.Preview),
        Charts = pack.Charts.Select(ToChartDiff).ToList(),
    };

    private static ChartDiff ToChartDiff(V2ChartEntry entry) => new()
    {
        ChartId = entry.Id,
        Difficulty = DifficultyName(entry.Difficulty),
        DifficultyKey = entry.DifficultyKey,
        Display = entry.Difficulty == V2Difficulty.Custom
            ? entry.DifficultyKey! : DifficultyName(entry.Difficulty),
        Level = entry.Level,
        Unrated = entry.Unrated,
        File = entry.File,
        Charters = entry.Charters,
        Audio = entry.Audio,
        Preview = ToPreview(entry.Preview),
    };

    private static ChartPreview? ToPreview(V2Preview? preview) => preview is null
        ? null : new ChartPreview(preview.StartSec, preview.DurationSec);

    private static string DifficultyName(V2Difficulty difficulty) => difficulty switch
    {
        V2Difficulty.Casual => "casual",
        V2Difficulty.Normal => "normal",
        V2Difficulty.Hard => "hard",
        V2Difficulty.Mega => "mega",
        V2Difficulty.Giga => "giga",
        V2Difficulty.Tech => "tech",
        V2Difficulty.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };
}

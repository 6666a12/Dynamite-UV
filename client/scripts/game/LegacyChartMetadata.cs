using System.Text.Json;

namespace DuxCommunity.Game;

/// <summary>Small JSON helpers for the permissive legacy package metadata format.</summary>
internal static class LegacyChartMetadata
{
    public static List<ChartDiff> ParseCharts(JsonElement root, string? packCharter,
        string? packAudio)
    {
        var charts = new List<ChartDiff>();
        foreach (var chart in RequiredArray(root, "charts"))
        {
            var difficulty = RequiredString(chart, "diff").ToLowerInvariant();
            var chartId = OptionalString(chart, "id") ?? difficulty;
            var audio = OptionalString(chart, "audio");
            if (audio == null && packAudio == null)
                throw new InvalidDataException($"chart '{chartId}' has no resolved audio");
            var level = ParseLevel(chart, chartId);
            charts.Add(new ChartDiff
            {
                ChartId = chartId,
                Difficulty = difficulty,
                DifficultyKey = OptionalString(chart, "difficultyKey"),
                Display = OptionalString(chart, "display") ?? difficulty,
                Level = level is > 0 ? level : null,
                Unrated = OptionalBool(chart, "unrated") || level == 0,
                File = RequiredString(chart, "file"),
                Charters = ParseCharters(chart, packCharter),
                Audio = audio,
                Preview = OptionalPreview(chart, "preview"),
            });
        }
        return charts;
    }

    public static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"missing non-empty string '{name}'");

    public static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static long? OptionalInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var parsed) ? parsed : null;

    public static bool OptionalBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static ChartPreview? OptionalPreview(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var preview) || preview.ValueKind == JsonValueKind.Null)
            return null;
        if (preview.ValueKind != JsonValueKind.Object ||
            !preview.TryGetProperty("startSec", out var start) || !start.TryGetDouble(out var startSec) ||
            !preview.TryGetProperty("durationSec", out var duration) ||
            !duration.TryGetDouble(out var durationSec))
            throw new InvalidDataException($"invalid preview '{name}'");
        return new ChartPreview(startSec, durationSec);
    }

    private static IEnumerable<JsonElement> RequiredArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"missing array '{name}'");
        return value.EnumerateArray();
    }

    private static int ParseLevel(JsonElement chart, string chartId)
    {
        if (!chart.TryGetProperty("level", out var value))
            return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var level) ||
            level is < 0 or > 99)
        {
            throw new InvalidDataException(
                $"legacy chart '{chartId}' level must be an integer in the range 0..99");
        }
        return level;
    }

    private static IReadOnlyList<string> ParseCharters(JsonElement chart, string? fallback)
    {
        if (chart.TryGetProperty("charters", out var charters) &&
            charters.ValueKind == JsonValueKind.Array)
        {
            return charters.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? ""
                    : throw new InvalidDataException("charters[] values must be strings"))
                .Where(value => value.Length > 0)
                .ToArray();
        }
        var one = OptionalString(chart, "charter") ?? fallback;
        return one == null ? [] : [one];
    }
}

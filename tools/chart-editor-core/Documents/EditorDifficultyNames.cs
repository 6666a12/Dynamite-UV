using System.Text;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Suggests copy names bounded by v2 limits without colliding with authored or extra files.</summary>
public static class EditorDifficultyNames
{
    public static (string ChartId, string File, string? DifficultyKey) SuggestCopy(EditorDocument document)
    {
        var source = document.SelectedChart;
        var occupied = EditorMediaSlots.Occupied(document);
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? "-copy" : $"-copy-{n}";
            var id = source.Id[..Math.Min(source.Id.Length, 64 - suffix.Length)] + suffix;
            var file = $"charts/{id}.json";
            if (document.Charts.Any(chart => chart.Id == id) || !EditorMediaSlots.Available(document, occupied, file)) continue;
            return (id, file, source.Difficulty == V2Difficulty.Custom ? NextCustomKey(document, source.DifficultyKey!) : null);
        }
    }

    internal static string NextCustomKey(EditorDocument document, string key)
    {
        var existing = document.Charts.Where(c => c.DifficultyKey is not null)
            .Select(c => Fold(c.DifficultyKey!)).ToHashSet(StringComparer.Ordinal);
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? " copy" : $" copy {n}";
            var candidate = string.Concat(key.EnumerateRunes().Take(24 - suffix.Length)) + suffix;
            if (!existing.Contains(Fold(candidate))) return candidate;
        }
    }

    private static string Fold(string key) => key.Normalize().ToUpperInvariant().ToLowerInvariant();
}

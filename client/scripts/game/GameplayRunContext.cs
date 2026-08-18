using Godot;

namespace DynamiteUniverse.Game;

/// <summary>Immutable metadata consumed by gameplay presentation and score persistence.</summary>
internal sealed record GameplayRunContext(
    string PackId,
    string ChartId,
    string LegacyScoreKey,
    string DifficultyColorKey,
    string DifficultyDisplay,
    int? DifficultyLevel,
    string SongTitle,
    string? CoverPath,
    string? RulesetId,
    string? GameplayDigest)
{
    public static GameplayRunContext FromSelection(ChartPack pack, ChartDiff diff,
        LoadedChart loaded) => new(
        loaded.PackId,
        loaded.ChartId,
        diff.LegacyScoreKey(pack.PackageFormat),
        diff.Difficulty,
        diff.Display,
        diff.Level,
        pack.Title,
        pack.CoverPath,
        loaded.RulesetId,
        loaded.GameplayDigest);

    public static GameplayRunContext InternalFallback { get; } = new(
        "tablear", "giga", "giga", "giga", "giga", 15, "Tablear", null, null, null);

    public string DifficultyText => DifficultyLevel is { } level
        ? $"{DifficultyDisplay.ToUpperInvariant()} · Lv {level}"
        : $"{DifficultyDisplay.ToUpperInvariant()} · UNRATED";
}

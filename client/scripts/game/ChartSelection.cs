namespace DuxCommunity.Game;

/// <summary>
/// Immutable cross-scene chart choice. The committed choice controls gameplay entry; an unrelated
/// selection-screen preload never changes it until <see cref="GameSession.CommitSelection"/> runs.
/// </summary>
public sealed record ChartSelection(ChartPack Pack, ChartDiff Diff, LoadedChart? LoadedChart)
{
    public bool Matches(ChartPack pack, ChartDiff diff) =>
        StringComparer.Ordinal.Equals(Pack.Id, pack.Id) &&
        StringComparer.Ordinal.Equals(Diff.ChartId, diff.ChartId);
}

/// <summary>One eagerly loaded selection-screen chart and the metadata it belongs to.</summary>
public sealed record ChartPreload(ChartPack Pack, ChartDiff Diff, LoadedChart LoadedChart)
{
    public bool Matches(ChartPack pack, ChartDiff diff) =>
        StringComparer.Ordinal.Equals(Pack.Id, pack.Id) &&
        StringComparer.Ordinal.Equals(Diff.ChartId, diff.ChartId);
}

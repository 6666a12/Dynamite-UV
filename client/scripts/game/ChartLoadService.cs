namespace DuxCommunity.Game;

/// <summary>
/// Stable client boundary for chart loading modes. The format-specific implementation remains in
/// the existing v2 integration so current gameplay compatibility helpers stay untouched.
/// </summary>
internal static class ChartLoadService
{
    public static LoadedChart Load(ChartPack pack, ChartDiff diff, ChartLoadMode mode) =>
        V2Integration.Load(pack, diff, mode);
}

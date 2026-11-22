namespace DynamiteUniverse.Shared.Chart;

/// <summary>The game's visual note speed setting in tenths of the normal speed.</summary>
public static class GameplayFallSpeed
{
    public const int MinimumLevel = 1;
    public const int DefaultLevel = 10;
    public const int MaximumLevel = 20;

    public static bool IsValid(int level) => level is >= MinimumLevel and <= MaximumLevel;
    public static int Clamp(int level) => Math.Clamp(level, MinimumLevel, MaximumLevel);
    public static double Multiplier(int level) => (double)level / DefaultLevel;
}

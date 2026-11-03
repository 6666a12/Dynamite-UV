namespace DynamiteUniverse.Shared.Chart;

/// <summary>Shared real-time visual scroll math; BPM only maps chart time to audio seconds.</summary>
public static class VisualScrollMath
{
    public static double SpeedPixelsPerSecond(
        double basePixelsPerSecond, double scrollSpeed, double playerSpeed,
        double noteSpeedMultiplier = 1.0) =>
        basePixelsPerSecond * scrollSpeed * playerSpeed * noteSpeedMultiplier;

    public static double DistancePixels(
        double noteSecond, double currentSecond, double basePixelsPerSecond,
        double scrollSpeed, double playerSpeed, double noteSpeedMultiplier = 1.0) =>
        (noteSecond - currentSecond) *
        SpeedPixelsPerSecond(basePixelsPerSecond, scrollSpeed, playerSpeed, noteSpeedMultiplier);

    public static double FallthroughSpeedPixelsPerSecond(
        double basePixelsPerSecond, double scrollSpeed, double playerSpeed,
        double noteSpeedMultiplier = 1.0) =>
        Math.Abs(SpeedPixelsPerSecond(basePixelsPerSecond, scrollSpeed, playerSpeed,
            noteSpeedMultiplier));
}

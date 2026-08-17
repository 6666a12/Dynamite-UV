namespace DuxShared.Chart;

/// <summary>Shared real-time visual scroll math; BPM only maps chart time to audio seconds.</summary>
public static class VisualScrollMath
{
    public static double SpeedPixelsPerSecond(
        double basePixelsPerSecond, double scrollSpeed, double playerSpeed) =>
        basePixelsPerSecond * scrollSpeed * playerSpeed;

    public static double DistancePixels(
        double noteSecond, double currentSecond, double basePixelsPerSecond,
        double scrollSpeed, double playerSpeed) =>
        (noteSecond - currentSecond) *
        SpeedPixelsPerSecond(basePixelsPerSecond, scrollSpeed, playerSpeed);

    public static double FallthroughSpeedPixelsPerSecond(
        double basePixelsPerSecond, double scrollSpeed, double playerSpeed) =>
        Math.Abs(SpeedPixelsPerSecond(basePixelsPerSecond, scrollSpeed, playerSpeed));
}

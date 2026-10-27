using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// The grabbed note or path node and the pointer at press time. Its exact time survives clicks
/// and spatial-only drags; only a displacement along the time axis is converted from seconds.
/// </summary>
public readonly record struct CanvasDragAnchor(
    ExactBarTime Time, double Seconds, double Center, double PointerSeconds, double PointerCenter)
{
    public ExactBarTime TimeAt(double pointerSeconds, IReadOnlyList<EditableBpm> bpms,
        int gridDivisor, bool snap, double toleranceSeconds)
    {
        var delta = pointerSeconds - PointerSeconds;
        return Math.Abs(delta) <= toleranceSeconds
            ? Time
            : EditorTime.SnapSeconds(Math.Max(0d, Seconds + delta), bpms, gridDivisor, snap);
    }

    public double CenterAt(double pointerCenter) => Center + (pointerCenter - PointerCenter);
}

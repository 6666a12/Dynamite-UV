namespace DynamiteUniverse.Shared.Judge;

/// <summary>
/// Filters audio mix/latency readback jitter within one playback epoch. A backward
/// sample holds the last position until audio catches up; it never adds artificial
/// time. Explicit play/seek starts a new epoch, resume preserves the paused floor.
/// User timing offset is applied by the caller after filtering.
/// </summary>
public sealed class MonotonicSongPosition
{
    private double? _last;

    public void Reset(double? floor = null) => _last = floor;

    public double Observe(double position)
    {
        if (!double.IsFinite(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        _last = _last is { } previous ? Math.Max(previous, position) : position;
        return _last.Value;
    }
}

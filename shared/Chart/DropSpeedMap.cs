namespace DuxShared.Chart;

/// <summary>
/// DropSpeed runtime mapping. Events are interpolated by BarTime; a later value wins on duplicates.
/// </summary>
public sealed class DropSpeedMap
{
    private readonly (double BarTime, double Mult)[] _events;

    public static DropSpeedMap Empty { get; } = new([]);

    public DropSpeedMap(IEnumerable<(double BarTime, double Mult)> events)
    {
        var byBar = new SortedDictionary<double, double>();
        foreach (var (barTime, mult) in events)
            byBar[barTime] = mult;
        _events = byBar.Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    public double SpeedAt(double barTime)
    {
        if (_events.Length == 0)
            return 1.0;
        if (barTime <= _events[0].BarTime)
            return _events[0].Mult;
        if (barTime >= _events[^1].BarTime)
            return _events[^1].Mult;

        var lo = 0;
        var hi = _events.Length - 1;
        while (lo + 1 < hi)
        {
            var mid = (lo + hi) / 2;
            if (_events[mid].BarTime < barTime)
                lo = mid;
            else
                hi = mid;
        }

        var (b0, v0) = _events[lo];
        var (b1, v1) = _events[hi];
        return v0 + (v1 - v0) * (barTime - b0) / (b1 - b0);
    }

    /// <summary>
    /// Current visual distance in BarTime-times-DropSpeed units.
    /// The caller applies the player's two-dimensional pixel scale.
    /// </summary>
    public double RemainingDistance(double noteBarTime, double currentBarTime) =>
        (noteBarTime - currentBarTime) * SpeedAt(currentBarTime);
}

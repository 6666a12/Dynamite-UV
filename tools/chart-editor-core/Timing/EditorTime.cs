using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// Exact bar/second conversion shared by the editor shell and the canvas command adapter.
/// Seconds are display values only; committed chart data always uses <see cref="ExactBarTime"/>.
/// </summary>
public static class EditorTime
{
    public const double FallbackSecondsPerBar = 1.6d;

    /// <summary>Converts an exact bar position to its playback second using the chart BPM timeline.</summary>
    public static double BarToSeconds(ExactBarTime bar, IReadOnlyList<EditableBpm> bpms)
    {
        ArgumentNullException.ThrowIfNull(bpms);
        if (bpms.Count == 0)
            return bar.ToDouble() * FallbackSecondsPerBar;

        var ordered = bpms.OrderBy(item => item.Time).ToArray();
        var seconds = 0d;
        var previousBar = ExactBarTime.Zero;
        var bpm = ordered[0].Bpm;
        foreach (var point in ordered.Skip(1))
        {
            if (point.Time > bar)
                break;
            seconds += (point.Time - previousBar).ToDouble() * 240d / bpm;
            previousBar = point.Time;
            bpm = point.Bpm;
        }
        return seconds + (bar - previousBar).ToDouble() * 240d / bpm;
    }

    /// <summary>Inverts the BPM timeline, returning an exact rational bar for a display second.</summary>
    public static ExactBarTime SecondsToBar(double seconds, IReadOnlyList<EditableBpm> bpms)
    {
        ArgumentNullException.ThrowIfNull(bpms);
        if (!double.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds <= 0d)
            return ExactBarTime.Zero;
        if (bpms.Count == 0)
            return FromDecimal(seconds / FallbackSecondsPerBar);

        var ordered = bpms.OrderBy(item => item.Time).ToArray();
        var elapsed = 0d;
        var previousBar = ExactBarTime.Zero;
        var bpm = ordered[0].Bpm;
        foreach (var point in ordered.Skip(1))
        {
            var segment = (point.Time - previousBar).ToDouble() * 240d / bpm;
            if (seconds < elapsed + segment)
                break;
            elapsed += segment;
            previousBar = point.Time;
            bpm = point.Bpm;
        }
        return previousBar + FromDecimal((seconds - elapsed) * bpm / 240d);
    }

    /// <summary>
    /// Maps a display second to an exact grid bar. Snapping happens in bar space so the stored
    /// v2 value never comes from treating floating seconds as bar time.
    /// </summary>
    public static ExactBarTime SnapSeconds(double seconds, IReadOnlyList<EditableBpm> bpms,
        int gridDivisor, bool snap)
    {
        var bar = SecondsToBar(seconds, bpms);
        return snap ? EditorGeometry.Snap(bar, gridDivisor) : bar;
    }

    private static ExactBarTime FromDecimal(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        const long scale = 1_000_000L;
        return ExactBarTime.FromFraction(new BigInteger(Math.Round(value * scale)), scale);
    }
}

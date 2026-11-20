using System.Numerics;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

/// <summary>Samples editor grid times, not Notes. Distance need not be monotonic across a scroll change.</summary>
internal sealed class EditorBarGuideGrid
{
    internal const int Subdivisions = 32, SampleBudget = 1024;
    private const int MaxGroups = 64;
    // Beyond this point double no longer distinguishes consecutive 1/32 bar ticks.
    private const long MaxTick = 1L << 53;
    private readonly V2RuntimeChartMetadata _map;
    private readonly double[] _starts, _minTree;
    private readonly int _leaves;
    internal readonly record struct Sample(long Tick, float Distance)
    {
        internal int Rank => Tick == 0 ? 64 : BitOperations.TrailingZeroCount((ulong)Tick);
        internal int Weight => Tick % 8 == 0 ? 2 : Tick % 4 == 0 ? 1 : 0;
        internal string Label => ExactBarTimeText.Format(ExactBarTime.FromFraction(Tick, Subdivisions));
    }

    internal EditorBarGuideGrid(V2RuntimeChartMetadata map)
    {
        _map = map;
        var events = map.SourceChart.ScrollSpeeds;
        _starts = events.Count == 0 ? [0d] : events.Select(e => e.Time.ToDouble()).ToArray();
        _leaves = (int)BitOperations.RoundUpToPowerOf2((uint)_starts.Length);
        _minTree = new double[_leaves * 2];
        Array.Fill(_minTree, double.PositiveInfinity);
        for (var i = 0; i < _starts.Length; ++i)
            _minTree[_leaves + i] = map.ScrollMap.MinimumSpeedFor(i);
        for (var i = _leaves - 1; i > 0; --i) _minTree[i] = Math.Min(_minTree[i * 2], _minTree[i * 2 + 1]);
    }

    private int Segment(double bar)
    {
        var i = Array.BinarySearch(_starts, bar);
        return i >= 0 ? i : Math.Max(0, ~i - 1);
    }

    private double Minimum(int from, int end)
    {
        var min = double.PositiveInfinity;
        for (int a = from + _leaves, b = end + _leaves; a < b; a >>= 1, b >>= 1)
        {
            if ((a & 1) != 0) min = Math.Min(min, _minTree[a++]);
            if ((b & 1) != 0) min = Math.Min(min, _minTree[--b]);
        }
        return min;
    }

    private double Horizon(double now, double speed, float travel, double minimum)
    {
        var latest = now + travel / VisualScrollMath.SpeedPixelsPerSecond(GameplayStageGeometry.BaseFallSpeedPx, minimum, speed);
        return double.IsPositiveInfinity(latest) ? MaxTick / (double)Subdivisions : _map.BpmTimeline.RuntimeBarAt(latest);
    }

    internal double Distance(long tick, double now, double speed)
    {
        var bar = tick / (double)Subdivisions;
        return VisualScrollMath.DistancePixels(_map.BpmTimeline.RuntimeSecondsAt(bar), now,
            GameplayStageGeometry.BaseFallSpeedPx, _map.ScrollMap.RuntimeSpeedAt(bar), speed);
    }

    internal int Collect(double now, double speed, float travel, List<Sample> output)
    {
        output.Clear();
        if (!double.IsFinite(now) || !(speed > 0) || !(travel > 0)) return 0;
        var bar = Math.Max(0d, _map.BpmTimeline.RuntimeBarAt(now));
        if (bar * Subdivisions > MaxTick) return 0;
        var first = Segment(bar);
        // Never stop at the first off-screen grid line: a later slow scroll may bring a later bar back into view.
        var last = Segment(Horizon(now, speed, travel, Minimum(first, _starts.Length)));
        var count = last - first + 1;
        if (count <= 0) return 0;
        var groups = Math.Min(count, MaxGroups);
        var quota = SampleBudget / groups;
        var evaluated = 0;
        for (var i = 0; i < groups; ++i)
        {
            var a = first + (int)((long)i * count / groups);
            var b = first + (int)((long)(i + 1) * count / groups);
            var hi = Horizon(now, speed, travel, Minimum(a, b));
            var lo = Math.Max(bar, a == 0 ? 0 : _starts[a]);
            if (hi < lo || lo * Subdivisions > MaxTick) continue;
            var start = (long)Math.Ceiling(lo * Subdivisions);
            var end = (long)Math.Min(MaxTick, Math.Floor(hi * Subdivisions));
            if (b < _starts.Length) end = Math.Min(end, (long)Math.Min(MaxTick, Math.Ceiling(_starts[b] * Subdivisions)) - 1);
            long stride = 1;
            while ((end - start) / stride >= quota) stride *= 2;
            // Power-of-two decimation retains strong musical divisions, anchored at bar zero.
            for (var tick = (start + stride - 1) / stride * stride; tick <= end; tick += stride)
            {
                ++evaluated;
                var distance = Distance(tick, now, speed);
                if (distance >= 0 && distance <= travel) output.Add(new(tick, (float)distance));
            }
        }
        output.Sort(static (a, b) => a.Rank != b.Rank ? b.Rank.CompareTo(a.Rank) : a.Tick.CompareTo(b.Tick));
        return evaluated;
    }
}

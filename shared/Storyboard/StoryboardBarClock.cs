using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Shared.Storyboard;

/// <summary>Load-time exact Bar conversion with an operand-size and total-work limit.
/// Stops before normalizing each requested moment against a pathological accumulated BPM denominator.</summary>
internal sealed class StoryboardBarClock(V2BpmTimeline timeline)
{
    private const int MaxOperandBits = 8192;
    private const long MaxWork = 64L * 1024 * 1024;
    private readonly IEnumerator<ExactBarTime> _changes = timeline.ChangesInside(ExactBarTime.Zero,
        new(StoryboardLimits.MaxBar + 1, 0, 1)).GetEnumerator();
    private readonly List<TempoSegment> _segments = [];
    private bool _hasNext;
    private long _work;

    internal ExactBarTime SecondsAt(ExactBarTime bar, SourcePoint point, string id)
    {
        if (_segments.Count == 0)
        {
            _segments.Add(new(ExactBarTime.Zero, timeline.ToExactSeconds(ExactBarTime.Zero), SecondsPerBarAt(ExactBarTime.Zero)));
            _hasNext = _changes.MoveNext();
        }
        while (_hasNext && _changes.Current <= bar)
        {
            var change = _changes.Current;
            var exactStart = Resolve(_segments[^1], change, point, id);
            _segments.Add(new(change, exactStart, SecondsPerBarAt(change)));
            _hasNext = _changes.MoveNext();
        }

        var lo = 0;
        var hi = _segments.Count;
        while (lo + 1 < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_segments[mid].Bar <= bar) lo = mid;
            else hi = mid;
        }
        return Resolve(_segments[lo], bar, point, id);
    }

    private ExactBarTime SecondsPerBarAt(ExactBarTime bar) =>
        new ExactBarTime(240, 0, 1) / ExactBarTime.FromDouble(timeline.BpmAt(bar));

    private ExactBarTime Resolve(TempoSegment segment, ExactBarTime bar, SourcePoint point, string id)
    {
        var baseBits = OperandBits(segment.Start);
        var delta = (bar - segment.Bar) * segment.SecondsPerBar;
        var deltaBits = OperandBits(delta);
        // The operands of the addition cannot exceed their combined bit widths before normalization.
        if (baseBits > MaxOperandBits || deltaBits > MaxOperandBits ||
            (_work += Math.Max(128, baseBits + deltaBits)) > MaxWork)
            throw Limit(point, id);
        var exact = delta.IsZero ? segment.Start : segment.Start + delta;
        return OperandBits(exact) <= MaxOperandBits ? exact : throw Limit(point, id);
    }

    private static long OperandBits(ExactBarTime time) =>
        Math.Max(time.Bar.GetBitLength() + time.Denominator.GetBitLength(), time.Numerator.GetBitLength());

    private static StoryboardError Limit(SourcePoint point, string id) =>
        new("UVSB002", point, id, "Exact BPM timing work limit exceeded.");

    private readonly record struct TempoSegment(ExactBarTime Bar, ExactBarTime Start, ExactBarTime SecondsPerBar);
}

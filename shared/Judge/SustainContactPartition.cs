using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Shared.Judge;

/// <summary>Partitions a span with stationary input at every possible change of coverage.
/// Callers own pointer snapshots, endpoint events, grace and scoring. Scratch is reused across spans.</summary>
internal sealed class SustainContactPartition
{
    private readonly List<double> _cuts = [], _bars = [];
    private double _start, _end, _from;
    private V2BpmTimeline? _clock;

    internal void Begin(SustainPath path, double from, double to, bool inBars)
    {
        // Hold's zero-tolerance contact must resolve isolated knot touches in bar space;
        // converting a turning knot to audio seconds and back can move it off its extremum.
        // Mixer retains its existing audio-space classification and tolerance for old scores.
        _clock = inBars ? path.V2BpmTimeline! : null;
        _from = from;
        _start = _clock?.RuntimeBarAt(from) ?? from;
        _end = _clock?.RuntimeBarAt(to) ?? to;
        _cuts.Clear(); _cuts.Add(_start); _cuts.Add(_end);
    }

    internal void Add(SustainPath path, double position, double halfTouchWidth = 0)
    {
        if (_clock is not null)
            path.V2Evaluator!.AddReceiverBoundaries(_start, _end, position, _cuts, receiverHalfWidth: halfTouchWidth);
        else
            MixerContactGeometry.AddBoundaries(path, _start, _end, position, _cuts, _bars);
    }

    // Observe both a closed boundary (including jumps and isolated grazing contact) and
    // the membership of the following open cell at its LEFT time, never at the sample time.
    // The caller observes the right endpoint only AFTER all simultaneous input has arrived.
    internal void Visit(SustainPath path, Action<double, NoteBounds> observe)
    {
        _cuts.Sort();
        for (var i = 1; i < _cuts.Count; i++)
        {
            var left = _cuts[i - 1]; var right = _cuts[i];
            if (right <= left) continue;
            var time = left == _start ? _from : _clock?.RuntimeSecondsAt(left) ?? left;
            observe(time, Bounds(left));
            var middle = left + (right - left) * .5;
            if (_clock is null || (middle > left && middle < right)) observe(time, Bounds(middle));
        }

        NoteBounds Bounds(double x)
        {
            if (_clock is null) return path.BoundsAtRuntime(x);
            var p = path.V2Evaluator!.EvaluateRuntime(x);
            return new(p.Left, p.Right);
        }
    }
}

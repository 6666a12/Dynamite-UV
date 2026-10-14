using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Shared.Judge;

/// <summary>One pointer may cover several chart tracks; projection/mirroring belongs to the adapter.</summary>
public readonly record struct HoldTouchProjection(
    TouchSample? Center, TouchSample? Left, TouchSample? Right)
{
    public TouchSample? On(Track track) => track switch
    {
        Track.Center => Center,
        Track.Left => Left,
        Track.Right => Right,
        _ => null,
    };
}

/// <summary>
/// Reusable timestamped Hold contact snapshots. Head input arbitration stays in the client;
/// this replay observes only events at/after its accepted head and at/before its tail.
/// </summary>
public sealed class HoldContactFrame
{
    private readonly Dictionary<int, HoldTouchProjection> _initial = new();
    private readonly Dictionary<int, HoldTouchProjection> _samples = new();
    private readonly List<(double Time, int Id, HoldTouchProjection? Touch)> _events = new();

    public void Clear()
    {
        _initial.Clear();
        _samples.Clear();
        _events.Clear();
    }

    public void Seed(int id, HoldTouchProjection touch) => _initial[id] = touch;

    /// <summary>Record accepted pointer events in time order; null means release.</summary>
    public void Record(double time, int id, HoldTouchProjection? touch) =>
        _events.Add((time, id, touch));

    public HoldContactState Observe(SustainPath path, double from, double until,
        HoldContactState contact, JudgeSettings settings, Func<double, double> bpmAt,
        double touchWidth)
    {
        until = Math.Min(until, path.EndTime);
        if (until < from)
            return contact; // Legal late head after the tail: no pre-head loss may be invented.

        _samples.Clear();
        foreach (var (id, touch) in _initial)
            _samples[id] = touch;
        for (var i = 0; i < _events.Count;)
        {
            var time = _events[i].Time;
            if (time > until)
                break;
            do
            {
                var input = _events[i++];
                if (input.Touch is { } touch)
                    _samples[input.Id] = touch;
                else
                    _samples.Remove(input.Id);
            } while (i < _events.Count && _events[i].Time == time);

            // Same-time replacement fingers form a single complete snapshot.
            if (time >= from)
                ObserveAt(time);
        }
        ObserveAt(until);
        return contact;

        void ObserveAt(double time)
        {
            var bounds = path.BoundsAtRuntime(time);
            var connected = false;
            foreach (var projection in _samples.Values)
            {
                if (projection.On(path.Track) is { } touch &&
                    InputJudgeRules.Overlaps(bounds, touch.Position, touchWidth))
                {
                    connected = true;
                    break;
                }
            }
            contact = contact.Observe(time, path.EndTime, connected, bpmAt(time), settings);
        }
    }
}

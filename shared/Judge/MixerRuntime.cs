namespace DynamiteUniverse.Shared.Judge;

public readonly record struct MixerInput(double Time, int Id, double? Position,
    ContactPhase Phase = ContactPhase.Began, bool AllowEarlyPlacement = false);

public enum MixerReception { Unplaced, Catching, Grace, Disconnected }

/// <summary>
/// Persistent receiver, independent of fingers. Replays one frame in timestamp order;
/// score ticks and visuals use this same state. All times are song seconds except grace,
/// which advances by 1/8 chart bar (including tempo changes).
/// </summary>
public sealed class MixerRuntime(SustainPath path, Func<double, double> barAt)
{
    public const double GraceBars = .125;
    private readonly Dictionary<int, double> _touches = new();
    private readonly List<double> _cuts = new();
    private double? _time;
    private double? _lossBar;
    public bool Placed { get; private set; }
    public double Position { get; private set; }
    public int? Controller { get; private set; }
    public bool Catching { get; private set; }
    public double? LossBar => _lossBar;
    public double? CurrentTime => _time;

    public MixerReception ReceptionAt(double second) => !Placed ? MixerReception.Unplaced :
        Catching ? MixerReception.Catching :
        _lossBar is { } loss && barAt(second) < loss + GraceBars - 1e-12
            ? MixerReception.Grace : MixerReception.Disconnected;

    public void ClearControls()
    {
        _touches.Clear();
        Controller = null;
    }

    /// <param name="seed">Frame-start contacts on this track; no contact is consumed.</param>
    /// <param name="inputs">Ordered physical events, including removal when a pointer leaves this track.</param>
    /// <param name="ticks">Unresolved tick seconds in ascending order.</param>
    public void AdvanceFrame(double until, IEnumerable<TouchSample> seed,
        IReadOnlyList<MixerInput> inputs, IReadOnlyList<double> ticks,
        Action<int, JudgeGrade> settle, bool auto = false)
    {
        until = Math.Min(until, path.EndTime);
        // Live audio may briefly regress (mix boundary / resume latency). Keep
        // committed geometry and grace time; newly delivered controls still apply.
        if (_time is { } old) until = Math.Max(until, old);
        _time ??= Math.Min(path.StartTime, inputs.Count > 0 ? inputs[0].Time : until);
        _touches.Clear();
        foreach (var touch in seed) _touches[touch.Id] = touch.Position;
        if (Controller is { } owner && !_touches.ContainsKey(owner)) Controller = null;
        var cursor = 0;
        var tick = 0;
        while (cursor < inputs.Count || tick < ticks.Count)
        {
            var inputTime = cursor < inputs.Count ? Math.Max(inputs[cursor].Time, _time.Value) : double.PositiveInfinity;
            var tickTime = tick < ticks.Count ? ticks[tick] : double.PositiveInfinity;
            var next = Math.Min(inputTime, tickTime);
            if (next > until) break;
            if (next < _time.Value) throw new ArgumentException("Mixer received an already-past score tick.", nameof(ticks));
            Advance(next, auto);
            // All events at a tick timestamp precede that tick, preserving arrival order.
            while (cursor < inputs.Count && inputs[cursor].Time <= next)
            {
                var input = inputs[cursor++];
                Apply(input with { Time = next });
            }
            Observe(next, path.BoundsAtRuntime(next), auto);
            while (tick < ticks.Count && ticks[tick] == next)
            {
                settle(tick, auto || ReceptionAt(next) is MixerReception.Catching or MixerReception.Grace
                    ? JudgeGrade.Perfect : JudgeGrade.Miss);
                tick++;
            }
        }
        Advance(until, auto);
        Observe(until, path.BoundsAtRuntime(until), auto);
    }

    private void Apply(MixerInput input)
    {
        if (input.Position is not { } position)
        {
            _touches.Remove(input.Id);
            if (Controller == input.Id) Controller = null;
            return;
        }
        // Unknown Move must not resurrect a released pointer.
        if (input.Phase != ContactPhase.Began && !_touches.ContainsKey(input.Id)) return;
        _touches[input.Id] = position;
        if (input.Time < path.StartTime && !input.AllowEarlyPlacement) return;
        if ((input.Phase == ContactPhase.Began || Controller == input.Id) &&
            MixerContactGeometry.CanCatch(path.BoundsAtRuntime(input.Time), position))
        {
            Placed = true;
            Position = position;
            Controller = input.Id;
        }
    }

    private void Advance(double to, bool auto)
    {
        var from = _time!.Value;
        if (to <= from) return;
        if (to <= path.StartTime) { _time = to; return; }
        from = Math.Max(from, path.StartTime);
        _cuts.Clear();
        _cuts.Add(from);
        _cuts.Add(to);
        if (Placed) MixerContactGeometry.AddBoundaries(path, from, to, Position, _cuts);
        foreach (var position in _touches.Values)
            MixerContactGeometry.AddBoundaries(path, from, to, position, _cuts);
        _cuts.Sort();
        for (var i = 1; i < _cuts.Count; i++)
        {
            var left = _cuts[i - 1];
            var right = _cuts[i];
            if (right <= left) continue;
            // Include isolated/tangent contact and Hold jumps as well as open cells.
            Observe(left, path.BoundsAtRuntime(left), auto);
            // Membership is constant in this open cell. Start losses at its exact
            // boundary, not at the midpoint or the render frame that discovered it.
            Observe(left, path.BoundsAtRuntime(left + (right - left) * .5), auto);
        }
        _time = to;
    }

    private void Observe(double time, NoteBounds bounds, bool auto)
    {
        if (auto)
        {
            if (time < path.StartTime) return;
            Placed = true;
            Position = bounds.Center;
        }
        if (time < path.StartTime)
        {
            Catching = Placed && MixerContactGeometry.CanCatch(bounds, Position);
            return;
        }
        if (!Placed || !MixerContactGeometry.CanCatch(bounds, Position))
        {
            int? best = null;
            var distance = double.PositiveInfinity;
            foreach (var (id, position) in _touches)
            {
                if (!MixerContactGeometry.CanCatch(bounds, position)) continue;
                var delta = Math.Abs(position - (Placed ? Position : bounds.Center));
                if (delta < distance || (delta == distance && (!best.HasValue || id < best.Value)))
                {
                    best = id;
                    distance = delta;
                }
            }
            if (best is { } selected)
            {
                Placed = true;
                Position = _touches[selected];
                Controller = selected;
            }
        }
        Catching = Placed && MixerContactGeometry.CanCatch(bounds, Position);
        if (Catching) _lossBar = null;
        else if (Placed) _lossBar ??= barAt(time);
    }
}

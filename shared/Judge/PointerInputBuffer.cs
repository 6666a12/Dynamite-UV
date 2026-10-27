namespace DynamiteUniverse.Shared.Judge;

public enum PointerAction { Press, Move, Release }

public readonly record struct PointerInput<TPosition>(
    int Id, TPosition ScreenPosition, double Time, PointerAction Action);

/// <summary>
/// Owns pointer lifetimes and reusable frame snapshots. The adapter supplies screen-to-chart
/// projection; judgement, input arbitration and scene navigation stay with the caller.
/// </summary>
public sealed class PointerInputBuffer<TPosition>(
    Action<List<TouchSample>, int, TPosition, ContactPhase> projectTouches,
    Func<int, TPosition, HoldTouchProjection> projectHold)
{
    private sealed class ActivePointer(int id, TPosition position)
    {
        public int Id { get; } = id;
        public TPosition Position = position;
        public ContactPhase Phase = ContactPhase.Began;
    }

    private readonly List<PointerInput<TPosition>> _pending = new();
    private readonly Dictionary<int, ActivePointer> _active = new();
    private readonly List<TouchSample> _touches = new();
    private readonly List<TouchSample> _initialTouches = new();
    private readonly List<PointerInput<TPosition>> _inputs = new();
    private readonly List<(TouchSample Touch, double Time)> _presses = new();

    public IReadOnlyList<TouchSample> Touches => _touches;
    public IReadOnlyList<TouchSample> InitialTouches => _initialTouches;
    public IReadOnlyList<PointerInput<TPosition>> Inputs => _inputs;
    public IReadOnlyList<(TouchSample Touch, double Time)> Presses => _presses;
    public HoldContactFrame HoldContacts { get; } = new();

    public void Queue(PointerInput<TPosition> input) => _pending.Add(input);
    public void DiscardPending() => _pending.Clear();

    /// <summary>
    /// Collect all events before processing presses. A press and release in one frame still
    /// retain their timestamps for short Hold replay, even when the final touch list is empty.
    /// Returned collections are reused and remain valid until the next CollectFrame/Clear.
    /// </summary>
    public void CollectFrame()
    {
        ClearFrame();
        foreach (var pointer in _active.Values)
        {
            HoldContacts.Seed(pointer.Id, projectHold(pointer.Id, pointer.Position));
            pointer.Phase = ContactPhase.Stationary;
            projectTouches(_touches, pointer.Id, pointer.Position, pointer.Phase);
            projectTouches(_initialTouches, pointer.Id, pointer.Position, pointer.Phase);
        }

        foreach (var input in _pending)
        {
            switch (input.Action)
            {
                case PointerAction.Press:
                {
                    _inputs.Add(input);
                    _active[input.Id] = new ActivePointer(input.Id, input.ScreenPosition);
                    RemoveTouches(input.Id);
                    var first = _touches.Count;
                    projectTouches(_touches, input.Id, input.ScreenPosition, ContactPhase.Began);
                    for (var i = first; i < _touches.Count; i++)
                        _presses.Add((_touches[i], input.Time));
                    HoldContacts.Record(input.Time, input.Id, projectHold(input.Id, input.ScreenPosition));
                    break;
                }
                case PointerAction.Move:
                    if (!_active.TryGetValue(input.Id, out var pointer))
                        break;
                    _inputs.Add(input);
                    pointer.Position = input.ScreenPosition;
                    if (pointer.Phase != ContactPhase.Began)
                        pointer.Phase = ContactPhase.Moved;
                    RemoveTouches(input.Id);
                    projectTouches(_touches, input.Id, input.ScreenPosition, pointer.Phase);
                    HoldContacts.Record(input.Time, input.Id, projectHold(input.Id, input.ScreenPosition));
                    break;
                case PointerAction.Release:
                    _inputs.Add(input);
                    HoldContacts.Record(input.Time, input.Id, null);
                    _active.Remove(input.Id);
                    RemoveTouches(input.Id);
                    break;
            }
        }
        _pending.Clear();
    }

    /// <summary>Pause/focus loss must clear both active contacts and timestamped replay.</summary>
    public void Clear()
    {
        _pending.Clear();
        _active.Clear();
        ClearFrame();
    }

    private void ClearFrame()
    {
        _touches.Clear();
        _initialTouches.Clear();
        _inputs.Clear();
        _presses.Clear();
        HoldContacts.Clear();
    }

    private void RemoveTouches(int id)
    {
        for (var i = _touches.Count - 1; i >= 0; i--)
            if (_touches[i].Id == id)
                _touches.RemoveAt(i);
    }
}

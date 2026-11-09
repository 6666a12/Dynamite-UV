using Godot;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor;

/// <summary>Owns a chart-space selection range, independent of render-view lifetime.</summary>
internal sealed class EditorMarqueeSelection
{
    internal readonly record struct Point(ExactBarTime Time, double Center);
    internal readonly record struct Candidate(int Id, ExactBarTime Time, double Center);
    private Vector2 _startPointer;
    private HashSet<int> _before = new();
    private bool _additive;
    public bool Active { get; private set; }
    public bool Moved { get; private set; }
    public EditorTrack Track { get; private set; }
    public Point Start { get; private set; }
    public Point End { get; private set; }
    public Vector2 Pointer { get; private set; }
    public IReadOnlySet<int> Preview { get; private set; } = new HashSet<int>();

    public void Begin(EditorTrack track, Vector2 pointer, Point start, IReadOnlySet<int> selection, bool additive)
    {
        Track = track;
        _startPointer = Pointer = pointer;
        Start = End = start;
        _before = new(selection);
        _additive = additive;
        Active = true;
        Moved = false;
        Preview = new HashSet<int>(selection);
    }

    public void Update(Vector2 pointer, Point end, IEnumerable<Candidate> candidates)
    {
        if (!Active) return;
        Pointer = pointer;
        End = end;
        // Playback can move the range without any new mouse-motion event.
        Moved |= _startPointer.DistanceTo(pointer) >= 3 || End.Time != Start.Time;
        if (!Moved) return;
        var selected = _additive ? new HashSet<int>(_before) : new HashSet<int>();
        var first = Start.Time < End.Time ? Start.Time : End.Time;
        var last = Start.Time > End.Time ? Start.Time : End.Time;
        var left = Math.Min(Start.Center, End.Center);
        var right = Math.Max(Start.Center, End.Center);
        foreach (var note in candidates)
            if (note.Time >= first && note.Time <= last && note.Center >= left && note.Center <= right)
                selected.Add(note.Id);
        Preview = selected;
    }

    public IReadOnlySet<int> Finish()
    {
        var result = Moved ? Preview : _additive ? _before : new HashSet<int>();
        Active = false;
        return result;
    }

    public void Cancel() => Active = false;
}

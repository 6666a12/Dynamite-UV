using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public sealed class CanvasSelectionState
{
    private readonly HashSet<string> _noteIds = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> NoteIds => _noteIds;
    public string? SingleNoteId => _noteIds.Count == 1 ? _noteIds.First() : null;
    public string? PrimaryNoteId { get; private set; }
    public ExactBarTime? EventTime { get; private set; }
    public string? SelectedNodeId { get; private set; }
    public bool MarqueeActive { get; private set; }
    public bool MarqueeMoved { get; private set; }
    public bool MarqueeAdditive { get; private set; }

    public void SelectNote(string id)
    {
        Clear();
        _noteIds.Add(id);
        PrimaryNoteId = id;
    }

    public void ToggleNote(string id)
    {
        if (!_noteIds.Add(id)) _noteIds.Remove(id);
        PrimaryNoteId = _noteIds.Count == 1 ? id : null;
        EventTime = null;
        SelectedNodeId = null;
    }

    public void SelectEvent(ExactBarTime time)
    {
        Clear();
        EventTime = time;
    }

    public void SelectNode(string noteId, string? nodeId)
    {
        SelectNote(noteId);
        SelectedNodeId = nodeId;
    }

    public void Clear()
    {
        _noteIds.Clear();
        PrimaryNoteId = null;
        EventTime = null;
        SelectedNodeId = null;
    }

    public void Restore(IReadOnlyList<AuthoringNote> notes, IReadOnlyList<AuthoringBpmEvent> events)
    {
        _noteIds.RemoveWhere(id => !notes.Any(note => note.Id == id));
        PrimaryNoteId = SingleNoteId;
        if (EventTime is { } time && !events.Any(item => item.TimeExact == time)) EventTime = null;
        var single = SingleNoteId;
        if (single is null || !notes.Any(note => note.Id == single &&
            note.Nodes.Any(node => node.Id == SelectedNodeId))) SelectedNodeId = null;
    }

    public void BeginMarquee(bool additive)
    {
        MarqueeActive = true;
        MarqueeMoved = false;
        MarqueeAdditive = additive;
        if (!additive) Clear();
    }

    public void UpdateMarquee(double distance) => MarqueeMoved |= distance >= 3d;

    public bool FinishMarquee(IEnumerable<string> hitNoteIds)
    {
        var moved = MarqueeActive && MarqueeMoved;
        if (moved)
        {
            foreach (var id in hitNoteIds) _noteIds.Add(id);
            PrimaryNoteId = SingleNoteId;
            EventTime = null;
            SelectedNodeId = null;
        }
        CancelMarquee();
        return moved;
    }

    public void CancelMarquee()
    {
        MarqueeActive = false;
        MarqueeMoved = false;
    }
}

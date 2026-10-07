using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>
/// View/controller adapter that turns canvas authoring gestures into document commands.
/// The <see cref="EditorDocument"/> stays the single authoring authority: the canvas only
/// renders a projection and every mutation flows through undoable commands and dirty tracking.
/// </summary>
public sealed class CanvasAuthoringController
{
    public CanvasAuthoringController(EditorDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public EditorDocument Document { get; }
    public EditableChart Chart => Document.SelectedChart;
    public bool CanUndo => Document.CanUndo;
    public bool CanRedo => Document.CanRedo;

    /// <summary>Adds a note and returns its document-assigned id.</summary>
    public string AddNote(V2NoteType type, EditorTrack track, ExactBarTime time,
        double center, double width, ExactBarTime? tailTime = null)
    {
        var id = Document.AllocateId(IdPrefix(type));
        var note = new EditableNote
        {
            Id = id,
            Type = type,
            Track = track,
            Time = time,
            Center = center,
            Width = width,
        };
        if (type is V2NoteType.Hold or V2NoteType.Mixer)
        {
            var tail = tailTime ?? time;
            if (tail <= time)
                throw new InvalidOperationException("Path tail must be strictly after the head.");
            note.Nodes.Add(new EditablePathNode
            {
                Id = $"{id}-tail",
                Time = tail,
                Center = center,
                Width = width,
                Judge = type == V2NoteType.Hold ? true : null,
            });
        }
        Document.Execute(new AddNoteCommand(note));
        return id;
    }

    public void MoveNote(string noteId, ExactBarTime time, double center, double width)
    {
        var note = Document.RequireNote(noteId);
        if (note.Time == time && note.Center == center && note.Width == width)
            return;
        Document.Execute(note.Nodes.Count == 0
            ? new MoveNoteCommand(noteId, time, center, width)
            : new MovePathNoteCommand(noteId, time, center, width));
    }

    public void EditPathNode(string noteId, string nodeId, ExactBarTime time, double center,
        double width)
    {
        var node = Document.RequirePathNode(noteId, nodeId);
        if (node.Time == time && node.Center == center && node.Width == width)
            return;
        Document.Execute(new EditPathNodeCommand(noteId, nodeId, time, center, width));
    }

    public void DeleteNotes(IReadOnlyList<string> noteIds)
    {
        ArgumentNullException.ThrowIfNull(noteIds);
        if (noteIds.Count == 0)
            return;
        var commands = noteIds.Select(id => (IEditorCommand)new DeleteNoteCommand(id)).ToArray();
        Document.Execute(commands.Length == 1 ? commands[0] : new CompositeEditorCommand(commands));
    }

    public void AddBpm(ExactBarTime time, double bpm) =>
        Document.Execute(new AddBpmCommand(time, bpm));

    public void EditBpm(ExactBarTime originalTime, ExactBarTime newTime, double bpm) =>
        Document.Execute(new EditBpmCommand(originalTime, newTime, bpm));

    public void DeleteBpm(ExactBarTime time) =>
        Document.Execute(new DeleteBpmCommand(time));

    public void Undo() => Document.Undo();
    public void Redo() => Document.Redo();

    private static string IdPrefix(V2NoteType type) => type switch
    {
        V2NoteType.Hold => "hold",
        V2NoteType.Mixer => "mixer",
        V2NoteType.Drag => "drag",
        V2NoteType.ExTap => "extap",
        V2NoteType.Mine => "mine",
        V2NoteType.BarLine => "barline",
        _ => "tap",
    };
}

using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

/// <summary>Explicit batch request; null fields mean untouched, not mixed-value defaults.</summary>
public sealed record EditorBatchEdit
{
    public ExactBarTime? Time { get; init; }
    public bool MoveStart { get; init; }
    public double? PositionDelta { get; init; }
    public double? Width { get; init; }
    public bool AddWidth { get; init; }
    public bool WholePath { get; init; }
    public EditorTrack? Track { get; init; }
    public V2NoteType? Type { get; init; }
}

/// <summary>Prevalidates all copies before mutation. One history entry preserves complete paths and identities.</summary>
public sealed class EditNoteSelectionCommand : IEditorCommand
{
    private readonly string[] _ids;
    private readonly EditorBatchEdit _edit;
    private EditableNote[]? _before;
    private EditableNote[]? _after;
    public EditNoteSelectionCommand(IEnumerable<string> ids, EditorBatchEdit edit)
    {
        _ids = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (_ids.Length == 0) throw new ArgumentException("Empty selection.");
        _edit = edit;
    }
    public static bool CanConvert(V2NoteType type) => type is V2NoteType.Tap or V2NoteType.Drag or V2NoteType.ExTap;
    public void Execute(EditorDocument document)
    {
        if (_after is null)
        {
            var before = _ids.Select(id => document.RequireNote(id).Clone()).ToArray();
            if (_edit.Type is { } type && (!CanConvert(type) || before.Any(n => !CanConvert(n.Type))))
                throw new InvalidOperationException("Batch type conversion supports Tap / Drag / ExTap only.");
            if (_edit.Track is { } track && !Enum.IsDefined(track)) throw new InvalidOperationException("Invalid track.");
            var delta = _edit.Time is { } time ? (_edit.MoveStart ? time - before.Min(n => n.Time) : time) : ExactBarTime.Zero;
            var after = before.Select(n => n.Clone()).ToArray();
            foreach (var note in after)
            {
                note.Time += delta;
                foreach (var node in note.Nodes) node.Time += delta;
                if (_edit.PositionDelta is { } position)
                {
                    note.Center = EditorSpatialMath.Add(note.Center, position);
                    foreach (var node in note.Nodes) node.Center = EditorSpatialMath.Add(node.Center, position);
                }
                if (_edit.Width is { } width)
                {
                    note.Width = _edit.AddWidth ? EditorSpatialMath.Add(note.Width, width) : width;
                    if (_edit.WholePath)
                        foreach (var node in note.Nodes) node.Width = _edit.AddWidth ? EditorSpatialMath.Add(node.Width, width) : width;
                }
                if (_edit.Track is { } targetTrack) note.Track = targetTrack;
                if (_edit.Type is { } targetType) note.Type = targetType;
                EditorCommandRules.ValidateNote(note);
            }
            _before = before;
            _after = after;
        }
        Replace(document, _after);
    }
    public void Undo(EditorDocument document) => Replace(document, _before ?? throw new InvalidOperationException("Command not executed."));
    private static void Replace(EditorDocument document, EditableNote[] notes)
    {
        var indices = notes.Select(n => document.SelectedChart.Notes.FindIndex(old => old.Id == n.Id)).ToArray();
        if (indices.Any(i => i < 0)) throw new InvalidOperationException("Selection changed before command execution.");
        for (var i = 0; i < notes.Length; i++) document.SelectedChart.Notes[indices[i]] = notes[i].Clone();
    }
}

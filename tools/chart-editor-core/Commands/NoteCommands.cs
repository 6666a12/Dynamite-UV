using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.ChartEditor.Core;

public sealed class AddNoteCommand(EditableNote note) : IEditorCommand
{
    private readonly EditableNote _note = note.Clone();

    public void Execute(EditorDocument document)
    {
        EditorCommandRules.ValidateNote(_note);
        var ids = new[] { _note.Id }.Concat(_note.Nodes.Select(node => node.Id)).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidOperationException($"Note '{_note.Id}' contains duplicate parent/node IDs.");
        foreach (var id in ids)
            document.RequireChartWideIdAvailable(id);

        document.SelectedChart.Notes.Add(_note.Clone());
        document.Select(new EditorSelection(EditorSelectionKind.Note, _note.Id));
    }

    public void Undo(EditorDocument document)
    {
        document.SelectedChart.Notes.RemoveAll(item => item.Id == _note.Id);
        document.Select(EditorSelection.None);
    }
}

public sealed class DeleteNoteCommand(string noteId) : IEditorCommand
{
    private EditableNote? _deleted;
    private int _index;

    public void Execute(EditorDocument document)
    {
        _index = document.SelectedChart.Notes.FindIndex(item => item.Id == noteId);
        if (_index < 0)
            throw new KeyNotFoundException($"Note '{noteId}' was not found.");
        _deleted = document.SelectedChart.Notes[_index].Clone();
        document.SelectedChart.Notes.RemoveAt(_index);
        document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete command has not executed.");
        document.SelectedChart.Notes.Insert(_index, _deleted.Clone());
        document.Select(new EditorSelection(EditorSelectionKind.Note, _deleted.Id));
    }
}

public sealed class MoveNoteCommand(string noteId, ExactBarTime time, double center, double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        var candidate = note.Clone();
        candidate.Time = time;
        candidate.Center = center;
        candidate.Width = width;
        EditorCommandRules.ValidateNote(candidate);
        if (!_captured)
        {
            _oldTime = note.Time;
            _oldCenter = note.Center;
            _oldWidth = note.Width;
            _captured = true;
        }
        note.Time = time;
        note.Center = center;
        note.Width = width;
    }

    public void Undo(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        note.Time = _oldTime;
        note.Center = _oldCenter;
        note.Width = _oldWidth;
    }
}

/// <summary>Moves a Hold/Mixer head and all of its path nodes by the same exact bar delta.</summary>
public sealed class MovePathNoteCommand(string noteId, ExactBarTime time, double center,
    double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private ExactBarTime _delta;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        if (!_captured)
        {
            _oldTime = note.Time;
            _oldCenter = note.Center;
            _oldWidth = note.Width;
            _delta = time - note.Time;
            _captured = true;
        }
        var candidate = note.Clone();
        candidate.Time = time;
        candidate.Center = center;
        candidate.Width = width;
        foreach (var node in candidate.Nodes)
            node.Time = node.Time + _delta;
        EditorCommandRules.ValidateNote(candidate);

        note.Time = time;
        note.Center = center;
        note.Width = width;
        foreach (var node in note.Nodes)
            node.Time = node.Time + _delta;
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Move path command has not executed.");
        var note = EditorCommandRules.RequirePathNote(document, noteId);
        note.Time = _oldTime;
        note.Center = _oldCenter;
        note.Width = _oldWidth;
        foreach (var node in note.Nodes)
            node.Time = node.Time - _delta;
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId));
    }
}

public sealed class RenameNoteCommand(string noteId, string newId) : IEditorCommand
{
    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        EditorCommandRules.ValidateId(newId, "Note");
        document.RequireChartWideIdAvailable(newId, noteId);
        note.Id = newId;
        document.Select(new EditorSelection(EditorSelectionKind.Note, newId, note.Key));
    }

    public void Undo(EditorDocument document)
    {
        var note = document.RequireNote(newId);
        document.RequireChartWideIdAvailable(noteId, newId);
        note.Id = noteId;
        document.Select(new EditorSelection(EditorSelectionKind.Note, noteId, note.Key));
    }
}

public sealed class SetNoteTrackCommand(string noteId, EditorTrack track) : IEditorCommand
{
    private EditorTrack _before;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        if (!_captured) { _before = note.Track; _captured = true; }
        note.Track = track;
        document.Select(new EditorSelection(EditorSelectionKind.Note, note.Id, note.Key));
    }

    public void Undo(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        note.Track = _before;
        document.Select(new EditorSelection(EditorSelectionKind.Note, note.Id, note.Key));
    }
}

public sealed class ChangeNoteTypeCommand : IEditorCommand
{
    private readonly string _noteId;
    private readonly V2NoteType _newType;
    private readonly EditablePathNode? _requestedTerminal;
    private EditableNote? _before;
    private EditableNote? _after;
    private int _index;

    public ChangeNoteTypeCommand(string noteId, V2NoteType newType)
        : this(noteId, newType, null)
    {
    }

    public ChangeNoteTypeCommand(string noteId, V2NoteType newType, EditablePathNode? pathTerminal)
    {
        _noteId = noteId;
        _newType = newType;
        _requestedTerminal = pathTerminal is null ? null : EditorCommandRules.CloneNode(pathTerminal);
    }

    public void Execute(EditorDocument document)
    {
        if (!Enum.IsDefined(_newType))
            throw new InvalidOperationException($"Unknown v2 note type '{_newType}'.");
        _index = document.SelectedChart.Notes.FindIndex(note => note.Id == _noteId);
        if (_index < 0)
            throw new KeyNotFoundException($"Note '{_noteId}' was not found.");

        if (_after is null)
        {
            _before = document.SelectedChart.Notes[_index].Clone();
            try
            {
                _after = BuildChangedNote(document, _before);
                EditorCommandRules.ValidateNote(_after);
            }
            catch
            {
                _before = null;
                throw;
            }
        }
        document.SelectedChart.Notes[_index] = _after.Clone();
        document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
    }

    public void Undo(EditorDocument document)
    {
        if (_before is null)
            throw new InvalidOperationException("Change note type command has not executed.");
        document.SelectedChart.Notes[_index] = _before.Clone();
        document.Select(new EditorSelection(EditorSelectionKind.Note, _noteId));
    }

    private EditableNote BuildChangedNote(EditorDocument document, EditableNote source)
    {
        var changed = source.Clone();
        changed.Type = _newType;
        if (_newType is not (V2NoteType.Hold or V2NoteType.Mixer))
        {
            changed.CurveToNext = null;
            changed.Nodes.Clear();
            return changed;
        }

        if (changed.Nodes.Count == 0)
        {
            EditablePathNode terminal;
            if (_requestedTerminal is not null)
            {
                terminal = EditorCommandRules.CloneNode(_requestedTerminal);
                document.RequireChartWideIdAvailable(terminal.Id);
            }
            else
            {
                if (document.GridDivisor <= 0)
                    throw new InvalidOperationException("Grid divisor must be positive when creating a path terminal.");
                terminal = new EditablePathNode
                {
                    Id = document.AllocateId(_newType == V2NoteType.Hold ? "hold-node" : "mixer-node"),
                    Time = changed.Time + ExactBarTime.FromFraction(1, document.GridDivisor),
                    Center = changed.Center,
                    Width = changed.Width,
                };
            }
            terminal.CurveToNext = null;
            changed.Nodes.Add(terminal);
        }

        foreach (var node in changed.Nodes)
            node.Judge = _newType == V2NoteType.Hold ? node.Judge ?? true : null;
        changed.Nodes[^1].CurveToNext = null;
        if (_newType == V2NoteType.Hold)
            changed.Nodes[^1].Judge = true;
        return changed;
    }
}

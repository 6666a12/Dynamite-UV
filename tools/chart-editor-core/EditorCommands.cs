using DuxShared.Chart.V2;

namespace DuxCommunity.ChartEditor.Core;

public interface IEditorCommand
{
    void Execute(EditorDocument document);
    void Undo(EditorDocument document);
}

public sealed class CompositeEditorCommand(params IEditorCommand[] commands) : IEditorCommand
{
    private readonly IReadOnlyList<IEditorCommand> _commands = commands.Length > 0
        ? commands
        : throw new ArgumentException("A composite command requires at least one command.", nameof(commands));

    public void Execute(EditorDocument document)
    {
        var completed = 0;
        try
        {
            for (; completed < _commands.Count; completed++)
                _commands[completed].Execute(document);
        }
        catch
        {
            for (var index = completed - 1; index >= 0; index--)
                _commands[index].Undo(document);
            throw;
        }
    }

    public void Undo(EditorDocument document)
    {
        for (var index = _commands.Count - 1; index >= 0; index--)
            _commands[index].Undo(document);
    }
}

public sealed class AddNoteCommand(EditableNote note) : IEditorCommand
{
    private readonly EditableNote _note = note.Clone();

    public void Execute(EditorDocument document)
    {
        if (document.SelectedChart.Notes.Any(item => item.Id == _note.Id))
            throw new InvalidOperationException($"Note '{_note.Id}' already exists.");
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

public sealed class EditBpmCommand(ExactBarTime originalTime, ExactBarTime newTime, double newBpm) : IEditorCommand
{
    private double _oldBpm;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var bpm = document.RequireBpm(originalTime);
        if (newTime != originalTime && document.SelectedChart.Bpms.Any(item =>
                item.Time == newTime && item.Time != originalTime))
            throw new InvalidOperationException($"BPM already exists at {newTime}.");
        if (!_captured)
        {
            _oldBpm = bpm.Bpm;
            _captured = true;
        }
        bpm.Time = newTime;
        bpm.Bpm = newBpm;
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, newTime.ToString()));
    }

    public void Undo(EditorDocument document)
    {
        var bpm = document.RequireBpm(newTime);
        bpm.Time = originalTime;
        bpm.Bpm = _oldBpm;
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, originalTime.ToString()));
    }
}

public sealed class AddBpmCommand(ExactBarTime time, double bpm) : IEditorCommand
{
    public void Execute(EditorDocument document)
    {
        if (document.SelectedChart.Bpms.Any(item => item.Time == time))
            throw new InvalidOperationException($"BPM already exists at {time}.");
        document.SelectedChart.Bpms.Add(new EditableBpm { Time = time, Bpm = bpm });
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, time.ToString()));
    }

    public void Undo(EditorDocument document)
    {
        document.SelectedChart.Bpms.RemoveAll(item => item.Time == time);
        document.Select(EditorSelection.None);
    }
}

public sealed class DeleteBpmCommand(ExactBarTime time) : IEditorCommand
{
    private EditableBpm? _deleted;
    private int _index;

    public void Execute(EditorDocument document)
    {
        if (time == ExactBarTime.Zero)
            throw new InvalidOperationException("The base BPM event at bar 0 cannot be deleted.");
        _index = document.SelectedChart.Bpms.FindIndex(item => item.Time == time);
        if (_index < 0)
            throw new KeyNotFoundException($"BPM at '{time}' was not found.");
        _deleted = document.SelectedChart.Bpms[_index];
        document.SelectedChart.Bpms.RemoveAt(_index);
        document.Select(EditorSelection.None);
    }

    public void Undo(EditorDocument document)
    {
        if (_deleted is null)
            throw new InvalidOperationException("Delete BPM command has not executed.");
        document.SelectedChart.Bpms.Insert(_index, _deleted);
        document.SelectedChart.Bpms.Sort((left, right) => left.Time.CompareTo(right.Time));
        document.Select(new EditorSelection(EditorSelectionKind.Bpm, _deleted.Time.ToString()));
    }
}

public sealed class EditChartOffsetCommand(double audioOffsetSec) : IEditorCommand
{
    private double _oldOffset;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        if (!double.IsFinite(audioOffsetSec))
            throw new InvalidOperationException("Chart audio offset must be finite.");
        if (!_captured)
        {
            _oldOffset = document.SelectedChart.AudioOffsetSec;
            _captured = true;
        }
        document.SelectedChart.AudioOffsetSec = audioOffsetSec;
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Chart offset command has not executed.");
        document.SelectedChart.AudioOffsetSec = _oldOffset;
    }
}

public sealed class EditPathTerminalCommand(
    string noteId,
    ExactBarTime time,
    double center,
    double width) : IEditorCommand
{
    private ExactBarTime _oldTime;
    private double _oldCenter;
    private double _oldWidth;
    private bool _captured;

    public void Execute(EditorDocument document)
    {
        var note = document.RequireNote(noteId);
        if (note.Type is not (V2NoteType.Hold or V2NoteType.Mixer) || note.Nodes.Count == 0)
            throw new InvalidOperationException($"Note '{noteId}' has no editable path terminal.");
        if (time <= note.Time)
            throw new InvalidOperationException("Path terminal must be later than the head.");
        if (!double.IsFinite(center) || !double.IsFinite(width) || width <= 0)
            throw new InvalidOperationException("Path terminal center/width is invalid.");

        var terminal = note.Nodes[^1];
        if (!_captured)
        {
            _oldTime = terminal.Time;
            _oldCenter = terminal.Center;
            _oldWidth = terminal.Width;
            _captured = true;
        }
        terminal.Time = time;
        terminal.Center = center;
        terminal.Width = width;
    }

    public void Undo(EditorDocument document)
    {
        if (!_captured)
            throw new InvalidOperationException("Path terminal command has not executed.");
        var terminal = document.RequireNote(noteId).Nodes[^1];
        terminal.Time = _oldTime;
        terminal.Center = _oldCenter;
        terminal.Width = _oldWidth;
    }
}
